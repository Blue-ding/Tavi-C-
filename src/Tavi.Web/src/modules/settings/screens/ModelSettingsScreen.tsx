import { useState, useCallback } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useDocumentTitle } from '@/shared/hooks/useDocumentTitle';
import { queryKeys } from '@/shared/query';
import { settingsClient } from '../client/settingsClient';
import { Button } from '@/shared/ui/Button';
import { ErrorNotice } from '@/shared/ui/ErrorNotice';
import { LoadingSkeleton } from '@/shared/ui/LoadingSkeleton';
import { useToast } from '@/shared/ui/Toast';

export default function ModelSettingsScreen() {
  useDocumentTitle('模型设置');
  const toast = useToast();
  const queryClient = useQueryClient();

  const lmQuery = useQuery({
    queryKey: queryKeys.settings.languageModel,
    queryFn: () => settingsClient.getLanguageModel(),
    staleTime: 30_000,
  });

  const openaiQuery = useQuery({
    queryKey: queryKeys.settings.openAI,
    queryFn: () => settingsClient.getOpenAI(),
    staleTime: 30_000,
  });

  const [form, setForm] = useState({
    endpoint: '',
    model: '',
    apiKey: '',
    clientType: 'Chat',
    supportsRequiredToolChoice: false,
    enableThinking: null as boolean | null,
    maxToolRounds: 8,
    maxOutputRepairAttempts: 3,
    overallTimeoutSeconds: 120,
    toolCalls: 'Auto',
    nativeJsonOutput: 'Auto',
    streaming: 'Auto',
  });

  const isLoading = lmQuery.isLoading || openaiQuery.isLoading;

  const updateFormFromData = useCallback(() => {
    if (openaiQuery.data && lmQuery.data) {
      setForm({
        endpoint: openaiQuery.data.endpoint,
        model: openaiQuery.data.model,
        apiKey: '',
        clientType: openaiQuery.data.clientType,
        supportsRequiredToolChoice: openaiQuery.data.supportsRequiredToolChoice,
        enableThinking: openaiQuery.data.enableThinking ?? null,
        maxToolRounds: lmQuery.data.maxToolRounds,
        maxOutputRepairAttempts: lmQuery.data.maxOutputRepairAttempts,
        overallTimeoutSeconds: lmQuery.data.overallTimeoutSeconds,
        toolCalls: lmQuery.data.toolCalls,
        nativeJsonOutput: lmQuery.data.nativeJsonOutput,
        streaming: lmQuery.data.streaming,
      });
    }
  }, [openaiQuery.data, lmQuery.data]);

  const saveMutation = useMutation({
    mutationFn: () =>
      settingsClient.updateSettings({
        languageModel: {
          maxToolRounds: form.maxToolRounds,
          maxOutputRepairAttempts: form.maxOutputRepairAttempts,
          overallTimeoutSeconds: form.overallTimeoutSeconds,
          toolCalls: form.toolCalls,
          nativeJsonOutput: form.nativeJsonOutput,
          streaming: form.streaming,
        },
        openAI: {
          endpoint: form.endpoint,
          model: form.model,
          apiKey: form.apiKey || undefined,
          clientType: form.clientType,
          supportsRequiredToolChoice: form.supportsRequiredToolChoice,
          enableThinking: form.enableThinking,
        },
      }),
    onSuccess: (res) => {
      queryClient.setQueryData(queryKeys.settings.languageModel, res.languageModel);
      queryClient.setQueryData(queryKeys.settings.openAI, res.openAI);
      queryClient.invalidateQueries({ queryKey: queryKeys.guidance.availability });
      toast.show(res.message || '设置已保存', 'success');
      setForm((f) => ({ ...f, apiKey: '' }));
    },
    onError: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.settings.languageModel });
      queryClient.invalidateQueries({ queryKey: queryKeys.settings.openAI });
      toast.show('保存失败', 'error');
    },
  });

  if (isLoading) {
    return (
      <div>
        <h1 style={{ fontSize: 24, fontWeight: 600, marginBottom: 16 }}>模型设置</h1>
        <LoadingSkeleton />
      </div>
    );
  }

  if (lmQuery.error || openaiQuery.error) {
    return (
      <div>
        <h1 style={{ fontSize: 24, fontWeight: 600, marginBottom: 16 }}>模型设置</h1>
        <ErrorNotice
          message="无法加载设置"
          onRetry={() => {
            queryClient.invalidateQueries({ queryKey: queryKeys.settings.languageModel });
            queryClient.invalidateQueries({ queryKey: queryKeys.settings.openAI });
          }}
        />
      </div>
    );
  }

  const hasData = openaiQuery.data && lmQuery.data;
  if (hasData && form.endpoint === '' && openaiQuery.data.endpoint) {
    updateFormFromData();
  }

  return (
    <div style={{ maxWidth: 640 }}>
      <h1 style={{ fontSize: 24, fontWeight: 600, marginBottom: 24 }}>模型设置</h1>

      <section style={{ marginBottom: 32 }}>
        <h2 style={{ fontSize: 16, fontWeight: 600, marginBottom: 16 }}>OpenAI 连接</h2>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
          <Field label="Endpoint">
            <input
              type="text"
              value={form.endpoint}
              onChange={(e) => setForm({ ...form, endpoint: e.target.value })}
            />
          </Field>
          <Field label="Model *">
            <input
              type="text"
              value={form.model}
              onChange={(e) => setForm({ ...form, model: e.target.value })}
            />
          </Field>
          <Field label="API Key">
            <input
              type="password"
              value={form.apiKey}
              placeholder={openaiQuery.data?.hasApiKey ? '已配置' : '未配置'}
              onChange={(e) => setForm({ ...form, apiKey: e.target.value })}
            />
            <p style={{ fontSize: 12, color: 'var(--color-text-subtle)', marginTop: 4 }}>
              {openaiQuery.data?.hasApiKey ? '已保存密钥，留空表示保留现值' : '请输入 API Key'}
            </p>
          </Field>
          <Field label="Client Type">
            <select
              value={form.clientType}
              onChange={(e) => setForm({ ...form, clientType: e.target.value })}
            >
              <option value="Chat">Chat</option>
            </select>
          </Field>
          <label style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 14 }}>
            <input
              type="checkbox"
              checked={form.supportsRequiredToolChoice}
              onChange={(e) =>
                setForm({ ...form, supportsRequiredToolChoice: e.target.checked })
              }
            />
            Supports Required Tool Choice
          </label>
          <Field label="Enable Thinking">
            <select
              value={form.enableThinking === null ? '' : String(form.enableThinking)}
              onChange={(e) => {
                const v = e.target.value;
                setForm({
                  ...form,
                  enableThinking: v === '' ? null : v === 'true',
                });
              }}
            >
              <option value="">默认</option>
              <option value="true">启用</option>
              <option value="false">禁用</option>
            </select>
          </Field>
        </div>
      </section>

      <section style={{ marginBottom: 32 }}>
        <h2 style={{ fontSize: 16, fontWeight: 600, marginBottom: 16 }}>运行策略</h2>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
          <Field label="Max Tool Rounds">
            <input
              type="number"
              value={form.maxToolRounds}
              onChange={(e) =>
                setForm({ ...form, maxToolRounds: Number(e.target.value) })
              }
            />
          </Field>
          <Field label="Max Output Repair Attempts">
            <input
              type="number"
              value={form.maxOutputRepairAttempts}
              onChange={(e) =>
                setForm({ ...form, maxOutputRepairAttempts: Number(e.target.value) })
              }
            />
          </Field>
          <Field label="Overall Timeout Seconds">
            <input
              type="number"
              value={form.overallTimeoutSeconds}
              onChange={(e) =>
                setForm({ ...form, overallTimeoutSeconds: Number(e.target.value) })
              }
            />
          </Field>
          <Field label="Tool Calls">
            <select
              value={form.toolCalls}
              onChange={(e) => setForm({ ...form, toolCalls: e.target.value })}
            >
              <option value="Auto">Auto</option>
              <option value="Enabled">Enabled</option>
              <option value="Disabled">Disabled</option>
            </select>
          </Field>
          <Field label="Native JSON Output">
            <select
              value={form.nativeJsonOutput}
              onChange={(e) => setForm({ ...form, nativeJsonOutput: e.target.value })}
            >
              <option value="Auto">Auto</option>
              <option value="Enabled">Enabled</option>
              <option value="Disabled">Disabled</option>
            </select>
          </Field>
          <Field label="Streaming">
            <select
              value={form.streaming}
              onChange={(e) => setForm({ ...form, streaming: e.target.value })}
            >
              <option value="Auto">Auto</option>
              <option value="Enabled">Enabled</option>
              <option value="Disabled">Disabled</option>
            </select>
          </Field>
        </div>
      </section>

      <div style={{ display: 'flex', gap: 12 }}>
        <Button onClick={() => saveMutation.mutate()} disabled={saveMutation.isPending}>
          保存
        </Button>
        <Button variant="ghost" onClick={updateFormFromData}>
          重置
        </Button>
      </div>

      <p
        style={{
          marginTop: 24,
          fontSize: 12,
          color: 'var(--color-text-subtle)',
        }}
      >
        设置以最后一次保存为准，多个窗口同时修改时可能互相覆盖。
      </p>
    </div>
  );
}

function Field({
  label,
  children,
}: {
  label: string;
  children: React.ReactNode;
}) {
  return (
    <div>
      <label
        style={{
          display: 'block',
          fontSize: 12,
          marginBottom: 6,
          color: 'var(--color-text-muted)',
        }}
      >
        {label}
      </label>
      {children}
    </div>
  );
}
