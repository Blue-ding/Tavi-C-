import { z } from 'zod';

export const guidanceAvailabilityViewModelSchema = z.object({
  available: z.boolean(),
  provider: z.string().nullable().optional(),
  message: z.string(),
});

export const guidanceMessageViewModelSchema = z.object({
  role: z.string(),
  text: z.string(),
});

export const proposalElementReferenceViewModelSchema = z.object({
  kind: z.string(),
  elementId: z.string().uuid(),
});

export const proposalScopeReferenceViewModelSchema = z.object({
  kind: z.string(),
  scopeId: z.string().uuid(),
});

export const proposalChangeViewModelSchema = z.discriminatedUnion('kind', [
  z.object({
    kind: z.literal('AddElement'),
    id: z.string(),
    rationale: z.string(),
    elementId: z.string().uuid(),
    name: z.string(),
    description: z.string(),
    type: z.string(),
  }),
  z.object({
    kind: z.literal('AddScope'),
    id: z.string(),
    rationale: z.string(),
    scopeId: z.string().uuid(),
    quantity: z.number().int(),
    type: z.string(),
    owner: proposalElementReferenceViewModelSchema,
  }),
  z.object({
    kind: z.literal('AddAspect'),
    id: z.string(),
    rationale: z.string(),
    quantity: z.number().int(),
    type: z.string(),
    element: proposalElementReferenceViewModelSchema,
    scope: proposalScopeReferenceViewModelSchema,
  }),
  z.object({
    kind: z.literal('AddRelation'),
    id: z.string(),
    rationale: z.string(),
    quantity: z.number().int(),
    type: z.string(),
    source: proposalElementReferenceViewModelSchema,
    target: proposalElementReferenceViewModelSchema,
    scope: proposalScopeReferenceViewModelSchema,
  }),
  z.object({
    kind: z.literal('AddLocalAspect'),
    id: z.string(),
    rationale: z.string(),
    name: z.string(),
    description: z.string(),
    quantity: z.number().int(),
    element: proposalElementReferenceViewModelSchema,
    scope: proposalScopeReferenceViewModelSchema,
  }),
  z.object({
    kind: z.literal('AddLocalRelation'),
    id: z.string(),
    rationale: z.string(),
    name: z.string(),
    description: z.string(),
    quantity: z.number().int(),
    source: proposalElementReferenceViewModelSchema,
    target: proposalElementReferenceViewModelSchema,
    scope: proposalScopeReferenceViewModelSchema,
  }),
]);

export const worldProposalViewModelSchema = z.object({
  id: z.string().uuid(),
  baseWorldStateId: z.string().uuid(),
  summary: z.string(),
  changes: z.array(proposalChangeViewModelSchema),
});

export const guidanceFailureViewModelSchema = z.object({
  code: z.string(),
  message: z.string(),
  isTransient: z.boolean(),
});

export const guidanceSnapshotViewModelSchema = z.object({
  sessionId: z.string().uuid(),
  state: z.string(),
  baseWorldStateId: z.string().uuid(),
  messages: z.array(guidanceMessageViewModelSchema),
  proposal: worldProposalViewModelSchema.nullable().optional(),
  failure: guidanceFailureViewModelSchema.nullable().optional(),
  retryMessage: z.string().nullable().optional(),
});

export const guidanceOperationViewModelSchema = z.object({
  operationId: z.string().uuid(),
  sessionId: z.string().uuid(),
  state: z.string(),
  snapshot: guidanceSnapshotViewModelSchema,
});

export const createdWorldEntityViewModelSchema = z.object({
  proposalId: z.string().uuid(),
  worldId: z.string().uuid(),
});

export const guidanceIssueViewModelSchema = z.object({
  code: z.string(),
  message: z.string(),
  changeId: z.string().nullable().optional(),
});

export const guidanceCommitViewModelSchema = z.object({
  status: z.string(),
  worldStateId: z.string().uuid().nullable().optional(),
  createdElements: z.array(createdWorldEntityViewModelSchema),
  createdScopes: z.array(createdWorldEntityViewModelSchema),
  issues: z.array(guidanceIssueViewModelSchema),
  expectedWorldStateId: z.string().uuid().nullable().optional(),
  actualWorldStateId: z.string().uuid().nullable().optional(),
  snapshot: guidanceSnapshotViewModelSchema,
});

export const guidanceEventViewModelSchema = z.object({
  type: z.string(),
  sessionId: z.string().uuid(),
  operationId: z.string().uuid().nullable().optional(),
  text: z.string().nullable().optional(),
  snapshot: guidanceSnapshotViewModelSchema.nullable().optional(),
  error: z.string().nullable().optional(),
});

export type GuidanceAvailabilityViewModel = z.infer<typeof guidanceAvailabilityViewModelSchema>;
export type GuidanceMessageViewModel = z.infer<typeof guidanceMessageViewModelSchema>;
export type ProposalChangeViewModel = z.infer<typeof proposalChangeViewModelSchema>;
export type WorldProposalViewModel = z.infer<typeof worldProposalViewModelSchema>;
export type GuidanceFailureViewModel = z.infer<typeof guidanceFailureViewModelSchema>;
export type GuidanceSnapshotViewModel = z.infer<typeof guidanceSnapshotViewModelSchema>;
export type GuidanceOperationViewModel = z.infer<typeof guidanceOperationViewModelSchema>;
export type GuidanceCommitViewModel = z.infer<typeof guidanceCommitViewModelSchema>;
export type GuidanceEventViewModel = z.infer<typeof guidanceEventViewModelSchema>;
