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
  stateId: string
  stagingRevision: number
  isDirty: boolean
  canUndo: boolean
  canRedo: boolean
  health: 'Healthy' | 'Faulted'
  nodes: AnchorViewModel[]
  edges: RelationViewModel[]
  subWorlds: SubWorldViewModel[]
  stagedChanges: WorldStagedChangeViewModel[]
}

export interface WorldStagedChangeViewModel {
  id: string
  source: 'Player' | 'Guidance'
  status: 'Valid' | 'Conflict' | 'Invalid'
  operation: string
  issue: string | null
  conflictingChangeIds: string[]
}

export interface WorldCommitViewModel {
  commitId: string
  previousStateId: string
  stateId: string
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

export type FeaturePolicy = 'Disabled' | 'Preferred' | 'Required'

export interface LanguageModelSettingsViewModel {
  maxToolRounds: number
  maxOutputRepairAttempts: number
  overallTimeoutSeconds: number
  toolCalls: FeaturePolicy
  nativeJsonOutput: FeaturePolicy
  streaming: FeaturePolicy
}

export type OpenAIClientType = 'Chat' | 'Responses'

export interface OpenAIConfigurationViewModel {
  endpoint: string
  model: string
  clientType: OpenAIClientType
  supportsRequiredToolChoice: boolean
  enableThinking: boolean | null
  hasApiKey: boolean
}

export interface OpenAIConfigurationInput extends OpenAIConfigurationViewModel {
  apiKey: string
}

export interface SettingsSaveResultViewModel {
  languageModel: LanguageModelSettingsViewModel
  openAI: OpenAIConfigurationViewModel
  sessionRecreationRequired: boolean
  message: string
}

export interface GuidanceAvailabilityViewModel {
  available: boolean
  provider: string | null
  message: string
}

export type GuidanceState = 'Idle' | 'Generating' | 'Faulted'
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
  baseWorldStateId: string
  summary: string
  changes: ProposalChangeViewModel[]
}

export interface GuidanceSnapshotViewModel {
  sessionId: string
  state: GuidanceState
  baseWorldStateId: string
  messages: GuidanceMessageViewModel[]
  proposal: WorldProposalViewModel | null
  failure: { code: string; message: string; isTransient: boolean } | null
  retryMessage: string | null
}

export interface GuidanceOperationViewModel {
  operationId: string
  sessionId: string
  state: string
  snapshot: GuidanceSnapshotViewModel
}

export interface GuidanceCommitViewModel {
  status: 'Committed' | 'InvalidSelection' | 'WorldConflict' | 'SessionNotReady'
  worldStateId: string | null
  createdAnchors: { proposalAnchorId: string; worldAnchorId: string }[]
  issues: { code: string; message: string; changeId: string | null }[]
  expectedWorldStateId: string | null
  actualWorldStateId: string | null
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
