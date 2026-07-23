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

export interface GuidanceAvailabilityViewModel {
  available: boolean
  provider: string | null
  message: string
}

export type GuidanceState = 'Created' | 'Generating' | 'AwaitingPlayer' | 'ReadyForReview' | 'Committing' | 'Completed' | 'Cancelled' | 'Failed'
export type GuidanceMessageRole = 'Player' | 'Guidance'

export interface GuidanceMessageViewModel {
  role: GuidanceMessageRole
  text: string
}

export interface ProposalAnchorReferenceViewModel {
  kind: 'Existing' | 'Proposed'
  anchorId: string
}

export interface ProposedRelationScopeViewModel {
  kind: 'World' | 'SubWorld'
  character: ProposalAnchorReferenceViewModel | null
}

export interface ProposeAddAnchorViewModel {
  kind: 'AddAnchor'
  id: string
  rationale: string
  anchorId: string
  name: string
  description: string
  type: AnchorType
}

export interface ProposeAddRelationViewModel {
  kind: 'AddRelation'
  id: string
  rationale: string
  name: string
  description: string
  source: ProposalAnchorReferenceViewModel
  target: ProposalAnchorReferenceViewModel
  scope: ProposedRelationScopeViewModel
}

export type ProposalChangeViewModel = ProposeAddAnchorViewModel | ProposeAddRelationViewModel

export interface WorldProposalViewModel {
  id: string
  baseWorldRevision: number
  summary: string
  changes: ProposalChangeViewModel[]
}

export interface GuidanceSnapshotViewModel {
  sessionId: string
  state: GuidanceState
  baseWorldRevision: number
  messages: GuidanceMessageViewModel[]
  proposal: WorldProposalViewModel | null
  failure: { code: string; message: string; isTransient: boolean } | null
}

export interface GuidanceOperationViewModel {
  operationId: string
  sessionId: string
  state: string
  snapshot: GuidanceSnapshotViewModel
}

export interface GuidanceCommitViewModel {
  status: 'Committed' | 'InvalidSelection' | 'WorldConflict' | 'SessionNotReady'
  worldRevision: number | null
  createdAnchors: { proposalAnchorId: string; worldAnchorId: string }[]
  issues: { code: string; message: string; changeId: string | null }[]
  expectedWorldRevision: number | null
  actualWorldRevision: number | null
  snapshot: GuidanceSnapshotViewModel
}

export interface GuidanceEventViewModel {
  type: string
  sessionId: string
  operationId: string | null
  text: string | null
  snapshot: GuidanceSnapshotViewModel | null
  error: string | null
}

export type Selection = { kind: 'anchor'; id: string } | { kind: 'relation'; id: string } | null
