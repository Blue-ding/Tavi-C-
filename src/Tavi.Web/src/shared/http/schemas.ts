import { z } from 'zod';

export const errorViewModelSchema = z.object({
  code: z.string(),
  message: z.string(),
  category: z.string(),
  operation: z.string().nullable().optional(),
  isTransient: z.boolean(),
  details: z.record(z.string()),
  traceId: z.string(),
});

export const worldEventSchema = z.object({
  type: z.string(),
  stateId: z.string().uuid(),
  isDirty: z.boolean(),
  commitId: z.string().uuid().nullable().optional(),
  operation: z.string().nullable().optional(),
  error: z.string().nullable().optional(),
});

export const scenarioEventSchema = worldEventSchema;
export const performanceEventSchema = worldEventSchema;
export const writingEventSchema = worldEventSchema;
