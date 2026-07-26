import { useState, useMemo, useCallback, useEffect, useRef } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useDocumentTitle } from '@/shared/hooks/useDocumentTitle';
import { useHotkeys } from '@/shared/hooks/useHotkeys';
import { queryKeys } from '@/shared/query';
import { writingClient } from '../client/writingClient';
import { Button } from '@/shared/ui/Button';
import { IconButton } from '@/shared/ui/IconButton';
import { Dialog } from '@/shared/ui/Dialog';
import { EmptyState } from '@/shared/ui/EmptyState';
import { ErrorNotice } from '@/shared/ui/ErrorNotice';
import { LoadingSkeleton } from '@/shared/ui/LoadingSkeleton';
import { useToast } from '@/shared/ui/Toast';
import { PenTool, Plus, Save, RotateCcw, RotateCw, Archive, Trash2 } from 'lucide-react';

export default function WritingScreen() {
  useDocumentTitle('Writing');
  const toast = useToast();
  const queryClient = useQueryClient();

  const workspaceQuery = useQuery({
    queryKey: queryKeys.writing.workspace,
    queryFn: () => writingClient.getWorkspace(),
    staleTime: 10_000,
  });

  const workspace = workspaceQuery.data;
  const session = workspace?.session;
  const activeManuscript = session?.manuscript;

  const [selectedArchiveId, setSelectedArchiveId] = useState<string | null>(null);
  const [drafts, setDrafts] = useState<Record<string, string>>({});
  const [titleDraft, setTitleDraft] = useState<string | null>(null);
  const [createOpen, setCreateOpen] = useState(false);
  const [newTitle, setNewTitle] = useState('');
  const [archiveConfirmOpen, setArchiveConfirmOpen] = useState(false);
  const [deleteConfirmId, setDeleteConfirmId] = useState<string | null>(null);
  const [deleteTitleInput, setDeleteTitleInput] = useState('');

  const pendingRef = useRef<Promise<unknown> | null>(null);

  const selectedArchive = useMemo(() => {
    if (!selectedArchiveId) return null;
    return workspace?.manuscripts.find((m) => m.id === selectedArchiveId) ?? null;
  }, [selectedArchiveId, workspace]);

  const isDirty = useMemo(() => {
    return Object.values(drafts).some((d) => d.trim().length > 0) || titleDraft !== null;
  }, [drafts, titleDraft]);

  const flushDrafts = useCallback(async () => {
    if (!activeManuscript) return;
    const stateId = activeManuscript.stateId;

    for (const para of activeManuscript.paragraphs) {
      const draft = drafts[para.id];
      if (draft !== undefined && draft !== para.text) {
        const promise = writingClient.updateParagraph(para.id, stateId, draft);
        pendingRef.current = promise;
        try {
          const res = await promise;
          pendingRef.current = null;
          if (res.session.manuscript) {
            queryClient.setQueryData(queryKeys.writing.workspace, res);
          }
        } catch {
          pendingRef.current = null;
          toast.show('保存段落失败', 'error');
          return;
        }
      }
    }

    if (titleDraft !== null && titleDraft !== activeManuscript.title) {
      const promise = writingClient.renameSessionTitle(titleDraft, stateId);
      pendingRef.current = promise;
      try {
        await promise;
        pendingRef.current = null;
        queryClient.invalidateQueries({ queryKey: queryKeys.writing.workspace });
      } catch {
        pendingRef.current = null;
        toast.show('保存标题失败', 'error');
        return;
      }
    }

    setDrafts({});
    setTitleDraft(null);
  }, [activeManuscript, drafts, titleDraft, queryClient, toast]);

  const createMutation = useMutation({
    mutationFn: (title: string) => writingClient.createManuscript(title),
    onSuccess: (res) => {
      queryClient.setQueryData(queryKeys.writing.workspace, res);
      setCreateOpen(false);
      setNewTitle('');
      toast.show('手稿已创建', 'success');
    },
  });

  const archiveMutation = useMutation({
    mutationFn: async () => {
      await flushDrafts();
      const stateId = activeManuscript?.stateId;
      if (!stateId) throw new Error('No manuscript');
      return writingClient.archive(stateId);
    },
    onSuccess: (res) => {
      queryClient.setQueryData(queryKeys.writing.workspace, res);
      setArchiveConfirmOpen(false);
      toast.show('已归档', 'success');
    },
  });

  const deleteMutation = useMutation({
    mutationFn: (id: string) => writingClient.deleteManuscript(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.writing.workspace });
      setDeleteConfirmId(null);
      setDeleteTitleInput('');
      toast.show('已删除', 'success');
    },
  });

  const undoMutation = useMutation({
    mutationFn: async () => {
      await flushDrafts();
      const stateId = activeManuscript?.stateId;
      if (!stateId) throw new Error('No manuscript');
      return writingClient.undo(stateId);
    },
    onSuccess: (res) => {
      queryClient.setQueryData(queryKeys.writing.workspace, res);
      toast.show('已撤销', 'success');
    },
  });

  const redoMutation = useMutation({
    mutationFn: async () => {
      await flushDrafts();
      const stateId = activeManuscript?.stateId;
      if (!stateId) throw new Error('No manuscript');
      return writingClient.redo(stateId);
    },
    onSuccess: (res) => {
      queryClient.setQueryData(queryKeys.writing.workspace, res);
      toast.show('已重做', 'success');
    },
  });

  const saveMutation = useMutation({
    mutationFn: async () => {
      await flushDrafts();
      const stateId = activeManuscript?.stateId;
      if (!stateId) throw new Error('No manuscript');
      return writingClient.save(stateId);
    },
    onSuccess: () => {
      toast.show('已保存', 'success');
      queryClient.invalidateQueries({ queryKey: queryKeys.writing.workspace });
    },
  });

  const handleParagraphChange = useCallback((id: string, text: string) => {
    setDrafts((prev) => ({ ...prev, [id]: text }));
  }, []);

  const handleAddParagraph = useCallback(async () => {
    if (!activeManuscript) return;
    await flushDrafts();
    try {
      const res = await writingClient.insertParagraph(
        activeManuscript.stateId,
        activeManuscript.paragraphs.length
      );
      queryClient.setQueryData(queryKeys.writing.workspace, res);
    } catch {
      toast.show('插入段落失败', 'error');
    }
  }, [activeManuscript, flushDrafts, queryClient, toast]);

  useHotkeys({
    'Ctrl+S': {
      action: () => saveMutation.mutate(),
      predicate: () => !saveMutation.isPending,
    },
    'Ctrl+Z': {
      action: () => undoMutation.mutate(),
      predicate: () => session?.canUndo === true && !isDirty && !undoMutation.isPending,
    },
    'Ctrl+Shift+Z': {
      action: () => redoMutation.mutate(),
      predicate: () => session?.canRedo === true && !isDirty && !redoMutation.isPending,
    },
  });

  useEffect(() => {
    const handler = (e: BeforeUnloadEvent) => {
      if (isDirty) {
        e.preventDefault();
      }
    };
    window.addEventListener('beforeunload', handler);
    return () => window.removeEventListener('beforeunload', handler);
  }, [isDirty]);

  if (workspaceQuery.isLoading) {
    return (
      <div>
        <h1 style={{ fontSize: 24, fontWeight: 600, marginBottom: 16 }}>Writing</h1>
        <LoadingSkeleton />
      </div>
    );
  }

  if (workspaceQuery.error) {
    return (
      <div>
        <h1 style={{ fontSize: 24, fontWeight: 600, marginBottom: 16 }}>Writing</h1>
        <ErrorNotice
          message="无法加载 Writing 工作区"
          onRetry={() =>
            queryClient.invalidateQueries({ queryKey: queryKeys.writing.workspace })
          }
        />
      </div>
    );
  }

  return (
    <div style={{ display: 'flex', gap: 24, height: '100%' }}>
      {/* Library sidebar */}
      <div
        style={{
          width: 280,
          flexShrink: 0,
          display: 'flex',
          flexDirection: 'column',
          gap: 12,
        }}
      >
        <Button onClick={() => setCreateOpen(true)}>
          <Plus size={16} /> 新建手稿
        </Button>

        <div>
          <h3
            style={{
              fontSize: 12,
              fontWeight: 600,
              color: 'var(--color-text-subtle)',
              marginBottom: 8,
              textTransform: 'uppercase',
              letterSpacing: 0.5,
            }}
          >
            编辑中
          </h3>
          {activeManuscript ? (
            <div
              style={{
                padding: 12,
                borderRadius: 8,
                background: 'var(--color-surface-2)',
                border: '1px solid var(--color-border)',
                cursor: 'pointer',
              }}
              onClick={() => setSelectedArchiveId(null)}
            >
              <div style={{ fontWeight: 500, marginBottom: 4 }}>
                {activeManuscript.title}
              </div>
              <div style={{ fontSize: 12, color: 'var(--color-text-subtle)' }}>
                {activeManuscript.paragraphs.length} 段落
              </div>
            </div>
          ) : (
            <div style={{ fontSize: 13, color: 'var(--color-text-subtle)' }}>
              无活动手稿
            </div>
          )}
        </div>

        <div style={{ flex: 1, overflow: 'auto' }}>
          <h3
            style={{
              fontSize: 12,
              fontWeight: 600,
              color: 'var(--color-text-subtle)',
              marginBottom: 8,
              textTransform: 'uppercase',
              letterSpacing: 0.5,
            }}
          >
            已归档
          </h3>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
            {workspace?.manuscripts
              .filter((m) => m.status === 'Archived')
              .map((m) => (
                <div
                  key={m.id}
                  style={{
                    padding: 10,
                    borderRadius: 6,
                    background:
                      selectedArchiveId === m.id
                        ? 'var(--color-surface-2)'
                        : 'transparent',
                    border: '1px solid transparent',
                    cursor: 'pointer',
                    display: 'flex',
                    alignItems: 'center',
                    gap: 8,
                  }}
                  onClick={() => setSelectedArchiveId(m.id)}
                >
                  <div style={{ flex: 1, minWidth: 0 }}>
                    <div
                      style={{
                        fontSize: 13,
                        fontWeight: 500,
                        overflow: 'hidden',
                        textOverflow: 'ellipsis',
                        whiteSpace: 'nowrap',
                      }}
                    >
                      {m.title}
                    </div>
                    <div style={{ fontSize: 11, color: 'var(--color-text-subtle)' }}>
                      {m.paragraphCount} 段落
                    </div>
                  </div>
                  <IconButton
                    label="删除"
                    size="sm"
                    onClick={(e) => {
                      e.stopPropagation();
                      setDeleteConfirmId(m.id);
                    }}
                  >
                    <Trash2 size={14} />
                  </IconButton>
                </div>
              ))}
          </div>
        </div>
      </div>

      {/* Editor */}
      <div style={{ flex: 1, display: 'flex', flexDirection: 'column', minWidth: 0 }}>
        {selectedArchive ? (
          <ArchiveReader manuscript={selectedArchive} />
        ) : activeManuscript ? (
          <>
            <div
              style={{
                display: 'flex',
                alignItems: 'center',
                gap: 12,
                marginBottom: 16,
              }}
            >
              <input
                type="text"
                value={titleDraft ?? activeManuscript.title}
                onChange={(e) => setTitleDraft(e.target.value)}
                onBlur={() => {
                  if (titleDraft !== null) {
                    flushDrafts();
                  }
                }}
                style={{
                  fontSize: 20,
                  fontWeight: 600,
                  background: 'transparent',
                  border: 'none',
                  borderBottom: '1px solid transparent',
                  color: 'var(--color-text)',
                  flex: 1,
                  padding: '4px 0',
                }}
              />
              <div style={{ flex: 1 }} />
              <IconButton
                label="撤销"
                onClick={() => undoMutation.mutate()}
                disabled={!session?.canUndo || isDirty || undoMutation.isPending}
              >
                <RotateCcw size={16} />
              </IconButton>
              <IconButton
                label="重做"
                onClick={() => redoMutation.mutate()}
                disabled={!session?.canRedo || isDirty || redoMutation.isPending}
              >
                <RotateCw size={16} />
              </IconButton>
              <IconButton
                label="保存"
                onClick={() => saveMutation.mutate()}
                disabled={saveMutation.isPending}
              >
                <Save size={16} />
              </IconButton>
              <IconButton
                label="归档"
                onClick={() => setArchiveConfirmOpen(true)}
              >
                <Archive size={16} />
              </IconButton>
              {isDirty && (
                <span
                  style={{
                    width: 8,
                    height: 8,
                    borderRadius: '50%',
                    background: '#4a9eff',
                  }}
                  title="本地草稿未提交"
                />
              )}
              {session?.isDirty && !isDirty && (
                <span
                  style={{
                    width: 8,
                    height: 8,
                    borderRadius: '50%',
                    background: 'var(--color-warning)',
                  }}
                  title="有未保存修改"
                />
              )}
            </div>

            <div style={{ flex: 1, overflow: 'auto' }}>
              <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
                {activeManuscript.paragraphs.map((para) => (
                  <textarea
                    key={para.id}
                    value={drafts[para.id] ?? para.text}
                    onChange={(e) => handleParagraphChange(para.id, e.target.value)}
                    onBlur={() => {
                      const draft = drafts[para.id];
                      if (draft !== undefined && draft !== para.text) {
                        flushDrafts();
                      }
                    }}
                    rows={4}
                    style={{
                      width: '100%',
                      padding: 12,
                      borderRadius: 8,
                      border: '1px solid var(--color-border)',
                      background: 'var(--color-surface-1)',
                      color: 'var(--color-text)',
                      resize: 'vertical',
                      lineHeight: 1.6,
                    }}
                  />
                ))}
              </div>
              <Button
                variant="ghost"
                onClick={handleAddParagraph}
                style={{ marginTop: 12 }}
              >
                <Plus size={16} /> 添加段落
              </Button>
            </div>
          </>
        ) : (
          <EmptyState
            icon={<PenTool size={48} />}
            title="创建手稿，已发布的 Beat 会追加到这里。"
            action={<Button onClick={() => setCreateOpen(true)}>新建手稿</Button>}
          />
        )}
      </div>

      <Dialog
        open={createOpen}
        title="新建手稿"
        onClose={() => {
          setCreateOpen(false);
          setNewTitle('');
        }}
        confirmLabel="创建"
        onConfirm={() => {
          if (newTitle.trim()) createMutation.mutate(newTitle.trim());
        }}
      >
        <input
          type="text"
          placeholder="手稿名称"
          value={newTitle}
          onChange={(e) => setNewTitle(e.target.value)}
          autoFocus
          style={{
            width: '100%',
            padding: '8px 12px',
            borderRadius: 6,
            border: '1px solid var(--color-border)',
            background: 'var(--color-surface-2)',
          }}
        />
      </Dialog>

      <Dialog
        open={archiveConfirmOpen}
        title="确认归档"
        onClose={() => setArchiveConfirmOpen(false)}
        confirmLabel="归档"
        onConfirm={() => archiveMutation.mutate()}
      >
        归档后当前手稿将变为只读，确定继续吗？
        {session && session.stagedChangeCount > 0 && (
          <p style={{ marginTop: 8, color: 'var(--color-warning)' }}>
            注意：后端会自动提交全部 {session.stagedChangeCount} 项暂存修改。
          </p>
        )}
      </Dialog>

      <Dialog
        open={!!deleteConfirmId}
        title="确认删除"
        onClose={() => {
          setDeleteConfirmId(null);
          setDeleteTitleInput('');
        }}
        confirmLabel="删除"
        destructive
        onConfirm={() => {
          if (deleteConfirmId) deleteMutation.mutate(deleteConfirmId);
        }}
      >
        <p>此操作不可撤销。请输入手稿标题以确认删除：</p>
        <input
          type="text"
          value={deleteTitleInput}
          onChange={(e) => setDeleteTitleInput(e.target.value)}
          placeholder={selectedArchive?.title ?? ''}
          style={{
            width: '100%',
            marginTop: 12,
            padding: '8px 12px',
            borderRadius: 6,
            border: '1px solid var(--color-border)',
            background: 'var(--color-surface-2)',
          }}
        />
      </Dialog>
    </div>
  );
}

function ArchiveReader({
  manuscript,
}: {
  manuscript: { id: string; title: string; preview: string; paragraphCount: number };
}) {
  return (
    <div>
      <h2 style={{ fontSize: 20, fontWeight: 600, marginBottom: 16 }}>
        {manuscript.title}
      </h2>
      <p style={{ color: 'var(--color-text-muted)', lineHeight: 1.6 }}>
        {manuscript.preview}
      </p>
      <p
        style={{
          marginTop: 16,
          fontSize: 12,
          color: 'var(--color-text-subtle)',
        }}
      >
        {manuscript.paragraphCount} 段落 · 归档手稿
      </p>
    </div>
  );
}
