using System.Collections.Frozen;
using Tavi.Extensibility;

namespace Tavi.Application.Extensions;

internal static class ExtensibilityCopies
{
    internal static ModulePackageDefinition Package(ModulePackageDefinition source) => new()
    {
        Manifest = Manifest(source.Manifest),
        Semantics = Semantics(source.Semantics),
        Scenes = Array.AsReadOnly(source.Scenes.Select(Scene).ToArray())
    };

    internal static ModuleManifest Manifest(ModuleManifest source) => source with { Dependencies = Array.AsReadOnly(source.Dependencies.Select(value => value with { }).ToArray()), Parameters = Array.AsReadOnly(source.Parameters.Select(value => value with { AllowedValues = Array.AsReadOnly(value.AllowedValues.ToArray()) }).ToArray()) };

    internal static SemanticModuleDefinition Semantics(SemanticModuleDefinition source) => new()
    {
        ElementTypes = Array.AsReadOnly(source.ElementTypes.Select(value => value with { Tags = value.Tags.ToFrozenSet() }).ToArray()),
        ScopeTypes = Array.AsReadOnly(source.ScopeTypes.Select(value => value with { OwnerElementTypes = value.OwnerElementTypes.ToFrozenSet(), Tags = value.Tags.ToFrozenSet() }).ToArray()),
        AspectGroups = Array.AsReadOnly(source.AspectGroups.Select(value => value with { }).ToArray()),
        AspectTypes = Array.AsReadOnly(source.AspectTypes.Select(value => value with { SubjectElementTypes = value.SubjectElementTypes.ToFrozenSet(), Tags = value.Tags.ToFrozenSet() }).ToArray()),
        RelationTypes = Array.AsReadOnly(source.RelationTypes.Select(value => value with { SourceElementTypes = value.SourceElementTypes.ToFrozenSet(), TargetElementTypes = value.TargetElementTypes.ToFrozenSet(), Tags = value.Tags.ToFrozenSet() }).ToArray()),
        Constraints = Array.AsReadOnly(source.Constraints.Select(value => value with { }).ToArray())
    };

    internal static SceneDefinition Scene(SceneDefinition source) => source with
    {
        Slots = Array.AsReadOnly(source.Slots.Select(slot => slot with
        {
            Requirement = slot.Requirement with { ElementTypes = slot.Requirement.ElementTypes.ToFrozenSet(), RequiredAspectGroups = slot.Requirement.RequiredAspectGroups.ToFrozenSet() }
        }).ToArray())
    };
}
