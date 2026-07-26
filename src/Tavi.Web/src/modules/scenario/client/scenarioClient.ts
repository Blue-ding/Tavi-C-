import { request, requestVoid } from '@/shared/http';
import {
  scenarioWorkspaceViewModelSchema,
  scenarioWorldLinkViewModelSchema,
  scenarioWorldStageViewModelSchema,
} from './scenarioSchemas';
import type {
  ScenarioWorkspaceViewModel,
  ScenarioWorldLinkViewModel,
  ScenarioWorldStageViewModel,
} from './scenarioSchemas';

export const scenarioClient = {
  async getWorkspace(seed: number): Promise<ScenarioWorkspaceViewModel> {
    return request(`/api/v1/scenario/?randomSeed=${seed}`, {
      schema: scenarioWorkspaceViewModelSchema,
    });
  },

  async getWorldLink(): Promise<ScenarioWorldLinkViewModel> {
    return request('/api/v1/scenario/world-link', {
      schema: scenarioWorldLinkViewModelSchema,
    });
  },

  async startFromWorld(expectedWorldStateId: string, expectedScenarioStateId: string): Promise<ScenarioWorkspaceViewModel> {
    return request('/api/v1/scenario/start-from-world', {
      method: 'POST',
      body: { expectedWorldStateId, expectedScenarioStateId },
      schema: scenarioWorkspaceViewModelSchema,
    });
  },

  async stageOutcome(expectedWorldStateId: string, expectedScenarioStateId: string): Promise<ScenarioWorldStageViewModel> {
    return request('/api/v1/scenario/stage-outcome', {
      method: 'POST',
      body: { expectedWorldStateId, expectedScenarioStateId },
      schema: scenarioWorldStageViewModelSchema,
    });
  },

  async createScene(expectedStateId: string, definitionId: string, randomSeed: number): Promise<ScenarioWorkspaceViewModel> {
    return request('/api/v1/scenario/scenes', {
      method: 'POST',
      body: { expectedStateId, definitionId, randomSeed },
      schema: scenarioWorkspaceViewModelSchema,
    });
  },

  async setBinding(sceneId: string, slotId: string, expectedStateId: string, elementIds: string[]): Promise<ScenarioWorkspaceViewModel> {
    return request(`/api/v1/scenario/scenes/${sceneId}/bindings/${slotId}`, {
      method: 'PUT',
      body: { expectedStateId, elementIds },
      schema: scenarioWorkspaceViewModelSchema,
    });
  },

  async clearBinding(sceneId: string, slotId: string, expectedStateId: string): Promise<void> {
    return requestVoid(`/api/v1/scenario/scenes/${sceneId}/bindings/${slotId}`, {
      method: 'DELETE',
      body: { expectedStateId },
    });
  },

  async startProcessing(sceneId: string, expectedStateId: string): Promise<ScenarioWorkspaceViewModel> {
    return request(`/api/v1/scenario/scenes/${sceneId}/processing`, {
      method: 'POST',
      body: { expectedStateId },
      schema: scenarioWorkspaceViewModelSchema,
    });
  },

  async settleRules(sceneId: string, expectedStateId: string, randomSeed: number): Promise<ScenarioWorkspaceViewModel> {
    return request(`/api/v1/scenario/scenes/${sceneId}/settle-rules`, {
      method: 'POST',
      body: { expectedStateId, randomSeed },
      schema: scenarioWorkspaceViewModelSchema,
    });
  },

  async deleteScene(sceneId: string, expectedStateId: string): Promise<ScenarioWorkspaceViewModel> {
    return request(`/api/v1/scenario/scenes/${sceneId}`, {
      method: 'DELETE',
      body: { expectedStateId },
      schema: scenarioWorkspaceViewModelSchema,
    });
  },

  async clearSettled(expectedStateId: string): Promise<ScenarioWorkspaceViewModel> {
    return request('/api/v1/scenario/scenes/clear-settled', {
      method: 'POST',
      body: { expectedStateId },
      schema: scenarioWorkspaceViewModelSchema,
    });
  },

  async undo(expectedStateId: string): Promise<ScenarioWorkspaceViewModel> {
    return request('/api/v1/scenario/undo', {
      method: 'POST',
      body: { expectedStateId },
      schema: scenarioWorkspaceViewModelSchema,
    });
  },

  async redo(expectedStateId: string): Promise<ScenarioWorkspaceViewModel> {
    return request('/api/v1/scenario/redo', {
      method: 'POST',
      body: { expectedStateId },
      schema: scenarioWorkspaceViewModelSchema,
    });
  },

  async save(expectedStateId: string): Promise<void> {
    return requestVoid('/api/v1/scenario/save', {
      method: 'POST',
      body: { expectedStateId },
    });
  },
};
