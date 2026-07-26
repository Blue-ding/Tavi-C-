using Tavi.Domain.Performance;

namespace Tavi.Infrastructure.Persistence;

internal sealed class PerformanceSaveDocumentV1
{
    internal const int CurrentVersion = 1;
    public int Version { get; init; } = CurrentVersion;
    public DateTimeOffset SavedAtUtc { get; init; }
    public required PerformanceSaveDataV1 Performance { get; init; }
}

internal sealed class PerformanceSaveDataV1
{
    public Guid PerformanceId { get; init; }
    public Guid StateId { get; init; }
    public Guid SourceScenarioStateId { get; init; }
    public required PerformanceSourceSceneDataV1 SourceScene { get; init; }
    public string Status { get; init; } = string.Empty;
    public PerformanceModuleDataV1[] Modules { get; init; } = [];
    public Guid[] ImportedElementIds { get; init; } = [];
    public ElementDataV1[] Elements { get; init; } = [];
    public ScopeDataV1[] Scopes { get; init; } = [];
    public AspectDataV1[] Aspects { get; init; } = [];
    public RelationDataV1[] Relations { get; init; } = [];
    public BeatDataV1[] Beats { get; init; } = [];
}

internal sealed class PerformanceSourceSceneDataV1
{
    public Guid Id { get; init; }
    public string DefinitionId { get; init; } = string.Empty;
    public string ModuleId { get; init; } = string.Empty;
    public string ModuleVersion { get; init; } = string.Empty;
    public Guid BasedOnScenarioStateId { get; init; }
    public string State { get; init; } = string.Empty;
    public BindingDataV1[] Bindings { get; init; } = [];
    public ElementDataV1[] Elements { get; init; } = [];
    public ScopeDataV1[] Scopes { get; init; } = [];
    public AspectDataV1[] Aspects { get; init; } = [];
    public RelationDataV1[] Relations { get; init; } = [];
}

internal sealed class PerformanceModuleDataV1
{
    public string Id { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public Dictionary<string, string> Parameters { get; init; } = new(StringComparer.Ordinal);
}

internal sealed class ElementDataV1
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
}

internal sealed class ScopeDataV1
{
    public Guid Id { get; init; }
    public int Quantity { get; init; }
    public string Type { get; init; } = string.Empty;
    public Guid OwnerElementId { get; init; }
}

internal sealed class AspectDataV1
{
    public Guid Id { get; init; }
    public int Quantity { get; init; }
    public string Type { get; init; } = string.Empty;
    public Guid ElementId { get; init; }
    public Guid ScopeId { get; init; }
}

internal sealed class RelationDataV1
{
    public Guid Id { get; init; }
    public int Quantity { get; init; }
    public string Type { get; init; } = string.Empty;
    public Guid SourceElementId { get; init; }
    public Guid TargetElementId { get; init; }
    public Guid ScopeId { get; init; }
}

internal sealed class BeatDataV1
{
    public Guid Id { get; init; }
    public string DefinitionId { get; init; } = string.Empty;
    public string ModuleId { get; init; } = string.Empty;
    public string ModuleVersion { get; init; } = string.Empty;
    public Guid BasedOnPerformanceStateId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public BeatSlotDataV1[] Slots { get; init; } = [];
    public BindingDataV1[] Bindings { get; init; } = [];
    public BeatParagraphDataV1[] Paragraphs { get; init; } = [];
    public BeatPublicationDataV1? Publication { get; init; }
}

internal sealed class BeatSlotDataV1
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public int Minimum { get; init; }
    public int? Maximum { get; init; }
}

internal sealed class BindingDataV1
{
    public string SlotId { get; init; } = string.Empty;
    public Guid[] ElementIds { get; init; } = [];
}

internal sealed class BeatParagraphDataV1
{
    public Guid Id { get; init; }
    public string Text { get; init; } = string.Empty;
}

internal sealed class BeatPublicationDataV1
{
    public Guid ManuscriptId { get; init; }
    public Guid ManuscriptStateId { get; init; }
}

internal static class PerformanceSaveMapper
{
    internal static PerformanceSaveDataV1 FromDomain(PerformanceSnapshot snapshot) => new()
    {
        PerformanceId = snapshot.PerformanceId,
        StateId = snapshot.StateId,
        SourceScenarioStateId = snapshot.SourceScenarioStateId,
        SourceScene = new PerformanceSourceSceneDataV1
        {
            Id = snapshot.SourceScene.Id,
            DefinitionId = snapshot.SourceScene.DefinitionId,
            ModuleId = snapshot.SourceScene.ModuleId,
            ModuleVersion = snapshot.SourceScene.ModuleVersion,
            BasedOnScenarioStateId = snapshot.SourceScene.BasedOnScenarioStateId,
            State = snapshot.SourceScene.State,
            Bindings = snapshot.SourceScene.Bindings.Select(ToData).ToArray(),
            Elements = snapshot.SourceScene.Elements.Select(value => new ElementDataV1 { Id = value.Id, Name = value.Name, Description = value.Description, Type = value.Type.Value }).ToArray(),
            Scopes = snapshot.SourceScene.Scopes.Select(value => new ScopeDataV1 { Id = value.Id, Quantity = value.Quantity, Type = value.Type.Value, OwnerElementId = value.OwnerElementId }).ToArray(),
            Aspects = snapshot.SourceScene.Aspects.Select(value => new AspectDataV1 { Id = value.Id, Quantity = value.Quantity, Type = value.Type.Value, ElementId = value.ElementId, ScopeId = value.ScopeId }).ToArray(),
            Relations = snapshot.SourceScene.Relations.Select(value => new RelationDataV1 { Id = value.Id, Quantity = value.Quantity, Type = value.Type.Value, SourceElementId = value.SourceElementId, TargetElementId = value.TargetElementId, ScopeId = value.ScopeId }).ToArray()
        },
        Status = snapshot.Status.ToString(),
        Modules = snapshot.Modules.Select(value => new PerformanceModuleDataV1 { Id = value.Id, Version = value.Version, Parameters = new Dictionary<string, string>(value.Parameters, StringComparer.Ordinal) }).ToArray(),
        ImportedElementIds = snapshot.ImportedElementIds.ToArray(),
        Elements = snapshot.Elements.Values.Select(value => new ElementDataV1 { Id = value.Id, Name = value.Name, Description = value.Description, Type = value.Type.Value }).ToArray(),
        Scopes = snapshot.Scopes.Values.Select(value => new ScopeDataV1 { Id = value.Id, Quantity = value.Quantity, Type = value.Type.Value, OwnerElementId = value.OwnerElementId }).ToArray(),
        Aspects = snapshot.Aspects.Values.Select(value => new AspectDataV1 { Id = value.Id, Quantity = value.Quantity, Type = value.Type.Value, ElementId = value.ElementId, ScopeId = value.ScopeId }).ToArray(),
        Relations = snapshot.Relations.Values.Select(value => new RelationDataV1 { Id = value.Id, Quantity = value.Quantity, Type = value.Type.Value, SourceElementId = value.SourceElementId, TargetElementId = value.TargetElementId, ScopeId = value.ScopeId }).ToArray(),
        Beats = snapshot.Beats.Values.Select(value => new BeatDataV1
        {
            Id = value.Id,
            DefinitionId = value.DefinitionId.Value,
            ModuleId = value.ModuleId,
            ModuleVersion = value.ModuleVersion,
            BasedOnPerformanceStateId = value.BasedOnPerformanceStateId,
            Name = value.Name,
            Description = value.Description,
            State = value.State.ToString(),
            Slots = value.GetSlotSpecifications().Select(slot => new BeatSlotDataV1 { Id = slot.Id, Name = slot.Name, Description = slot.Description, Minimum = slot.Minimum, Maximum = slot.Maximum }).ToArray(),
            Bindings = value.GetBindings().Select(ToData).ToArray(),
            Paragraphs = value.Paragraphs.Select(paragraph => new BeatParagraphDataV1 { Id = paragraph.Id, Text = paragraph.Text }).ToArray(),
            Publication = value.Publication is null ? null : new BeatPublicationDataV1 { ManuscriptId = value.Publication.ManuscriptId, ManuscriptStateId = value.Publication.ManuscriptStateId }
        }).ToArray()
    };

    internal static PerformanceSnapshot ToDomain(PerformanceSaveDataV1 data)
    {
        var snapshot = new PerformanceSnapshot
        {
            PerformanceId = data.PerformanceId,
            StateId = data.StateId,
            SourceScenarioStateId = data.SourceScenarioStateId,
            SourceScene = new PerformanceSourceScene
            {
                Id = data.SourceScene.Id,
                DefinitionId = data.SourceScene.DefinitionId,
                ModuleId = data.SourceScene.ModuleId,
                ModuleVersion = data.SourceScene.ModuleVersion,
                BasedOnScenarioStateId = data.SourceScene.BasedOnScenarioStateId,
                State = data.SourceScene.State,
                Bindings = data.SourceScene.Bindings.Select(value => new PerformanceSourceSceneBinding(value.SlotId, value.ElementIds)).ToArray(),
                Elements = data.SourceScene.Elements.Select(value => new Element(value.Id, value.Name, value.Description, new ElementType(value.Type))).ToArray(),
                Scopes = data.SourceScene.Scopes.Select(value => new Scope(value.Id, value.Quantity, new ScopeType(value.Type), value.OwnerElementId)).ToArray(),
                Aspects = data.SourceScene.Aspects.Select(value => new Aspect(value.Id, value.Quantity, new AspectType(value.Type), value.ElementId, value.ScopeId)).ToArray(),
                Relations = data.SourceScene.Relations.Select(value => new Relation(value.Id, value.Quantity, new RelationType(value.Type), value.SourceElementId, value.TargetElementId, value.ScopeId)).ToArray()
            },
            Status = Enum.Parse<PerformanceStatus>(data.Status),
            Modules = data.Modules.Select(value => new PerformanceModuleReference(value.Id, value.Version, value.Parameters)).ToList(),
            ImportedElementIds = data.ImportedElementIds.ToHashSet(),
            Elements = data.Elements.ToDictionary(value => value.Id, value => new Element(value.Id, value.Name, value.Description, new ElementType(value.Type))),
            Scopes = data.Scopes.ToDictionary(value => value.Id, value => new Scope(value.Id, value.Quantity, new ScopeType(value.Type), value.OwnerElementId)),
            Aspects = data.Aspects.ToDictionary(value => value.Id, value => new Aspect(value.Id, value.Quantity, new AspectType(value.Type), value.ElementId, value.ScopeId)),
            Relations = data.Relations.ToDictionary(value => value.Id, value => new Relation(value.Id, value.Quantity, new RelationType(value.Type), value.SourceElementId, value.TargetElementId, value.ScopeId)),
            Beats = data.Beats.ToDictionary(value => value.Id, value => new Beat(
                value.Id,
                new BeatDefinitionType(value.DefinitionId),
                value.ModuleId,
                value.ModuleVersion,
                value.BasedOnPerformanceStateId,
                value.Name,
                value.Description,
                Enum.Parse<BeatState>(value.State),
                value.Slots.Select(slot => new BeatSlotSpecification(slot.Id, slot.Name, slot.Description, slot.Minimum, slot.Maximum)),
                value.Bindings.Select(binding => new BeatSlotBinding(binding.SlotId, binding.ElementIds)),
                value.Paragraphs.Select(paragraph => new BeatParagraph(paragraph.Id, paragraph.Text)),
                value.Publication is null ? null : new BeatPublicationReceipt(value.Publication.ManuscriptId, value.Publication.ManuscriptStateId)))
        };
        _ = Tavi.Domain.Performance.Performance.Create(snapshot);
        return snapshot;
    }

    private static BindingDataV1 ToData(PerformanceSourceSceneBinding value) => new() { SlotId = value.SlotId, ElementIds = value.ElementIds.ToArray() };
    private static BindingDataV1 ToData(BeatSlotBinding value) => new() { SlotId = value.SlotId, ElementIds = value.ElementIds.ToArray() };
}
