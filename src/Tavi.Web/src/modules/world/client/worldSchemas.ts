import { z } from 'zod';

export const elementViewModelSchema = z.object({
  id: z.string().uuid(),
  name: z.string(),
  description: z.string(),
  type: z.string(),
});

export const aspectViewModelSchema = z.object({
  id: z.string().uuid(),
  quantity: z.number().int(),
  type: z.string(),
  elementId: z.string().uuid(),
  scopeId: z.string().uuid(),
});

export const relationViewModelSchema = z.object({
  id: z.string().uuid(),
  quantity: z.number().int(),
  type: z.string(),
  sourceElementId: z.string().uuid(),
  targetElementId: z.string().uuid(),
  scopeId: z.string().uuid(),
});

export const scopeViewModelSchema = z.object({
  id: z.string().uuid(),
  quantity: z.number().int(),
  type: z.string(),
  ownerElementId: z.string().uuid(),
});

export const localAspectViewModelSchema = z.object({
  id: z.string().uuid(),
  name: z.string(),
  description: z.string(),
  quantity: z.number().int(),
  elementId: z.string().uuid(),
  scopeId: z.string().uuid(),
});

export const localRelationViewModelSchema = z.object({
  id: z.string().uuid(),
  name: z.string(),
  description: z.string(),
  quantity: z.number().int(),
  sourceElementId: z.string().uuid(),
  targetElementId: z.string().uuid(),
  scopeId: z.string().uuid(),
});

export const worldStagedChangeViewModelSchema = z.object({
  id: z.string().uuid(),
  source: z.string(),
  status: z.string(),
  operation: z.string(),
  issue: z.string().nullable().optional(),
  conflictingChangeIds: z.array(z.string().uuid()),
});

export const worldGraphViewModelSchema = z.object({
  stateId: z.string().uuid(),
  stagingRevision: z.number().int(),
  isDirty: z.boolean(),
  canUndo: z.boolean(),
  canRedo: z.boolean(),
  health: z.string(),
  elements: z.array(elementViewModelSchema),
  aspects: z.array(aspectViewModelSchema),
  relations: z.array(relationViewModelSchema),
  scopes: z.array(scopeViewModelSchema),
  localAspects: z.array(localAspectViewModelSchema),
  localRelations: z.array(localRelationViewModelSchema),
  stagedChanges: z.array(worldStagedChangeViewModelSchema),
});

export const worldStagingResultViewModelSchema = z.object({
  affectedChangeIds: z.array(z.string().uuid()),
  world: worldGraphViewModelSchema,
});

export const worldCommitViewModelSchema = z.object({
  commitId: z.string().uuid(),
  previousStateId: z.string().uuid(),
  stateId: z.string().uuid(),
  changed: z.boolean(),
  entityId: z.string().uuid().nullable().optional(),
});

export const saveWorldViewModelSchema = z.object({
  stateId: z.string().uuid(),
  isDirty: z.boolean(),
});

export const elementTypeDefinitionViewModelSchema = z.object({
  key: z.string(),
  moduleId: z.string(),
  moduleName: z.string(),
  name: z.string(),
  description: z.string(),
});

export const scopeTypeDefinitionViewModelSchema = z.object({
  key: z.string(),
  moduleId: z.string(),
  moduleName: z.string(),
  name: z.string(),
  description: z.string(),
});

export const aspectTypeDefinitionViewModelSchema = z.object({
  key: z.string(),
  moduleId: z.string(),
  moduleName: z.string(),
  name: z.string(),
  description: z.string(),
});

export const relationTypeDefinitionViewModelSchema = z.object({
  key: z.string(),
  moduleId: z.string(),
  moduleName: z.string(),
  name: z.string(),
  description: z.string(),
});

export const worldTypeLibraryViewModelSchema = z.object({
  elementTypes: z.array(elementTypeDefinitionViewModelSchema),
  scopeTypes: z.array(scopeTypeDefinitionViewModelSchema),
  aspectTypes: z.array(aspectTypeDefinitionViewModelSchema),
  relationTypes: z.array(relationTypeDefinitionViewModelSchema),
});

export const worldAuthoringActionViewModelSchema = z.object({
  id: z.string(),
  name: z.string(),
  description: z.string(),
  parameterSchema: z.string(),
});

export type ElementViewModel = z.infer<typeof elementViewModelSchema>;
export type AspectViewModel = z.infer<typeof aspectViewModelSchema>;
export type RelationViewModel = z.infer<typeof relationViewModelSchema>;
export type ScopeViewModel = z.infer<typeof scopeViewModelSchema>;
export type LocalAspectViewModel = z.infer<typeof localAspectViewModelSchema>;
export type LocalRelationViewModel = z.infer<typeof localRelationViewModelSchema>;
export type WorldStagedChangeViewModel = z.infer<typeof worldStagedChangeViewModelSchema>;
export type WorldGraphViewModel = z.infer<typeof worldGraphViewModelSchema>;
export type WorldStagingResultViewModel = z.infer<typeof worldStagingResultViewModelSchema>;
export type WorldCommitViewModel = z.infer<typeof worldCommitViewModelSchema>;
export type SaveWorldViewModel = z.infer<typeof saveWorldViewModelSchema>;
export type ElementTypeDefinitionViewModel = z.infer<typeof elementTypeDefinitionViewModelSchema>;
export type ScopeTypeDefinitionViewModel = z.infer<typeof scopeTypeDefinitionViewModelSchema>;
export type AspectTypeDefinitionViewModel = z.infer<typeof aspectTypeDefinitionViewModelSchema>;
export type RelationTypeDefinitionViewModel = z.infer<typeof relationTypeDefinitionViewModelSchema>;
export type WorldTypeLibraryViewModel = z.infer<typeof worldTypeLibraryViewModelSchema>;
export type WorldAuthoringActionViewModel = z.infer<typeof worldAuthoringActionViewModelSchema>;
