import { useCallback, useEffect, useMemo, useState } from 'react'
import { ArrowLeft, Check, ChevronRight, CirclePlus, Clapperboard, Cloud, CloudOff, Layers3, LoaderCircle, Play, Redo2, RotateCcw, Save, Sparkles, Trash2, Undo2, X } from 'lucide-react'
import { ApiError, scenarioApi } from './api'
import type { ElementViewModel, ScenarioWorkspaceViewModel, SceneDefinitionViewModel, SceneSlotViewModel, SceneViewModel } from './types'

export function ScenarioPage() {
  const [workspace, setWorkspace] = useState<ScenarioWorkspaceViewModel | null>(null)
  const [working, setWorking] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const previous = document.title
    document.title = 'Tavi · Scenario 工作台'
    return () => { document.title = previous }
  }, [])

  const load = useCallback(async () => {
    setWorking(true)
    try {
      setWorkspace(await scenarioApi.get())
      setError(null)
    } catch (value) {
      setError(message(value))
    } finally {
      setWorking(false)
    }
  }, [])

  useEffect(() => { void load() }, [load])

  async function perform(operation: () => Promise<ScenarioWorkspaceViewModel>) {
    if (working)
      return
    setWorking(true)
    try {
      setWorkspace(await operation())
      setError(null)
    } catch (value) {
      setError(message(value))
    } finally {
      setWorking(false)
    }
  }

  if (!workspace)
    return <main className="scenario-loading"><LoaderCircle className="spin" size={28} /><span>{error ?? '正在打开独立 Scenario…'}</span>{error && <button onClick={() => void load()}>重试</button>}</main>

  const settledCount = workspace.scenes.filter(scene => scene.state === 'Settled').length
  return (
    <main className="scenario-shell">
      <header className="scenario-topbar">
        <div className="scenario-title">
          <a href="/" aria-label="返回世界工作台"><ArrowLeft size={18} /></a>
          <span><Clapperboard size={20} /></span>
          <div><strong>Scenario</strong><small>独立场景工作台</small></div>
        </div>
        <div className="scenario-toolbar">
          <span className={`scenario-health ${workspace.health.toLowerCase()}`}>{workspace.health === 'Healthy' ? '运行正常' : '会话异常'}</span>
          <button title="撤销" disabled={working || !workspace.canUndo} onClick={() => void perform(() => scenarioApi.undo(workspace.stateId))}><Undo2 size={16} /></button>
          <button title="重做" disabled={working || !workspace.canRedo} onClick={() => void perform(() => scenarioApi.redo(workspace.stateId))}><Redo2 size={16} /></button>
          <button disabled={working} onClick={() => void perform(scenarioApi.save)}>{workspace.isDirty ? <CloudOff size={16} /> : <Cloud size={16} />}{workspace.isDirty ? '保存' : '已保存'}</button>
          <span className="scenario-revision" title={workspace.stateId}>state {workspace.stateId.slice(0, 8)}</span>
        </div>
      </header>

      <section className="scenario-summary">
        <div><small>SOURCE WORLD</small><strong>{workspace.sourceWorldStateId.slice(0, 8)}</strong></div>
        <div><small>ELEMENTS</small><strong>{workspace.elements.length}</strong></div>
        <div><small>ACTIVE SCENES</small><strong>{workspace.scenes.filter(scene => scene.state !== 'Settled').length}</strong></div>
        <div className="scenario-module-strip"><small>MODULES</small><span>{workspace.modules.map(module => <i key={module.id}>{module.id}<b>{module.version}</b></i>)}</span></div>
        <button disabled={working || settledCount === 0} onClick={() => void perform(() => scenarioApi.clearSettled(workspace.stateId))}><RotateCcw size={15} />清理 {settledCount} 个已结算 Scene</button>
      </section>

      <section className="scenario-grid">
        <aside className="scenario-definitions">
          <header><div><small>SCENE CATALOG</small><h2>可用定义</h2></div><span>{workspace.definitions.length}</span></header>
          <div className="scenario-scroll">
            {workspace.definitions.map(definition => <DefinitionCard key={definition.id} definition={definition} disabled={working} create={() => perform(() => scenarioApi.createScene(workspace.stateId, definition.id))} />)}
            {!workspace.definitions.length && <Empty title="没有可用定义" text="启用的 Module 尚未为当前状态提供 SceneDefinition。" />}
          </div>
        </aside>

        <section className="scenario-board">
          <header><div><small>SCENE LIFECYCLE</small><h2>场景队列</h2></div><div className="scenario-legend"><span className="binding">Binding</span><ChevronRight size={13} /><span className="processing">Processing</span><ChevronRight size={13} /><span className="settled">Settled</span></div></header>
          <div className="scenario-scroll scenario-scene-list">
            {workspace.scenes.map(scene => <SceneCard key={scene.id} scene={scene} elements={workspace.elements} disabled={working} perform={perform} stateId={workspace.stateId} />)}
            {!workspace.scenes.length && <Empty title="场景队列为空" text="从左侧 Definition 创建 Scene，然后为槽位绑定参与者。" />}
          </div>
        </section>

        <aside className="scenario-elements">
          <header><div><small>LOCAL CAST</small><h2>可绑定 Element</h2></div><span>{workspace.elements.length}</span></header>
          <div className="scenario-scroll">
            {workspace.elements.map(element => <ElementCard key={element.id} element={element} workspace={workspace} />)}
            {!workspace.elements.length && <Empty title="没有 Element" text="Scenario 创建时会从来源 World 复制 EARS 数据。" />}
          </div>
        </aside>
      </section>
      {working && <div className="scenario-progress"><LoaderCircle className="spin" size={17} />正在提交原子变化…</div>}
      {error && <div className="error-toast" role="alert"><span>{error}</span><button aria-label="关闭错误" onClick={() => setError(null)}><X size={15} /></button></div>}
    </main>
  )
}

function DefinitionCard({ definition, disabled, create }: { definition: SceneDefinitionViewModel; disabled: boolean; create: () => Promise<void> }) {
  return <article className="scenario-definition-card">
    <div className="scenario-card-kicker"><span>{definition.module}</span><i>{definition.settlement.join(' + ')}</i></div>
    <h3>{definition.name}</h3>
    <p>{definition.description || '这个定义没有额外说明。'}</p>
    <div className="scenario-slot-preview">{definition.slots.map(slot => <span key={slot.id}><Layers3 size={12} />{slot.name}<b>{slot.minimum}–{slot.maximum ?? '∞'}</b></span>)}</div>
    <button disabled={disabled} onClick={() => void create()}><CirclePlus size={15} />创建 Scene</button>
  </article>
}

function SceneCard({ scene, elements, disabled, perform, stateId }: { scene: SceneViewModel; elements: ElementViewModel[]; disabled: boolean; perform: (operation: () => Promise<ScenarioWorkspaceViewModel>) => Promise<void>; stateId: string }) {
  return <article className={`scenario-scene-card ${scene.state.toLowerCase()}`}>
    <header>
      <div><span className={`scenario-state ${scene.state.toLowerCase()}`}>{scene.state}</span><small>{scene.definitionId}</small></div>
      <button aria-label="删除 Scene" title="删除 Scene" disabled={disabled || scene.state === 'Processing'} onClick={() => void perform(() => scenarioApi.removeScene(scene.id, stateId))}><Trash2 size={15} /></button>
    </header>
    <h3>{scene.name}</h3>
    <p>{scene.description}</p>
    <div className="scenario-bindings">
      {scene.slots.map(slot => <SlotEditor key={slot.id} scene={scene} slot={slot} elements={elements} stateId={stateId} disabled={disabled} perform={perform} />)}
    </div>
    <footer>
      {scene.state === 'Binding' && <button className="scenario-primary" disabled={disabled || !complete(scene)} onClick={() => void perform(() => scenarioApi.beginProcessing(scene.id, stateId))}><Play size={15} />冻结并开始处理</button>}
      {scene.state === 'Processing' && scene.settlement.includes('Rules') && <button className="scenario-primary magic" disabled={disabled} onClick={() => void perform(() => scenarioApi.settleRules(scene.id, stateId))}><Sparkles size={15} />按 Module 规则结算</button>}
      {scene.state === 'Processing' && !scene.settlement.includes('Rules') && <span>等待 Writing 流程提交结果</span>}
      {scene.state === 'Settled' && <span><Check size={14} />局部结果已原子提交</span>}
    </footer>
  </article>
}

function SlotEditor({ scene, slot, elements, stateId, disabled, perform }: { scene: SceneViewModel; slot: SceneSlotViewModel; elements: ElementViewModel[]; stateId: string; disabled: boolean; perform: (operation: () => Promise<ScenarioWorkspaceViewModel>) => Promise<void> }) {
  const [selected, setSelected] = useState(slot.elementIds)
  useEffect(() => setSelected(slot.elementIds), [slot.elementIds])
  const candidates = useMemo(() => elements.filter(element => slot.elementTypes.length === 0 || slot.elementTypes.includes(element.type)), [elements, slot.elementTypes])
  const editable = scene.state === 'Binding' && !disabled
  const changed = selected.join('|') !== slot.elementIds.join('|')
  const maximum = slot.maximum ?? candidates.length
  return <div className="scenario-slot-editor">
    <label><span><strong>{slot.name}</strong><small>{slot.minimum}–{slot.maximum ?? '∞'} 个 Element</small></span>{slot.requiredAspectGroups.map(group => <i key={group}>{group}</i>)}</label>
    <select multiple={maximum !== 1} size={maximum === 1 ? 1 : Math.min(4, Math.max(2, candidates.length))} value={maximum === 1 ? selected[0] ?? '' : selected} disabled={!editable} onChange={event => {
      const values = Array.from(event.currentTarget.selectedOptions).map(option => option.value).filter(Boolean).slice(0, maximum)
      setSelected(values)
    }}>
      {maximum === 1 && <option value="">选择 Element…</option>}
      {candidates.map(element => <option key={element.id} value={element.id}>{element.name} · {element.type}</option>)}
    </select>
    {editable && <div className="scenario-slot-actions">
      <button disabled={!changed} onClick={() => void perform(() => scenarioApi.setBinding(scene.id, slot.id, stateId, selected))}><Save size={13} />应用绑定</button>
      <button disabled={!slot.elementIds.length} onClick={() => void perform(() => scenarioApi.clearBinding(scene.id, slot.id, stateId))}>清除</button>
    </div>}
  </div>
}

function ElementCard({ element, workspace }: { element: ElementViewModel; workspace: ScenarioWorkspaceViewModel }) {
  const aspects = workspace.aspects.filter(aspect => aspect.elementId === element.id)
  return <article className="scenario-element-card">
    <div><span>{element.name.slice(0, 1).toUpperCase()}</span><section><strong>{element.name}</strong><small>{element.type}</small></section></div>
    {element.description && <p>{element.description}</p>}
    <footer>{aspects.map(aspect => <span key={aspect.id} title={aspect.type}>{aspect.type}<b>{aspect.quantity}</b></span>)}{!aspects.length && <i>无 Aspect</i>}</footer>
  </article>
}

function Empty({ title, text }: { title: string; text: string }) {
  return <div className="scenario-empty"><Clapperboard size={25} /><strong>{title}</strong><p>{text}</p></div>
}

function complete(scene: SceneViewModel) {
  return scene.slots.every(slot => slot.elementIds.length >= slot.minimum && (slot.maximum === null || slot.elementIds.length <= slot.maximum))
}

function message(value: unknown) {
  return value instanceof ApiError || value instanceof Error ? value.message : 'Scenario 操作失败。'
}
