import { useState, useMemo, useCallback } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useDocumentTitle } from '@/shared/hooks/useDocumentTitle';
import { queryKeys } from '@/shared/query';
import { scenarioClient } from '../client/scenarioClient';
import { Button } from '@/shared/ui/Button';
import { IconButton } from '@/shared/ui/IconButton';
import { Dialog } from '@/shared/ui/Dialog';
import { ErrorNotice } from '@/shared/ui/ErrorNotice';
import { LoadingSkeleton } from '@/shared/ui/LoadingSkeleton';
import { useToast } from '@/shared/ui/Toast';
import { Plus, RotateCcw, RotateCw, Save, Trash2, Play } from 'lucide-react';

export default function ScenarioScreen() {
  useDocumentTitle('Scenario');
  const toast = useToast();
  const queryClient = useQueryClient();

  const [seed] = useState(() => {
    const stored = sessionStorage.getItem('scenario-definition-seed');
    if (stored) return Number(stored);
    const s = Math.floor(Math.random() * Number.MAX_SAFE_INTEGER);
    sessionStorage.setItem('scenario-definition-seed', String(s));
    return s;
  });

  const workspaceQuery = useQuery({
    queryKey: queryKeys.scenario.workspace(seed),
    queryFn: () => scenarioClient.getWorkspace(seed),
    staleTime: 10_000,
  });

  const worldLinkQuery = useQuery({
    queryKey: queryKeys.scenario.worldLink,
    queryFn: () => scenarioClient.getWorldLink(),
    staleTime: 10_000,
  });

  const workspace = workspaceQuery.data;
  const worldLink = worldLinkQuery.data;

  const [selectedDefId, setSelectedDefId] = useState<string | null>(null);
  const [deleteSceneId, setDeleteSceneId] = useState<string | null>(null);

  const bindingScenes = useMemo(
    () => workspace?.scenes.filter((s) => s.state === 'Binding') ?? [],
    [workspace]
  );
  const processingScenes = useMemo(
    () => workspace?.scenes.filter((s) => s.state === 'Processing') ?? [],
    [workspace]
  );
  const settledScenes = useMemo(
    () => workspace?.scenes.filter((s) => s.state === 'Settled') ?? [],
    [workspace]
  );

  const setWorkspaceData = useCallback(
    (data: NonNullable<typeof workspace>) => {
      queryClient.setQueryData(queryKeys.scenario.workspace(seed), data);
    },
    [queryClient, seed]
  );

  const createSceneMutation = useMutation({
    mutationFn: (definitionId: string) => {
      if (!workspace) throw new Error('No workspace');
      return scenarioClient.createScene(workspace.stateId, definitionId, seed);
    },
    onSuccess: (res) => {
      setWorkspaceData(res);
      toast.show('Scene 已创建', 'success');
    },
  });

  const deleteSceneMutation = useMutation({
    mutationFn: (sceneId: string) => {
      if (!workspace) throw new Error('No workspace');
      return scenarioClient.deleteScene(sceneId, workspace.stateId);
    },
    onSuccess: (res) => {
      setWorkspaceData(res);
      setDeleteSceneId(null);
      toast.show('Scene 已删除', 'success');
    },
  });

  const startProcessingMutation = useMutation({
    mutationFn: (sceneId: string) => {
      if (!workspace) throw new Error('No workspace');
      return scenarioClient.startProcessing(sceneId, workspace.stateId);
    },
    onSuccess: (res) => {
      setWorkspaceData(res);
      toast.show('已开始处理', 'success');
    },
  });

  const settleRulesMutation = useMutation({
    mutationFn: (sceneId: string) => {
      if (!workspace) throw new Error('No workspace');
      return scenarioClient.settleRules(sceneId, workspace.stateId, seed);
    },
    onSuccess: (res) => {
      setWorkspaceData(res);
      toast.show('已结算', 'success');
    },
  });

  const undoMutation = useMutation({
    mutationFn: () => {
      if (!workspace) throw new Error('No workspace');
      return scenarioClient.undo(workspace.stateId);
    },
    onSuccess: (res) => {
      setWorkspaceData(res);
      toast.show('已撤销', 'success');
    },
  });

  const redoMutation = useMutation({
    mutationFn: () => {
      if (!workspace) throw new Error('No workspace');
      return scenarioClient.redo(workspace.stateId);
    },
    onSuccess: (res) => {
      setWorkspaceData(res);
      toast.show('已重做', 'success');
    },
  });

  const saveMutation = useMutation({
    mutationFn: () => {
      if (!workspace) throw new Error('No workspace');
      return scenarioClient.save(workspace.stateId);
    },
    onSuccess: () => {
      toast.show('已保存', 'success');
    },
  });

  if (workspaceQuery.isLoading) {
    return (
      <div>
        <h1 style={{ fontSize: 24, fontWeight: 600, marginBottom: 16 }}>Scenario</h1>
        <LoadingSkeleton />
      </div>
    );
  }

  if (workspaceQuery.error) {
    return (
      <div>
        <h1 style={{ fontSize: 24, fontWeight: 600, marginBottom: 16 }}>Scenario</h1>
        <ErrorNotice
          message="无法加载 Scenario 工作区"
          onRetry={() =>
            queryClient.invalidateQueries({ queryKey: queryKeys.scenario.workspace(seed) })
          }
        />
      </div>
    );
  }

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
        <h1 style={{ fontSize: 24, fontWeight: 600 }}>Scenario</h1>
        <div style={{ flex: 1 }} />
        {worldLink && (
          <span style={{ fontSize: 12, color: 'var(--color-text-subtle)' }}>
            World: {worldLink.state} · {worldLink.bindingScenes} 待绑定 ·{' '}
            {worldLink.processingScenes} 处理中 · {worldLink.settledScenes} 已结算
          </span>
        )}
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
      </div>

      <div style={{ display: 'flex', gap: 24, flex: 1, minHeight: 0 }}>
        {/* Definitions */}
        <div
          style={{
            width: 280,
            flexShrink: 0,
            display: 'flex',
            flexDirection: 'column',
            gap: 8,
            overflow: 'auto',
          }}
        >
          <h3
            style={{
              fontSize: 12,
              fontWeight: 600,
              color: 'var(--color-text-subtle)',
              textTransform: 'uppercase',
            }}
          >
            Scene Definitions
          </h3>
          {workspace?.definitions.map((def) => (
            <div
              key={def.id}
              style={{
                padding: 12,
                borderRadius: 8,
                background:
                  selectedDefId === def.id
                    ? 'var(--color-surface-2)'
                    : 'var(--color-surface-1)',
                border: '1px solid var(--color-border)',
                cursor: 'pointer',
              }}
              onClick={() => setSelectedDefId(def.id)}
            >
              <div style={{ fontWeight: 500, marginBottom: 4 }}>{def.name}</div>
              <div
                style={{
                  fontSize: 12,
                  color: 'var(--color-text-subtle)',
                  marginBottom: 4,
                }}
              >
                {def.module}
              </div>
              <div
                style={{
                  fontSize: 12,
                  color: 'var(--color-text-muted)',
                }}
              >
                {def.description}
              </div>
              <Button
                size="sm"
                variant="secondary"
                style={{ marginTop: 8 }}
                onClick={(e) => {
                  e.stopPropagation();
                  createSceneMutation.mutate(def.id);
                }}
              >
                <Plus size={14} /> 创建 Scene
              </Button>
            </div>
          ))}
        </div>

        {/* Scene lanes */}
        <div style={{ flex: 1, display: 'flex', gap: 16, overflow: 'auto' }}>
          <SceneLane
            title="待绑定"
            scenes={bindingScenes}
            onDelete={setDeleteSceneId}
            onStartProcessing={startProcessingMutation.mutate}
          />
          <SceneLane
            title="处理中"
            scenes={processingScenes}
            onSettle={settleRulesMutation.mutate}
          />
          <SceneLane title="已结算" scenes={settledScenes} onDelete={setDeleteSceneId} />
        </div>
      </div>

      <Dialog
        open={!!deleteSceneId}
        title="确认删除"
        onClose={() => setDeleteSceneId(null)}
        confirmLabel="删除"
        destructive
        onConfirm={() => {
          if (deleteSceneId) deleteSceneMutation.mutate(deleteSceneId);
        }}
      >
        确定要删除这个 Scene 吗？
      </Dialog>
    </div>
  );
}

function SceneLane({
  title,
  scenes,
  onDelete,
  onStartProcessing,
  onSettle,
}: {
  title: string;
  scenes: Array<{
    id: string;
    name: string;
    description: string;
    state: string;
    settlement: string[];
  }>;
  onDelete?: (id: string) => void;
  onStartProcessing?: (id: string) => void;
  onSettle?: (id: string) => void;
}) {
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
        {title} ({scenes.length})
      </h3>
      <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
        {scenes.map((scene) => (
          <div
            key={scene.id}
            style={{
              padding: 12,
              borderRadius: 8,
              background: 'var(--color-surface-1)',
              border: '1px solid var(--color-border)',
            }}
          >
            <div style={{ fontWeight: 500, marginBottom: 4 }}>{scene.name}</div>
            <div
              style={{
                fontSize: 12,
                color: 'var(--color-text-muted)',
                marginBottom: 8,
              }}
            >
              {scene.description}
            </div>
            <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
              {onStartProcessing && scene.settlement.includes('Performance') && (
                <Button
                  size="sm"
                  variant="primary"
                  onClick={() => onStartProcessing(scene.id)}
                >
                  <Play size={14} /> Performance
                </Button>
              )}
              {onSettle && scene.settlement.includes('Rules') && (
                <Button size="sm" onClick={() => onSettle(scene.id)}>
                  规则结算
                </Button>
              )}
              {onDelete && (
                <IconButton
                  label="删除"
                  size="sm"
                  onClick={() => onDelete(scene.id)}
                >
                  <Trash2 size={14} />
                </IconButton>
              )}
            </div>
          </div>
        ))}
        {scenes.length === 0 && (
          <div
            style={{
              padding: 24,
              textAlign: 'center',
              color: 'var(--color-text-subtle)',
              fontSize: 13,
            }}
          >
            无 Scene
          </div>
        )}
      </div>
    </div>
  );
}
