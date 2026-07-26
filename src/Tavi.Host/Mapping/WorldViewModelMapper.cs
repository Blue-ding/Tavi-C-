using Tavi.Application.World;
using Tavi.Domain.World;
using Tavi.Host.ViewModels;

namespace Tavi.Host.Mapping;

/// <summary>将 Application World 快照和操作转换为不泄露运行时引用的 Host 契约。</summary>
internal static class WorldViewModelMapper
{
    internal static WorldGraphViewModel ToGraph(IWorldBuildView workspace)
    {
        WorldStagingSnapshot staging = workspace.CreateStagingSnapshot();
        WorldSnapshot snapshot = staging.ProjectedWorld;
        ElementViewModel[] elements = snapshot.Elements.Values.OrderBy(value => value.Name, StringComparer.OrdinalIgnoreCase).ThenBy(value => value.Id).Select(value => new ElementViewModel(value.Id, value.Name, value.Description, value.Type.Value)).ToArray();
        AspectViewModel[] aspects = snapshot.Aspects.Values.OrderBy(value => value.Type.Value, StringComparer.Ordinal).ThenBy(value => value.Id).Select(value => new AspectViewModel(value.Id, value.Quantity, value.Type.Value, value.ElementId, value.ScopeId)).ToArray();
        RelationViewModel[] relations = snapshot.Relations.Values.OrderBy(value => value.Type.Value, StringComparer.Ordinal).ThenBy(value => value.Id).Select(value => new RelationViewModel(value.Id, value.Quantity, value.Type.Value, value.SourceElementId, value.TargetElementId, value.ScopeId)).ToArray();
        ScopeViewModel[] scopes = snapshot.Scopes.Values.OrderBy(value => value.Type.Value, StringComparer.Ordinal).ThenBy(value => value.Id).Select(value => new ScopeViewModel(value.Id, value.Quantity, value.Type.Value, value.OwnerElementId)).ToArray();
        LocalAspectViewModel[] localAspects = snapshot.LocalAspects.Values.OrderBy(value => value.Name, StringComparer.OrdinalIgnoreCase).ThenBy(value => value.Id).Select(value => new LocalAspectViewModel(value.Id, value.Name, value.Description, value.Quantity, value.ElementId, value.ScopeId)).ToArray();
        LocalRelationViewModel[] localRelations = snapshot.LocalRelations.Values.OrderBy(value => value.Name, StringComparer.OrdinalIgnoreCase).ThenBy(value => value.Id).Select(value => new LocalRelationViewModel(value.Id, value.Name, value.Description, value.Quantity, value.SourceElementId, value.TargetElementId, value.ScopeId)).ToArray();
        WorldStagedChangeViewModel[] changes = staging.Changes.Select(change => new WorldStagedChangeViewModel(change.Id, change.Source.ToString(), change.Status.ToString(), string.Join("；", change.ChangeSet.Operations.Select(Describe)), change.Issue, change.ConflictingChangeIds)).ToArray();
        return new WorldGraphViewModel(staging.WorldStateId, staging.Revision, workspace.IsDirty, workspace.CanUndo, workspace.CanRedo, workspace.Health.ToString(), elements, aspects, relations, scopes, localAspects, localRelations, changes);
    }

    internal static WorldCommitViewModel ToCommit(WorldCommitResult result, Guid? entityId = null) => new(result.CommitId, result.PreviousStateId, result.StateId, result.Changed, entityId);

    private static string Describe(WorldOperation operation) => operation switch
    {
        AddElementOperation value => $"新增 Element：{value.Name}",
        RemoveElementOperation value => $"删除 Element：{value.ElementId}",
        UpdateElementNameOperation value => $"修改 Element 名称：{value.ElementId} → {value.Name}",
        UpdateElementDescriptionOperation value => $"修改 Element 说明：{value.ElementId}",
        UpdateElementTypeOperation value => $"修改 Element 类型：{value.ElementId} → {value.Type}",
        AddAspectOperation value => $"新增 Aspect：{value.Type}",
        RemoveAspectOperation value => $"删除 Aspect：{value.AspectId}",
        UpdateAspectQuantityOperation value => $"修改 Aspect Quantity：{value.AspectId} → {value.Quantity}",
        UpdateAspectTypeOperation value => $"修改 Aspect 类型：{value.AspectId} → {value.Type}",
        AddRelationOperation value => $"新增 Relation：{value.Type}",
        RemoveRelationOperation value => $"删除 Relation：{value.RelationId}",
        UpdateRelationQuantityOperation value => $"修改 Relation Quantity：{value.RelationId} → {value.Quantity}",
        UpdateRelationTypeOperation value => $"修改 Relation 类型：{value.RelationId} → {value.Type}",
        AddScopeOperation value => $"新增 Scope：{value.Type}",
        RemoveScopeOperation value => $"删除 Scope：{value.ScopeId}",
        UpdateScopeQuantityOperation value => $"修改 Scope Quantity：{value.ScopeId} → {value.Quantity}",
        UpdateScopeTypeOperation value => $"修改 Scope 类型：{value.ScopeId} → {value.Type}",
        AddLocalAspectOperation value => $"新增 LocalAspect：{value.Name}",
        RemoveLocalAspectOperation value => $"删除 LocalAspect：{value.LocalAspectId}",
        UpdateLocalAspectOperation value => $"修改 LocalAspect：{value.LocalAspectId}",
        AddLocalRelationOperation value => $"新增 LocalRelation：{value.Name}",
        RemoveLocalRelationOperation value => $"删除 LocalRelation：{value.LocalRelationId}",
        UpdateLocalRelationOperation value => $"修改 LocalRelation：{value.LocalRelationId}",
        _ => operation.GetType().Name
    };
}
