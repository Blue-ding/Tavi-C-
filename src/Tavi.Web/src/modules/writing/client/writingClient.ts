import { request, requestVoid } from '@/shared/http';
import {
  writingWorkspaceViewModelSchema,
  manuscriptViewModelSchema,
} from './writingSchemas';
import type { WritingWorkspaceViewModel, ManuscriptViewModel } from './writingSchemas';

export const writingClient = {
  async getWorkspace(): Promise<WritingWorkspaceViewModel> {
    return request('/api/v1/writing/', {
      schema: writingWorkspaceViewModelSchema,
    });
  },

  async createManuscript(title: string): Promise<WritingWorkspaceViewModel> {
    return request('/api/v1/writing/manuscripts', {
      method: 'POST',
      body: { title },
      schema: writingWorkspaceViewModelSchema,
    });
  },

  async getManuscript(id: string): Promise<ManuscriptViewModel> {
    return request(`/api/v1/writing/manuscripts/${id}`, {
      schema: manuscriptViewModelSchema,
    });
  },

  async renameManuscript(id: string, title: string, expectedStateId?: string): Promise<void> {
    return requestVoid(`/api/v1/writing/manuscripts/${id}/title`, {
      method: 'PATCH',
      body: { title, expectedStateId },
    });
  },

  async deleteManuscript(id: string): Promise<void> {
    return requestVoid(`/api/v1/writing/manuscripts/${id}`, {
      method: 'DELETE',
    });
  },

  async renameSessionTitle(title: string, expectedStateId: string): Promise<void> {
    return requestVoid('/api/v1/writing/session/title', {
      method: 'PATCH',
      body: { title, expectedStateId },
    });
  },

  async insertParagraph(expectedStateId: string, index: number, text = ''): Promise<WritingWorkspaceViewModel> {
    return request('/api/v1/writing/session/paragraphs', {
      method: 'POST',
      body: { expectedStateId, index, text },
      schema: writingWorkspaceViewModelSchema,
    });
  },

  async updateParagraph(id: string, expectedStateId: string, text: string): Promise<WritingWorkspaceViewModel> {
    return request(`/api/v1/writing/session/paragraphs/${id}`, {
      method: 'PATCH',
      body: { expectedStateId, text },
      schema: writingWorkspaceViewModelSchema,
    });
  },

  async deleteParagraph(id: string, expectedStateId: string): Promise<WritingWorkspaceViewModel> {
    return request(`/api/v1/writing/session/paragraphs/${id}`, {
      method: 'DELETE',
      body: { expectedStateId },
      schema: writingWorkspaceViewModelSchema,
    });
  },

  async undo(expectedStateId: string): Promise<WritingWorkspaceViewModel> {
    return request('/api/v1/writing/session/undo', {
      method: 'POST',
      body: { expectedStateId },
      schema: writingWorkspaceViewModelSchema,
    });
  },

  async redo(expectedStateId: string): Promise<WritingWorkspaceViewModel> {
    return request('/api/v1/writing/session/redo', {
      method: 'POST',
      body: { expectedStateId },
      schema: writingWorkspaceViewModelSchema,
    });
  },

  async save(expectedStateId: string): Promise<void> {
    return requestVoid('/api/v1/writing/session/save', {
      method: 'POST',
      body: { expectedStateId },
    });
  },

  async archive(expectedStateId: string): Promise<WritingWorkspaceViewModel> {
    return request('/api/v1/writing/session/archive', {
      method: 'POST',
      body: { expectedStateId },
      schema: writingWorkspaceViewModelSchema,
    });
  },
};
