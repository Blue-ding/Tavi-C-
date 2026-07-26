import { request } from '@/shared/http';
import {
  languageModelSettingsViewModelSchema,
  openAIConfigurationViewModelSchema,
  settingsSaveResultViewModelSchema,
} from './settingsSchemas';
import type {
  LanguageModelSettingsViewModel,
  OpenAIConfigurationViewModel,
  SettingsSaveResultViewModel,
} from './settingsSchemas';

export const settingsClient = {
  async getLanguageModel(): Promise<LanguageModelSettingsViewModel> {
    return request('/api/v1/settings/language-model', {
      schema: languageModelSettingsViewModelSchema,
    });
  },

  async getOpenAI(): Promise<OpenAIConfigurationViewModel> {
    return request('/api/v1/settings/openai', {
      schema: openAIConfigurationViewModelSchema,
    });
  },

  async updateSettings(body: {
    languageModel: {
      maxToolRounds: number;
      maxOutputRepairAttempts: number;
      overallTimeoutSeconds: number;
      toolCalls: string;
      nativeJsonOutput: string;
      streaming: string;
    };
    openAI: {
      endpoint: string;
      model: string;
      apiKey?: string;
      clientType: string;
      supportsRequiredToolChoice: boolean;
      enableThinking?: boolean | null;
    };
  }): Promise<SettingsSaveResultViewModel> {
    return request('/api/v1/settings/', {
      method: 'PUT',
      body,
      schema: settingsSaveResultViewModelSchema,
    });
  },
};
