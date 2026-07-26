import { z } from 'zod';
import {
  elementViewModelSchema,
  aspectViewModelSchema,
  relationViewModelSchema,
  scopeViewModelSchema,
} from '@/modules/world/client/worldSchemas';

export const performanceSourceBindingViewModelSchema = z.object({
  slotId: z.string(),
  elementIds: z.array(z.string().uuid()),
});

export const performanceSourceSceneViewModelSchema = z.object({
  id: z.string().uuid(),
  definitionId: z.string(),
  module: z.string(),
  moduleVersion: z.string(),
  basedOnScenarioStateId: z.string().uuid(),
  state: z.string(),
  bindings: z.array(performanceSourceBindingViewModelSchema),
});

export const performanceModuleViewModelSchema = z.object({
  id: z.string(),
  version: z.string(),
});

export const beatSlotViewModelSchema = z.object({
  id: z.string(),
  name: z.string(),
  description: z.string(),
  minimum: z.number().int(),
  maximum: z.number().int().nullable().optional(),
  elementIds: z.array(z.string().uuid()),
});

export const beatParagraphViewModelSchema = z.object({
  id: z.string().uuid(),
  text: z.string(),
});

export const beatPublicationViewModelSchema = z.object({
  manuscriptId: z.string().uuid(),
  manuscriptStateId: z.string().uuid(),
});

export const beatViewModelSchema = z.object({
  id: z.string().uuid(),
  definitionId: z.string(),
  module: z.string(),
  moduleVersion: z.string(),
  basedOnPerformanceStateId: z.string().uuid(),
  name: z.string(),
  description: z.string(),
  state: z.string(),
  slots: z.array(beatSlotViewModelSchema),
  paragraphs: z.array(beatParagraphViewModelSchema),
  publication: beatPublicationViewModelSchema.nullable().optional(),
});

export const beatDefinitionSlotViewModelSchema = z.object({
  id: z.string(),
  name: z.string(),
  description: z.string(),
  minimum: z.number().int(),
  maximum: z.number().int().nullable().optional(),
});

export const beatDefinitionViewModelSchema = z.object({
  id: z.string(),
  module: z.string(),
  moduleVersion: z.string(),
  name: z.string(),
  description: z.string(),
  sourcePerformanceStateId: z.string().uuid().nullable().optional(),
  slots: z.array(beatDefinitionSlotViewModelSchema),
});

export const performanceSnapshotViewModelSchema = z.object({
  performanceId: z.string().uuid(),
  stateId: z.string().uuid(),
  sourceScenarioStateId: z.string().uuid(),
  status: z.string(),
  sourceScene: performanceSourceSceneViewModelSchema,
  modules: z.array(performanceModuleViewModelSchema),
  elements: z.array(elementViewModelSchema),
  scopes: z.array(scopeViewModelSchema),
  aspects: z.array(aspectViewModelSchema),
  relations: z.array(relationViewModelSchema),
  beats: z.array(beatViewModelSchema),
});

export const performanceWorkspaceViewModelSchema = z.object({
  hasSession: z.boolean(),
  performance: performanceSnapshotViewModelSchema.nullable().optional(),
  health: z.string().nullable().optional(),
  isDirty: z.boolean(),
  canUndo: z.boolean(),
  canRedo: z.boolean(),
  autoSaveError: z.string().nullable().optional(),
  definitions: z.array(beatDefinitionViewModelSchema),
});

export const performanceArchiveSummaryViewModelSchema = z.object({
  performanceId: z.string().uuid(),
  stateId: z.string().uuid(),
  sourceScenarioStateId: z.string().uuid(),
  sourceSceneId: z.string().uuid(),
  status: z.string(),
  beatCount: z.number().int(),
});

export const scenarioPerformanceCompletionViewModelSchema = z.object({
  sceneId: z.string().uuid(),
  scenarioStateId: z.string().uuid(),
  performanceStateId: z.string().uuid(),
  workspace: performanceWorkspaceViewModelSchema,
});

export const performanceEventViewModelSchema = z.object({
  type: z.string(),
  stateId: z.string().uuid(),
  isDirty: z.boolean(),
  commitId: z.string().uuid().nullable().optional(),
  operation: z.string().nullable().optional(),
  error: z.string().nullable().optional(),
});

export type PerformanceWorkspaceViewModel = z.infer<typeof performanceWorkspaceViewModelSchema>;
export type PerformanceSnapshotViewModel = z.infer<typeof performanceSnapshotViewModelSchema>;
export type PerformanceArchiveSummaryViewModel = z.infer<typeof performanceArchiveSummaryViewModelSchema>;
export type BeatViewModel = z.infer<typeof beatViewModelSchema>;
export type BeatDefinitionViewModel = z.infer<typeof beatDefinitionViewModelSchema>;
export type ScenarioPerformanceCompletionViewModel = z.infer<typeof scenarioPerformanceCompletionViewModelSchema>;
