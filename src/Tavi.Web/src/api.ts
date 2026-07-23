import type { ApiProblem, WorldGraphViewModel } from './types'

const worldUrl = '/api/v1/world'

export class ApiError extends Error {
  readonly code: string

  constructor(message: string, code = 'TAVI.WEB.REQUEST.FAILED') {
    super(message)
    this.name = 'ApiError'
    this.code = code
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${worldUrl}${path}`, {
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
  get: () => request<WorldGraphViewModel>('/'),
  addAnchor: (revision: number, name: string, description: string, type: string) => request('/anchors', { method: 'POST', body: JSON.stringify({ expectedRevision: revision, name, description, type }) }),
  updateAnchor: (id: string, revision: number, changes: { name?: string; description?: string; type?: string }) => request(`/anchors/${id}`, { method: 'PATCH', body: JSON.stringify({ expectedRevision: revision, ...changes }) }),
  removeAnchor: (id: string, revision: number) => request(`/anchors/${id}?expectedRevision=${revision}`, { method: 'DELETE' }),
  addRelation: (revision: number, name: string, description: string, sourceId: string, targetId: string, domainCharacterId: string | null) => request('/relations', { method: 'POST', body: JSON.stringify({ expectedRevision: revision, name, description, sourceId, targetId, domainCharacterId }) }),
  updateRelation: (id: string, revision: number, changes: { name?: string; description?: string }) => request(`/relations/${id}`, { method: 'PATCH', body: JSON.stringify({ expectedRevision: revision, ...changes }) }),
  removeRelation: (id: string, revision: number) => request(`/relations/${id}?expectedRevision=${revision}`, { method: 'DELETE' }),
  createSubWorld: (revision: number, characterId: string) => request('/subworlds', { method: 'POST', body: JSON.stringify({ expectedRevision: revision, characterId }) }),
  removeSubWorld: (revision: number, characterId: string) => request(`/subworlds/${characterId}?expectedRevision=${revision}`, { method: 'DELETE' }),
  undo: (revision: number) => request('/undo', { method: 'POST', body: JSON.stringify({ expectedRevision: revision }) }),
  redo: (revision: number) => request('/redo', { method: 'POST', body: JSON.stringify({ expectedRevision: revision }) }),
  save: () => request('/save', { method: 'POST', body: '{}' }),
}
