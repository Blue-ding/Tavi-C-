import { useCallback, useEffect, useState } from 'react'
import { Archive, ArrowLeft, BookOpen, Check, CirclePlus, Cloud, CloudOff, FileText, Library, LoaderCircle, Pencil, Redo2, Trash2, Undo2, X } from 'lucide-react'
import { ApiError, writingApi } from './api'
import type { ManuscriptParagraphViewModel, ManuscriptViewModel, WritingSnapshotViewModel, WritingWorkspaceViewModel } from './types'

export function WritingWorkspace({ open, onClose }: { open: boolean; onClose: () => void }) {
  const [workspace, setWorkspace] = useState<WritingWorkspaceViewModel | null>(null)
  const [session, setSession] = useState<WritingSnapshotViewModel | null>(null)
  const [reader, setReader] = useState<ManuscriptViewModel | null>(null)
  const [loading, setLoading] = useState(false)
  const [working, setWorking] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [showLibrary, setShowLibrary] = useState(true)

  const refresh = useCallback(async () => {
    setLoading(true)
    try {
      const next = await writingApi.workspace()
      setWorkspace(next)
      setSession(next.session)
      if (next.session.manuscript)
        setShowLibrary(false)
      setError(null)
    } catch (requestError) {
      setError(toMessage(requestError))
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    if (open)
      void refresh()
  }, [open, refresh])

  useEffect(() => {
    if (!open || !session?.manuscript)
      return
    const saveOnHidden = () => {
      if (document.visibilityState === 'hidden' && session.isDirty)
        void writingApi.save().then(setSession).catch(() => undefined)
    }
    const saveOnBlur = () => {
      if (session.isDirty)
        void writingApi.save().then(setSession).catch(() => undefined)
    }
    document.addEventListener('visibilitychange', saveOnHidden)
    window.addEventListener('blur', saveOnBlur)
    return () => {
      document.removeEventListener('visibilitychange', saveOnHidden)
      window.removeEventListener('blur', saveOnBlur)
    }
  }, [open, session])

  if (!open)
    return null

  async function perform(operation: () => Promise<WritingSnapshotViewModel>) {
    setWorking(true)
    try {
      const next = await operation()
      setSession(next)
      setWorkspace(current => current ? { ...current, session: next } : current)
      setError(null)
      return true
    } catch (requestError) {
      setError(toMessage(requestError))
      if (requestError instanceof ApiError && requestError.code === 'TAVI.WRITING.STATE.CONFLICT')
        await refresh()
      return false
    } finally {
      setWorking(false)
    }
  }

  async function close() {
    if (session?.isDirty) {
      try {
        setSession(await writingApi.save())
      } catch {
      }
    }
    onClose()
  }

  async function create() {
    if (session?.manuscript)
      return
    if (await perform(() => writingApi.create('未命名手稿')))
      setShowLibrary(false)
  }

  async function openManuscript(id: string, status: string) {
    if (status === 'Editing') {
      setShowLibrary(false)
      setReader(null)
      return
    }
    setWorking(true)
    try {
      setReader(await writingApi.get(id))
      setError(null)
    } catch (requestError) {
      setError(toMessage(requestError))
    } finally {
      setWorking(false)
    }
  }

  async function renameArchived(manuscript: { id: string; title: string }) {
    const title = window.prompt('修改归档名称', manuscript.title)?.trim()
    if (!title || title === manuscript.title)
      return
    setWorking(true)
    try {
      await writingApi.renameArchived(manuscript.id, title)
      await refresh()
    } catch (requestError) {
      setError(toMessage(requestError))
    } finally {
      setWorking(false)
    }
  }

  async function removeArchived(manuscript: { id: string; title: string }) {
    if (!window.confirm(`永久删除归档“${manuscript.title}”吗？此操作无法撤销。`))
      return
    setWorking(true)
    try {
      await writingApi.deleteArchived(manuscript.id)
      if (reader?.id === manuscript.id)
        setReader(null)
      await refresh()
    } catch (requestError) {
      setError(toMessage(requestError))
    } finally {
      setWorking(false)
    }
  }

  async function archive() {
    const manuscript = session?.manuscript
    if (!manuscript || !window.confirm(`归档“${manuscript.title}”吗？归档后正文将无法继续修改。`))
      return
    setWorking(true)
    try {
      await writingApi.archive(manuscript.stateId)
      await refresh()
      setShowLibrary(true)
    } catch (requestError) {
      setError(toMessage(requestError))
    } finally {
      setWorking(false)
    }
  }

  return (
    <section className="writing-shell" aria-label="手稿工作区">
      <header className="writing-topbar">
        <div className="writing-brand"><span><BookOpen size={19} /></span><div><strong>Writing</strong><small>{showLibrary ? '手稿库' : '段落写作台'}</small></div></div>
        <div className="writing-top-actions">
          {!showLibrary && <button onClick={() => setShowLibrary(true)}><Library size={16} />手稿库</button>}
          <button aria-label="关闭手稿工作区" title="关闭并保存" onClick={() => void close()}><X size={18} /></button>
        </div>
      </header>
      {loading && !workspace ? <div className="writing-center"><LoaderCircle className="spin" size={24} />正在整理手稿…</div> : showLibrary || !session?.manuscript ? (
        <ManuscriptLibrary workspace={workspace} working={working} reader={reader} onCreate={() => void create()} onOpen={(id, status) => void openManuscript(id, status)} onRename={manuscript => void renameArchived(manuscript)} onDelete={manuscript => void removeArchived(manuscript)} onCloseReader={() => setReader(null)} />
      ) : (
        <ManuscriptEditor session={session} working={working} perform={perform} archive={() => void archive()} />
      )}
      {error && <div className="writing-error" role="alert"><span>{error}</span><button aria-label="关闭错误" onClick={() => setError(null)}><X size={15} /></button></div>}
    </section>
  )
}

function ManuscriptLibrary({ workspace, working, reader, onCreate, onOpen, onRename, onDelete, onCloseReader }: { workspace: WritingWorkspaceViewModel | null; working: boolean; reader: ManuscriptViewModel | null; onCreate: () => void; onOpen: (id: string, status: string) => void; onRename: (manuscript: { id: string; title: string }) => void; onDelete: (manuscript: { id: string; title: string }) => void; onCloseReader: () => void }) {
  const manuscripts = workspace?.manuscripts ?? []
  if (reader)
    return <ArchivedReader manuscript={reader} onBack={onCloseReader} />
  return (
    <div className="manuscript-library">
      <div className="library-heading"><div><span>MANUSCRIPT ARCHIVE</span><h1>你的故事</h1><p>正在编辑的手稿会持续保存；归档后正文成为只读记录。</p></div><button disabled={working || Boolean(workspace?.session.manuscript)} onClick={onCreate}><CirclePlus size={17} />新建空白手稿</button></div>
      {workspace?.session.manuscript && <div className="active-manuscript-callout"><CloudOff size={17} /><span><strong>仍有一场写作尚未结束</strong><small>归档当前手稿后才能开始新的故事。</small></span><button onClick={() => onOpen(workspace.session.manuscript!.id, 'Editing')}>继续写作</button></div>}
      <div className="manuscript-grid">
        {manuscripts.map(manuscript => <article key={manuscript.id} className={manuscript.status === 'Editing' ? 'active' : ''} onClick={() => onOpen(manuscript.id, manuscript.status)}>
          <div className="manuscript-card-icon">{manuscript.status === 'Editing' ? <Pencil size={18} /> : <FileText size={18} />}</div>
          <div className="manuscript-card-body"><span>{manuscript.status === 'Editing' ? '正在编辑' : '已归档'} · {formatDate(manuscript.updatedAtUtc)}</span><h2>{manuscript.title}</h2><p>{manuscript.preview || '这篇手稿还没有正文。'}</p><small>{manuscript.paragraphCount} 个段落</small></div>
          {manuscript.status === 'Archived' && <div className="manuscript-card-actions" onClick={event => event.stopPropagation()}><button aria-label="修改名称" title="修改名称" disabled={working} onClick={() => onRename(manuscript)}><Pencil size={14} /></button><button aria-label="删除归档" title="删除归档" disabled={working} onClick={() => onDelete(manuscript)}><Trash2 size={14} /></button></div>}
        </article>)}
        {!manuscripts.length && <div className="library-empty"><BookOpen size={34} /><h2>还没有写下故事</h2><p>新建一篇空白手稿，从第一个段落开始。</p><button onClick={onCreate}><CirclePlus size={16} />新建手稿</button></div>}
      </div>
    </div>
  )
}

function ManuscriptEditor({ session, working, perform, archive }: { session: WritingSnapshotViewModel; working: boolean; perform: (operation: () => Promise<WritingSnapshotViewModel>) => Promise<boolean>; archive: () => void }) {
  const manuscript = session.manuscript!
  const [title, setTitle] = useState(manuscript.title)
  useEffect(() => setTitle(manuscript.title), [manuscript.title])

  async function saveTitle() {
    const normalized = title.trim()
    if (!normalized) {
      setTitle(manuscript.title)
      return
    }
    if (normalized !== manuscript.title)
      await perform(() => writingApi.renameActive(manuscript.stateId, normalized))
  }

  return (
    <div className="writing-editor">
      <div className="editor-toolbar">
        <div className="editor-title"><span>ACTIVE MANUSCRIPT</span><input aria-label="手稿名称" value={title} disabled={working} onChange={event => setTitle(event.target.value)} onBlur={() => void saveTitle()} onKeyDown={event => { if (event.key === 'Enter') event.currentTarget.blur() }} /></div>
        <div className="editor-actions">
          <button title="撤销" aria-label="撤销" disabled={working || !session.canUndo} onClick={() => void perform(() => writingApi.undo(manuscript.stateId))}><Undo2 size={16} /></button>
          <button title="重做" aria-label="重做" disabled={working || !session.canRedo} onClick={() => void perform(() => writingApi.redo(manuscript.stateId))}><Redo2 size={16} /></button>
          <button className="writing-save" disabled={working || !session.isDirty} onClick={() => void perform(writingApi.save)}>{working ? <LoaderCircle className="spin" size={15} /> : session.isDirty ? <CloudOff size={15} /> : <Cloud size={15} />}{session.isDirty ? '保存' : '已保存'}</button>
          <button className="writing-archive" disabled={working} onClick={archive}><Archive size={15} />归档</button>
        </div>
      </div>
      {session.autoSaveError && <div className="autosave-warning">自动保存失败：{session.autoSaveError}</div>}
      <div className="paragraph-scroll">
        <div className="paragraph-paper">
          {manuscript.paragraphs.map((paragraph, index) => <ParagraphEditor key={paragraph.id} paragraph={paragraph} number={index + 1} disabled={working} save={text => perform(() => writingApi.updateParagraph(paragraph.id, manuscript.stateId, text))} remove={() => perform(() => writingApi.removeParagraph(paragraph.id, manuscript.stateId))} insertAfter={() => perform(() => writingApi.insertParagraph(manuscript.stateId, index + 1))} />)}
          {!manuscript.paragraphs.length && <div className="paragraph-empty"><FileText size={28} /><p>空白还没有成为故事。</p><button disabled={working} onClick={() => void perform(() => writingApi.insertParagraph(manuscript.stateId, 0))}><CirclePlus size={16} />添加第一个段落</button></div>}
          {manuscript.paragraphs.length > 0 && <button className="append-paragraph" disabled={working} onClick={() => void perform(() => writingApi.insertParagraph(manuscript.stateId, manuscript.paragraphs.length))}><CirclePlus size={15} />在结尾添加段落</button>}
        </div>
      </div>
      <footer className="editor-status"><span>{manuscript.paragraphs.length} 个段落</span><span>state {manuscript.stateId.slice(0, 8)}</span><span>{session.isDirty ? '等待保存' : `保存于 ${formatTime(manuscript.updatedAtUtc)}`}</span></footer>
    </div>
  )
}

function ParagraphEditor({ paragraph, number, disabled, save, remove, insertAfter }: { paragraph: ManuscriptParagraphViewModel; number: number; disabled: boolean; save: (text: string) => Promise<boolean>; remove: () => Promise<boolean>; insertAfter: () => Promise<boolean> }) {
  const [text, setText] = useState(paragraph.text)
  const [editing, setEditing] = useState(paragraph.text.length === 0)
  useEffect(() => setText(paragraph.text), [paragraph.text])
  async function commit() {
    if (text !== paragraph.text && !await save(text))
      return
    setEditing(false)
  }
  if (editing)
    return <article className="paragraph-block editing"><span className="paragraph-number">{number.toString().padStart(2, '0')}</span><div><textarea autoFocus aria-label={`第 ${number} 段`} disabled={disabled} value={text} rows={Math.max(3, text.split('\n').length)} onChange={event => setText(event.target.value)} placeholder="写下这一段故事…" /><div className="paragraph-edit-actions"><button disabled={disabled} onClick={() => { setText(paragraph.text); setEditing(false) }}><X size={14} />取消</button><button className="confirm" disabled={disabled} onClick={() => void commit()}><Check size={14} />完成</button></div></div></article>
  return <article className="paragraph-block"><span className="paragraph-number">{number.toString().padStart(2, '0')}</span><button className="paragraph-text" onClick={() => setEditing(true)}>{paragraph.text || <em>空白段落，点击编辑</em>}</button><div className="paragraph-tools"><button aria-label="在后面添加段落" title="在后面添加段落" disabled={disabled} onClick={() => void insertAfter()}><CirclePlus size={14} /></button><button aria-label="编辑段落" title="编辑段落" disabled={disabled} onClick={() => setEditing(true)}><Pencil size={14} /></button><button aria-label="删除段落" title="删除段落" disabled={disabled} onClick={() => { if (window.confirm(`删除第 ${number} 段吗？`)) void remove() }}><Trash2 size={14} /></button></div></article>
}

function ArchivedReader({ manuscript, onBack }: { manuscript: ManuscriptViewModel; onBack: () => void }) {
  return <div className="archived-reader"><header><button onClick={onBack}><ArrowLeft size={16} />返回手稿库</button><span><Archive size={14} />归档于 {formatDate(manuscript.updatedAtUtc)}</span></header><article><h1>{manuscript.title}</h1><div className="reader-rule" />{manuscript.paragraphs.map(paragraph => <p key={paragraph.id}>{paragraph.text || '　'}</p>)}{!manuscript.paragraphs.length && <p className="reader-empty">这篇手稿没有正文。</p>}</article></div>
}

function formatDate(value: string) {
  return new Intl.DateTimeFormat('zh-CN', { year: 'numeric', month: 'short', day: 'numeric' }).format(new Date(value))
}

function formatTime(value: string) {
  return new Intl.DateTimeFormat('zh-CN', { hour: '2-digit', minute: '2-digit' }).format(new Date(value))
}

function toMessage(error: unknown) {
  return error instanceof Error ? error.message : '手稿操作失败，请稍后重试。'
}
