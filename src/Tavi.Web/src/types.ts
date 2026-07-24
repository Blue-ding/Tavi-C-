export interface ElementViewModel {
  id: string
  name: string
  description: string
  type: string
}

export interface AspectViewModel {
  id: string
  name: string
  description: string
  quantity: number
  type: string
  elementId: string
  scopeId: string
}

export interface RelationViewModel {
  id: string
  name: string
  description: string
  quantity: number
  type: string
  sourceElementId: string
  targetElementId: string
  scopeId: string
}

export interface ScopeViewModel {
  id: string
  name: string
  description: string
  quantity: number
  type: string
  ownerElementId: string
}

export interface WorldGraphViewModel {
  stateId: string
  stagingRevision: number
  isDirty: boolean
  canUndo: boolean
  canRedo: boolean
  health: 'Healthy' | 'Faulted'
  elements: ElementViewModel[]
  aspects: AspectViewModel[]
  relations: RelationViewModel[]
  scopes: ScopeViewModel[]
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

export interface ProposalElementReferenceViewModel {
  kind: 'Existing' | 'Proposed'
  elementId: string
}

export interface ProposalScopeReferenceViewModel {
  kind: 'Existing' | 'Proposed'
  scopeId: string
}

export interface ProposeAddElementViewModel {
  kind: 'AddElement'
  id: string
  rationale: string
  elementId: string
  name: string
  description: string
  type: string
}

export interface ProposeAddScopeViewModel {
  kind: 'AddScope'
  id: string
  rationale: string
  scopeId: string
  name: string
  description: string
  quantity: number
  type: string
  owner: ProposalElementReferenceViewModel
}

export interface ProposeAddAspectViewModel {
  kind: 'AddAspect'
  id: string
  rationale: string
  name: string
  description: string
  quantity: number
  type: string
  element: ProposalElementReferenceViewModel
  scope: ProposalScopeReferenceViewModel
}

export interface ProposeAddRelationViewModel {
  kind: 'AddRelation'
  id: string
  rationale: string
  name: string
  description: string
  quantity: number
  type: string
  source: ProposalElementReferenceViewModel
  target: ProposalElementReferenceViewModel
  scope: ProposalScopeReferenceViewModel
}

export type ProposalChangeViewModel = ProposeAddElementViewModel | ProposeAddScopeViewModel | ProposeAddAspectViewModel | ProposeAddRelationViewModel

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
  createdElements: { proposalId: string; worldId: string }[]
  createdScopes: { proposalId: string; worldId: string }[]
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

export type Selection = { kind: 'element' | 'aspect' | 'relation' | 'scope'; id: string } | null

export type ManuscriptStatus = 'Editing' | 'Archived'

export interface ManuscriptParagraphViewModel {
  id: string
  text: string
}

export interface ManuscriptViewModel {
  id: string
  stateId: string
  title: string
  status: ManuscriptStatus
  paragraphs: ManuscriptParagraphViewModel[]
  createdAtUtc: string
  updatedAtUtc: string
}

export interface ManuscriptSummaryViewModel {
  id: string
  title: string
  status: ManuscriptStatus
  paragraphCount: number
  preview: string
  createdAtUtc: string
  updatedAtUtc: string
}

export interface WritingSnapshotViewModel {
  manuscript: ManuscriptViewModel | null
  stagingRevision: number
  isDirty: boolean
  canUndo: boolean
  canRedo: boolean
  stagedChangeCount: number
  autoSaveError: string | null
}

export interface WritingWorkspaceViewModel {
  manuscripts: ManuscriptSummaryViewModel[]
  session: WritingSnapshotViewModel
}
