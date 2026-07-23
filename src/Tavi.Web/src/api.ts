import type { ApiProblem, GuidanceAvailabilityViewModel, GuidanceCommitViewModel, GuidanceOperationViewModel, GuidanceSnapshotViewModel, WorldGraphViewModel } from './types'

const worldUrl = '/api/v1/world'
const guidanceUrl = '/api/v1/guidance'

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
  addAnchor: (revision: number, name: string, description: string, type: string) => request(`${worldUrl}/anchors`, { method: 'POST', body: JSON.stringify({ expectedRevision: revision, name, description, type }) }),
  updateAnchor: (id: string, revision: number, changes: { name?: string; description?: string; type?: string }) => request(`${worldUrl}/anchors/${id}`, { method: 'PATCH', body: JSON.stringify({ expectedRevision: revision, ...changes }) }),
  removeAnchor: (id: string, revision: number) => request(`${worldUrl}/anchors/${id}?expectedRevision=${revision}`, { method: 'DELETE' }),
  addRelation: (revision: number, name: string, description: string, sourceId: string, targetId: string, domainCharacterId: string | null) => request(`${worldUrl}/relations`, { method: 'POST', body: JSON.stringify({ expectedRevision: revision, name, description, sourceId, targetId, domainCharacterId }) }),
  updateRelation: (id: string, revision: number, changes: { name?: string; description?: string }) => request(`${worldUrl}/relations/${id}`, { method: 'PATCH', body: JSON.stringify({ expectedRevision: revision, ...changes }) }),
  removeRelation: (id: string, revision: number) => request(`${worldUrl}/relations/${id}?expectedRevision=${revision}`, { method: 'DELETE' }),
  createSubWorld: (revision: number, characterId: string) => request(`${worldUrl}/subworlds`, { method: 'POST', body: JSON.stringify({ expectedRevision: revision, characterId }) }),
  removeSubWorld: (revision: number, characterId: string) => request(`${worldUrl}/subworlds/${characterId}?expectedRevision=${revision}`, { method: 'DELETE' }),
  undo: (revision: number) => request(`${worldUrl}/undo`, { method: 'POST', body: JSON.stringify({ expectedRevision: revision }) }),
  redo: (revision: number) => request(`${worldUrl}/redo`, { method: 'POST', body: JSON.stringify({ expectedRevision: revision }) }),
  save: () => request(`${worldUrl}/save`, { method: 'POST', body: '{}' }),
}

export const guidanceApi = {
  availability: () => request<GuidanceAvailabilityViewModel>(`${guidanceUrl}/`),
  start: (potential: string) => request<GuidanceOperationViewModel>(`${guidanceUrl}/sessions`, { method: 'POST', body: JSON.stringify({ potential }) }),
  get: (sessionId: string) => request<GuidanceSnapshotViewModel>(`${guidanceUrl}/sessions/${sessionId}`),
  continue: (sessionId: string, message: string) => request<GuidanceOperationViewModel>(`${guidanceUrl}/sessions/${sessionId}/messages`, { method: 'POST', body: JSON.stringify({ message }) }),
  commit: (sessionId: string, acceptedChangeIds: string[]) => request<GuidanceCommitViewModel>(`${guidanceUrl}/sessions/${sessionId}/commit`, { method: 'POST', body: JSON.stringify({ acceptedChangeIds }) }),
  cancel: (sessionId: string) => request<GuidanceSnapshotViewModel>(`${guidanceUrl}/sessions/${sessionId}/cancel`, { method: 'POST', body: '{}' }),
  forget: (sessionId: string) => request<void>(`${guidanceUrl}/sessions/${sessionId}`, { method: 'DELETE' }),
}
