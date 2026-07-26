import { z } from 'zod';

export const languageModelSettingsViewModelSchema = z.object({
  maxToolRounds: z.number().int(),
  maxOutputRepairAttempts: z.number().int(),
  overallTimeoutSeconds: z.number().int(),
  toolCalls: z.string(),
  nativeJsonOutput: z.string(),
  streaming: z.string(),
});

export const openAIConfigurationViewModelSchema = z.object({
  endpoint: z.string(),
  model: z.string(),
  clientType: z.string(),
  supportsRequiredToolChoice: z.boolean(),
  enableThinking: z.boolean().nullable().optional(),
  hasApiKey: z.boolean(),
});

export const settingsSaveResultViewModelSchema = z.object({
  languageModel: languageModelSettingsViewModelSchema,
  openAI: openAIConfigurationViewModelSchema,
  sessionRecreationRequired: z.boolean(),
  message: z.string(),
});

export type LanguageModelSettingsViewModel = z.infer<typeof languageModelSettingsViewModelSchema>;
export type OpenAIConfigurationViewModel = z.infer<typeof openAIConfigurationViewModelSchema>;
export type SettingsSaveResultViewModel = z.infer<typeof settingsSaveResultViewModelSchema>;
