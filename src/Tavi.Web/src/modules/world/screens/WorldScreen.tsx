import { useState, useMemo, useCallback } from 'react';
import { useDocumentTitle } from '@/shared/hooks/useDocumentTitle';
import { useHotkeys } from '@/shared/hooks/useHotkeys';
import { useWorldWorkspace } from '../hooks/useWorldWorkspace';
import { Button } from '@/shared/ui/Button';
import { IconButton } from '@/shared/ui/IconButton';
import { Dialog } from '@/shared/ui/Dialog';
import { EmptyState } from '@/shared/ui/EmptyState';
import { ErrorNotice } from '@/shared/ui/ErrorNotice';
import { LoadingSkeleton } from '@/shared/ui/LoadingSkeleton';
import { useToast } from '@/shared/ui/Toast';
import { Globe, Plus, Search, RotateCcw, RotateCw, Save, Trash2, Edit3 } from 'lucide-react';

export default function WorldScreen() {
  useDocumentTitle('World');
  const toast = useToast();
  const {
    workspace,
    isLoading,
    error,
    types,
    addElement,
    updateElement,
    deleteElement,
    commit,
    undo,
    redo,
    save,
    isCommitting,
    isSaving,
    isUndoing,
    isRedoing,
    invalidate,
  } = useWorldWorkspace();

  const [search, setSearch] = useState('');
  const [createOpen, setCreateOpen] = useState(false);
  const [editId, setEditId] = useState<string | null>(null);
  const [deleteId, setDeleteId] = useState<string | null>(null);
  const [form, setForm] = useState({ name: '', description: '', type: '' });

  const elements = useMemo(() => {
    if (!workspace) return [];
    if (!search.trim()) return workspace.elements;
    const q = search.toLowerCase();
    return workspace.elements.filter(
      (e) =>
        e.name.toLowerCase().includes(q) ||
        e.type.toLowerCase().includes(q) ||
        e.description.toLowerCase().includes(q)
    );
  }, [workspace, search]);

  const stagedValid = useMemo(
    () => workspace?.stagedChanges.filter((c) => c.status === 'Valid') ?? [],
    [workspace]
  );
  const stagedConflict = useMemo(
    () => workspace?.stagedChanges.filter((c) => c.status === 'Conflict') ?? [],
    [workspace]
  );
  const stagedInvalid = useMemo(
    () => workspace?.stagedChanges.filter((c) => c.status === 'Invalid') ?? [],
    [workspace]
  );

  const resetForm = useCallback(() => {
    setForm({ name: '', description: '', type: '' });
  }, []);

  const openCreate = useCallback(() => {
    resetForm();
    setCreateOpen(true);
  }, [resetForm]);

  const openEdit = useCallback(
    (id: string) => {
      const el = workspace?.elements.find((e) => e.id === id);
      if (!el) return;
      setForm({ name: el.name, description: el.description, type: el.type });
      setEditId(id);
    },
    [workspace]
  );

  const handleSaveElement = useCallback(async () => {
    try {
      if (editId) {
        await updateElement({ id: editId, ...form });
        toast.show('已更新', 'success');
        setEditId(null);
      } else {
        await addElement(form);
        toast.show('已创建', 'success');
        setCreateOpen(false);
        resetForm();
      }
    } catch {
      toast.show('操作失败', 'error');
    }
  }, [editId, form, updateElement, addElement, toast, resetForm]);

  const handleDelete = useCallback(async () => {
    if (!deleteId) return;
    try {
      await deleteElement(deleteId);
      toast.show('已删除', 'success');
      setDeleteId(null);
    } catch {
      toast.show('删除失败', 'error');
    }
  }, [deleteId, deleteElement, toast]);

  const handleCommit = useCallback(async () => {
    try {
      await commit(stagedValid.map((c) => c.id));
      toast.show(`已提交 ${stagedValid.length} 项`, 'success');
    } catch {
      toast.show('提交失败', 'error');
    }
  }, [commit, stagedValid, toast]);

  const handleUndo = useCallback(async () => {
    try {
      await undo();
      toast.show('已撤销', 'success');
    } catch {
      toast.show('撤销失败', 'error');
    }
  }, [undo, toast]);

  const handleRedo = useCallback(async () => {
    try {
      await redo();
      toast.show('已重做', 'success');
    } catch {
      toast.show('重做失败', 'error');
    }
  }, [redo, toast]);

  const handleSave = useCallback(async () => {
    try {
      await save();
      toast.show('已保存', 'success');
    } catch {
      toast.show('保存失败', 'error');
    }
  }, [save, toast]);

  useHotkeys({
    'Ctrl+S': {
      action: handleSave,
      predicate: () => !isSaving,
    },
    'Ctrl+Z': {
      action: handleUndo,
      predicate: () => workspace?.canUndo === true && !isUndoing,
    },
    'Ctrl+Shift+Z': {
      action: handleRedo,
      predicate: () => workspace?.canRedo === true && !isRedoing,
    },
  });

  if (isLoading) {
    return (
      <div>
        <h1 style={{ fontSize: 24, fontWeight: 600, marginBottom: 16 }}>World</h1>
        <LoadingSkeleton />
      </div>
    );
  }

  if (error) {
    return (
      <div>
        <h1 style={{ fontSize: 24, fontWeight: 600, marginBottom: 16 }}>World</h1>
        <ErrorNotice message="无法加载 World 工作区" onRetry={invalidate} />
      </div>
    );
  }

  const isFormValid = form.name.trim().length > 0 && form.type.trim().length > 0;

  return (
    <div style={{ display: 'flex', flexDirection: 'column', height: '100%' }}>
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: 12,
          marginBottom: 16,
          flexWrap: 'wrap',
        }}>
        <h1 style={{ fontSize: 24, fontWeight: 600 }}>World</h1>
        <div style={{ flex: 1 }} />
        <div style={{ position: 'relative' }}>
          <Search
            size={16}
            style={{
              position: 'absolute',
              left: 10,
              top: '50%',
              transform: 'translateY(-50%)',
              color: 'var(--color-text-subtle)',
            }}
          />
          <input
            type="text"
            placeholder="搜索…"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            style={{
              padding: '8px 12px 8px 34px',
              borderRadius: 6,
              border: '1px solid var(--color-border)',
              background: 'var(--color-surface-2)',
              color: 'var(--color-text)',
              width: 220,
            }}
          />
        </div>
        <Button onClick={openCreate}>
          <Plus size={16} /> 新建 Element
        </Button>
        <IconButton
          label="撤销 (Ctrl+Z)"
          onClick={handleUndo}
          disabled={!workspace?.canUndo || isUndoing}
        >
          <RotateCcw size={16} />
        </IconButton>
        <IconButton
          label="重做 (Ctrl+Shift+Z)"
          onClick={handleRedo}
          disabled={!workspace?.canRedo || isRedoing}
        >
          <RotateCw size={16} />
        </IconButton>
        <IconButton
          label="保存 (Ctrl+S)"
          onClick={handleSave}
          disabled={isSaving}
        >
          <Save size={16} />
        </IconButton>
        {workspace?.isDirty && (
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

      {workspace && workspace.elements.length === 0 ? (
        <EmptyState
          icon={<Globe size={48} />}
          title="从一个角色、地点或概念开始构筑世界。"
          action={<Button onClick={openCreate}>新建 Element</Button>}
        />
      ) : (
        <div
          style={{
            flex: 1,
            overflow: 'auto',
            border: '1px solid var(--color-border)',
            borderRadius: 10,
            background: 'var(--color-surface-1)',
          }}>
          <table style={{ width: '100%', borderCollapse: 'collapse' }}>
            <thead>
              <tr style={{ borderBottom: '1px solid var(--color-border)' }}>
                <th
                  style={{
                    textAlign: 'left',
                    padding: '12px 16px',
                    color: 'var(--color-text-subtle)',
                    fontWeight: 500,
                    fontSize: 12,
                  }}
                >
                  名称
                </th>
                <th
                  style={{
                    textAlign: 'left',
                    padding: '12px 16px',
                    color: 'var(--color-text-subtle)',
                    fontWeight: 500,
                    fontSize: 12,
                  }}
                >
                  类型
                </th>
                <th
                  style={{
                    textAlign: 'left',
                    padding: '12px 16px',
                    color: 'var(--color-text-subtle)',
                    fontWeight: 500,
                    fontSize: 12,
                  }}
                >
                  说明
                </th>
                <th style={{ width: 80 }}></th>
              </tr>
            </thead>
            <tbody>
              {elements.map((el) => (
                <tr
                  key={el.id}
                  style={{
                    borderBottom: '1px solid var(--color-border)',
                    transition: 'background 0.15s',
                  }}
                  onMouseEnter={(e) =>
                    (e.currentTarget.style.background = 'var(--color-surface-2)')
                  }
                  onMouseLeave={(e) =>
                    (e.currentTarget.style.background = 'transparent')
                  }
                >
                  <td style={{ padding: '10px 16px' }}>{el.name}</td>
                  <td style={{ padding: '10px 16px' }}>
                    <span
                      style={{
                        display: 'inline-block',
                        padding: '2px 8px',
                        borderRadius: 4,
                        background: 'var(--color-surface-3)',
                        fontSize: 12,
                        color: 'var(--color-text-muted)',
                      }}
                    >
                      {el.type}
                    </span>
                  </td>
                  <td
                    style={{
                      padding: '10px 16px',
                      color: 'var(--color-text-muted)',
                      maxWidth: 400,
                      overflow: 'hidden',
                      textOverflow: 'ellipsis',
                      whiteSpace: 'nowrap',
                    }}
                  >
                    {el.description}
                  </td>
                  <td style={{ padding: '10px 16px' }}>
                    <div style={{ display: 'flex', gap: 4 }}>
                      <IconButton
                        label="编辑"
                        size="sm"
                        onClick={() => openEdit(el.id)}
                      >
                        <Edit3 size={14} />
                      </IconButton>
                      <IconButton
                        label="删除"
                        size="sm"
                        onClick={() => setDeleteId(el.id)}
                      >
                        <Trash2 size={14} />
                      </IconButton>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {/* Staging bar */}
      {workspace && workspace.stagedChanges.length > 0 && (
        <div
          style={{
            marginTop: 16,
            padding: '12px 16px',
            borderRadius: 10,
            background: 'var(--color-surface-1)',
            border: '1px solid var(--color-border)',
            display: 'flex',
            alignItems: 'center',
            gap: 12,
          }}
        >
          <span style={{ fontWeight: 500 }}>
            暂存栏：{workspace.stagedChanges.length} 项
          </span>
          {stagedValid.length > 0 && (
            <span style={{ color: 'var(--color-success)', fontSize: 12 }}>
              有效 {stagedValid.length}
            </span>
          )}
          {stagedConflict.length > 0 && (
            <span style={{ color: 'var(--color-danger)', fontSize: 12 }}>
              冲突 {stagedConflict.length}
            </span>
          )}
          {stagedInvalid.length > 0 && (
            <span style={{ color: 'var(--color-warning)', fontSize: 12 }}>
              无效 {stagedInvalid.length}
            </span>
          )}
          <div style={{ flex: 1 }} />
          <Button
            onClick={handleCommit}
            disabled={stagedValid.length === 0 || isCommitting}
          >
            提交 {stagedValid.length} 项
          </Button>
        </div>
      )}

      {/* Create Dialog */}
      <Dialog
        open={createOpen}
        title="新建 Element"
        onClose={() => {
          setCreateOpen(false);
          resetForm();
        }}
        confirmLabel="创建"
        cancelLabel="取消"
        onConfirm={isFormValid ? handleSaveElement : undefined}
      >
        <ElementForm
          form={form}
          onChange={setForm}
          types={types?.elementTypes.map((t) => t.key) ?? []}
        />
      </Dialog>

      {/* Edit Dialog */}
      <Dialog
        open={!!editId}
        title="编辑 Element"
        onClose={() => {
          setEditId(null);
          resetForm();
        }}
        confirmLabel="保存"
        cancelLabel="取消"
        onConfirm={isFormValid ? handleSaveElement : undefined}
      >
        <ElementForm
          form={form}
          onChange={setForm}
          types={types?.elementTypes.map((t) => t.key) ?? []}
        />
      </Dialog>

      {/* Delete Dialog */}
      <Dialog
        open={!!deleteId}
        title="确认删除"
        onClose={() => setDeleteId(null)}
        confirmLabel="删除"
        cancelLabel="取消"
        onConfirm={handleDelete}
        destructive
      >
        确定要删除这个 Element 吗？此操作不可撤销。
      </Dialog>
    </div>
  );
}

function ElementForm({
  form,
  onChange,
  types,
}: {
  form: { name: string; description: string; type: string };
  onChange: (f: typeof form) => void;
  types: string[];
}) {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
      <div>
        <label style={{ display: 'block', fontSize: 12, marginBottom: 6, color: 'var(--color-text-muted)' }}>
          名称 *
        </label>
        <input
          type="text"
          value={form.name}
          onChange={(e) => onChange({ ...form, name: e.target.value })}
          style={{
            width: '100%',
            padding: '8px 12px',
            borderRadius: 6,
            border: '1px solid var(--color-border)',
            background: 'var(--color-surface-2)',
          }}
        />
      </div>
      <div>
        <label style={{ display: 'block', fontSize: 12, marginBottom: 6, color: 'var(--color-text-muted)' }}>
          类型 *
        </label>
        <input
          type="text"
          list="element-types"
          value={form.type}
          onChange={(e) => onChange({ ...form, type: e.target.value })}
          style={{
            width: '100%',
            padding: '8px 12px',
            borderRadius: 6,
            border: '1px solid var(--color-border)',
            background: 'var(--color-surface-2)',
          }}
        />
        <datalist id="element-types">
          {types.map((t) => (
            <option key={t} value={t} />
          ))}
        </datalist>
      </div>
      <div>
        <label style={{ display: 'block', fontSize: 12, marginBottom: 6, color: 'var(--color-text-muted)' }}>
          说明
        </label>
        <textarea
          value={form.description}
          onChange={(e) => onChange({ ...form, description: e.target.value })}
          rows={4}
          style={{
            width: '100%',
            padding: '8px 12px',
            borderRadius: 6,
            border: '1px solid var(--color-border)',
            background: 'var(--color-surface-2)',
            resize: 'vertical',
          }}
        />
      </div>
    </div>
  );
}
