import { useState, useCallback } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { useDocumentTitle } from '@/shared/hooks/useDocumentTitle';
import { queryKeys } from '@/shared/query';
import { performanceClient } from '../client/performanceClient';
import { Button } from '@/shared/ui/Button';
import { IconButton } from '@/shared/ui/IconButton';
import { EmptyState } from '@/shared/ui/EmptyState';
import { ErrorNotice } from '@/shared/ui/ErrorNotice';
import { LoadingSkeleton } from '@/shared/ui/LoadingSkeleton';
import { Tabs, TabPanel } from '@/shared/ui/Tabs';
import { useToast } from '@/shared/ui/Toast';
import { Theater, Plus, RotateCcw, RotateCw, Save, Archive, CheckCircle, XCircle } from 'lucide-react';

export default function PerformanceScreen() {
  useDocumentTitle('Performance');
  const toast = useToast();
  const queryClient = useQueryClient();
  const navigate = useNavigate();

  const [seed] = useState(() => {
    const stored = sessionStorage.getItem('performance-seed');
    if (stored) return Number(stored);
    const s = Math.floor(Math.random() * Number.MAX_SAFE_INTEGER);
    sessionStorage.setItem('performance-seed', String(s));
    return s;
  });

  const [tab, setTab] = useState('current');

  const workspaceQuery = useQuery({
    queryKey: queryKeys.performance.workspace(seed),
    queryFn: () => performanceClient.getWorkspace(seed),
    staleTime: 10_000,
  });

  const archivesQuery = useQuery({
    queryKey: queryKeys.performance.archives,
    queryFn: () => performanceClient.getArchives(),
    staleTime: 30_000,
    enabled: tab === 'history',
  });

  const workspace = workspaceQuery.data;
  const performance = workspace?.performance;

  const setWorkspaceData = useCallback(
    (data: NonNullable<typeof workspace>) => {
      queryClient.setQueryData(queryKeys.performance.workspace(seed), data);
    },
    [queryClient, seed]
  );

  const createBeatMutation = useMutation({
    mutationFn: (definitionId: string) => {
      if (!performance) throw new Error('No performance');
      return performanceClient.createBeat(definitionId, seed, performance.stateId);
    },
    onSuccess: (res) => {
      setWorkspaceData(res);
      toast.show('Beat 已创建', 'success');
    },
  });

  const resolveMutation = useMutation({
    mutationFn: ({ beatId, interaction }: { beatId: string; interaction: string }) => {
      if (!performance) throw new Error('No performance');
      return performanceClient.resolveBeat(beatId, interaction, seed, performance.stateId);
    },
    onSuccess: (res) => {
      setWorkspaceData(res);
      toast.show('Beat 已解决', 'success');
    },
  });

  const publishMutation = useMutation({
    mutationFn: (beatId: string) => {
      if (!performance) throw new Error('No performance');
      return performanceClient.publishBeat(
        beatId,
        performance.stateId,
        performance.stateId // placeholder; should fetch manuscript state
      );
    },
    onSuccess: (res) => {
      setWorkspaceData(res);
      toast.show('已发布到手稿', 'success');
    },
  });

  const completeMutation = useMutation({
    mutationFn: () => {
      if (!performance) throw new Error('No performance');
      return performanceClient.complete(performance.sourceScenarioStateId, performance.stateId);
    },
    onSuccess: (res) => {
      setWorkspaceData(res.workspace);
      queryClient.invalidateQueries({ queryKey: queryKeys.scenario.worldLink });
      toast.show('已结算', 'success');
    },
  });

  const abandonMutation = useMutation({
    mutationFn: () => {
      if (!performance) throw new Error('No performance');
      return performanceClient.abandon(performance.stateId);
    },
    onSuccess: (res) => {
      setWorkspaceData(res);
      toast.show('已放弃', 'success');
    },
  });

  const archiveMutation = useMutation({
    mutationFn: () => {
      if (!performance) throw new Error('No performance');
      return performanceClient.archive(performance.stateId);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.performance.archives });
      queryClient.invalidateQueries({ queryKey: queryKeys.performance.workspace(seed) });
      toast.show('已归档', 'success');
    },
  });

  const undoMutation = useMutation({
    mutationFn: () => {
      if (!performance) throw new Error('No performance');
      return performanceClient.undo(performance.stateId);
    },
    onSuccess: (res) => {
      setWorkspaceData(res);
      toast.show('已撤销', 'success');
    },
  });

  const redoMutation = useMutation({
    mutationFn: () => {
      if (!performance) throw new Error('No performance');
      return performanceClient.redo(performance.stateId);
    },
    onSuccess: (res) => {
      setWorkspaceData(res);
      toast.show('已重做', 'success');
    },
  });

  const saveMutation = useMutation({
    mutationFn: () => {
      if (!performance) throw new Error('No performance');
      return performanceClient.save(performance.stateId);
    },
    onSuccess: () => {
      toast.show('已保存', 'success');
    },
  });

  if (workspaceQuery.isLoading) {
    return (
      <div>
        <h1 style={{ fontSize: 24, fontWeight: 600, marginBottom: 16 }}>Performance</h1>
        <LoadingSkeleton />
      </div>
    );
  }

  if (workspaceQuery.error) {
    return (
      <div>
        <h1 style={{ fontSize: 24, fontWeight: 600, marginBottom: 16 }}>Performance</h1>
        <ErrorNotice
          message="无法加载 Performance 工作区"
          onRetry={() =>
            queryClient.invalidateQueries({ queryKey: queryKeys.performance.workspace(seed) })
          }
        />
      </div>
    );
  }

  if (!workspace?.hasSession) {
    return (
      <EmptyState
        icon={<Theater size={48} />}
        title="在 Scenario 中让支持 Performance 的 Scene 进入处理中。"
        action={
          <Button onClick={() => navigate('/scenario')}>前往 Scenario</Button>
        }
      />
    );
  }

  const bindingBeats = performance?.beats.filter((b) => b.state === 'Binding') ?? [];
  const processingBeats = performance?.beats.filter((b) => b.state === 'Processing') ?? [];
  const resolvedBeats = performance?.beats.filter((b) => b.state === 'Resolved') ?? [];
  const publishedBeats = performance?.beats.filter((b) => b.state === 'Published') ?? [];

  const status = performance?.status ?? 'Unknown';
  const isEnded = status === 'Completed' || status === 'Abandoned';

  return (
    <div style={{ display: 'flex', flexDirection: 'column', height: '100%' }}>
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: 12,
          marginBottom: 16,
          flexWrap: 'wrap',
        }}
      >
        <h1 style={{ fontSize: 24, fontWeight: 600 }}>Performance</h1>
        <span
          style={{
            fontSize: 12,
            padding: '2px 8px',
            borderRadius: 4,
            background: 'var(--color-surface-2)',
            color: 'var(--color-text-muted)',
          }}
        >
          {status}
        </span>
        <div style={{ flex: 1 }} />
        {!isEnded && (
          <>
            <IconButton
              label="撤销"
              onClick={() => undoMutation.mutate()}
              disabled={!workspace?.canUndo || undoMutation.isPending}
            >
              <RotateCcw size={16} />
            </IconButton>
            <IconButton
              label="重做"
              onClick={() => redoMutation.mutate()}
              disabled={!workspace?.canRedo || redoMutation.isPending}
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
            <Button variant="danger" onClick={() => abandonMutation.mutate()}>
              <XCircle size={16} /> 放弃
            </Button>
            <Button onClick={() => completeMutation.mutate()}>
              <CheckCircle size={16} /> 完成
            </Button>
          </>
        )}
        {isEnded && (
          <Button onClick={() => archiveMutation.mutate()}>
            <Archive size={16} /> 归档
          </Button>
        )}
      </div>

      <Tabs
        tabs={[
          { key: 'current', label: '当前' },
          { key: 'history', label: '历史' },
        ]}
        activeKey={tab}
        onChange={setTab}
      />

      <TabPanel active={tab === 'current'}>
        {isEnded ? (
          <div
            style={{
              padding: 32,
              textAlign: 'center',
              color: 'var(--color-text-muted)',
            }}
          >
            Performance 已结束（{status}）
          </div>
        ) : (
          <div style={{ display: 'flex', gap: 24, marginTop: 16 }}>
            <div style={{ width: 260, flexShrink: 0 }}>
              <h3
                style={{
                  fontSize: 12,
                  fontWeight: 600,
                  color: 'var(--color-text-subtle)',
                  marginBottom: 12,
                  textTransform: 'uppercase',
                }}
              >
                Beat Definitions
              </h3>
              {workspace?.definitions.map((def) => (
                <div
                  key={def.id}
                  style={{
                    padding: 12,
                    borderRadius: 8,
                    background: 'var(--color-surface-1)',
                    border: '1px solid var(--color-border)',
                    marginBottom: 8,
                  }}
                >
                  <div style={{ fontWeight: 500, marginBottom: 4 }}>
                    {def.name}
                  </div>
                  <div
                    style={{
                      fontSize: 12,
                      color: 'var(--color-text-muted)',
                      marginBottom: 8,
                    }}
                  >
                    {def.description}
                  </div>
                  <Button
                    size="sm"
                    onClick={() => createBeatMutation.mutate(def.id)}
                  >
                    <Plus size={14} /> 创建 Beat
                  </Button>
                </div>
              ))}
            </div>

            <div
              style={{
                flex: 1,
                display: 'flex',
                gap: 16,
                overflow: 'auto',
              }}
            >
              <BeatLane title="待绑定" beats={bindingBeats} />
              <BeatLane
                title="处理中"
                beats={processingBeats}
                onResolve={resolveMutation.mutate}
              />
              <BeatLane
                title="已解决"
                beats={resolvedBeats}
                onPublish={publishMutation.mutate}
              />
              <BeatLane title="已发布" beats={publishedBeats} />
            </div>
          </div>
        )}
      </TabPanel>

      <TabPanel active={tab === 'history'}>
        {archivesQuery.isLoading && <LoadingSkeleton />}
        <div style={{ display: 'flex', flexDirection: 'column', gap: 8, marginTop: 16 }}>
          {archivesQuery.data?.map((a) => (
            <div
              key={a.performanceId}
              style={{
                padding: 12,
                borderRadius: 8,
                background: 'var(--color-surface-1)',
                border: '1px solid var(--color-border)',
              }}
            >
              <div style={{ fontWeight: 500 }}>{a.status}</div>
              <div style={{ fontSize: 12, color: 'var(--color-text-subtle)' }}>
                {a.beatCount} Beats
              </div>
            </div>
          ))}
          {archivesQuery.data?.length === 0 && (
            <div style={{ textAlign: 'center', color: 'var(--color-text-subtle)', padding: 32 }}>
              无归档记录
            </div>
          )}
        </div>
      </TabPanel>
    </div>
  );
}

function BeatLane({
  title,
  beats,
  onResolve,
  onPublish,
}: {
  title: string;
  beats: Array<{
    id: string;
    name: string;
    description: string;
    state: string;
    paragraphs: Array<{ id: string; text: string }>;
    publication?: { manuscriptId: string } | null;
  }>;
  onResolve?: (input: { beatId: string; interaction: string }) => void;
  onPublish?: (beatId: string) => void;
}) {
  const [interaction, setInteraction] = useState('');
  const [activeBeatId, setActiveBeatId] = useState<string | null>(null);

  return (
    <div style={{ flex: 1, minWidth: 220 }}>
      <h3
        style={{
          fontSize: 12,
          fontWeight: 600,
          color: 'var(--color-text-subtle)',
          marginBottom: 12,
          textTransform: 'uppercase',
        }}
      >
        {title} ({beats.length})
      </h3>
      <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
        {beats.map((beat) => (
          <div
            key={beat.id}
            style={{
              padding: 12,
              borderRadius: 8,
              background: 'var(--color-surface-1)',
              border: '1px solid var(--color-border)',
            }}
          >
            <div style={{ fontWeight: 500, marginBottom: 4 }}>{beat.name}</div>
            <div
              style={{
                fontSize: 12,
                color: 'var(--color-text-muted)',
                marginBottom: 8,
              }}
            >
              {beat.description}
            </div>
            {onResolve && (
              <>
                <textarea
                  value={activeBeatId === beat.id ? interaction : ''}
                  onChange={(e) => {
                    setActiveBeatId(beat.id);
                    setInteraction(e.target.value);
                  }}
                  placeholder="输入 interaction..."
                  rows={3}
                  style={{
                    width: '100%',
                    padding: 8,
                    borderRadius: 6,
                    border: '1px solid var(--color-border)',
                    background: 'var(--color-surface-2)',
                    marginBottom: 8,
                  }}
                />
                <Button
                  size="sm"
                  onClick={() => {
                    onResolve({ beatId: beat.id, interaction });
                    setInteraction('');
                    setActiveBeatId(null);
                  }}
                >
                  解决
                </Button>
              </>
            )}
            {beat.paragraphs.length > 0 && (
              <div style={{ marginTop: 8 }}>
                {beat.paragraphs.map((p) => (
                  <p
                    key={p.id}
                    style={{
                      fontSize: 13,
                      lineHeight: 1.5,
                      color: 'var(--color-text-muted)',
                      marginBottom: 4,
                    }}
                  >
                    {p.text}
                  </p>
                ))}
              </div>
            )}
            {onPublish && !beat.publication && (
              <Button
                size="sm"
                style={{ marginTop: 8 }}
                onClick={() => onPublish(beat.id)}
              >
                发布到手稿
              </Button>
            )}
            {beat.publication && (
              <div
                style={{
                  marginTop: 8,
                  fontSize: 12,
                  color: 'var(--color-success)',
                }}
              >
                已发布
              </div>
            )}
          </div>
        ))}
        {beats.length === 0 && (
          <div
            style={{
              padding: 24,
              textAlign: 'center',
              color: 'var(--color-text-subtle)',
              fontSize: 13,
            }}
          >
            无 Beat
          </div>
        )}
      </div>
    </div>
  );
}
