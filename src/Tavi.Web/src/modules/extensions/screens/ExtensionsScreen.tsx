import { useState, useMemo, useCallback } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useDocumentTitle } from '@/shared/hooks/useDocumentTitle';
import { queryKeys } from '@/shared/query';
import { extensionsClient } from '../client/extensionsClient';
import type { ExtensionModuleViewModel } from '../client/extensionsSchemas';
import { Button } from '@/shared/ui/Button';
import { ErrorNotice } from '@/shared/ui/ErrorNotice';
import { LoadingSkeleton } from '@/shared/ui/LoadingSkeleton';
import { useToast } from '@/shared/ui/Toast';
import { Dialog } from '@/shared/ui/Dialog';

export default function ExtensionsScreen() {
  useDocumentTitle('Extensions');
  const toast = useToast();
  const queryClient = useQueryClient();

  const workspaceQuery = useQuery({
    queryKey: queryKeys.extensions.workspace,
    queryFn: () => extensionsClient.getWorkspace(),
    staleTime: 30_000,
  });

  const [draft, setDraft] = useState<ExtensionModuleViewModel[]>([]);
  const [search, setSearch] = useState('');
  const [conflictOpen, setConflictOpen] = useState(false);

  const workspace = workspaceQuery.data;

  const filtered = useMemo(() => {
    const list = draft.length > 0 ? draft : workspace?.modules ?? [];
    if (!search.trim()) return list;
    const q = search.toLowerCase();
    return list.filter(
      (m) =>
        m.name.toLowerCase().includes(q) ||
        m.id.toLowerCase().includes(q)
    );
  }, [draft, workspace, search]);

  const changed = useMemo(() => {
    if (!workspace || draft.length === 0) return false;
    return draft.some((m, i) => {
      const original = workspace.modules[i];
      return (
        !original ||
        m.desiredEnabled !== original.desiredEnabled ||
        JSON.stringify(m.desiredSettings) !== JSON.stringify(original.desiredSettings)
      );
    });
  }, [draft, workspace]);

  const updateModule = useCallback(
    (id: string, patch: Partial<ExtensionModuleViewModel>) => {
      setDraft((prev) => {
        const list = prev.length > 0 ? [...prev] : [...(workspace?.modules ?? [])];
        const idx = list.findIndex((m) => m.id === id);
        if (idx >= 0) {
          list[idx] = { ...list[idx], ...patch };
        }
        return list;
      });
    },
    [workspace]
  );

  const saveMutation = useMutation({
    mutationFn: () => {
      const modules = (draft.length > 0 ? draft : workspace?.modules ?? []).map(
        ({ id, desiredEnabled, desiredSettings }) => ({
          id,
          enabled: desiredEnabled,
          settings: desiredSettings,
        })
      );
      return extensionsClient.updateSettings(workspace!.revision, modules);
    },
    onSuccess: (res) => {
      queryClient.setQueryData(queryKeys.extensions.workspace, res);
      setDraft([]);
      toast.show('配置已保存', 'success');
    },
    onError: (err: Error) => {
      if (err.message.includes('409') || err.message.includes('Conflict')) {
        setConflictOpen(true);
      }
      toast.show('保存失败', 'error');
    },
  });

  if (workspaceQuery.isLoading) {
    return (
      <div>
        <h1 style={{ fontSize: 24, fontWeight: 600, marginBottom: 16 }}>Extensions</h1>
        <LoadingSkeleton />
      </div>
    );
  }

  if (workspaceQuery.error) {
    return (
      <div>
        <h1 style={{ fontSize: 24, fontWeight: 600, marginBottom: 16 }}>Extensions</h1>
        <ErrorNotice
          message="无法加载 Extension 配置"
          onRetry={() =>
            queryClient.invalidateQueries({ queryKey: queryKeys.extensions.workspace })
          }
        />
      </div>
    );
  }

  return (
    <div>
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: 12,
          marginBottom: 16,
        }}
      >
        <h1 style={{ fontSize: 24, fontWeight: 600 }}>Extensions</h1>
        <div style={{ flex: 1 }} />
        <input
          type="text"
          placeholder="搜索 Module..."
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          style={{
            padding: '8px 12px',
            borderRadius: 6,
            border: '1px solid var(--color-border)',
            background: 'var(--color-surface-2)',
            color: 'var(--color-text)',
            width: 220,
          }}
        />
      </div>

      {workspace?.restartRequired && (
        <div
          style={{
            padding: '12px 16px',
            borderRadius: 8,
            background: 'rgb(216 182 108 / 12%)',
            border: '1px solid rgb(216 182 108 / 20%)',
            color: 'var(--color-warning)',
            marginBottom: 16,
            fontSize: 13,
          }}
        >
          配置已更改，需要重启后生效
        </div>
      )}

      <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
        {filtered.map((mod) => (
          <div
            key={mod.id}
            style={{
              padding: 16,
              borderRadius: 10,
              background: 'var(--color-surface-1)',
              border: '1px solid var(--color-border)',
            }}
          >
            <div
              style={{
                display: 'flex',
                alignItems: 'center',
                gap: 12,
                marginBottom: 8,
              }}
            >
              <span style={{ fontWeight: 600 }}>{mod.name}</span>
              <span
                style={{
                  fontSize: 12,
                  color: 'var(--color-text-subtle)',
                }}
              >
                {mod.id} @ {mod.version}
              </span>
              <div style={{ flex: 1 }} />
              <label
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: 6,
                  fontSize: 13,
                  cursor: 'pointer',
                }}
              >
                <input
                  type="checkbox"
                  checked={mod.desiredEnabled}
                  onChange={(e) =>
                    updateModule(mod.id, {
                      desiredEnabled: e.target.checked,
                    })
                  }
                />
                启用
              </label>
            </div>
            <p
              style={{
                fontSize: 13,
                color: 'var(--color-text-muted)',
                marginBottom: 8,
              }}
            >
              {mod.description}
            </p>
            {mod.dependencies.length > 0 && (
              <div
                style={{
                  fontSize: 12,
                  color: 'var(--color-text-subtle)',
                  marginBottom: 8,
                }}
              >
                依赖: {mod.dependencies.map((d) => `${d.id} >= ${d.minimumVersion}`).join(', ')}
              </div>
            )}
            <SettingsEditor
              schema={mod.settingsSchema}
              value={mod.desiredSettings}
              onChange={(v) => updateModule(mod.id, { desiredSettings: v })}
            />
          </div>
        ))}
      </div>

      {filtered.length === 0 && (
        <p style={{ color: 'var(--color-text-subtle)', textAlign: 'center', padding: 32 }}>
          当前部署没有发现可配置 Module。
        </p>
      )}

      {changed && (
        <div
          style={{
            position: 'sticky',
            bottom: 16,
            display: 'flex',
            justifyContent: 'flex-end',
            gap: 12,
            marginTop: 16,
          }}
        >
          <Button
            variant="ghost"
            onClick={() => {
              setDraft([]);
            }}
          >
            放弃
          </Button>
          <Button
            onClick={() => saveMutation.mutate()}
            disabled={saveMutation.isPending}
          >
            保存配置
          </Button>
        </div>
      )}

      <Dialog
        open={conflictOpen}
        title="配置冲突"
        onClose={() => setConflictOpen(false)}
        confirmLabel="刷新"
        onConfirm={() => {
          setConflictOpen(false);
          setDraft([]);
          queryClient.invalidateQueries({ queryKey: queryKeys.extensions.workspace });
        }}
      >
        配置已被其他会话修改，请刷新后重试。
      </Dialog>
    </div>
  );
}

function SettingsEditor({
  schema,
  value,
  onChange,
}: {
  schema: unknown;
  value: unknown;
  onChange: (v: unknown) => void;
}) {
  const safeValue = value ?? {};
  const [jsonMode, setJsonMode] = useState(false);
  const [jsonText, setJsonText] = useState(() => JSON.stringify(safeValue, null, 2));

  const parsedSchema = schema as
    | {
        type?: string;
        properties?: Record<string, { type?: string; description?: string; default?: unknown }>;
        required?: string[];
      }
    | undefined;

  if (!parsedSchema || typeof parsedSchema !== 'object') {
    return (
      <textarea
        value={jsonText}
        onChange={(e) => {
          setJsonText(e.target.value);
          try {
            onChange(JSON.parse(e.target.value));
          } catch {
            /* ignore parse errors while typing */
          }
        }}
        rows={6}
        style={{
          width: '100%',
          padding: 8,
          borderRadius: 6,
          border: '1px solid var(--color-border)',
          background: 'var(--color-surface-2)',
          fontFamily: 'monospace',
          fontSize: 12,
        }}
      />
    );
  }

  const properties = parsedSchema.properties ?? {};

  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'flex-end', marginBottom: 8 }}>
        <button
          onClick={() => {
            if (jsonMode) {
              try {
                onChange(JSON.parse(jsonText));
              } catch {
                return;
              }
            } else {
              setJsonText(JSON.stringify(safeValue, null, 2));
            }
            setJsonMode(!jsonMode);
          }}
          style={{ fontSize: 12, color: 'var(--color-accent)' }}
        >
          {jsonMode ? '表单模式' : 'JSON 模式'}
        </button>
      </div>
      {jsonMode ? (
        <textarea
          value={jsonText}
          onChange={(e) => setJsonText(e.target.value)}
          onBlur={() => {
            try {
              onChange(JSON.parse(jsonText));
            } catch {
              /* keep invalid text */
            }
          }}
          rows={6}
          style={{
            width: '100%',
            padding: 8,
            borderRadius: 6,
            border: '1px solid var(--color-border)',
            background: 'var(--color-surface-2)',
            fontFamily: 'monospace',
            fontSize: 12,
          }}
        />
      ) : (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
          {Object.entries(properties).map(([key, prop]) => {
            const val = (safeValue as Record<string, unknown>)[key];
            return (
              <div key={key}>
                <label
                  style={{
                    display: 'block',
                    fontSize: 12,
                    marginBottom: 4,
                    color: 'var(--color-text-muted)',
                  }}
                >
                  {key}
                  {parsedSchema.required?.includes(key) && ' *'}
                </label>
                {prop.type === 'boolean' ? (
                  <label style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                    <input
                      type="checkbox"
                      checked={Boolean(val)}
                      onChange={(e) =>
                        onChange({ ...safeValue, [key]: e.target.checked })
                      }
                    />
                    {prop.description}
                  </label>
                ) : prop.type === 'number' || prop.type === 'integer' ? (
                  <input
                    type="number"
                    value={val as number}
                    onChange={(e) =>
                      onChange({
                        ...safeValue,
                        [key]: Number(e.target.value),
                      })
                    }
                    style={{
                      width: '100%',
                      padding: '6px 10px',
                      borderRadius: 6,
                      border: '1px solid var(--color-border)',
                      background: 'var(--color-surface-2)',
                    }}
                  />
                ) : (
                  <input
                    type="text"
                    value={String(val ?? '')}
                    onChange={(e) =>
                      onChange({ ...safeValue, [key]: e.target.value })
                    }
                    style={{
                      width: '100%',
                      padding: '6px 10px',
                      borderRadius: 6,
                      border: '1px solid var(--color-border)',
                      background: 'var(--color-surface-2)',
                    }}
                  />
                )}
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}
