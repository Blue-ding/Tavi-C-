import { z } from 'zod';
import {
  elementViewModelSchema,
  aspectViewModelSchema,
  relationViewModelSchema,
  scopeViewModelSchema,
} from '@/modules/world/client/worldSchemas';

export const scenarioModuleViewModelSchema = z.object({
  id: z.string(),
  version: z.string(),
});

export const sceneSlotViewModelSchema = z.object({
  id: z.string(),
  name: z.string(),
  description: z.string(),
  minimum: z.number().int(),
  maximum: z.number().int().nullable().optional(),
  elementTypes: z.array(z.string()),
  requiredAspectGroups: z.array(z.string()),
  elementIds: z.array(z.string().uuid()),
});

export const sceneDefinitionViewModelSchema = z.object({
  id: z.string(),
  module: z.string(),
  moduleVersion: z.string(),
  name: z.string(),
  description: z.string(),
  settlement: z.array(z.string()),
  slots: z.array(sceneSlotViewModelSchema),
});

export const sceneViewModelSchema = z.object({
  id: z.string().uuid(),
  definitionId: z.string(),
  module: z.string(),
  moduleVersion: z.string(),
  name: z.string(),
  description: z.string(),
  state: z.string(),
  settlement: z.array(z.string()),
  definitionFrozen: z.boolean(),
  slots: z.array(sceneSlotViewModelSchema),
});

export const scenarioWorkspaceViewModelSchema = z.object({
  stateId: z.string().uuid(),
  sourceWorldStateId: z.string().uuid(),
  isDirty: z.boolean(),
  canUndo: z.boolean(),
  canRedo: z.boolean(),
  health: z.string(),
  modules: z.array(scenarioModuleViewModelSchema),
  elements: z.array(elementViewModelSchema),
  aspects: z.array(aspectViewModelSchema),
  relations: z.array(relationViewModelSchema),
  scopes: z.array(scopeViewModelSchema),
  definitions: z.array(sceneDefinitionViewModelSchema),
  scenes: z.array(sceneViewModelSchema),
});

export const scenarioWorldLinkViewModelSchema = z.object({
  state: z.string(),
  currentWorldStateId: z.string().uuid(),
  sourceWorldStateId: z.string().uuid(),
  scenarioStateId: z.string().uuid(),
  bindingScenes: z.number().int(),
  processingScenes: z.number().int(),
  settledScenes: z.number().int(),
});

export const scenarioWorldStageViewModelSchema = z.object({
  worldStateId: z.string().uuid(),
  scenarioStateId: z.string().uuid(),
  changeId: z.string().uuid().nullable().optional(),
  changed: z.boolean(),
});

export const scenarioEventViewModelSchema = z.object({
  type: z.string(),
  stateId: z.string().uuid(),
  isDirty: z.boolean(),
  commitId: z.string().uuid().nullable().optional(),
  operation: z.string().nullable().optional(),
  error: z.string().nullable().optional(),
});

export type ScenarioWorkspaceViewModel = z.infer<typeof scenarioWorkspaceViewModelSchema>;
export type ScenarioWorldLinkViewModel = z.infer<typeof scenarioWorldLinkViewModelSchema>;
export type ScenarioWorldStageViewModel = z.infer<typeof scenarioWorldStageViewModelSchema>;
export type SceneDefinitionViewModel = z.infer<typeof sceneDefinitionViewModelSchema>;
export type SceneViewModel = z.infer<typeof sceneViewModelSchema>;
export type SceneSlotViewModel = z.infer<typeof sceneSlotViewModelSchema>;
