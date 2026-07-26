namespace Tavi.Host.ViewModels;

/// <summary>表示 Performance 来源 Scene 的冻结身份与绑定。</summary>
public sealed record PerformanceSourceSceneViewModel(
    Guid Id,
    string DefinitionId,
    string Module,
    string ModuleVersion,
    Guid BasedOnScenarioStateId,
    string State,
    IReadOnlyList<PerformanceSourceBindingViewModel> Bindings);

public sealed record PerformanceSourceBindingViewModel(
    string SlotId,
    IReadOnlyList<Guid> ElementIds);

public sealed record PerformanceModuleViewModel(
    string Id,
    string Version);

public sealed record BeatSlotViewModel(
    string Id,
    string Name,
    string Description,
    int Minimum,
    int? Maximum,
    IReadOnlyList<Guid> ElementIds);

public sealed record BeatParagraphViewModel(Guid Id, string Text);

public sealed record BeatPublicationViewModel(
    Guid ManuscriptId,
    Guid ManuscriptStateId);

public sealed record BeatViewModel(
    Guid Id,
    string DefinitionId,
    string Module,
    string ModuleVersion,
    Guid BasedOnPerformanceStateId,
    string Name,
    string Description,
    string State,
    IReadOnlyList<BeatSlotViewModel> Slots,
    IReadOnlyList<BeatParagraphViewModel> Paragraphs,
    BeatPublicationViewModel? Publication);

public sealed record BeatDefinitionViewModel(
    string Id,
    string Module,
    string ModuleVersion,
    string Name,
    string Description,
    Guid? SourcePerformanceStateId,
    IReadOnlyList<BeatDefinitionSlotViewModel> Slots);

public sealed record BeatDefinitionSlotViewModel(
    string Id,
    string Name,
    string Description,
    int Minimum,
    int? Maximum);

/// <summary>表示活动或归档 Performance 的完整只读状态。</summary>
public sealed record PerformanceSnapshotViewModel(
    Guid PerformanceId,
    Guid StateId,
    Guid SourceScenarioStateId,
    string Status,
    PerformanceSourceSceneViewModel SourceScene,
    IReadOnlyList<PerformanceModuleViewModel> Modules,
    IReadOnlyList<ElementViewModel> Elements,
    IReadOnlyList<ScopeViewModel> Scopes,
    IReadOnlyList<AspectViewModel> Aspects,
    IReadOnlyList<RelationViewModel> Relations,
    IReadOnlyList<BeatViewModel> Beats);

/// <summary>表示前端推进活动 Performance 所需的权威工作区。</summary>
public sealed record PerformanceWorkspaceViewModel(
    bool HasSession,
    PerformanceSnapshotViewModel? Performance,
    string? Health,
    bool IsDirty,
    bool CanUndo,
    bool CanRedo,
    string? AutoSaveError,
    IReadOnlyList<BeatDefinitionViewModel> Definitions);

public sealed record PerformanceArchiveSummaryViewModel(
    Guid PerformanceId,
    Guid StateId,
    Guid SourceScenarioStateId,
    Guid SourceSceneId,
    string Status,
    int BeatCount);

public sealed record StartPerformanceRequest(
    Guid SceneId,
    Guid ExpectedScenarioStateId,
    long RandomSeed);

public sealed record CreateBeatRequest(
    string DefinitionId,
    long RandomSeed,
    Guid ExpectedStateId);

public sealed record SetBeatBindingRequest(
    IReadOnlyList<Guid> ElementIds,
    Guid ExpectedStateId);

public sealed record PerformanceStateRequest(Guid ExpectedStateId);

public sealed record ResolveBeatRequest(
    string Interaction,
    long RandomSeed,
    Guid ExpectedStateId);

public sealed record PublishBeatRequest(
    Guid ExpectedStateId,
    Guid ExpectedManuscriptStateId);

public sealed record CompletePerformanceRequest(
    Guid ExpectedScenarioStateId,
    Guid ExpectedPerformanceStateId);

public sealed record ScenarioPerformanceCompletionViewModel(
    Guid SceneId,
    Guid ScenarioStateId,
    Guid PerformanceStateId,
    PerformanceWorkspaceViewModel Workspace);

/// <summary>表示推送给前端的 Performance 状态事件。</summary>
public sealed record PerformanceEventViewModel(
    string Type,
    Guid StateId,
    bool IsDirty,
    Guid? CommitId,
    string? Operation,
    string? Error);
