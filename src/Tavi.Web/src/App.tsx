import { useCallback, useEffect, useMemo, useRef, useState, type FormEvent, type ReactNode } from 'react'
import { Background, BackgroundVariant, Controls, MarkerType, MiniMap, ReactFlow, useNodesState, type Edge, type Node, type NodeChange, type NodePositionChange } from '@xyflow/react'
import { Box, Check, ChevronDown, CirclePlus, Cloud, CloudOff, GitBranch, LoaderCircle, Network, Package, PanelRightClose, Redo2, Save, Search, Settings, Sparkles, Trash2, Undo2, UserRound, X } from 'lucide-react'
import { ApiError, settingsApi, worldApi } from './api'
import { GuidancePanel } from './GuidancePanel'
import type { AnchorType, AnchorViewModel, FeaturePolicy, LanguageModelSettingsViewModel, OpenAIConfigurationInput, OpenAIClientType, RelationViewModel, Selection, WorldGraphViewModel } from './types'

type ScopeFilter = 'all' | 'world' | string
type Dialog = 'anchor' | 'relation' | null

const emptyWorld: WorldGraphViewModel = { worldId: '', revision: 0, stagingRevision: 0, isDirty: false, canUndo: false, canRedo: false, health: 'Healthy', nodes: [], edges: [], subWorlds: [], stagedChanges: [] }

function layoutPosition(index: number, total: number) {
  const columns = Math.max(1, Math.ceil(Math.sqrt(total)))
  return { x: (index % columns) * 250, y: Math.floor(index / columns) * 170 }
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
  const [flowNodes, setFlowNodes, applyNodeChanges] = useNodesState<Node>([])
  const refreshSequence = useRef(0)
  const lastRefreshAt = useRef(0)
  const refreshTimer = useRef<number | null>(null)
  const positionCache = useRef(new Map<string, { x: number; y: number }>())

  const refresh = useCallback(async (quiet = false) => {
    const sequence = ++refreshSequence.current
    if (!quiet)
      setLoading(true)
    try {
      const nextWorld = await worldApi.get()
      if (sequence !== refreshSequence.current)
        return
      setWorld(current => {
        if (nextWorld.revision < current.revision)
          return current
        if (current.worldId === nextWorld.worldId && nextWorld.revision === current.revision && !current.isDirty && nextWorld.isDirty)
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
      setWorld(current => state.revision < current.revision ? current : { ...current, revision: state.revision, isDirty: state.isDirty })
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

  const visibleRelations = useMemo(() => world.edges.filter(edge => scope === 'all' || scope === 'world' ? scope === 'all' || edge.scope === 'World' : edge.domainCharacterId === scope), [scope, world.edges])
  const connectedIds = useMemo(() => new Set(visibleRelations.flatMap(edge => [edge.sourceId, edge.targetId])), [visibleRelations])
  const visibleAnchors = useMemo(() => {
    const query = search.trim().toLocaleLowerCase()
    return world.nodes.filter(anchor => {
      const matchesSearch = !query || `${anchor.name}\n${anchor.description}\n${anchor.type}`.toLocaleLowerCase().includes(query)
      const matchesScope = scope === 'all' || scope === 'world' || anchor.id === scope || connectedIds.has(anchor.id)
      return matchesSearch && matchesScope
    })
  }, [connectedIds, scope, search, world.nodes])
  const visibleAnchorIds = useMemo(() => new Set(visibleAnchors.map(anchor => anchor.id)), [visibleAnchors])

  useEffect(() => {
    setFlowNodes(current => {
      const existing = new Map(current.map(node => [node.id, node]))
      return visibleAnchors.map((anchor, index) => {
        const previous = existing.get(anchor.id)
        return {
          ...previous,
          id: anchor.id,
          position: previous?.position ?? positionCache.current.get(anchor.id) ?? layoutPosition(index, visibleAnchors.length),
          data: { label: <NodeLabel anchor={anchor} /> },
          className: `world-node ${anchor.type.toLowerCase()}${selection?.kind === 'anchor' && selection.id === anchor.id ? ' selected' : ''}`,
          style: { width: 190 },
        }
      })
    })
  }, [selection, setFlowNodes, visibleAnchors])

  const edges: Edge[] = useMemo(() => visibleRelations.filter(edge => visibleAnchorIds.has(edge.sourceId) && visibleAnchorIds.has(edge.targetId)).map(edge => ({
    id: edge.id,
    source: edge.sourceId,
    target: edge.targetId,
    label: edge.name,
    type: 'smoothstep',
    animated: selection?.kind === 'relation' && selection.id === edge.id,
    markerEnd: { type: MarkerType.ArrowClosed, color: edge.scope === 'World' ? '#99a9a3' : '#d4a85a' },
    className: edge.scope === 'World' ? 'world-edge' : 'subworld-edge',
    style: { stroke: edge.scope === 'World' ? '#6f817b' : '#bd8c3e', strokeWidth: 1.6, strokeDasharray: edge.scope === 'World' ? undefined : '7 5' },
    labelStyle: { fill: '#d5ddd9', fontSize: 12, fontWeight: 600 },
    labelBgStyle: { fill: '#171d1e', fillOpacity: 0.9 },
  })), [selection, visibleAnchorIds, visibleRelations])

  const selectedAnchor = selection?.kind === 'anchor' ? world.nodes.find(anchor => anchor.id === selection.id) ?? null : null
  const selectedRelation = selection?.kind === 'relation' ? world.edges.find(edge => edge.id === selection.id) ?? null : null

  useEffect(() => {
    if (selection?.kind === 'anchor' && !world.nodes.some(anchor => anchor.id === selection.id))
      setSelection(null)
    if (selection?.kind === 'relation' && !world.edges.some(relation => relation.id === selection.id))
      setSelection(null)
  }, [selection, world.edges, world.nodes])

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
              <option value="all">全部世界</option>
              <option value="world">事实世界</option>
              {world.nodes.filter(anchor => anchor.type === 'Character' && anchor.hasSubWorld).map(anchor => <option key={anchor.id} value={anchor.id}>{anchor.name} · 认知世界</option>)}
            </select>
            <ChevronDown size={14} />
          </label>
          <div className="toolbar-divider" />
          <button className={`guidance-toggle${showGuidance ? ' active' : ''}`} onClick={() => setShowGuidance(current => !current)}><Sparkles size={16} />Guidance</button>
          <IconButton label="设置" onClick={() => setShowSettings(true)}><Settings size={17} /></IconButton>
          <div className="toolbar-divider" />
          <IconButton label="撤销" disabled={!world.canUndo || working} onClick={() => void perform(() => worldApi.undo(world.revision))}><Undo2 size={17} /></IconButton>
          <IconButton label="重做" disabled={!world.canRedo || working} onClick={() => void perform(() => worldApi.redo(world.revision))}><Redo2 size={17} /></IconButton>
          <button className="save-button" disabled={working} onClick={() => void perform(worldApi.save)}>
            {working ? <LoaderCircle className="spin" size={15} /> : world.isDirty ? <CloudOff size={15} /> : <Cloud size={15} />}
            {statusLabel}
          </button>
          <span className="revision">rev {world.revision}</span>
        </div>
      </header>

      <section className={`workspace${showInspector ? '' : ' inspector-hidden'}`}>
        <aside className="sidebar">
          <div className="sidebar-heading"><span>世界要素</span><span className="count">{world.nodes.length}</span></div>
          <label className="search-box"><Search size={16} /><input value={search} onChange={event => setSearch(event.target.value)} placeholder="搜索名称或描述" /></label>
          <div className="sidebar-actions">
            <button onClick={() => setDialog('anchor')}><CirclePlus size={16} />添加要素</button>
            <button onClick={() => setDialog('relation')} disabled={world.nodes.length < 2}><GitBranch size={16} />添加关系</button>
          </div>
          <div className="anchor-list">
            {world.nodes.map(anchor => (
              <button key={anchor.id} className={selection?.kind === 'anchor' && selection.id === anchor.id ? 'active' : ''} onClick={() => { setSelection({ kind: 'anchor', id: anchor.id }); setShowInspector(true) }}>
                <span className={`anchor-icon ${anchor.type.toLowerCase()}`}>{anchor.type === 'Character' ? <UserRound size={15} /> : <Package size={15} />}</span>
                <span><strong>{anchor.name}</strong><small>{anchor.type === 'Character' ? '角色' : '物品'}{anchor.hasSubWorld ? ' · 有认知世界' : ''}</small></span>
              </button>
            ))}
            {!world.nodes.length && <div className="empty-list">世界尚无要素。<br />从一次添加开始。</div>}
          </div>
          <div className="sidebar-heading"><span>暂存修改</span><span className="count">{world.stagedChanges.length}</span></div>
          <div className="anchor-list">
            {world.stagedChanges.map(change => <button key={change.id} title={change.issue ?? change.operation} onClick={() => void perform(() => worldApi.deleteStaged(change.id))}><span className={`anchor-icon ${change.status === 'Valid' ? 'item' : 'character'}`}>{change.status === 'Valid' ? <Check size={14} /> : <X size={14} />}</span><span><strong>{change.operation}</strong><small>{change.source} · {change.status}{change.issue ? ` · ${change.issue}` : ''}</small></span></button>)}
            {!world.stagedChanges.length && <div className="empty-list">暂存区为空。</div>}
          </div>
          <div className="sidebar-actions">
            <button disabled={working || !world.stagedChanges.some(change => change.status === 'Valid')} onClick={() => void perform(() => worldApi.commitStaged(world.revision, world.stagedChanges.filter(change => change.status === 'Valid').map(change => change.id)))}><Check size={16} />提交有效项</button>
            <button disabled={working || !world.stagedChanges.some(change => change.status === 'Invalid')} onClick={() => void perform(worldApi.deleteInvalidStaged)}><Trash2 size={16} />清理无效项</button>
          </div>
        </aside>

        <section className="canvas">
          {loading ? <LoadingState /> : (
            <ReactFlow nodes={flowNodes} edges={edges} onNodesChange={moveNodes} fitView fitViewOptions={{ padding: 0.28 }} minZoom={0.25} maxZoom={1.8} nodesDraggable onPaneClick={() => setSelection(null)} onNodeClick={(_, node) => { setSelection({ kind: 'anchor', id: node.id }); setShowInspector(true) }} onEdgeClick={(_, edge) => { setSelection({ kind: 'relation', id: edge.id }); setShowInspector(true) }}>
              <Background variant={BackgroundVariant.Dots} color="#34413e" gap={24} size={1.15} />
              <Controls showInteractive={false} />
              <MiniMap nodeColor={node => node.className?.toString().includes('character') ? '#b77d55' : '#4c8d82'} maskColor="rgba(10, 14, 15, .76)" pannable zoomable />
            </ReactFlow>
          )}
          {!loading && world.nodes.length === 0 && <EmptyCanvas onAdd={() => setDialog('anchor')} />}
          <div className="legend"><span><i className="character-dot" />角色</span><span><i className="item-dot" />物品</span><span><i className="subworld-line" />认知关系</span></div>
          {!showInspector && <button className="open-inspector" onClick={() => setShowInspector(true)}><PanelRightClose size={16} />打开检查器</button>}
        </section>

        {showInspector && (
          <aside className="inspector">
            <div className="inspector-top"><span>检查器</span><button aria-label="关闭检查器" onClick={() => setShowInspector(false)}><X size={17} /></button></div>
            {selectedAnchor ? <AnchorInspector anchor={selectedAnchor} world={world} working={working} perform={perform} onRemoved={() => setSelection(null)} /> : selectedRelation ? <RelationInspector relation={selectedRelation} world={world} working={working} perform={perform} onRemoved={() => setSelection(null)} /> : <InspectorEmpty />}
          </aside>
        )}
      </section>

      {error && <div className="error-toast" role="alert"><span>{error}</span><button aria-label="关闭错误提示" onClick={() => setError(null)}><X size={16} /></button></div>}
      {dialog === 'anchor' && <AnchorDialog revision={world.revision} working={working} close={() => setDialog(null)} submit={perform} />}
      {dialog === 'relation' && <RelationDialog world={world} working={working} close={() => setDialog(null)} submit={perform} />}
      <GuidancePanel open={showGuidance} world={world} onClose={() => setShowGuidance(false)} onWorldChanged={() => refresh(true)} onError={setError} />
      {showSettings && <SettingsDialog close={closeSettings} onError={setError} />}
    </main>
  )
}

function NodeLabel({ anchor }: { anchor: AnchorViewModel }) {
  return <div className="node-label"><span className="node-kind">{anchor.type === 'Character' ? <UserRound size={15} /> : <Box size={15} />}</span><span><strong>{anchor.name}</strong><small>{anchor.description || '暂无描述'}</small></span>{anchor.hasSubWorld && <span className="subworld-badge" title="拥有认知世界"><GitBranch size={12} /></span>}</div>
}

function IconButton({ label, disabled, onClick, children }: { label: string; disabled?: boolean; onClick: () => void; children: ReactNode }) {
  return <button className="icon-button" aria-label={label} title={label} disabled={disabled} onClick={onClick}>{children}</button>
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
  return <div className="empty-canvas"><div className="empty-orbit"><Network size={28} /></div><h2>这个世界仍是一张白纸</h2><p>添加角色或物品，然后用关系将叙事线索连接起来。</p><button onClick={onAdd}><CirclePlus size={17} />添加第一个要素</button></div>
}

function InspectorEmpty() {
  return <div className="inspector-empty"><PanelRightClose size={28} /><h3>选择一个世界要素</h3><p>点击画布中的节点或关系，在此查看和修改它的属性。</p></div>
}

function AnchorInspector({ anchor, world, working, perform, onRemoved }: { anchor: AnchorViewModel; world: WorldGraphViewModel; working: boolean; perform: (operation: () => Promise<unknown>) => Promise<boolean>; onRemoved: () => void }) {
  const [name, setName] = useState(anchor.name)
  const [description, setDescription] = useState(anchor.description)
  const [type, setType] = useState<AnchorType>(anchor.type)
  useEffect(() => { setName(anchor.name); setDescription(anchor.description); setType(anchor.type) }, [anchor])
  const changed = name !== anchor.name || description !== anchor.description || type !== anchor.type

  async function save(event: FormEvent) {
    event.preventDefault()
    const changes: { name?: string; description?: string; type?: string } = {}
    if (name !== anchor.name) changes.name = name
    if (description !== anchor.description) changes.description = description
    if (type !== anchor.type) changes.type = type
    await perform(() => worldApi.updateAnchor(anchor.id, world.revision, changes))
  }

  async function remove() {
    const impact = world.edges.filter(edge => edge.sourceId === anchor.id || edge.targetId === anchor.id).length
    const detail = impact ? `，并级联删除 ${impact} 条相连关系` : ''
    if (!window.confirm(`确定删除“${anchor.name}”${detail}吗？此操作可通过撤销恢复。`))
      return
    if (await perform(() => worldApi.removeAnchor(anchor.id, world.revision)))
      onRemoved()
  }

  async function toggleSubWorld() {
    const action = anchor.hasSubWorld ? () => worldApi.removeSubWorld(world.revision, anchor.id) : () => worldApi.createSubWorld(world.revision, anchor.id)
    await perform(action)
  }

  return (
    <form className="property-form" onSubmit={save}>
      <div className={`entity-chip ${anchor.type.toLowerCase()}`}>{anchor.type === 'Character' ? <UserRound size={15} /> : <Package size={15} />}{anchor.type === 'Character' ? '角色' : '物品'}</div>
      <label>名称<input required value={name} onChange={event => setName(event.target.value)} /></label>
      <label>描述<textarea rows={7} value={description} onChange={event => setDescription(event.target.value)} placeholder="记录这个要素在世界中的意义…" /></label>
      <label>类型<select value={type} onChange={event => setType(event.target.value as AnchorType)}><option value="Character">角色</option><option value="Item" disabled={anchor.hasSubWorld}>物品</option></select></label>
      {anchor.hasSubWorld && <p className="field-hint">删除该角色的认知世界后，才可以将其改为物品。</p>}
      {anchor.type === 'Character' && <button className="secondary-action" type="button" disabled={working} onClick={() => void toggleSubWorld()}><GitBranch size={16} />{anchor.hasSubWorld ? '删除认知世界' : '创建认知世界'}</button>}
      <div className="form-spacer" />
      <button className="primary-action" disabled={!changed || working}><Check size={16} />应用修改</button>
      <button className="danger-action" type="button" disabled={working} onClick={() => void remove()}><Trash2 size={16} />删除要素</button>
      <small className="entity-id">ID · {anchor.id}</small>
    </form>
  )
}

function RelationInspector({ relation, world, working, perform, onRemoved }: { relation: RelationViewModel; world: WorldGraphViewModel; working: boolean; perform: (operation: () => Promise<unknown>) => Promise<boolean>; onRemoved: () => void }) {
  const [name, setName] = useState(relation.name)
  const [description, setDescription] = useState(relation.description)
  useEffect(() => { setName(relation.name); setDescription(relation.description) }, [relation])
  const source = world.nodes.find(node => node.id === relation.sourceId)
  const target = world.nodes.find(node => node.id === relation.targetId)
  const domain = world.nodes.find(node => node.id === relation.domainCharacterId)

  async function save(event: FormEvent) {
    event.preventDefault()
    const changes: { name?: string; description?: string } = {}
    if (name !== relation.name) changes.name = name
    if (description !== relation.description) changes.description = description
    await perform(() => worldApi.updateRelation(relation.id, world.revision, changes))
  }

  async function remove() {
    if (!window.confirm(`确定删除关系“${relation.name}”吗？此操作可通过撤销恢复。`))
      return
    if (await perform(() => worldApi.removeRelation(relation.id, world.revision)))
      onRemoved()
  }

  return (
    <form className="property-form" onSubmit={save}>
      <div className="entity-chip relation"><GitBranch size={15} />{relation.scope === 'World' ? '事实关系' : `${domain?.name ?? '角色'}的认知关系`}</div>
      <label>名称<input required value={name} onChange={event => setName(event.target.value)} /></label>
      <label>描述<textarea rows={7} value={description} onChange={event => setDescription(event.target.value)} /></label>
      <div className="relation-route"><span>{source?.name ?? '未知'}</span><GitBranch size={15} /><span>{target?.name ?? '未知'}</span></div>
      <p className="field-hint">关系端点和所属世界属于结构信息。需要更改时，请删除后重新创建。</p>
      <div className="form-spacer" />
      <button className="primary-action" disabled={(name === relation.name && description === relation.description) || working}><Check size={16} />应用修改</button>
      <button className="danger-action" type="button" disabled={working} onClick={() => void remove()}><Trash2 size={16} />删除关系</button>
      <small className="entity-id">ID · {relation.id}</small>
    </form>
  )
}

function AnchorDialog({ revision, working, close, submit }: { revision: number; working: boolean; close: () => void; submit: (operation: () => Promise<unknown>) => Promise<boolean> }) {
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [type, setType] = useState<AnchorType>('Character')
  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    if (await submit(() => worldApi.addAnchor(revision, name, description, type)))
      close()
  }
  return <Modal title="添加世界要素" close={close}><form className="modal-form" onSubmit={handleSubmit}><label>名称<input autoFocus required value={name} onChange={event => setName(event.target.value)} placeholder="例如：旅行者" /></label><label>类型<select value={type} onChange={event => setType(event.target.value as AnchorType)}><option value="Character">角色</option><option value="Item">物品</option></select></label><label>描述<textarea rows={5} value={description} onChange={event => setDescription(event.target.value)} placeholder="简要描述它在世界中的意义…" /></label><div className="modal-actions"><button type="button" onClick={close}>取消</button><button className="primary-action" disabled={working}><CirclePlus size={16} />添加要素</button></div></form></Modal>
}

function RelationDialog({ world, working, close, submit }: { world: WorldGraphViewModel; working: boolean; close: () => void; submit: (operation: () => Promise<unknown>) => Promise<boolean> }) {
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [sourceId, setSourceId] = useState(world.nodes[0]?.id ?? '')
  const [targetId, setTargetId] = useState(world.nodes[1]?.id ?? world.nodes[0]?.id ?? '')
  const [domainId, setDomainId] = useState('')
  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    if (await submit(() => worldApi.addRelation(world.revision, name, description, sourceId, targetId, domainId || null)))
      close()
  }
  return <Modal title="添加世界关系" close={close}><form className="modal-form" onSubmit={handleSubmit}><label>关系名称<input autoFocus required value={name} onChange={event => setName(event.target.value)} placeholder="例如：守护" /></label><div className="form-columns"><label>起点<select value={sourceId} onChange={event => setSourceId(event.target.value)}>{world.nodes.map(anchor => <option key={anchor.id} value={anchor.id}>{anchor.name}</option>)}</select></label><label>终点<select value={targetId} onChange={event => setTargetId(event.target.value)}>{world.nodes.map(anchor => <option key={anchor.id} value={anchor.id}>{anchor.name}</option>)}</select></label></div><label>所属世界<select value={domainId} onChange={event => setDomainId(event.target.value)}><option value="">事实世界</option>{world.nodes.filter(anchor => anchor.type === 'Character' && anchor.hasSubWorld).map(anchor => <option key={anchor.id} value={anchor.id}>{anchor.name}的认知世界</option>)}</select></label><label>描述<textarea rows={4} value={description} onChange={event => setDescription(event.target.value)} placeholder="描述关系成立的方式或缘由…" /></label><div className="modal-actions"><button type="button" onClick={close}>取消</button><button className="primary-action" disabled={working || !sourceId || !targetId}><GitBranch size={16} />添加关系</button></div></form></Modal>
}

function Modal({ title, close, children }: { title: string; close: () => void; children: ReactNode }) {
  return <div className="modal-backdrop" role="presentation" onMouseDown={event => event.target === event.currentTarget && close()}><section className="modal" role="dialog" aria-modal="true" aria-label={title}><header><div><span className="modal-kicker">WORLD GRAPH</span><h2>{title}</h2></div><button aria-label="关闭" onClick={close}><X size={18} /></button></header>{children}</section></div>
}

function toMessage(error: unknown) {
  return error instanceof Error ? error.message : '操作失败，请稍后重试。'
}

function parseWorldEvent(value: string) {
  try {
    const event = JSON.parse(value) as { revision?: number; isDirty?: boolean; error?: string | null; Revision?: number; IsDirty?: boolean; Error?: string | null }
    const revision = event.revision ?? event.Revision
    const isDirty = event.isDirty ?? event.IsDirty
    if (revision === undefined || isDirty === undefined)
      return null
    return { revision, isDirty, error: event.error ?? event.Error ?? null }
  } catch {
    return null
  }
}

export default App
