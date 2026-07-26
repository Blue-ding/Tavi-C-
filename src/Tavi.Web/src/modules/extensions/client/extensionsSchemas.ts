import { z } from 'zod';

export const extensionDependencyViewModelSchema = z.object({
  id: z.string(),
  minimumVersion: z.string(),
});

export const extensionModuleViewModelSchema = z.object({
  id: z.string(),
  version: z.string(),
  name: z.string(),
  description: z.string(),
  activeEnabled: z.boolean(),
  desiredEnabled: z.boolean(),
  dependencies: z.array(extensionDependencyViewModelSchema),
  settingsSchema: z.any(),
  activeSettings: z.any(),
  desiredSettings: z.any(),
});

export const extensionWorkspaceViewModelSchema = z.object({
  revision: z.number().int(),
  restartRequired: z.boolean(),
  message: z.string(),
  modules: z.array(extensionModuleViewModelSchema),
});

export type ExtensionWorkspaceViewModel = z.infer<typeof extensionWorkspaceViewModelSchema>;
export type ExtensionModuleViewModel = z.infer<typeof extensionModuleViewModelSchema>;
export type ExtensionDependencyViewModel = z.infer<typeof extensionDependencyViewModelSchema>;
