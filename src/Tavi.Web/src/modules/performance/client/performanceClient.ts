import { request, requestVoid } from '@/shared/http';
import {
  performanceWorkspaceViewModelSchema,
  performanceArchiveSummaryViewModelSchema,
  performanceSnapshotViewModelSchema,
  scenarioPerformanceCompletionViewModelSchema,
} from './performanceSchemas';
import type {
  PerformanceWorkspaceViewModel,
  PerformanceArchiveSummaryViewModel,
  PerformanceSnapshotViewModel,
  ScenarioPerformanceCompletionViewModel,
} from './performanceSchemas';

export const performanceClient = {
  async getWorkspace(seed: number): Promise<PerformanceWorkspaceViewModel> {
    return request(`/api/v1/performance/?randomSeed=${seed}`, {
      schema: performanceWorkspaceViewModelSchema,
    });
  },

  async start(sceneId: string, expectedScenarioStateId: string, randomSeed: number): Promise<PerformanceWorkspaceViewModel> {
    return request('/api/v1/performance/start', {
      method: 'POST',
      body: { sceneId, expectedScenarioStateId, randomSeed },
      schema: performanceWorkspaceViewModelSchema,
    });
  },

  async getArchives(): Promise<PerformanceArchiveSummaryViewModel[]> {
    return request('/api/v1/performance/archives', {
      schema: performanceArchiveSummaryViewModelSchema.array(),
    });
  },

  async getArchive(id: string): Promise<PerformanceSnapshotViewModel> {
    return request(`/api/v1/performance/archives/${id}`, {
      schema: performanceSnapshotViewModelSchema,
    });
  },

  async createBeat(definitionId: string, randomSeed: number, expectedStateId: string): Promise<PerformanceWorkspaceViewModel> {
    return request('/api/v1/performance/beats', {
      method: 'POST',
      body: { definitionId, randomSeed, expectedStateId },
      schema: performanceWorkspaceViewModelSchema,
    });
  },

  async setBeatBinding(beatId: string, slotId: string, elementIds: string[], expectedStateId: string): Promise<PerformanceWorkspaceViewModel> {
    return request(`/api/v1/performance/beats/${beatId}/bindings/${slotId}`, {
      method: 'PUT',
      body: { elementIds, expectedStateId },
      schema: performanceWorkspaceViewModelSchema,
    });
  },

  async clearBeatBinding(beatId: string, slotId: string, expectedStateId: string): Promise<void> {
    return requestVoid(`/api/v1/performance/beats/${beatId}/bindings/${slotId}`, {
      method: 'DELETE',
      body: { expectedStateId },
    });
  },

  async startBeatProcessing(beatId: string, expectedStateId: string): Promise<PerformanceWorkspaceViewModel> {
    return request(`/api/v1/performance/beats/${beatId}/processing`, {
      method: 'POST',
      body: { expectedStateId },
      schema: performanceWorkspaceViewModelSchema,
    });
  },

  async resolveBeat(beatId: string, interaction: string, randomSeed: number, expectedStateId: string): Promise<PerformanceWorkspaceViewModel> {
    return request(`/api/v1/performance/beats/${beatId}/resolve`, {
      method: 'POST',
      body: { interaction, randomSeed, expectedStateId },
      schema: performanceWorkspaceViewModelSchema,
    });
  },

  async publishBeat(beatId: string, expectedStateId: string, expectedManuscriptStateId: string): Promise<PerformanceWorkspaceViewModel> {
    return request(`/api/v1/performance/beats/${beatId}/publish`, {
      method: 'POST',
      body: { expectedStateId, expectedManuscriptStateId },
      schema: performanceWorkspaceViewModelSchema,
    });
  },

  async complete(expectedScenarioStateId: string, expectedPerformanceStateId: string): Promise<ScenarioPerformanceCompletionViewModel> {
    return request('/api/v1/performance/complete', {
      method: 'POST',
      body: { expectedScenarioStateId, expectedPerformanceStateId },
      schema: scenarioPerformanceCompletionViewModelSchema,
    });
  },

  async abandon(expectedStateId: string): Promise<PerformanceWorkspaceViewModel> {
    return request('/api/v1/performance/abandon', {
      method: 'POST',
      body: { expectedStateId },
      schema: performanceWorkspaceViewModelSchema,
    });
  },

  async undo(expectedStateId: string): Promise<PerformanceWorkspaceViewModel> {
    return request('/api/v1/performance/undo', {
      method: 'POST',
      body: { expectedStateId },
      schema: performanceWorkspaceViewModelSchema,
    });
  },

  async redo(expectedStateId: string): Promise<PerformanceWorkspaceViewModel> {
    return request('/api/v1/performance/redo', {
      method: 'POST',
      body: { expectedStateId },
      schema: performanceWorkspaceViewModelSchema,
    });
  },

  async save(expectedStateId: string): Promise<void> {
    return requestVoid('/api/v1/performance/save', {
      method: 'POST',
      body: { expectedStateId },
    });
  },

  async archive(expectedStateId: string): Promise<void> {
    return requestVoid('/api/v1/performance/archive', {
      method: 'POST',
      body: { expectedStateId },
    });
  },
};
