import { request } from '@/shared/http';
import { extensionWorkspaceViewModelSchema } from './extensionsSchemas';
import type { ExtensionWorkspaceViewModel } from './extensionsSchemas';

export const extensionsClient = {
  async getWorkspace(): Promise<ExtensionWorkspaceViewModel> {
    return request('/api/v1/extensions/', {
      schema: extensionWorkspaceViewModelSchema,
    });
  },

  async updateSettings(
    expectedRevision: number,
    modules: { id: string; enabled: boolean; settings: unknown }[]
  ): Promise<ExtensionWorkspaceViewModel> {
    return request('/api/v1/extensions/settings', {
      method: 'PUT',
      body: { expectedRevision, modules },
      schema: extensionWorkspaceViewModelSchema,
    });
  },
};
