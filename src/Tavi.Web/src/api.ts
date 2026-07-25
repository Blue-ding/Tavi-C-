import type { ApiProblem, GuidanceAvailabilityViewModel, GuidanceCommitViewModel, GuidanceOperationViewModel, GuidanceSnapshotViewModel, LanguageModelSettingsViewModel, ManuscriptViewModel, OpenAIConfigurationInput, OpenAIConfigurationViewModel, ScenarioWorkspaceViewModel, SettingsSaveResultViewModel, WorldGraphViewModel, WorldTypeLibraryViewModel, WritingSnapshotViewModel, WritingWorkspaceViewModel } from './types'

const worldUrl = '/api/v1/world'
const guidanceUrl = '/api/v1/guidance'
const settingsUrl = '/api/v1/settings'
const writingUrl = '/api/v1/writing'
const scenarioUrl = '/api/v1/scenario'

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
  types: () => request<WorldTypeLibraryViewModel>(`${worldUrl}/types`),
  addElement: (stateId: string, name: string, description: string, type: string) => request(`${worldUrl}/elements`, { method: 'POST', body: JSON.stringify({ expectedStateId: stateId, name, description, type }) }),
  updateElement: (id: string, stateId: string, changes: { name?: string; description?: string; type?: string }) => request(`${worldUrl}/elements/${id}`, { method: 'PATCH', body: JSON.stringify({ expectedStateId: stateId, ...changes }) }),
  removeElement: (id: string, stateId: string) => request(`${worldUrl}/elements/${id}?expectedStateId=${stateId}`, { method: 'DELETE' }),
  addScope: (stateId: string, name: string, description: string, quantity: number, type: string, ownerElementId: string) => request(`${worldUrl}/scopes`, { method: 'POST', body: JSON.stringify({ expectedStateId: stateId, name, description, quantity, type, ownerElementId }) }),
  updateScope: (id: string, stateId: string, changes: { name?: string; description?: string; quantity?: number; type?: string }) => request(`${worldUrl}/scopes/${id}`, { method: 'PATCH', body: JSON.stringify({ expectedStateId: stateId, ...changes }) }),
  removeScope: (id: string, stateId: string) => request(`${worldUrl}/scopes/${id}?expectedStateId=${stateId}`, { method: 'DELETE' }),
  addAspect: (stateId: string, name: string, description: string, quantity: number, type: string, elementId: string, scopeId: string) => request(`${worldUrl}/aspects`, { method: 'POST', body: JSON.stringify({ expectedStateId: stateId, name, description, quantity, type, elementId, scopeId }) }),
  updateAspect: (id: string, stateId: string, changes: { name?: string; description?: string; quantity?: number; type?: string }) => request(`${worldUrl}/aspects/${id}`, { method: 'PATCH', body: JSON.stringify({ expectedStateId: stateId, ...changes }) }),
  removeAspect: (id: string, stateId: string) => request(`${worldUrl}/aspects/${id}?expectedStateId=${stateId}`, { method: 'DELETE' }),
  addRelation: (stateId: string, name: string, description: string, quantity: number, type: string, sourceElementId: string, targetElementId: string, scopeId: string) => request(`${worldUrl}/relations`, { method: 'POST', body: JSON.stringify({ expectedStateId: stateId, name, description, quantity, type, sourceElementId, targetElementId, scopeId }) }),
  updateRelation: (id: string, stateId: string, changes: { name?: string; description?: string; quantity?: number; type?: string }) => request(`${worldUrl}/relations/${id}`, { method: 'PATCH', body: JSON.stringify({ expectedStateId: stateId, ...changes }) }),
  removeRelation: (id: string, stateId: string) => request(`${worldUrl}/relations/${id}?expectedStateId=${stateId}`, { method: 'DELETE' }),
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

export const scenarioApi = {
  get: (randomSeed = 0) => request<ScenarioWorkspaceViewModel>(`${scenarioUrl}/?randomSeed=${randomSeed}`),
  createScene: (stateId: string, definitionId: string, randomSeed = 0) => request<ScenarioWorkspaceViewModel>(`${scenarioUrl}/scenes`, { method: 'POST', body: JSON.stringify({ expectedStateId: stateId, definitionId, randomSeed }) }),
  setBinding: (sceneId: string, slotId: string, stateId: string, elementIds: string[]) => request<ScenarioWorkspaceViewModel>(`${scenarioUrl}/scenes/${sceneId}/bindings/${encodeURIComponent(slotId)}`, { method: 'PUT', body: JSON.stringify({ expectedStateId: stateId, elementIds }) }),
  clearBinding: (sceneId: string, slotId: string, stateId: string) => request<ScenarioWorkspaceViewModel>(`${scenarioUrl}/scenes/${sceneId}/bindings/${encodeURIComponent(slotId)}?expectedStateId=${stateId}`, { method: 'DELETE' }),
  beginProcessing: (sceneId: string, stateId: string) => request<ScenarioWorkspaceViewModel>(`${scenarioUrl}/scenes/${sceneId}/processing`, { method: 'POST', body: JSON.stringify({ expectedStateId: stateId }) }),
  settleRules: (sceneId: string, stateId: string, randomSeed = 0) => request<ScenarioWorkspaceViewModel>(`${scenarioUrl}/scenes/${sceneId}/settle-rules`, { method: 'POST', body: JSON.stringify({ expectedStateId: stateId, randomSeed }) }),
  removeScene: (sceneId: string, stateId: string) => request<ScenarioWorkspaceViewModel>(`${scenarioUrl}/scenes/${sceneId}?expectedStateId=${stateId}`, { method: 'DELETE' }),
  clearSettled: (stateId: string) => request<ScenarioWorkspaceViewModel>(`${scenarioUrl}/scenes/clear-settled`, { method: 'POST', body: JSON.stringify({ expectedStateId: stateId }) }),
  undo: (stateId: string) => request<ScenarioWorkspaceViewModel>(`${scenarioUrl}/undo`, { method: 'POST', body: JSON.stringify({ expectedStateId: stateId }) }),
  redo: (stateId: string) => request<ScenarioWorkspaceViewModel>(`${scenarioUrl}/redo`, { method: 'POST', body: JSON.stringify({ expectedStateId: stateId }) }),
  save: () => request<ScenarioWorkspaceViewModel>(`${scenarioUrl}/save`, { method: 'POST', body: '{}' }),
}
