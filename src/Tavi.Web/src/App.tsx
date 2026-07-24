import { useCallback, useEffect, useMemo, useRef, useState, type FormEvent, type ReactNode } from 'react'
import { Background, BackgroundVariant, BaseEdge, Controls, EdgeLabelRenderer, Handle, MarkerType, MiniMap, Position, ReactFlow, getStraightPath, useNodesState, type Edge, type EdgeProps, type Node, type NodeChange, type NodePositionChange, type NodeProps } from '@xyflow/react'
import { Archive, BookOpen, Check, ChevronDown, CirclePlus, Cloud, CloudOff, GitBranch, Layers3, LoaderCircle, Network, PanelRightClose, Redo2, Save, Search, Settings, Sparkles, Trash2, Undo2, X } from 'lucide-react'
import { ApiError, settingsApi, worldApi } from './api'
import { GuidancePanel } from './GuidancePanel'
import { WritingWorkspace } from './WritingWorkspace'
import type { AspectViewModel, ElementViewModel, FeaturePolicy, LanguageModelSettingsViewModel, OpenAIConfigurationInput, OpenAIClientType, RelationViewModel, ScopeViewModel, Selection, WorldGraphViewModel } from './types'

type ScopeFilter = 'all' | string
type Dialog = 'element' | 'scope' | 'aspect' | 'relation' | null

const emptyWorld: WorldGraphViewModel = { stateId: '', stagingRevision: 0, isDirty: false, canUndo: false, canRedo: false, health: 'Healthy', elements: [], aspects: [], relations: [], scopes: [], stagedChanges: [] }

interface RelationshipBundleData extends Record<string, unknown> {
  relations: RelationViewModel[]
  nodeNames: Record<string, string>
  selectedRelationId: string | null
  tooltipsEnabled: boolean
  onSelect: (relationId: string) => void
}

interface ElementNodeData extends Record<string, unknown> {
  element: ElementViewModel
}

type RelationshipBundleEdge = Edge<RelationshipBundleData, 'relationshipBundle'>
type ElementFlowNode = Node<ElementNodeData, 'elementNode'>
const edgeTypes = { relationshipBundle: RelationshipBundle }
const nodeTypes = { elementNode: ElementNode }

function layoutPosition(index: number, total: number) {
  const columns = Math.max(1, Math.ceil(Math.sqrt(total)))
  return { x: (index % columns) * 250, y: Math.floor(index / columns) * 170 }
}

function RelationshipBundle({ id, sourceX, sourceY, targetX, targetY, markerStart, markerEnd, data, selected }: EdgeProps<RelationshipBundleEdge>) {
  const [hovered, setHovered] = useState(false)
  const closeTimer = useRef<number | null>(null)
  const relations = data?.relations ?? []
  const containsMultipleScopes = new Set(relations.map(relation => relation.scopeId)).size > 1
  const distance = Math.hypot(targetX - sourceX, targetY - sourceY)
  const direction = distance > 0 ? { x: (targetX - sourceX) / distance, y: (targetY - sourceY) / distance } : { x: 0, y: 0 }
  const endpointInset = Math.min(17, distance / 3)
  const [path, labelX, labelY] = getStraightPath({
    sourceX: sourceX + direction.x * endpointInset,
    sourceY: sourceY + direction.y * endpointInset,
    targetX: targetX - direction.x * endpointInset,
    targetY: targetY - direction.y * endpointInset,
  })

  const openTooltip = () => {
    if (!data?.tooltipsEnabled)
      return
    if (closeTimer.current !== null)
      window.clearTimeout(closeTimer.current)
    closeTimer.current = null
    setHovered(true)
  }

  const scheduleClose = () => {
    if (closeTimer.current !== null)
      window.clearTimeout(closeTimer.current)
    closeTimer.current = window.setTimeout(() => {
      closeTimer.current = null
      setHovered(false)
    }, 120)
  }

  useEffect(() => {
    if (!data?.tooltipsEnabled) {
      if (closeTimer.current !== null)
        window.clearTimeout(closeTimer.current)
      closeTimer.current = null
      setHovered(false)
    }
    return () => {
      if (closeTimer.current !== null)
        window.clearTimeout(closeTimer.current)
    }
  }, [data?.tooltipsEnabled])

  return (
    <>
      <g className="relationship-bundle" onMouseEnter={openTooltip} onMouseLeave={scheduleClose}>
        <BaseEdge
          id={id}
          path={path}
          interactionWidth={30}
          className={`${containsMultipleScopes ? 'subworld' : 'world'}${selected ? ' selected' : ''}`}
          markerStart={markerStart}
          markerEnd={markerEnd}
        />
      </g>
      <EdgeLabelRenderer>
        <div
          className={`edge-bundle-overlay nodrag nopan${hovered ? ' open' : ''}`}
          style={{ transform: `translate(-50%, -50%) translate(${labelX}px, ${labelY}px)`, pointerEvents: hovered || relations.length > 1 ? 'all' : 'none' }}
          onMouseEnter={openTooltip}
          onMouseLeave={scheduleClose}
        >
          {relations.length > 1 && <span className="edge-bundle-count" aria-label={`${relations.length} 条重合关系`}>{relations.length}</span>}
          <div className="edge-tooltip" role="list" aria-label={relations.length > 1 ? '重合关系列表' : '关系信息'}>
            <header>{relations.length > 1 ? `${relations.length} 条关系` : '关系'}</header>
            {relations.map(relation => (
              <button
                key={relation.id}
                className={relation.id === data?.selectedRelationId ? 'selected' : ''}
                onClick={event => {
                  event.stopPropagation()
                  data?.onSelect(relation.id)
                  setHovered(false)
                }}
              >
                <span><strong>{relation.name}</strong><small>{data?.nodeNames[relation.sourceElementId] ?? '未知节点'} → {data?.nodeNames[relation.targetElementId] ?? '未知节点'}</small></span>
                <i>{relation.type}</i>
              </button>
            ))}
          </div>
        </div>
      </EdgeLabelRenderer>
    </>
  )
}

function ElementNode({ data, selected }: NodeProps<ElementFlowNode>) {
  const { element } = data
  return (
    <div className={`anchor-node-core${selected ? ' selected' : ''}`}>
      <Handle className="anchor-handle anchor-handle-center" type="target" position={Position.Left} />
      <span className="anchor-node-dot" />
      <div className="anchor-node-tooltip">
        <header><strong>{element.name}</strong><span>{element.type}</span></header>
        <p>{element.description || '暂无描述'}</p>
      </div>
      <Handle className="anchor-handle anchor-handle-center" type="source" position={Position.Right} />
    </div>
  )
}

function App() {
  const [world, setWorld] = useState<WorldGraphViewModel>(emptyWorld)
  const [loading, setLoading] = useState(true)
  const [working, setWorking] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [search, setSearch] = useState('')
  const [scope, setScope] = useState<ScopeFilter>('all')
  const [selection, setSelection] = useState<Selection>(null)
  const [dialog, setDialog] = useState<Dialog>(null)
  const [showInspector, setShowInspector] = useState(true)
  const [showGuidance, setShowGuidance] = useState(false)
  const [showSettings, setShowSettings] = useState(false)
  const [showStaging, setShowStaging] = useState(false)
  const [showWriting, setShowWriting] = useState(false)
  const [tooltipsSuppressed, setTooltipsSuppressed] = useState(false)
  const [flowNodes, setFlowNodes, applyNodeChanges] = useNodesState<Node>([])
  const refreshSequence = useRef(0)
  const lastRefreshAt = useRef(0)
  const refreshTimer = useRef<number | null>(null)
  const positionCache = useRef(new Map<string, { x: number; y: number }>())
  const dragMotion = useRef(new Map<string, { position: { x: number; y: number }; time: number; velocity: { x: number; y: number } }>())
  const inertiaFrames = useRef(new Map<string, number>())
  const tooltipResumeTimer = useRef<number | null>(null)

  const refresh = useCallback(async (quiet = false) => {
    const sequence = ++refreshSequence.current
    if (!quiet)
      setLoading(true)
    try {
      const nextWorld = await worldApi.get()
      if (sequence !== refreshSequence.current)
        return
      setWorld(current => {
        if (current.stateId === nextWorld.stateId && !current.isDirty && nextWorld.isDirty)
          return { ...nextWorld, isDirty: false }
        return nextWorld
      })
      lastRefreshAt.current = Date.now()
      setError(null)
    } catch (requestError) {
      if (sequence === refreshSequence.current)
        setError(toMessage(requestError))
    } finally {
      if (!quiet && sequence === refreshSequence.current)
        setLoading(false)
    }
  }, [])

  useEffect(() => {
    void refresh()
    const events = new EventSource('/api/v1/world/events')
    const scheduleGraphRefresh = () => {
      if (refreshTimer.current !== null)
        window.clearTimeout(refreshTimer.current)
      refreshTimer.current = window.setTimeout(() => {
        refreshTimer.current = null
        if (Date.now() - lastRefreshAt.current >= 100)
          void refresh(true)
      }, 120)
    }
    const updateState = (event: MessageEvent<string>) => {
      const state = parseWorldEvent(event.data)
      if (!state)
        return
      setWorld(current => state.stateId === current.stateId ? { ...current, isDirty: state.isDirty } : current)
      if (state.error)
        setError(state.error)
    }
    events.addEventListener('world.changed', scheduleGraphRefresh)
    const stateEventNames = ['world.dirty-changed', 'world.save-started', 'world.save-completed', 'world.save-failed', 'world.save-cancelled']
    stateEventNames.forEach(name => events.addEventListener(name, updateState as EventListener))
    return () => {
      events.removeEventListener('world.changed', scheduleGraphRefresh)
      stateEventNames.forEach(name => events.removeEventListener(name, updateState as EventListener))
      events.close()
      if (refreshTimer.current !== null)
        window.clearTimeout(refreshTimer.current)
    }
  }, [refresh])

  const visibleRelations = useMemo(() => world.relations.filter(relation => scope === 'all' || relation.scopeId === scope), [scope, world.relations])
  const connectedIds = useMemo(() => new Set(visibleRelations.flatMap(relation => [relation.sourceElementId, relation.targetElementId])), [visibleRelations])
  const visibleElements = useMemo(() => {
    const query = search.trim().toLocaleLowerCase()
    return world.elements.filter(element => {
      const matchesSearch = !query || `${element.name}\n${element.description}\n${element.type}`.toLocaleLowerCase().includes(query)
      const matchesScope = scope === 'all' || connectedIds.has(element.id) || world.aspects.some(aspect => aspect.scopeId === scope && aspect.elementId === element.id) || world.scopes.some(candidate => candidate.id === scope && candidate.ownerElementId === element.id)
      return matchesSearch && matchesScope
    })
  }, [connectedIds, scope, search, world.aspects, world.elements, world.scopes])
  const visibleElementIds = useMemo(() => new Set(visibleElements.map(element => element.id)), [visibleElements])

  useEffect(() => {
    setFlowNodes(current => {
      const existing = new Map(current.map(node => [node.id, node]))
      return visibleElements.map((element, index) => {
        const previous = existing.get(element.id)
        return {
          ...previous,
          id: element.id,
          position: previous?.position ?? positionCache.current.get(element.id) ?? layoutPosition(index, visibleElements.length),
          type: 'elementNode',
          data: { element },
          className: `world-node${selection?.kind === 'element' && selection.id === element.id ? ' selected' : ''}`,
          style: { width: 48, height: 48 },
        }
      })
    })
  }, [selection, setFlowNodes, visibleElements])

  const selectRelation = useCallback((relationId: string) => {
    setSelection({ kind: 'relation', id: relationId })
    setShowInspector(true)
  }, [])

  const edges: RelationshipBundleEdge[] = useMemo(() => {
    const bundles = new Map<string, RelationViewModel[]>()
    visibleRelations.filter(relation => visibleElementIds.has(relation.sourceElementId) && visibleElementIds.has(relation.targetElementId)).forEach(relation => {
      const key = [relation.scopeId, ...[relation.sourceElementId, relation.targetElementId].sort()].map(encodeURIComponent).join('|')
      const bundle = bundles.get(key)
      if (bundle)
        bundle.push(relation)
      else
        bundles.set(key, [relation])
    })
    const nodeNames = Object.fromEntries(world.elements.map(element => [element.id, element.name]))
    const selectedRelationId = selection?.kind === 'relation' ? selection.id : null
    return [...bundles.entries()].map(([key, relations]) => {
      const [source, target] = [relations[0].sourceElementId, relations[0].targetElementId].sort()
      const hasForwardRelation = relations.some(relation => relation.sourceElementId === source && relation.targetElementId === target)
      const hasReverseRelation = relations.some(relation => relation.sourceElementId === target && relation.targetElementId === source)
      const markerColor = '#bd9550'
      return {
        id: `relationship-bundle:${key}`,
        source,
        target,
        type: 'relationshipBundle',
        selected: relations.some(relation => relation.id === selectedRelationId),
        markerStart: hasReverseRelation ? { type: MarkerType.ArrowClosed, color: markerColor, width: 14, height: 14 } : undefined,
        markerEnd: hasForwardRelation ? { type: MarkerType.ArrowClosed, color: markerColor, width: 14, height: 14 } : undefined,
        data: { relations, nodeNames, selectedRelationId, tooltipsEnabled: !tooltipsSuppressed, onSelect: selectRelation },
      }
    })
  }, [selectRelation, selection, tooltipsSuppressed, visibleElementIds, visibleRelations, world.elements])

  const selectedElement = selection?.kind === 'element' ? world.elements.find(element => element.id === selection.id) ?? null : null
  const selectedAspect = selection?.kind === 'aspect' ? world.aspects.find(aspect => aspect.id === selection.id) ?? null : null
  const selectedRelation = selection?.kind === 'relation' ? world.relations.find(relation => relation.id === selection.id) ?? null : null
  const selectedScope = selection?.kind === 'scope' ? world.scopes.find(candidate => candidate.id === selection.id) ?? null : null

  useEffect(() => {
    if (selection?.kind === 'element' && !world.elements.some(element => element.id === selection.id))
      setSelection(null)
    if (selection?.kind === 'aspect' && !world.aspects.some(aspect => aspect.id === selection.id))
      setSelection(null)
    if (selection?.kind === 'relation' && !world.relations.some(relation => relation.id === selection.id))
      setSelection(null)
    if (selection?.kind === 'scope' && !world.scopes.some(candidate => candidate.id === selection.id))
      setSelection(null)
  }, [selection, world.aspects, world.elements, world.relations, world.scopes])

  async function perform(operation: () => Promise<unknown>) {
    setWorking(true)
    try {
      await operation()
      await refresh(true)
      setError(null)
      return true
    } catch (requestError) {
      if (requestError instanceof ApiError && requestError.code === 'TAVI.WORLD.REVISION.CONFLICT')
        await refresh(true)
      setError(toMessage(requestError))
      return false
    } finally {
      setWorking(false)
    }
  }

  const moveNodes = useCallback((changes: NodeChange<Node>[]) => {
    changes.filter((change): change is NodePositionChange => change.type === 'position' && change.position !== undefined).forEach(change => {
      if (change.position)
        positionCache.current.set(change.id, change.position)
    })
    applyNodeChanges(changes)
  }, [applyNodeChanges])

  const stopInertia = useCallback((nodeId: string) => {
    const frame = inertiaFrames.current.get(nodeId)
    if (frame !== undefined)
      window.cancelAnimationFrame(frame)
    inertiaFrames.current.delete(nodeId)
  }, [])

  const handleNodeDragStart = useCallback((_: unknown, node: Node) => {
    if (tooltipResumeTimer.current !== null)
      window.clearTimeout(tooltipResumeTimer.current)
    tooltipResumeTimer.current = null
    setTooltipsSuppressed(true)
    stopInertia(node.id)
    dragMotion.current.set(node.id, { position: node.position, time: performance.now(), velocity: { x: 0, y: 0 } })
  }, [stopInertia])

  const handleNodeDrag = useCallback((_: unknown, node: Node) => {
    const now = performance.now()
    const previous = dragMotion.current.get(node.id)
    if (!previous)
      return
    const elapsed = Math.max(1, now - previous.time)
    const measured = { x: (node.position.x - previous.position.x) / elapsed, y: (node.position.y - previous.position.y) / elapsed }
    dragMotion.current.set(node.id, {
      position: node.position,
      time: now,
      velocity: {
        x: previous.velocity.x * .35 + measured.x * .65,
        y: previous.velocity.y * .35 + measured.y * .65,
      },
    })
  }, [])

  const handleNodeDragStop = useCallback((_: unknown, node: Node) => {
    if (tooltipResumeTimer.current !== null)
      window.clearTimeout(tooltipResumeTimer.current)
    tooltipResumeTimer.current = window.setTimeout(() => {
      tooltipResumeTimer.current = null
      setTooltipsSuppressed(false)
    }, 180)
    const motion = dragMotion.current.get(node.id)
    dragMotion.current.delete(node.id)
    if (!motion)
      return
    const releaseDelay = performance.now() - motion.time
    const releaseFactor = Math.max(0, 1 - releaseDelay / 140)
    let velocity = {
      x: Math.max(-1.15, Math.min(1.15, motion.velocity.x)) * releaseFactor,
      y: Math.max(-1.15, Math.min(1.15, motion.velocity.y)) * releaseFactor,
    }
    if (Math.hypot(velocity.x, velocity.y) < .08)
      return
    let position = { ...node.position }
    let previousTime = performance.now()
    const animate = (now: number) => {
      const elapsed = Math.min(32, now - previousTime)
      previousTime = now
      position = { x: position.x + velocity.x * elapsed, y: position.y + velocity.y * elapsed }
      velocity = { x: velocity.x * Math.pow(.88, elapsed / 16.67), y: velocity.y * Math.pow(.88, elapsed / 16.67) }
      positionCache.current.set(node.id, position)
      setFlowNodes(current => current.map(candidate => candidate.id === node.id ? { ...candidate, position } : candidate))
      if (Math.hypot(velocity.x, velocity.y) >= .018)
        inertiaFrames.current.set(node.id, window.requestAnimationFrame(animate))
      else
        inertiaFrames.current.delete(node.id)
    }
    inertiaFrames.current.set(node.id, window.requestAnimationFrame(animate))
  }, [setFlowNodes])

  useEffect(() => () => {
    inertiaFrames.current.forEach(frame => window.cancelAnimationFrame(frame))
    inertiaFrames.current.clear()
    if (tooltipResumeTimer.current !== null)
      window.clearTimeout(tooltipResumeTimer.current)
  }, [])
  const closeSettings = useCallback(() => setShowSettings(false), [])

  const statusLabel = world.health === 'Faulted' ? '世界会话异常' : world.isDirty ? '等待自动保存' : '已保存'

  return (
    <main className="app-shell">
      <header className="topbar">
        <div className="brand">
          <div className="brand-mark"><Network size={21} /></div>
          <div><strong>Tavi</strong><span>世界图工作台</span></div>
        </div>
        <div className="toolbar">
          <label className="scope-select">
            <GitBranch size={15} />
            <select value={scope} onChange={event => setScope(event.target.value)}>
              <option value="all">全部 Scope</option>
              {world.scopes.map(candidate => <option key={candidate.id} value={candidate.id}>{candidate.name} · {candidate.type}</option>)}
            </select>
            <ChevronDown size={14} />
          </label>
          <div className="toolbar-divider" />
          <button className="writing-toggle" onClick={() => setShowWriting(true)}><BookOpen size={16} />Writing</button>
          <button className={`guidance-toggle${showGuidance ? ' active' : ''}`} onClick={() => setShowGuidance(current => !current)}><Sparkles size={16} />Guidance</button>
          <button className="staging-toggle" disabled={!world.stagedChanges.length} onClick={() => setShowStaging(true)} title={world.stagedChanges.length ? `查看 ${world.stagedChanges.length} 项暂存修改` : '暂存区为空'}>
            <Archive size={16} />暂存区{world.stagedChanges.length > 0 && <span>{world.stagedChanges.length}</span>}
          </button>
          <IconButton label="设置" onClick={() => setShowSettings(true)}><Settings size={17} /></IconButton>
          <div className="toolbar-divider" />
          <IconButton label="撤销" disabled={!world.canUndo || working} onClick={() => void perform(() => worldApi.undo(world.stateId))}><Undo2 size={17} /></IconButton>
          <IconButton label="重做" disabled={!world.canRedo || working} onClick={() => void perform(() => worldApi.redo(world.stateId))}><Redo2 size={17} /></IconButton>
          <button className="save-button" disabled={working} onClick={() => void perform(worldApi.save)}>
            {working ? <LoaderCircle className="spin" size={15} /> : world.isDirty ? <CloudOff size={15} /> : <Cloud size={15} />}
            {statusLabel}
          </button>
          <span className="revision" title={world.stateId}>state {world.stateId.slice(0, 8)}</span>
        </div>
      </header>

      <section className={`workspace${showInspector ? '' : ' inspector-hidden'}`}>
        <aside className="sidebar">
          <div className="sidebar-heading"><span>世界断言图</span><span className="count">{world.elements.length + world.scopes.length + world.aspects.length + world.relations.length}</span></div>
          <label className="search-box"><Search size={16} /><input value={search} onChange={event => setSearch(event.target.value)} placeholder="搜索名称或描述" /></label>
          <div className="sidebar-actions">
            <button onClick={() => setDialog('element')}><CirclePlus size={16} />Element</button>
            <button onClick={() => setDialog('scope')} disabled={!world.elements.length}><Layers3 size={16} />Scope</button>
            <button onClick={() => setDialog('aspect')} disabled={!world.elements.length || !world.scopes.length}><Sparkles size={16} />Aspect</button>
            <button onClick={() => setDialog('relation')} disabled={!world.elements.length || !world.scopes.length}><GitBranch size={16} />Relation</button>
          </div>
          <div className="anchor-list">
            {world.elements.map(element => (
              <button key={element.id} className={selection?.kind === 'element' && selection.id === element.id ? 'active' : ''} onClick={() => { setSelection({ kind: 'element', id: element.id }); setShowInspector(true) }}>
                <span className="anchor-icon item"><Network size={15} /></span>
                <span><strong>{element.name}</strong><small>Element · {element.type}</small></span>
              </button>
            ))}
            {world.scopes.map(candidate => <button key={candidate.id} className={selection?.kind === 'scope' && selection.id === candidate.id ? 'active' : ''} onClick={() => { setSelection({ kind: 'scope', id: candidate.id }); setShowInspector(true) }}><span className="anchor-icon relation"><Layers3 size={15} /></span><span><strong>{candidate.name}</strong><small>Scope · {candidate.type}</small></span></button>)}
            {world.aspects.map(aspect => <button key={aspect.id} className={selection?.kind === 'aspect' && selection.id === aspect.id ? 'active' : ''} onClick={() => { setSelection({ kind: 'aspect', id: aspect.id }); setShowInspector(true) }}><span className="anchor-icon item"><Sparkles size={15} /></span><span><strong>{aspect.name}</strong><small>Aspect · {aspect.type}</small></span></button>)}
            {world.relations.map(relation => <button key={relation.id} className={selection?.kind === 'relation' && selection.id === relation.id ? 'active' : ''} onClick={() => { setSelection({ kind: 'relation', id: relation.id }); setShowInspector(true) }}><span className="anchor-icon relation"><GitBranch size={15} /></span><span><strong>{relation.name}</strong><small>Relation · {relation.type}</small></span></button>)}
            {!world.elements.length && <div className="empty-list">世界尚无 Element。<br />从一次添加开始。</div>}
          </div>
        </aside>

        <section className={`canvas${tooltipsSuppressed ? ' tooltips-suppressed' : ''}`}>
          {loading ? <LoadingState /> : (
            <ReactFlow nodes={flowNodes} edges={edges} nodeTypes={nodeTypes} edgeTypes={edgeTypes} onNodesChange={moveNodes} onNodeDragStart={handleNodeDragStart} onNodeDrag={handleNodeDrag} onNodeDragStop={handleNodeDragStop} fitView fitViewOptions={{ padding: 0.28 }} minZoom={0.25} maxZoom={1.8} nodesDraggable onPaneClick={() => setSelection(null)} onNodeClick={(_, node) => { stopInertia(node.id); setSelection({ kind: 'element', id: node.id }); setShowInspector(true) }}>
              <Background variant={BackgroundVariant.Dots} color="#34413e" gap={24} size={1.15} />
              <Controls showInteractive={false} />
              <MiniMap nodeColor="#4c8d82" maskColor="rgba(10, 14, 15, .76)" pannable zoomable />
            </ReactFlow>
          )}
          {!loading && world.elements.length === 0 && <EmptyCanvas onAdd={() => setDialog('element')} />}
          <div className="legend"><span><i className="item-dot" />Element</span><span><i className="subworld-line" />Scope 内 Relation</span></div>
          {!showInspector && <button className="open-inspector" onClick={() => setShowInspector(true)}><PanelRightClose size={16} />打开检查器</button>}
        </section>

        {showInspector && (
          <aside className="inspector">
            <div className="inspector-top"><span>检查器</span><button aria-label="关闭检查器" onClick={() => setShowInspector(false)}><X size={17} /></button></div>
            {selectedElement ? <ElementInspector element={selectedElement} world={world} working={working} perform={perform} onRemoved={() => setSelection(null)} /> : selectedScope ? <ScopeInspector scope={selectedScope} world={world} working={working} perform={perform} onRemoved={() => setSelection(null)} /> : selectedAspect ? <AspectInspector aspect={selectedAspect} world={world} working={working} perform={perform} onRemoved={() => setSelection(null)} /> : selectedRelation ? <RelationInspector relation={selectedRelation} world={world} working={working} perform={perform} onRemoved={() => setSelection(null)} /> : <InspectorEmpty />}
          </aside>
        )}
      </section>

      {error && <div className="error-toast" role="alert"><span>{error}</span><button aria-label="关闭错误提示" onClick={() => setError(null)}><X size={16} /></button></div>}
      {dialog === 'element' && <ElementDialog stateId={world.stateId} working={working} close={() => setDialog(null)} submit={perform} />}
      {dialog === 'scope' && <ScopeDialog world={world} working={working} close={() => setDialog(null)} submit={perform} />}
      {dialog === 'aspect' && <AspectDialog world={world} working={working} close={() => setDialog(null)} submit={perform} />}
      {dialog === 'relation' && <RelationDialog world={world} working={working} close={() => setDialog(null)} submit={perform} />}
      <GuidancePanel open={showGuidance} world={world} onClose={() => setShowGuidance(false)} onWorldChanged={() => refresh(true)} onError={setError} />
      {showSettings && <SettingsDialog close={closeSettings} onError={setError} />}
      {showStaging && <StagingDialog world={world} working={working} close={() => setShowStaging(false)} perform={perform} />}
      <WritingWorkspace open={showWriting} onClose={() => setShowWriting(false)} />
    </main>
  )
}

function IconButton({ label, disabled, onClick, children }: { label: string; disabled?: boolean; onClick: () => void; children: ReactNode }) {
  return <button className="icon-button" aria-label={label} title={label} disabled={disabled} onClick={onClick}>{children}</button>
}

function StagingDialog({ world, working, close, perform }: { world: WorldGraphViewModel; working: boolean; close: () => void; perform: (operation: () => Promise<unknown>) => Promise<boolean> }) {
  const validChanges = world.stagedChanges.filter(change => change.status === 'Valid')
  const invalidChanges = world.stagedChanges.filter(change => change.status === 'Invalid')

  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={event => { if (event.target === event.currentTarget) close() }}>
      <section className="modal staging-modal" role="dialog" aria-modal="true" aria-labelledby="staging-title">
        <header>
          <div><span className="modal-kicker">STAGING AREA</span><h2 id="staging-title">暂存修改 <span className="count">{world.stagedChanges.length}</span></h2></div>
          <button aria-label="关闭暂存区" onClick={close}><X size={18} /></button>
        </header>
        <div className="staging-list">
          {world.stagedChanges.map(change => (
            <article className={`staging-item ${change.status.toLowerCase()}`} key={change.id}>
              <span className="staging-status">{change.status === 'Valid' ? <Check size={15} /> : <X size={15} />}</span>
              <div><strong>{change.operation}</strong><small>{change.source === 'Guidance' ? 'Guidance' : '玩家'} · {stagingStatusLabel(change.status)}</small>{change.issue && <p>{change.issue}</p>}</div>
              <button aria-label={`删除暂存项 ${change.operation}`} title="删除此项" disabled={working} onClick={() => void perform(() => worldApi.deleteStaged(change.id))}><Trash2 size={15} /></button>
            </article>
          ))}
          {!world.stagedChanges.length && <div className="empty-list">暂存区为空。</div>}
        </div>
        <footer className="staging-actions">
          <button className="secondary-action" disabled={working || !invalidChanges.length} onClick={() => void perform(worldApi.deleteInvalidStaged)}><Trash2 size={15} />清理无效项</button>
          <button className="primary-action" disabled={working || !validChanges.length} onClick={() => void perform(() => worldApi.commitStaged(world.stateId, validChanges.map(change => change.id)))}>{working ? <LoaderCircle className="spin" size={15} /> : <Check size={15} />}提交 {validChanges.length} 项有效修改</button>
        </footer>
      </section>
    </div>
  )
}

function stagingStatusLabel(status: WorldGraphViewModel['stagedChanges'][number]['status']) {
  return { Valid: '有效', Conflict: '冲突', Invalid: '无效' }[status]
}

function SettingsDialog({ close, onError }: { close: () => void; onError: (message: string | null) => void }) {
  const [languageSettings, setLanguageSettings] = useState<LanguageModelSettingsViewModel | null>(null)
  const [openAISettings, setOpenAISettings] = useState<OpenAIConfigurationInput | null>(null)
  const [saving, setSaving] = useState(false)
  const [savedMessage, setSavedMessage] = useState<string | null>(null)

  useEffect(() => {
    let active = true
    Promise.all([settingsApi.getOpenAI(), settingsApi.getLanguageModel()])
      .then(([openAI, language]) => {
        if (active) {
          setOpenAISettings({ ...openAI, apiKey: '' })
          setLanguageSettings(language)
        }
      })
      .catch(requestError => {
        if (active) {
          onError(toMessage(requestError))
          close()
        }
      })
    return () => { active = false }
  }, [close, onError])

  function updateLanguage<K extends keyof LanguageModelSettingsViewModel>(key: K, value: LanguageModelSettingsViewModel[K]) {
    setLanguageSettings(current => current ? { ...current, [key]: value } : current)
    setSavedMessage(null)
  }

  function updateOpenAI<K extends keyof OpenAIConfigurationInput>(key: K, value: OpenAIConfigurationInput[K]) {
    setOpenAISettings(current => current ? { ...current, [key]: value } : current)
    setSavedMessage(null)
  }

  async function save(event: FormEvent) {
    event.preventDefault()
    if (!languageSettings || !openAISettings)
      return
    setSaving(true)
    try {
      const result = await settingsApi.update(languageSettings, openAISettings)
      setOpenAISettings({ ...result.openAI, apiKey: '' })
      setLanguageSettings(result.languageModel)
      setSavedMessage(result.message)
      onError(null)
    } catch (requestError) {
      onError(toMessage(requestError))
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={event => { if (event.target === event.currentTarget) close() }}>
      <section className="modal settings-modal" role="dialog" aria-modal="true" aria-labelledby="settings-title">
        <header>
          <div><span className="modal-kicker">CONFIGURATION</span><h2 id="settings-title">语言模型设置</h2></div>
          <button aria-label="关闭设置" onClick={close}><X size={18} /></button>
        </header>
        {!languageSettings || !openAISettings ? <div className="settings-loading"><LoaderCircle className="spin" size={22} />正在读取配置…</div> : (
          <form className="modal-form" onSubmit={save}>
            <div className="settings-columns">
              <section className="settings-column">
                <div className="settings-section-title"><span>OPENAI</span><strong>连接配置</strong></div>
                <label>Endpoint<input type="url" required value={openAISettings.endpoint} onChange={event => updateOpenAI('endpoint', event.target.value)} /></label>
                <label>模型名称<input required value={openAISettings.model} onChange={event => updateOpenAI('model', event.target.value)} placeholder="例如 gpt-5.1" /></label>
                <label>API Key<input type="password" value={openAISettings.apiKey} onChange={event => updateOpenAI('apiKey', event.target.value)} placeholder={openAISettings.hasApiKey ? '已配置；留空则保留原值' : '请输入 API Key'} autoComplete="new-password" /></label>
                <label>API 类型<select value={openAISettings.clientType} onChange={event => updateOpenAI('clientType', event.target.value as OpenAIClientType)}><option value="Chat">Chat Completions</option><option value="Responses">Responses</option></select></label>
                <label>enable_thinking<select value={openAISettings.enableThinking === null ? 'default' : String(openAISettings.enableThinking)} onChange={event => updateOpenAI('enableThinking', event.target.value === 'default' ? null : event.target.value === 'true')}><option value="default">不发送</option><option value="true">启用</option><option value="false">禁用</option></select></label>
                <label className="settings-check"><input type="checkbox" checked={openAISettings.supportsRequiredToolChoice} onChange={event => updateOpenAI('supportsRequiredToolChoice', event.target.checked)} /><span>支持 tool_choice=required</span></label>
                <p className="settings-security">API Key 仅写入本机 openai.json，后端不会向浏览器回显已有密钥。</p>
              </section>
              <section className="settings-column">
                <div className="settings-section-title"><span>RUNTIME</span><strong>运行策略</strong></div>
                <div className="form-columns">
                  <label>最大工具轮次<input type="number" min="0" required value={languageSettings.maxToolRounds} onChange={event => updateLanguage('maxToolRounds', event.currentTarget.valueAsNumber)} /></label>
                  <label>输出修复次数<input type="number" min="0" required value={languageSettings.maxOutputRepairAttempts} onChange={event => updateLanguage('maxOutputRepairAttempts', event.currentTarget.valueAsNumber)} /></label>
                </div>
                <label>总超时时间（秒）<input type="number" min="1" required value={languageSettings.overallTimeoutSeconds} onChange={event => updateLanguage('overallTimeoutSeconds', event.currentTarget.valueAsNumber)} /></label>
                <PolicySelect label="工具调用" value={languageSettings.toolCalls} onChange={value => updateLanguage('toolCalls', value)} />
                <PolicySelect label="原生 JSON 输出" value={languageSettings.nativeJsonOutput} onChange={value => updateLanguage('nativeJsonOutput', value)} />
                <PolicySelect label="流式输出" value={languageSettings.streaming} onChange={value => updateLanguage('streaming', value)} />
                <p className="settings-note">保存后，新发起的模型运行会立即使用最新配置；当前正在生成的请求仍安全地使用启动时的配置。已有 Guidance Session 无需重建。</p>
              </section>
            </div>
            <div className="modal-actions">
              {savedMessage && <span className="settings-saved" title={savedMessage}><Check size={14} />{savedMessage}</span>}
              <button type="button" onClick={close}>取消</button>
              <button className="primary-action" disabled={saving}>{saving ? <LoaderCircle className="spin" size={15} /> : <Save size={15} />}保存设置</button>
            </div>
          </form>
        )}
      </section>
    </div>
  )
}

function PolicySelect({ label, value, onChange }: { label: string; value: FeaturePolicy; onChange: (value: FeaturePolicy) => void }) {
  return <label>{label}<select value={value} onChange={event => onChange(event.target.value as FeaturePolicy)}><option value="Disabled">禁用</option><option value="Preferred">优先，允许降级</option><option value="Required">必须支持</option></select></label>
}

function LoadingState() {
  return <div className="center-state"><LoaderCircle className="spin" size={26} /><span>正在展开世界图…</span></div>
}

function EmptyCanvas({ onAdd }: { onAdd: () => void }) {
  return <div className="empty-canvas"><div className="empty-orbit"><Network size={28} /></div><h2>这个世界仍是一张白纸</h2><p>先添加 Element，再用 Scope、Aspect 与 Relation 表达断言。</p><button onClick={onAdd}><CirclePlus size={17} />添加第一个 Element</button></div>
}

function InspectorEmpty() {
  return <div className="inspector-empty"><PanelRightClose size={28} /><h3>选择一个世界实体</h3><p>从侧栏或画布选择 Element、Scope、Aspect 或 Relation。</p></div>
}

function ElementInspector({ element, world, working, perform, onRemoved }: { element: ElementViewModel; world: WorldGraphViewModel; working: boolean; perform: (operation: () => Promise<unknown>) => Promise<boolean>; onRemoved: () => void }) {
  const [name, setName] = useState(element.name)
  const [description, setDescription] = useState(element.description)
  const [type, setType] = useState(element.type)
  useEffect(() => { setName(element.name); setDescription(element.description); setType(element.type) }, [element])
  const changed = name !== element.name || description !== element.description || type !== element.type

  async function save(event: FormEvent) {
    event.preventDefault()
    const changes: { name?: string; description?: string; type?: string } = {}
    if (name !== element.name) changes.name = name
    if (description !== element.description) changes.description = description
    if (type !== element.type) changes.type = type
    await perform(() => worldApi.updateElement(element.id, world.stateId, changes))
  }

  async function remove() {
    const impact = world.scopes.filter(scope => scope.ownerElementId === element.id).length + world.aspects.filter(aspect => aspect.elementId === element.id).length + world.relations.filter(relation => relation.sourceElementId === element.id || relation.targetElementId === element.id).length
    if (!window.confirm(`确定删除 Element“${element.name}”吗？这将级联删除 ${impact} 个直接依赖实体，并可通过撤销恢复。`))
      return
    if (await perform(() => worldApi.removeElement(element.id, world.stateId)))
      onRemoved()
  }

  return (
    <form className="property-form" onSubmit={save}>
      <div className="entity-chip item"><Network size={15} />Element</div>
      <label>名称<input required value={name} onChange={event => setName(event.target.value)} /></label>
      <label>描述<textarea rows={7} value={description} onChange={event => setDescription(event.target.value)} /></label>
      <label>ElementType<input required value={type} onChange={event => setType(event.target.value)} placeholder="namespace:name" /></label>
      <div className="form-spacer" />
      <button className="primary-action" disabled={!changed || working}><Check size={16} />应用修改</button>
      <button className="danger-action" type="button" disabled={working} onClick={() => void remove()}><Trash2 size={16} />删除 Element</button>
      <small className="entity-id">ID · {element.id}</small>
    </form>
  )
}

function ScopeInspector({ scope, world, working, perform, onRemoved }: { scope: ScopeViewModel; world: WorldGraphViewModel; working: boolean; perform: (operation: () => Promise<unknown>) => Promise<boolean>; onRemoved: () => void }) {
  const [name, setName] = useState(scope.name)
  const [description, setDescription] = useState(scope.description)
  const [quantity, setQuantity] = useState(scope.quantity)
  const [type, setType] = useState(scope.type)
  useEffect(() => { setName(scope.name); setDescription(scope.description); setQuantity(scope.quantity); setType(scope.type) }, [scope])
  const changed = name !== scope.name || description !== scope.description || quantity !== scope.quantity || type !== scope.type
  async function save(event: FormEvent) {
    event.preventDefault()
    const changes: { name?: string; description?: string; quantity?: number; type?: string } = {}
    if (name !== scope.name) changes.name = name
    if (description !== scope.description) changes.description = description
    if (quantity !== scope.quantity) changes.quantity = quantity
    if (type !== scope.type) changes.type = type
    await perform(() => worldApi.updateScope(scope.id, world.stateId, changes))
  }
  async function remove() {
    const impact = world.aspects.filter(aspect => aspect.scopeId === scope.id).length + world.relations.filter(relation => relation.scopeId === scope.id).length
    if (!window.confirm(`确定删除 Scope“${scope.name}”及其中 ${impact} 项断言吗？`))
      return
    if (await perform(() => worldApi.removeScope(scope.id, world.stateId)))
      onRemoved()
  }
  return <form className="property-form" onSubmit={save}><div className="entity-chip relation"><Layers3 size={15} />Scope</div><label>名称<input required value={name} onChange={event => setName(event.target.value)} /></label><label>描述<textarea rows={5} value={description} onChange={event => setDescription(event.target.value)} /></label><label>Quantity<input required type="number" step="any" value={quantity} onChange={event => setQuantity(event.currentTarget.valueAsNumber)} /></label><label>ScopeType<input required value={type} onChange={event => setType(event.target.value)} placeholder="namespace:name" /></label><p className="field-hint">Owner：{world.elements.find(element => element.id === scope.ownerElementId)?.name ?? scope.ownerElementId}。Owner 是结构字段，需要更改时请删除后重建。</p><div className="form-spacer" /><button className="primary-action" disabled={!changed || working || !Number.isFinite(quantity)}><Check size={16} />应用修改</button><button className="danger-action" type="button" disabled={working} onClick={() => void remove()}><Trash2 size={16} />删除 Scope</button><small className="entity-id">ID · {scope.id}</small></form>
}

function AspectInspector({ aspect, world, working, perform, onRemoved }: { aspect: AspectViewModel; world: WorldGraphViewModel; working: boolean; perform: (operation: () => Promise<unknown>) => Promise<boolean>; onRemoved: () => void }) {
  const [name, setName] = useState(aspect.name)
  const [description, setDescription] = useState(aspect.description)
  const [quantity, setQuantity] = useState(aspect.quantity)
  const [type, setType] = useState(aspect.type)
  useEffect(() => { setName(aspect.name); setDescription(aspect.description); setQuantity(aspect.quantity); setType(aspect.type) }, [aspect])
  const changed = name !== aspect.name || description !== aspect.description || quantity !== aspect.quantity || type !== aspect.type
  async function save(event: FormEvent) {
    event.preventDefault()
    const changes: { name?: string; description?: string; quantity?: number; type?: string } = {}
    if (name !== aspect.name) changes.name = name
    if (description !== aspect.description) changes.description = description
    if (quantity !== aspect.quantity) changes.quantity = quantity
    if (type !== aspect.type) changes.type = type
    await perform(() => worldApi.updateAspect(aspect.id, world.stateId, changes))
  }
  async function remove() {
    if (window.confirm(`确定删除 Aspect“${aspect.name}”吗？`) && await perform(() => worldApi.removeAspect(aspect.id, world.stateId)))
      onRemoved()
  }
  return <form className="property-form" onSubmit={save}><div className="entity-chip item"><Sparkles size={15} />Aspect</div><label>名称<input required value={name} onChange={event => setName(event.target.value)} /></label><label>描述<textarea rows={5} value={description} onChange={event => setDescription(event.target.value)} /></label><label>Quantity<input required type="number" step="any" value={quantity} onChange={event => setQuantity(event.currentTarget.valueAsNumber)} /></label><label>AspectType<input required value={type} onChange={event => setType(event.target.value)} placeholder="namespace:name" /></label><p className="field-hint">Element：{world.elements.find(element => element.id === aspect.elementId)?.name ?? aspect.elementId}<br />Scope：{world.scopes.find(scope => scope.id === aspect.scopeId)?.name ?? aspect.scopeId}<br />引用是结构字段，需要更改时请删除后重建。</p><div className="form-spacer" /><button className="primary-action" disabled={!changed || working || !Number.isFinite(quantity)}><Check size={16} />应用修改</button><button className="danger-action" type="button" disabled={working} onClick={() => void remove()}><Trash2 size={16} />删除 Aspect</button><small className="entity-id">ID · {aspect.id}</small></form>
}

function RelationInspector({ relation, world, working, perform, onRemoved }: { relation: RelationViewModel; world: WorldGraphViewModel; working: boolean; perform: (operation: () => Promise<unknown>) => Promise<boolean>; onRemoved: () => void }) {
  const [name, setName] = useState(relation.name)
  const [description, setDescription] = useState(relation.description)
  const [quantity, setQuantity] = useState(relation.quantity)
  const [type, setType] = useState(relation.type)
  useEffect(() => { setName(relation.name); setDescription(relation.description); setQuantity(relation.quantity); setType(relation.type) }, [relation])
  const source = world.elements.find(element => element.id === relation.sourceElementId)
  const target = world.elements.find(element => element.id === relation.targetElementId)
  const relationScope = world.scopes.find(scope => scope.id === relation.scopeId)

  async function save(event: FormEvent) {
    event.preventDefault()
    const changes: { name?: string; description?: string; quantity?: number; type?: string } = {}
    if (name !== relation.name) changes.name = name
    if (description !== relation.description) changes.description = description
    if (quantity !== relation.quantity) changes.quantity = quantity
    if (type !== relation.type) changes.type = type
    await perform(() => worldApi.updateRelation(relation.id, world.stateId, changes))
  }

  async function remove() {
    if (!window.confirm(`确定删除关系“${relation.name}”吗？此操作可通过撤销恢复。`))
      return
    if (await perform(() => worldApi.removeRelation(relation.id, world.stateId)))
      onRemoved()
  }

  return (
    <form className="property-form" onSubmit={save}>
      <div className="entity-chip relation"><GitBranch size={15} />Relation</div>
      <label>名称<input required value={name} onChange={event => setName(event.target.value)} /></label>
      <label>描述<textarea rows={5} value={description} onChange={event => setDescription(event.target.value)} /></label>
      <label>Quantity<input required type="number" step="any" value={quantity} onChange={event => setQuantity(event.currentTarget.valueAsNumber)} /></label>
      <label>RelationType<input required value={type} onChange={event => setType(event.target.value)} placeholder="namespace:name" /></label>
      <div className="relation-route"><span>{source?.name ?? '未知'}</span><GitBranch size={15} /><span>{target?.name ?? '未知'}</span></div>
      <p className="field-hint">Scope：{relationScope?.name ?? relation.scopeId}。端点和 Scope 属于结构信息，需要更改时请删除后重建。</p>
      <div className="form-spacer" />
      <button className="primary-action" disabled={(name === relation.name && description === relation.description && quantity === relation.quantity && type === relation.type) || working || !Number.isFinite(quantity)}><Check size={16} />应用修改</button>
      <button className="danger-action" type="button" disabled={working} onClick={() => void remove()}><Trash2 size={16} />删除关系</button>
      <small className="entity-id">ID · {relation.id}</small>
    </form>
  )
}

function ElementDialog({ stateId, working, close, submit }: { stateId: string; working: boolean; close: () => void; submit: (operation: () => Promise<unknown>) => Promise<boolean> }) {
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [type, setType] = useState('core:none')
  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    if (await submit(() => worldApi.addElement(stateId, name, description, type)))
      close()
  }
  return <Modal title="添加 Element" close={close}><form className="modal-form" onSubmit={handleSubmit}><label>名称<input autoFocus required value={name} onChange={event => setName(event.target.value)} /></label><label>ElementType<input required value={type} onChange={event => setType(event.target.value)} placeholder="namespace:name" /></label><label>描述<textarea rows={5} value={description} onChange={event => setDescription(event.target.value)} /></label><div className="modal-actions"><button type="button" onClick={close}>取消</button><button className="primary-action" disabled={working}><CirclePlus size={16} />添加 Element</button></div></form></Modal>
}

function ScopeDialog({ world, working, close, submit }: { world: WorldGraphViewModel; working: boolean; close: () => void; submit: (operation: () => Promise<unknown>) => Promise<boolean> }) {
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [quantity, setQuantity] = useState(1)
  const [type, setType] = useState('core:none')
  const [ownerElementId, setOwnerElementId] = useState(world.elements[0]?.id ?? '')
  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    if (await submit(() => worldApi.addScope(world.stateId, name, description, quantity, type, ownerElementId)))
      close()
  }
  return <Modal title="添加 Scope" close={close}><form className="modal-form" onSubmit={handleSubmit}><label>名称<input autoFocus required value={name} onChange={event => setName(event.target.value)} /></label><label>Owner Element<select required value={ownerElementId} onChange={event => setOwnerElementId(event.target.value)}>{world.elements.map(element => <option key={element.id} value={element.id}>{element.name}</option>)}</select></label><div className="form-columns"><label>Quantity<input required type="number" step="any" value={quantity} onChange={event => setQuantity(event.currentTarget.valueAsNumber)} /></label><label>ScopeType<input required value={type} onChange={event => setType(event.target.value)} /></label></div><label>描述<textarea rows={4} value={description} onChange={event => setDescription(event.target.value)} /></label><div className="modal-actions"><button type="button" onClick={close}>取消</button><button className="primary-action" disabled={working || !ownerElementId || !Number.isFinite(quantity)}><Layers3 size={16} />添加 Scope</button></div></form></Modal>
}

function AspectDialog({ world, working, close, submit }: { world: WorldGraphViewModel; working: boolean; close: () => void; submit: (operation: () => Promise<unknown>) => Promise<boolean> }) {
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [quantity, setQuantity] = useState(1)
  const [type, setType] = useState('core:none')
  const [elementId, setElementId] = useState(world.elements[0]?.id ?? '')
  const [scopeId, setScopeId] = useState(world.scopes[0]?.id ?? '')
  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    if (await submit(() => worldApi.addAspect(world.stateId, name, description, quantity, type, elementId, scopeId)))
      close()
  }
  return <Modal title="添加 Aspect" close={close}><form className="modal-form" onSubmit={handleSubmit}><label>名称<input autoFocus required value={name} onChange={event => setName(event.target.value)} /></label><div className="form-columns"><label>Element<select required value={elementId} onChange={event => setElementId(event.target.value)}>{world.elements.map(element => <option key={element.id} value={element.id}>{element.name}</option>)}</select></label><label>Scope<select required value={scopeId} onChange={event => setScopeId(event.target.value)}>{world.scopes.map(scope => <option key={scope.id} value={scope.id}>{scope.name}</option>)}</select></label></div><div className="form-columns"><label>Quantity<input required type="number" step="any" value={quantity} onChange={event => setQuantity(event.currentTarget.valueAsNumber)} /></label><label>AspectType<input required value={type} onChange={event => setType(event.target.value)} /></label></div><label>描述<textarea rows={4} value={description} onChange={event => setDescription(event.target.value)} /></label><div className="modal-actions"><button type="button" onClick={close}>取消</button><button className="primary-action" disabled={working || !elementId || !scopeId || !Number.isFinite(quantity)}><Sparkles size={16} />添加 Aspect</button></div></form></Modal>
}

function RelationDialog({ world, working, close, submit }: { world: WorldGraphViewModel; working: boolean; close: () => void; submit: (operation: () => Promise<unknown>) => Promise<boolean> }) {
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [quantity, setQuantity] = useState(1)
  const [type, setType] = useState('core:none')
  const [sourceElementId, setSourceElementId] = useState(world.elements[0]?.id ?? '')
  const [targetElementId, setTargetElementId] = useState(world.elements[1]?.id ?? world.elements[0]?.id ?? '')
  const [scopeId, setScopeId] = useState(world.scopes[0]?.id ?? '')
  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    if (await submit(() => worldApi.addRelation(world.stateId, name, description, quantity, type, sourceElementId, targetElementId, scopeId)))
      close()
  }
  return <Modal title="添加 Relation" close={close}><form className="modal-form" onSubmit={handleSubmit}><label>名称<input autoFocus required value={name} onChange={event => setName(event.target.value)} /></label><div className="form-columns"><label>Source Element<select value={sourceElementId} onChange={event => setSourceElementId(event.target.value)}>{world.elements.map(element => <option key={element.id} value={element.id}>{element.name}</option>)}</select></label><label>Target Element<select value={targetElementId} onChange={event => setTargetElementId(event.target.value)}>{world.elements.map(element => <option key={element.id} value={element.id}>{element.name}</option>)}</select></label></div><label>Scope<select value={scopeId} onChange={event => setScopeId(event.target.value)}>{world.scopes.map(scope => <option key={scope.id} value={scope.id}>{scope.name}</option>)}</select></label><div className="form-columns"><label>Quantity<input required type="number" step="any" value={quantity} onChange={event => setQuantity(event.currentTarget.valueAsNumber)} /></label><label>RelationType<input required value={type} onChange={event => setType(event.target.value)} /></label></div><label>描述<textarea rows={4} value={description} onChange={event => setDescription(event.target.value)} /></label><div className="modal-actions"><button type="button" onClick={close}>取消</button><button className="primary-action" disabled={working || !sourceElementId || !targetElementId || !scopeId || !Number.isFinite(quantity)}><GitBranch size={16} />添加 Relation</button></div></form></Modal>
}

function Modal({ title, close, children }: { title: string; close: () => void; children: ReactNode }) {
  return <div className="modal-backdrop" role="presentation" onMouseDown={event => event.target === event.currentTarget && close()}><section className="modal" role="dialog" aria-modal="true" aria-label={title}><header><div><span className="modal-kicker">WORLD GRAPH</span><h2>{title}</h2></div><button aria-label="关闭" onClick={close}><X size={18} /></button></header>{children}</section></div>
}

function toMessage(error: unknown) {
  return error instanceof Error ? error.message : '操作失败，请稍后重试。'
}

function parseWorldEvent(value: string) {
  try {
    const event = JSON.parse(value) as { stateId?: string; isDirty?: boolean; error?: string | null; StateId?: string; IsDirty?: boolean; Error?: string | null }
    const stateId = event.stateId ?? event.StateId
    const isDirty = event.isDirty ?? event.IsDirty
    if (stateId === undefined || isDirty === undefined)
      return null
    return { stateId, isDirty, error: event.error ?? event.Error ?? null }
  } catch {
    return null
  }
}

export default App
