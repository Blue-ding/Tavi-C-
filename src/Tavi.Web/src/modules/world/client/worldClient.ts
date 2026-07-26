import { request } from '@/shared/http';
import {
  worldGraphViewModelSchema,
  worldTypeLibraryViewModelSchema,
  worldAuthoringActionViewModelSchema,
  worldStagingResultViewModelSchema,
  saveWorldViewModelSchema,
  worldCommitViewModelSchema,
} from './worldSchemas';
import type {
  WorldGraphViewModel,
  WorldTypeLibraryViewModel,
  WorldAuthoringActionViewModel,
  WorldStagingResultViewModel,
  SaveWorldViewModel,
  WorldCommitViewModel,
} from './worldSchemas';

export const worldClient = {
  async getWorkspace(): Promise<WorldGraphViewModel> {
    return request('/api/v1/world/', { schema: worldGraphViewModelSchema });
  },

  async getTypes(): Promise<WorldTypeLibraryViewModel> {
    return request('/api/v1/world/types', { schema: worldTypeLibraryViewModelSchema });
  },

  async getModuleActions(): Promise<WorldAuthoringActionViewModel[]> {
    return request('/api/v1/world/module-actions', {
      schema: worldAuthoringActionViewModelSchema.array(),
    });
  },

  async addElement(body: {
    expectedStateId: string;
    name: string;
    description: string;
    type: string;
  }): Promise<WorldStagingResultViewModel> {
    return request('/api/v1/world/elements', {
      method: 'POST',
      body,
      schema: worldStagingResultViewModelSchema,
    });
  },

  async updateElement(
    id: string,
    body: { expectedStateId: string; name?: string; description?: string; type?: string }
  ): Promise<WorldStagingResultViewModel> {
    return request(`/api/v1/world/elements/${id}`, {
      method: 'PATCH',
      body,
      schema: worldStagingResultViewModelSchema,
    });
  },

  async deleteElement(id: string, expectedStateId: string): Promise<WorldStagingResultViewModel> {
    return request(`/api/v1/world/elements/${id}`, {
      method: 'DELETE',
      body: { expectedStateId },
      schema: worldStagingResultViewModelSchema,
    });
  },

  async addScope(body: {
    expectedStateId: string;
    quantity: number;
    type: string;
    ownerElementId: string;
  }): Promise<WorldStagingResultViewModel> {
    return request('/api/v1/world/scopes', {
      method: 'POST',
      body,
      schema: worldStagingResultViewModelSchema,
    });
  },

  async updateScope(
    id: string,
    body: { expectedStateId: string; quantity?: number; type?: string }
  ): Promise<WorldStagingResultViewModel> {
    return request(`/api/v1/world/scopes/${id}`, {
      method: 'PATCH',
      body,
      schema: worldStagingResultViewModelSchema,
    });
  },

  async deleteScope(id: string, expectedStateId: string): Promise<WorldStagingResultViewModel> {
    return request(`/api/v1/world/scopes/${id}`, {
      method: 'DELETE',
      body: { expectedStateId },
      schema: worldStagingResultViewModelSchema,
    });
  },

  async addAspect(body: {
    expectedStateId: string;
    quantity: number;
    type: string;
    elementId: string;
    scopeId: string;
  }): Promise<WorldStagingResultViewModel> {
    return request('/api/v1/world/aspects', {
      method: 'POST',
      body,
      schema: worldStagingResultViewModelSchema,
    });
  },

  async updateAspect(
    id: string,
    body: { expectedStateId: string; quantity?: number; type?: string }
  ): Promise<WorldStagingResultViewModel> {
    return request(`/api/v1/world/aspects/${id}`, {
      method: 'PATCH',
      body,
      schema: worldStagingResultViewModelSchema,
    });
  },

  async deleteAspect(id: string, expectedStateId: string): Promise<WorldStagingResultViewModel> {
    return request(`/api/v1/world/aspects/${id}`, {
      method: 'DELETE',
      body: { expectedStateId },
      schema: worldStagingResultViewModelSchema,
    });
  },

  async addRelation(body: {
    expectedStateId: string;
    quantity: number;
    type: string;
    sourceElementId: string;
    targetElementId: string;
    scopeId: string;
  }): Promise<WorldStagingResultViewModel> {
    return request('/api/v1/world/relations', {
      method: 'POST',
      body,
      schema: worldStagingResultViewModelSchema,
    });
  },

  async updateRelation(
    id: string,
    body: { expectedStateId: string; quantity?: number; type?: string }
  ): Promise<WorldStagingResultViewModel> {
    return request(`/api/v1/world/relations/${id}`, {
      method: 'PATCH',
      body,
      schema: worldStagingResultViewModelSchema,
    });
  },

  async deleteRelation(id: string, expectedStateId: string): Promise<WorldStagingResultViewModel> {
    return request(`/api/v1/world/relations/${id}`, {
      method: 'DELETE',
      body: { expectedStateId },
      schema: worldStagingResultViewModelSchema,
    });
  },

  async addLocalAspect(body: {
    expectedStateId: string;
    name: string;
    description: string;
    quantity: number;
    elementId: string;
    scopeId: string;
  }): Promise<WorldStagingResultViewModel> {
    return request('/api/v1/world/local-aspects', {
      method: 'POST',
      body,
      schema: worldStagingResultViewModelSchema,
    });
  },

  async updateLocalAspect(
    id: string,
    body: { expectedStateId: string; name?: string; description?: string; quantity?: number }
  ): Promise<WorldStagingResultViewModel> {
    return request(`/api/v1/world/local-aspects/${id}`, {
      method: 'PATCH',
      body,
      schema: worldStagingResultViewModelSchema,
    });
  },

  async deleteLocalAspect(id: string, expectedStateId: string): Promise<WorldStagingResultViewModel> {
    return request(`/api/v1/world/local-aspects/${id}`, {
      method: 'DELETE',
      body: { expectedStateId },
      schema: worldStagingResultViewModelSchema,
    });
  },

  async addLocalRelation(body: {
    expectedStateId: string;
    name: string;
    description: string;
    quantity: number;
    sourceElementId: string;
    targetElementId: string;
    scopeId: string;
  }): Promise<WorldStagingResultViewModel> {
    return request('/api/v1/world/local-relations', {
      method: 'POST',
      body,
      schema: worldStagingResultViewModelSchema,
    });
  },

  async updateLocalRelation(
    id: string,
    body: { expectedStateId: string; name?: string; description?: string; quantity?: number }
  ): Promise<WorldStagingResultViewModel> {
    return request(`/api/v1/world/local-relations/${id}`, {
      method: 'PATCH',
      body,
      schema: worldStagingResultViewModelSchema,
    });
  },

  async deleteLocalRelation(id: string, expectedStateId: string): Promise<WorldStagingResultViewModel> {
    return request(`/api/v1/world/local-relations/${id}`, {
      method: 'DELETE',
      body: { expectedStateId },
      schema: worldStagingResultViewModelSchema,
    });
  },

  async commitStaged(expectedStateId: string, changeIds: string[]): Promise<WorldCommitViewModel> {
    return request('/api/v1/world/staging/commit', {
      method: 'POST',
      body: { expectedStateId, changeIds },
      schema: worldCommitViewModelSchema,
    });
  },

  async deleteStagedChange(changeId: string): Promise<WorldStagingResultViewModel> {
    return request(`/api/v1/world/staging/${changeId}`, {
      method: 'DELETE',
      schema: worldStagingResultViewModelSchema,
    });
  },

  async deleteInvalidStaged(expectedStateId: string): Promise<WorldStagingResultViewModel> {
    return request('/api/v1/world/staging/invalid', {
      method: 'DELETE',
      body: { expectedStateId },
      schema: worldStagingResultViewModelSchema,
    });
  },

  async undo(expectedStateId: string): Promise<WorldStagingResultViewModel> {
    return request('/api/v1/world/undo', {
      method: 'POST',
      body: { expectedStateId },
      schema: worldStagingResultViewModelSchema,
    });
  },

  async redo(expectedStateId: string): Promise<WorldStagingResultViewModel> {
    return request('/api/v1/world/redo', {
      method: 'POST',
      body: { expectedStateId },
      schema: worldStagingResultViewModelSchema,
    });
  },

  async save(expectedStateId: string): Promise<SaveWorldViewModel> {
    return request('/api/v1/world/save', {
      method: 'POST',
      body: { expectedStateId },
      schema: saveWorldViewModelSchema,
    });
  },

  async invokeModuleAction(
    module: string,
    action: string,
    expectedStateId: string,
    argumentsJson: Record<string, unknown>
  ): Promise<WorldStagingResultViewModel> {
    return request(`/api/v1/world/module-actions/${module}/${action}`, {
      method: 'POST',
      body: { expectedStateId, arguments: argumentsJson },
      schema: worldStagingResultViewModelSchema,
    });
  },
};
