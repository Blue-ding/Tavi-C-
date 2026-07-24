import { useEffect, useMemo, useRef, useState, type FormEvent } from 'react'
import { ArrowRight, Bot, Check, CircleStop, GitBranch, LoaderCircle, RefreshCw, Send, Sparkles, UserRound, X } from 'lucide-react'
import { ApiError, guidanceApi } from './api'
import type { GuidanceAvailabilityViewModel, GuidanceEventViewModel, GuidanceSnapshotViewModel, ProposalAnchorReferenceViewModel, ProposalChangeViewModel, ProposeAddRelationViewModel, WorldGraphViewModel } from './types'

const sessionStorageKey = 'tavi.guidance.session'

interface GuidancePanelProps {
  open: boolean
  world: WorldGraphViewModel
  onClose: () => void
  onWorldChanged: () => Promise<void>
  onError: (message: string) => void
}

export function GuidancePanel({ open, world, onClose, onWorldChanged, onError }: GuidancePanelProps) {
  const [availability, setAvailability] = useState<GuidanceAvailabilityViewModel | null>(null)
  const [snapshot, setSnapshot] = useState<GuidanceSnapshotViewModel | null>(null)
  const [input, setInput] = useState('')
  const [streamingText, setStreamingText] = useState('')
  const [selectedChanges, setSelectedChanges] = useState<Set<string>>(new Set())
  const [retryText, setRetryText] = useState('')
  const [working, setWorking] = useState(false)
  const conversationEnd = useRef<HTMLDivElement | null>(null)

  useEffect(() => {
    void guidanceApi.availability().then(async status => {
      setAvailability(status)
      const sessionId = window.localStorage.getItem(sessionStorageKey)
      if (!status.available)
        return
      try {
        const current = sessionId ? await guidanceApi.get(sessionId) : await guidanceApi.current()
        if (current.messages.length > 0 || current.proposal || current.failure || current.state !== 'Idle') {
          window.localStorage.setItem(sessionStorageKey, current.sessionId)
          setSnapshot(current)
        }
      } catch (error) {
        if (error instanceof ApiError && error.code === 'TAVI.GUIDANCE.SESSION.NOT_FOUND')
          window.localStorage.removeItem(sessionStorageKey)
      }
    }).catch(error => onError(toMessage(error)))
  }, [onError])

  useEffect(() => {
    const proposal = snapshot?.proposal
    if (proposal)
      setSelectedChanges(new Set(proposal.changes.map(change => change.id)))
  }, [snapshot?.proposal?.id])

  useEffect(() => setRetryText(snapshot?.retryMessage ?? ''), [snapshot?.retryMessage])

  useEffect(() => {
    if (!snapshot || snapshot.state !== 'Generating')
      return
    const timer = window.setInterval(() => {
      void guidanceApi.get(snapshot.sessionId).then(next => {
        setSnapshot(next)
        if (next.state !== 'Generating')
          setStreamingText('')
      }).catch(error => onError(toMessage(error)))
    }, 900)
    return () => window.clearInterval(timer)
  }, [onError, snapshot?.sessionId, snapshot?.state])

  useEffect(() => {
    if (!snapshot)
      return
    const events = new EventSource(`/api/v1/guidance/sessions/${snapshot.sessionId}/events`)
    const receive = (event: MessageEvent<string>) => {
      const guidanceEvent = JSON.parse(event.data) as GuidanceEventViewModel
      if (guidanceEvent.text)
        setStreamingText(current => current + guidanceEvent.text)
      if (guidanceEvent.snapshot) {
        setSnapshot(guidanceEvent.snapshot)
        if (guidanceEvent.snapshot.state !== 'Generating')
          setStreamingText('')
      }
      if (guidanceEvent.error)
        onError(guidanceEvent.error)
    }
    const names = ['guidance.text.delta', 'guidance.operation.started', 'guidance.operation.completed', 'guidance.operation.cancelled', 'guidance.operation.failed', 'guidance.session.changed', 'guidance.cancelled']
    names.forEach(name => events.addEventListener(name, receive as EventListener))
    return () => {
      names.forEach(name => events.removeEventListener(name, receive as EventListener))
      events.close()
    }
  }, [onError, snapshot?.sessionId])

  useEffect(() => {
    if (open)
      conversationEnd.current?.scrollIntoView({ behavior: 'smooth', block: 'nearest' })
  }, [open, snapshot?.messages.length, streamingText])

  const proposedAnchors = useMemo(() => new Map(snapshot?.proposal?.changes.filter(change => change.kind === 'AddAnchor').map(change => [change.anchorId, change]) ?? []), [snapshot?.proposal])

  async function start(event: FormEvent) {
    event.preventDefault()
    const potential = input.trim()
    if (!potential)
      return
    await run(async () => {
      const operation = await guidanceApi.start(potential)
      window.localStorage.setItem(sessionStorageKey, operation.sessionId)
      setSnapshot(operation.snapshot)
      setStreamingText('')
      setInput('')
    })
  }

  async function continueConversation(event: FormEvent) {
    event.preventDefault()
    if (!snapshot || !input.trim())
      return
    const message = input.trim()
    await run(async () => {
      const operation = await guidanceApi.continue(snapshot.sessionId, message)
      setSnapshot(operation.snapshot)
      setStreamingText('')
      setInput('')
    })
  }

  async function cancel() {
    if (!snapshot)
      return
    await run(async () => setSnapshot(await guidanceApi.cancel(snapshot.sessionId)))
  }

  async function retry(event: FormEvent) {
    event.preventDefault()
    if (!snapshot || !retryText.trim())
      return
    await run(async () => {
      const operation = await guidanceApi.retry(snapshot.sessionId, retryText.trim())
      setSnapshot(operation.snapshot)
      setStreamingText('')
    })
  }

  async function refreshSession() {
    if (!snapshot)
      return
    await run(async () => {
      await guidanceApi.refresh(snapshot.sessionId)
      window.localStorage.removeItem(sessionStorageKey)
      setSnapshot(null)
      setInput('')
      setRetryText('')
    })
  }

  async function commit() {
    if (!snapshot)
      return
    await run(async () => {
      const result = await guidanceApi.commit(snapshot.sessionId, [...selectedChanges])
      setSnapshot(result.snapshot)
      if (result.status === 'Committed') {
        await onWorldChanged()
        return
      }
      onError(result.issues.map(issue => issue.message).join('；') || '提案暂时无法提交。')
    })
  }

  async function run(operation: () => Promise<void>) {
    setWorking(true)
    try {
      await operation()
    } catch (error) {
      onError(toMessage(error))
    } finally {
      setWorking(false)
    }
  }

  function toggleChange(change: ProposalChangeViewModel) {
    if (!snapshot?.proposal)
      return
    const anchorChanges = new Map(snapshot.proposal.changes.filter(candidate => candidate.kind === 'AddAnchor').map(candidate => [candidate.anchorId, candidate.id]))
    setSelectedChanges(current => {
      const next = new Set(current)
      if (next.has(change.id)) {
        next.delete(change.id)
        if (change.kind === 'AddAnchor')
          snapshot.proposal?.changes.filter(candidate => candidate.kind === 'AddRelation' && relationReferences(candidate).includes(change.anchorId)).forEach(candidate => next.delete(candidate.id))
      } else {
        next.add(change.id)
        if (change.kind === 'AddRelation')
          relationReferences(change).forEach(anchorId => { const dependency = anchorChanges.get(anchorId); if (dependency) next.add(dependency) })
      }
      return next
    })
  }

  if (!open)
    return null

  const generating = snapshot?.state === 'Generating'
  const ready = snapshot?.state === 'Idle' && !!snapshot.proposal
  const terminal = snapshot?.state === 'Faulted'
  const conflict = snapshot?.proposal && snapshot.proposal.baseWorldRevision !== world.revision

  return (
    <aside className="guidance-panel" aria-label="Guidance">
      <header className="guidance-header">
        <div><span className="guidance-mark"><Sparkles size={16} /></span><span><strong>Guidance</strong><small>{availability?.available ? availability.provider : '叙事构筑助手'}</small></span></div>
        <div className="guidance-header-actions">
          {snapshot && <button aria-label="刷新 Guidance 对话" title="刷新 Guidance 对话" disabled={working || generating} onClick={() => void refreshSession()}><RefreshCw size={16} /></button>}
          {generating && <button className="danger" aria-label="停止生成" title="停止生成" disabled={working} onClick={() => void cancel()}><CircleStop size={16} /></button>}
          <button aria-label="关闭 Guidance" title="关闭 Guidance" onClick={onClose}><X size={18} /></button>
        </div>
      </header>

      {!availability && <div className="guidance-center"><LoaderCircle className="spin" size={24} />正在确认 Guidance 状态…</div>}
      {availability && !availability.available && <div className="guidance-empty"><Bot size={32} /><h3>Guidance 尚未就绪</h3><p>{availability.message}</p></div>}
      {availability?.available && !snapshot && (
        <form className="guidance-start" onSubmit={start}>
          <div className="guidance-orbit"><Sparkles size={24} /></div>
          <h2>从一点叙事势能开始</h2>
          <p>描述一个声音、念头、冲突或尚未发生的可能。Guidance 会了解当前世界，并提出可逐项审阅的变化。</p>
          <textarea autoFocus rows={6} value={input} onChange={event => setInput(event.target.value)} placeholder="例如：雨夜里传来一声不存在的钟响……" />
          <button disabled={working || !input.trim()}><Sparkles size={16} />开始构筑</button>
        </form>
      )}

      {availability?.available && snapshot && (
        <>
          <div className="guidance-status"><span className={`guidance-state ${snapshot.state.toLowerCase()}`}>{stateLabel(snapshot.state)}</span><span>基于 rev {snapshot.baseWorldRevision}</span></div>
          <div className="guidance-scroll">
            <section className="guidance-conversation">
              {snapshot.messages.map((message, index) => <article key={`${index}-${message.role}`} className={`guidance-message ${message.role.toLowerCase()}`}><span>{message.role === 'Player' ? <UserRound size={14} /> : <Sparkles size={14} />}</span><p>{message.text}</p></article>)}
              {generating && <article className="guidance-message guidance streaming"><span><Sparkles className="spin-soft" size={14} /></span><p>{streamingText || '正在阅读世界并构筑提案…'}</p></article>}
              <div ref={conversationEnd} />
            </section>

            {snapshot.failure && <div className="guidance-failure"><strong>本次构筑未能完成</strong><span>{snapshot.failure.message}</span></div>}
            {snapshot.retryMessage && <form className="guidance-start" onSubmit={retry}><p>上一条消息尚未成功处理，可以编辑后重试：</p><textarea rows={4} value={retryText} onChange={event => setRetryText(event.target.value)} /><button disabled={working || !retryText.trim()}><Sparkles size={16} />重试这条消息</button></form>}
            {snapshot.proposal && (
              <section className="proposal-review">
                <header><div><span>WORLD PROPOSAL</span><h3>{snapshot.proposal.summary || '世界变化提案'}</h3></div><small>{selectedChanges.size}/{snapshot.proposal.changes.length}</small></header>
                {conflict && <div className="proposal-conflict">世界已从 rev {snapshot.proposal.baseWorldRevision} 前进到 rev {world.revision}。请重新开始 Guidance，以当前世界生成新提案。</div>}
                <div className="proposal-changes">
                  {snapshot.proposal.changes.map(change => <ProposalChange key={change.id} change={change} selected={selectedChanges.has(change.id)} world={world} proposedAnchors={proposedAnchors} onToggle={() => toggleChange(change)} />)}
                </div>
              </section>
            )}
          </div>

          {!generating && !terminal && <footer className="guidance-footer">
            {!ready && <form onSubmit={continueConversation}><textarea rows={2} value={input} onChange={event => setInput(event.target.value)} placeholder="准备下一轮消息…" /><button aria-label="发送" disabled={working || !!snapshot.retryMessage || !input.trim()}><Send size={16} /></button></form>}
            {ready && <><form onSubmit={continueConversation}><textarea rows={2} value={input} onChange={event => setInput(event.target.value)} placeholder="也可以补充要求，让 Guidance 继续考虑…" /><button aria-label="发送" disabled={working || !input.trim()}><Send size={16} /></button></form>{!conflict && <button className="guidance-commit" disabled={working || selectedChanges.size === 0} onClick={() => void commit()}>{working ? <LoaderCircle className="spin" size={16} /> : <Check size={16} />}提交 {selectedChanges.size} 项变化</button>}</>}
          </footer>}
        </>
      )}
    </aside>
  )
}

function ProposalChange({ change, selected, world, proposedAnchors, onToggle }: { change: ProposalChangeViewModel; selected: boolean; world: WorldGraphViewModel; proposedAnchors: Map<string, Extract<ProposalChangeViewModel, { kind: 'AddAnchor' }>>; onToggle: () => void }) {
  if (change.kind === 'AddAnchor')
    return <button className={`proposal-change${selected ? ' selected' : ''}`} onClick={onToggle}><span className="proposal-check">{selected && <Check size={13} />}</span><span className={`anchor-icon ${change.type.toLowerCase()}`}>{change.type === 'Character' ? <UserRound size={14} /> : <Sparkles size={14} />}</span><span><strong>{change.name}</strong><small>添加{change.type === 'Character' ? '角色' : '物品'} · {change.rationale}</small></span></button>
  return <button className={`proposal-change relation${selected ? ' selected' : ''}`} onClick={onToggle}><span className="proposal-check">{selected && <Check size={13} />}</span><span className="anchor-icon relation"><GitBranch size={14} /></span><span><strong>{change.name}</strong><small>{referenceName(change.source, world, proposedAnchors)} <ArrowRight size={10} /> {referenceName(change.target, world, proposedAnchors)} · {change.rationale}</small></span></button>
}

function relationReferences(change: ProposeAddRelationViewModel) {
  return [change.source, change.target, change.scope.character].filter((reference): reference is ProposalAnchorReferenceViewModel => reference?.kind === 'Proposed').map(reference => reference.anchorId)
}

function referenceName(reference: ProposalAnchorReferenceViewModel, world: WorldGraphViewModel, proposedAnchors: Map<string, Extract<ProposalChangeViewModel, { kind: 'AddAnchor' }>>) {
  return reference.kind === 'Existing' ? world.nodes.find(anchor => anchor.id === reference.anchorId)?.name ?? '现有要素' : proposedAnchors.get(reference.anchorId)?.name ?? '提议要素'
}

function stateLabel(state: GuidanceSnapshotViewModel['state']) {
  return { Idle: '空闲', Generating: '构筑中', Faulted: '需要刷新' }[state]
}

function toMessage(error: unknown) {
  return error instanceof Error ? error.message : 'Guidance 操作失败，请稍后重试。'
}
