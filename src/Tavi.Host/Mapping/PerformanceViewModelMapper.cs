using Tavi.Application.Performance;
using Tavi.Domain.Performance;
using Tavi.Extensibility;
using Tavi.Host.ViewModels;

namespace Tavi.Host.Mapping;

internal static class PerformanceViewModelMapper
{
    internal static PerformanceWorkspaceViewModel EmptyWorkspace() =>
        new(false, null, null, false, false, false, null, []);

    internal static PerformanceWorkspaceViewModel ToWorkspace(
        IPerformanceWorkspace workspace,
        IReadOnlyList<BeatDefinition> definitions) =>
        new(
            true,
            ToSnapshot(workspace.Queries.CreateSnapshot()),
            workspace.Health.ToString(),
            workspace.IsDirty,
            workspace.CanUndo,
            workspace.CanRedo,
            workspace.LastAutoSaveException?.Message,
            definitions.Select(ToDefinition).ToArray());

    internal static PerformanceSnapshotViewModel ToSnapshot(
        PerformanceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new PerformanceSnapshotViewModel(
            snapshot.PerformanceId,
            snapshot.StateId,
            snapshot.SourceScenarioStateId,
            snapshot.Status.ToString(),
            new PerformanceSourceSceneViewModel(
                snapshot.SourceScene.Id,
                snapshot.SourceScene.DefinitionId,
                snapshot.SourceScene.ModuleId,
                snapshot.SourceScene.ModuleVersion,
                snapshot.SourceScene.BasedOnScenarioStateId,
                snapshot.SourceScene.State,
                snapshot.SourceScene.Bindings
                    .Select(value => new PerformanceSourceBindingViewModel(
                        value.SlotId,
                        value.ElementIds))
                    .ToArray()),
            snapshot.Modules
                .OrderBy(value => value.Id, StringComparer.Ordinal)
                .Select(value => new PerformanceModuleViewModel(
                    value.Id,
                    value.Version))
                .ToArray(),
            snapshot.Elements.Values
                .OrderBy(value => value.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(value => value.Id)
                .Select(value => new ElementViewModel(
                    value.Id,
                    value.Name,
                    value.Description,
                    value.Type.Value))
                .ToArray(),
            snapshot.Scopes.Values
                .OrderBy(value => value.Type.Value, StringComparer.Ordinal)
                .ThenBy(value => value.Id)
                .Select(value => new ScopeViewModel(
                    value.Id,
                    value.Quantity,
                    value.Type.Value,
                    value.OwnerElementId))
                .ToArray(),
            snapshot.Aspects.Values
                .OrderBy(value => value.Type.Value, StringComparer.Ordinal)
                .ThenBy(value => value.Id)
                .Select(value => new AspectViewModel(
                    value.Id,
                    value.Quantity,
                    value.Type.Value,
                    value.ElementId,
                    value.ScopeId))
                .ToArray(),
            snapshot.Relations.Values
                .OrderBy(value => value.Type.Value, StringComparer.Ordinal)
                .ThenBy(value => value.Id)
                .Select(value => new RelationViewModel(
                    value.Id,
                    value.Quantity,
                    value.Type.Value,
                    value.SourceElementId,
                    value.TargetElementId,
                    value.ScopeId))
                .ToArray(),
            snapshot.Beats.Values
                .OrderBy(value => value.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(value => value.Id)
                .Select(ToBeat)
                .ToArray());
    }

    internal static PerformanceArchiveSummaryViewModel ToSummary(
        PerformanceSnapshot snapshot) =>
        new(
            snapshot.PerformanceId,
            snapshot.StateId,
            snapshot.SourceScenarioStateId,
            snapshot.SourceScene.Id,
            snapshot.Status.ToString(),
            snapshot.Beats.Count);

    private static BeatViewModel ToBeat(Beat beat)
    {
        Dictionary<string, IReadOnlyList<Guid>> bindings = beat.GetBindings()
            .ToDictionary(value => value.SlotId, value => value.ElementIds);
        return new BeatViewModel(
            beat.Id,
            beat.DefinitionId.Value,
            beat.ModuleId,
            beat.ModuleVersion,
            beat.BasedOnPerformanceStateId,
            beat.Name,
            beat.Description,
            beat.State.ToString(),
            beat.GetSlotSpecifications()
                .Select(value => new BeatSlotViewModel(
                    value.Id,
                    value.Name,
                    value.Description,
                    value.Minimum,
                    value.Maximum,
                    bindings.GetValueOrDefault(value.Id) ?? []))
                .ToArray(),
            beat.Paragraphs
                .Select(value => new BeatParagraphViewModel(value.Id, value.Text))
                .ToArray(),
            beat.Publication is null
                ? null
                : new BeatPublicationViewModel(
                    beat.Publication.ManuscriptId,
                    beat.Publication.ManuscriptStateId));
    }

    private static BeatDefinitionViewModel ToDefinition(
        BeatDefinition definition) =>
        new(
            definition.Id.Value,
            definition.Module.Value,
            definition.ModuleVersion.Value,
            definition.Name,
            definition.Description,
            definition.SourcePerformanceStateId,
            definition.Slots.Select(value =>
                new BeatDefinitionSlotViewModel(
                    value.Id,
                    value.Name,
                    value.Description,
                    value.Minimum,
                    value.Maximum)).ToArray());
}
