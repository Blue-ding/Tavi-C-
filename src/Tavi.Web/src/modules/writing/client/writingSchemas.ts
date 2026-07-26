import { z } from 'zod';

export const manuscriptParagraphViewModelSchema = z.object({
  id: z.string().uuid(),
  text: z.string(),
});

export const manuscriptViewModelSchema = z.object({
  id: z.string().uuid(),
  stateId: z.string().uuid(),
  title: z.string(),
  status: z.string(),
  paragraphs: z.array(manuscriptParagraphViewModelSchema),
  createdAtUtc: z.string(),
  updatedAtUtc: z.string(),
});

export const manuscriptSummaryViewModelSchema = z.object({
  id: z.string().uuid(),
  title: z.string(),
  status: z.string(),
  paragraphCount: z.number().int(),
  preview: z.string(),
  createdAtUtc: z.string(),
  updatedAtUtc: z.string(),
});

export const writingSnapshotViewModelSchema = z.object({
  manuscript: manuscriptViewModelSchema.nullable().optional(),
  stagingRevision: z.number().int(),
  isDirty: z.boolean(),
  canUndo: z.boolean(),
  canRedo: z.boolean(),
  stagedChangeCount: z.number().int(),
  autoSaveError: z.string().nullable().optional(),
});

export const writingWorkspaceViewModelSchema = z.object({
  manuscripts: z.array(manuscriptSummaryViewModelSchema),
  session: writingSnapshotViewModelSchema,
});

export const writingEventViewModelSchema = z.object({
  type: z.string(),
  stateId: z.string().uuid(),
  isDirty: z.boolean(),
  commitId: z.string().uuid().nullable().optional(),
  operation: z.string().nullable().optional(),
  error: z.string().nullable().optional(),
});

export type ManuscriptViewModel = z.infer<typeof manuscriptViewModelSchema>;
export type ManuscriptSummaryViewModel = z.infer<typeof manuscriptSummaryViewModelSchema>;
export type WritingSnapshotViewModel = z.infer<typeof writingSnapshotViewModelSchema>;
export type WritingWorkspaceViewModel = z.infer<typeof writingWorkspaceViewModelSchema>;
