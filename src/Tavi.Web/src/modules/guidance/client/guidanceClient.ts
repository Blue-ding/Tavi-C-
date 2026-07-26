import { request, requestVoid } from '@/shared/http';
import {
  guidanceAvailabilityViewModelSchema,
  guidanceSnapshotViewModelSchema,
  guidanceOperationViewModelSchema,
  guidanceCommitViewModelSchema,
} from './guidanceSchemas';
import type {
  GuidanceAvailabilityViewModel,
  GuidanceSnapshotViewModel,
  GuidanceOperationViewModel,
  GuidanceCommitViewModel,
} from './guidanceSchemas';

export const guidanceClient = {
  async getAvailability(): Promise<GuidanceAvailabilityViewModel> {
    return request('/api/v1/guidance/', {
      schema: guidanceAvailabilityViewModelSchema,
    });
  },

  async getSession(): Promise<GuidanceSnapshotViewModel> {
    return request('/api/v1/guidance/session', {
      schema: guidanceSnapshotViewModelSchema,
    });
  },

  async startSession(potential: string): Promise<GuidanceOperationViewModel> {
    return request('/api/v1/guidance/sessions', {
      method: 'POST',
      body: { potential },
      schema: guidanceOperationViewModelSchema,
    });
  },

  async getSessionById(sessionId: string): Promise<GuidanceSnapshotViewModel> {
    return request(`/api/v1/guidance/sessions/${sessionId}`, {
      schema: guidanceSnapshotViewModelSchema,
    });
  },

  async sendMessage(sessionId: string, message: string): Promise<GuidanceOperationViewModel> {
    return request(`/api/v1/guidance/sessions/${sessionId}/messages`, {
      method: 'POST',
      body: { message },
      schema: guidanceOperationViewModelSchema,
    });
  },

  async retryMessage(sessionId: string, message: string): Promise<GuidanceOperationViewModel> {
    return request(`/api/v1/guidance/sessions/${sessionId}/retry`, {
      method: 'POST',
      body: { message },
      schema: guidanceOperationViewModelSchema,
    });
  },

  async refreshSession(sessionId: string): Promise<GuidanceSnapshotViewModel> {
    return request(`/api/v1/guidance/sessions/${sessionId}/refresh`, {
      method: 'POST',
      schema: guidanceSnapshotViewModelSchema,
    });
  },

  async commit(sessionId: string, acceptedChangeIds: string[]): Promise<GuidanceCommitViewModel> {
    return request(`/api/v1/guidance/sessions/${sessionId}/commit`, {
      method: 'POST',
      body: { acceptedChangeIds },
      schema: guidanceCommitViewModelSchema,
    });
  },

  async cancelOperation(sessionId: string): Promise<GuidanceSnapshotViewModel> {
    return request(`/api/v1/guidance/sessions/${sessionId}/cancel`, {
      method: 'POST',
      schema: guidanceSnapshotViewModelSchema,
    });
  },

  async deleteSession(sessionId: string): Promise<void> {
    return requestVoid(`/api/v1/guidance/sessions/${sessionId}`, {
      method: 'DELETE',
    });
  },
};
