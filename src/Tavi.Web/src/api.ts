import type { ApiProblem, GuidanceAvailabilityViewModel, GuidanceCommitViewModel, GuidanceOperationViewModel, GuidanceSnapshotViewModel, LanguageModelSettingsViewModel, ManuscriptViewModel, OpenAIConfigurationInput, OpenAIConfigurationViewModel, SettingsSaveResultViewModel, WorldGraphViewModel, WritingSnapshotViewModel, WritingWorkspaceViewModel } from './types'

const worldUrl = '/api/v1/world'
const guidanceUrl = '/api/v1/guidance'
const settingsUrl = '/api/v1/settings'
const writingUrl = '/api/v1/writing'

export class ApiError extends Error {
  readonly code: string

  constructor(message: string, code = 'TAVI.WEB.REQUEST.FAILED') {
    super(message)
    this.name = 'ApiError'
    this.code = code
  }
}

async function request<T>(url: string, init?: RequestInit): Promise<T> {
  const response = await fetch(url, {
    ...init,
    headers: { 'Content-Type': 'application/json', ...init?.headers },
  })
  if (!response.ok) {
    let problem: ApiProblem | undefined
    try {
      problem = await response.json() as ApiProblem
    } catch {
      throw new ApiError(`请求失败（${response.status}）`)
    }
    throw new ApiError(problem.error?.message ?? problem.title ?? `请求失败（${response.status}）`, problem.error?.code)
  }
  return response.status === 204 ? undefined as T : await response.json() as T
}

export const worldApi = {
  get: () => request<WorldGraphViewModel>(`${worldUrl}/`),
  addAnchor: (stateId: string, name: string, description: string, type: string) => request(`${worldUrl}/anchors`, { method: 'POST', body: JSON.stringify({ expectedStateId: stateId, name, description, type }) }),
  updateAnchor: (id: string, stateId: string, changes: { name?: string; description?: string; type?: string }) => request(`${worldUrl}/anchors/${id}`, { method: 'PATCH', body: JSON.stringify({ expectedStateId: stateId, ...changes }) }),
  removeAnchor: (id: string, stateId: string) => request(`${worldUrl}/anchors/${id}?expectedStateId=${stateId}`, { method: 'DELETE' }),
  addRelation: (stateId: string, name: string, description: string, sourceId: string, targetId: string, domainCharacterId: string | null) => request(`${worldUrl}/relations`, { method: 'POST', body: JSON.stringify({ expectedStateId: stateId, name, description, sourceId, targetId, domainCharacterId }) }),
  updateRelation: (id: string, stateId: string, changes: { name?: string; description?: string }) => request(`${worldUrl}/relations/${id}`, { method: 'PATCH', body: JSON.stringify({ expectedStateId: stateId, ...changes }) }),
  removeRelation: (id: string, stateId: string) => request(`${worldUrl}/relations/${id}?expectedStateId=${stateId}`, { method: 'DELETE' }),
  createSubWorld: (stateId: string, characterId: string) => request(`${worldUrl}/subworlds`, { method: 'POST', body: JSON.stringify({ expectedStateId: stateId, characterId }) }),
  removeSubWorld: (stateId: string, characterId: string) => request(`${worldUrl}/subworlds/${characterId}?expectedStateId=${stateId}`, { method: 'DELETE' }),
  undo: (stateId: string) => request(`${worldUrl}/undo`, { method: 'POST', body: JSON.stringify({ expectedStateId: stateId }) }),
  redo: (stateId: string) => request(`${worldUrl}/redo`, { method: 'POST', body: JSON.stringify({ expectedStateId: stateId }) }),
  save: () => request(`${worldUrl}/save`, { method: 'POST', body: '{}' }),
  commitStaged: (stateId: string, changeIds: string[]) => request(`${worldUrl}/staging/commit`, { method: 'POST', body: JSON.stringify({ expectedStateId: stateId, changeIds }) }),
  deleteStaged: (changeId: string) => request(`${worldUrl}/staging/${changeId}`, { method: 'DELETE' }),
  deleteInvalidStaged: () => request(`${worldUrl}/staging/invalid`, { method: 'DELETE' }),
}

export const guidanceApi = {
  availability: () => request<GuidanceAvailabilityViewModel>(`${guidanceUrl}/`),
  current: () => request<GuidanceSnapshotViewModel>(`${guidanceUrl}/session`),
  start: (potential: string) => request<GuidanceOperationViewModel>(`${guidanceUrl}/sessions`, { method: 'POST', body: JSON.stringify({ potential }) }),
  get: (sessionId: string) => request<GuidanceSnapshotViewModel>(`${guidanceUrl}/sessions/${sessionId}`),
  continue: (sessionId: string, message: string) => request<GuidanceOperationViewModel>(`${guidanceUrl}/sessions/${sessionId}/messages`, { method: 'POST', body: JSON.stringify({ message }) }),
  retry: (sessionId: string, message: string) => request<GuidanceOperationViewModel>(`${guidanceUrl}/sessions/${sessionId}/retry`, { method: 'POST', body: JSON.stringify({ message }) }),
  refresh: (sessionId: string) => request<GuidanceSnapshotViewModel>(`${guidanceUrl}/sessions/${sessionId}/refresh`, { method: 'POST', body: '{}' }),
  commit: (sessionId: string, acceptedChangeIds: string[]) => request<GuidanceCommitViewModel>(`${guidanceUrl}/sessions/${sessionId}/commit`, { method: 'POST', body: JSON.stringify({ acceptedChangeIds }) }),
  cancel: (sessionId: string) => request<GuidanceSnapshotViewModel>(`${guidanceUrl}/sessions/${sessionId}/cancel`, { method: 'POST', body: '{}' }),
  forget: (sessionId: string) => request<void>(`${guidanceUrl}/sessions/${sessionId}`, { method: 'DELETE' }),
}

export const settingsApi = {
  update: (languageModel: LanguageModelSettingsViewModel, openAI: OpenAIConfigurationInput) =>
    request<SettingsSaveResultViewModel>(`${settingsUrl}/`, {
      method: 'PUT',
      body: JSON.stringify({ languageModel, openAI }),
    }),
  getLanguageModel: () => request<LanguageModelSettingsViewModel>(`${settingsUrl}/language-model`),
  updateLanguageModel: (settings: LanguageModelSettingsViewModel) => request<LanguageModelSettingsViewModel>(`${settingsUrl}/language-model`, { method: 'PUT', body: JSON.stringify(settings) }),
  getOpenAI: () => request<OpenAIConfigurationViewModel>(`${settingsUrl}/openai`),
  updateOpenAI: (configuration: OpenAIConfigurationInput) => request<OpenAIConfigurationViewModel>(`${settingsUrl}/openai`, { method: 'PUT', body: JSON.stringify(configuration) }),
}

export const writingApi = {
  workspace: () => request<WritingWorkspaceViewModel>(`${writingUrl}/`),
  create: (title: string) => request<WritingSnapshotViewModel>(`${writingUrl}/manuscripts`, { method: 'POST', body: JSON.stringify({ title }) }),
  get: (id: string) => request<ManuscriptViewModel>(`${writingUrl}/manuscripts/${id}`),
  renameArchived: (id: string, title: string) => request<ManuscriptViewModel>(`${writingUrl}/manuscripts/${id}/title`, { method: 'PATCH', body: JSON.stringify({ title }) }),
  deleteArchived: (id: string) => request<void>(`${writingUrl}/manuscripts/${id}`, { method: 'DELETE' }),
  renameActive: (stateId: string, title: string) => request<WritingSnapshotViewModel>(`${writingUrl}/session/title`, { method: 'PATCH', body: JSON.stringify({ expectedStateId: stateId, title }) }),
  insertParagraph: (stateId: string, index: number, text = '') => request<WritingSnapshotViewModel>(`${writingUrl}/session/paragraphs`, { method: 'POST', body: JSON.stringify({ expectedStateId: stateId, index, text }) }),
  updateParagraph: (paragraphId: string, stateId: string, text: string) => request<WritingSnapshotViewModel>(`${writingUrl}/session/paragraphs/${paragraphId}`, { method: 'PATCH', body: JSON.stringify({ expectedStateId: stateId, text }) }),
  removeParagraph: (paragraphId: string, stateId: string) => request<WritingSnapshotViewModel>(`${writingUrl}/session/paragraphs/${paragraphId}?expectedStateId=${stateId}`, { method: 'DELETE' }),
  undo: (stateId: string) => request<WritingSnapshotViewModel>(`${writingUrl}/session/undo`, { method: 'POST', body: JSON.stringify({ expectedStateId: stateId }) }),
  redo: (stateId: string) => request<WritingSnapshotViewModel>(`${writingUrl}/session/redo`, { method: 'POST', body: JSON.stringify({ expectedStateId: stateId }) }),
  save: () => request<WritingSnapshotViewModel>(`${writingUrl}/session/save`, { method: 'POST', body: '{}' }),
  archive: (stateId: string) => request<ManuscriptViewModel>(`${writingUrl}/session/archive`, { method: 'POST', body: JSON.stringify({ expectedStateId: stateId }) }),
}
