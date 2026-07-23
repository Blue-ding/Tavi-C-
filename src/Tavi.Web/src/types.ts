export type AnchorType = 'Character' | 'Item'
export type WorldScope = 'World' | 'SubWorld'

export interface AnchorViewModel {
  id: string
  name: string
  description: string
  type: AnchorType
  hasSubWorld: boolean
}

export interface RelationViewModel {
  id: string
  name: string
  description: string
  sourceId: string
  targetId: string
  scope: WorldScope
  domainCharacterId: string | null
}

export interface SubWorldViewModel {
  id: string
  characterId: string
}

export interface WorldGraphViewModel {
  worldId: string
  revision: number
  isDirty: boolean
  canUndo: boolean
  canRedo: boolean
  health: 'Healthy' | 'Faulted'
  nodes: AnchorViewModel[]
  edges: RelationViewModel[]
  subWorlds: SubWorldViewModel[]
}

export interface WorldCommitViewModel {
  commitId: string
  previousRevision: number
  revision: number
  changed: boolean
  entityId: string | null
}

export interface ApiProblem {
  title?: string
  error?: {
    code: string
    message: string
    category: string
    traceId: string
  }
}

export type Selection = { kind: 'anchor'; id: string } | { kind: 'relation'; id: string } | null
