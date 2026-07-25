using System.ComponentModel;
using System.Text.Json;
using Tavi.Application.LanguageModel;
using Tavi.Domain.World;

namespace Tavi.Application.World;

/// <summary>创建用于查询和编辑 World EARS 与 Local 事实的语言模型工具。</summary>
internal static class WorldGuidanceTool
{
    private const int MaxResults = 20;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    internal static IReadOnlyCollection<ITool> CreateTools(IWorldService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        return
        [
            new QueryElementsTool(service), new QueryScopesTool(service), new QueryAspectsTool(service), new QueryRelationsTool(service), new QueryLocalAspectsTool(service), new QueryLocalRelationsTool(service),
            new AddElementTool(service), new AddScopeTool(service), new AddAspectTool(service), new AddRelationTool(service), new AddLocalAspectTool(service), new AddLocalRelationTool(service),
            new UpdateElementTool(service), new UpdateScopeTool(service), new UpdateAspectTool(service), new UpdateRelationTool(service), new UpdateLocalAspectTool(service), new UpdateLocalRelationTool(service), new RemoveEntityTool(service)
        ];
    }

    internal static IReadOnlyCollection<ITool> CreateQueryTools(IWorldService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        return [new QueryElementsTool(service), new QueryScopesTool(service), new QueryAspectsTool(service), new QueryRelationsTool(service), new QueryLocalAspectsTool(service), new QueryLocalRelationsTool(service)];
    }

    private static string Invoke(Func<object> action)
    {
        try { return JsonSerializer.Serialize(action(), JsonOptions); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or WorldException) { throw new ToolArgumentException(exception.Message, exception); }
    }

    private static object Limited<T>(IEnumerable<T> values)
    {
        T[] all = values.ToArray();
        return new { total = all.Length, returned = Math.Min(all.Length, MaxResults), truncated = all.Length > MaxResults, items = all.Take(MaxResults).ToArray() };
    }

    private static Guid Apply(IWorldService service, params WorldOperation[] operations) => service.Apply(new WorldChangeSet(operations), service.StateId).StateId;
    private static object Output(Element value) => new { value.Id, value.Name, value.Description, Type = value.Type.Value };
    private static object Output(ResolvedScope value) => new { value.Scope.Id, value.Scope.Quantity, Type = value.Scope.Type.Value, value.Scope.OwnerElementId, Owner = Output(value.Owner) };
    private static object Output(ResolvedAspect value) => new { value.Aspect.Id, value.Aspect.Quantity, Type = value.Aspect.Type.Value, value.Aspect.ElementId, value.Aspect.ScopeId, Element = Output(value.Element), Scope = Output(new ResolvedScope(value.Scope, value.ScopeOwner)) };
    private static object Output(ResolvedRelation value) => new { value.Relation.Id, value.Relation.Quantity, Type = value.Relation.Type.Value, value.Relation.SourceElementId, value.Relation.TargetElementId, value.Relation.ScopeId, Source = Output(value.Source), Target = Output(value.Target), Scope = Output(new ResolvedScope(value.Scope, value.ScopeOwner)) };
    private static object Output(ResolvedLocalAspect value) => new { value.LocalAspect.Id, value.LocalAspect.Name, value.LocalAspect.Description, value.LocalAspect.Quantity, value.LocalAspect.ElementId, value.LocalAspect.ScopeId, Element = Output(value.Element), Scope = Output(new ResolvedScope(value.Scope, value.ScopeOwner)) };
    private static object Output(ResolvedLocalRelation value) => new { value.LocalRelation.Id, value.LocalRelation.Name, value.LocalRelation.Description, value.LocalRelation.Quantity, value.LocalRelation.SourceElementId, value.LocalRelation.TargetElementId, value.LocalRelation.ScopeId, Source = Output(value.Source), Target = Output(value.Target), Scope = Output(new ResolvedScope(value.Scope, value.ScopeOwner)) };

    private class QueryArguments : IToolArgument
    {
        [Description("全部结果必须同时包含的搜索线索；列出全部时传空数组")]
        public string[] Clues { get; set; } = [];
    }

    private sealed class ScopedQueryArguments : QueryArguments
    {
        [Description("限制查询的 Scope Id；空 Guid 表示全部 Scope")]
        public Guid ScopeId { get; set; }
    }

    private sealed class QueryElementsTool(IWorldService service) : Tool<QueryArguments>
    {
        public override string name => "query_elements";
        public override string description => "查询 World Element。";
        protected override Task<string> Execute(QueryArguments arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Invoke(() => Limited((arguments.Clues.Length == 0 ? service.Queries.GetElements() : service.Queries.QueryElements(arguments.Clues)).Select(Output))));
        }
    }

    private sealed class QueryScopesTool(IWorldService service) : Tool<QueryArguments>
    {
        public override string name => "query_scopes";
        public override string description => "按 Type、Quantity 和 Owner 查询规则化 Scope。";
        protected override Task<string> Execute(QueryArguments arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Invoke(() => Limited((arguments.Clues.Length == 0 ? service.Queries.GetScopes() : service.Queries.QueryScopes(arguments.Clues)).Select(Output))));
        }
    }

    private sealed class QueryAspectsTool(IWorldService service) : Tool<ScopedQueryArguments>
    {
        public override string name => "query_aspects";
        public override string description => "查询规则化 Aspect。";
        protected override Task<string> Execute(ScopedQueryArguments arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Guid? scopeId = arguments.ScopeId == Guid.Empty ? null : arguments.ScopeId;
            return Task.FromResult(Invoke(() => Limited((arguments.Clues.Length == 0 ? scopeId.HasValue ? service.Queries.GetAspectsInScope(scopeId.Value) : service.Queries.GetAspects() : service.Queries.QueryAspects(arguments.Clues, scopeId)).Select(Output))));
        }
    }

    private sealed class QueryRelationsTool(IWorldService service) : Tool<ScopedQueryArguments>
    {
        public override string name => "query_relations";
        public override string description => "查询规则化 Relation。";
        protected override Task<string> Execute(ScopedQueryArguments arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Guid? scopeId = arguments.ScopeId == Guid.Empty ? null : arguments.ScopeId;
            return Task.FromResult(Invoke(() => Limited((arguments.Clues.Length == 0 ? scopeId.HasValue ? service.Queries.GetRelationsInScope(scopeId.Value) : service.Queries.GetRelations() : service.Queries.QueryRelations(arguments.Clues, scopeId)).Select(Output))));
        }
    }

    private sealed class QueryLocalAspectsTool(IWorldService service) : Tool<ScopedQueryArguments>
    {
        public override string name => "query_local_aspects";
        public override string description => "查询只存在于 World 且不承担 Evolution 语义的 LocalAspect。";
        protected override Task<string> Execute(ScopedQueryArguments arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Guid? scopeId = arguments.ScopeId == Guid.Empty ? null : arguments.ScopeId;
            return Task.FromResult(Invoke(() => Limited((arguments.Clues.Length == 0 ? scopeId.HasValue ? service.Queries.GetLocalAspectsInScope(scopeId.Value) : service.Queries.GetLocalAspects() : service.Queries.QueryLocalAspects(arguments.Clues, scopeId)).Select(Output))));
        }
    }

    private sealed class QueryLocalRelationsTool(IWorldService service) : Tool<ScopedQueryArguments>
    {
        public override string name => "query_local_relations";
        public override string description => "查询只存在于 World 且不承担 Evolution 语义的 LocalRelation。";
        protected override Task<string> Execute(ScopedQueryArguments arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Guid? scopeId = arguments.ScopeId == Guid.Empty ? null : arguments.ScopeId;
            return Task.FromResult(Invoke(() => Limited((arguments.Clues.Length == 0 ? scopeId.HasValue ? service.Queries.GetLocalRelationsInScope(scopeId.Value) : service.Queries.GetLocalRelations() : service.Queries.QueryLocalRelations(arguments.Clues, scopeId)).Select(Output))));
        }
    }

    private class AddElementArguments : IToolArgument
    {
        [Description("Element 名称")] public string Name { get; set; } = string.Empty;
        [Description("Element 说明")] public string Description { get; set; } = string.Empty;
        [Description("已注册 ElementType 键")] public string Type { get; set; } = "core:none";
    }

    private sealed class AddElementTool(IWorldService service) : Tool<AddElementArguments>
    {
        public override string name => "add_element";
        public override string description => "直接添加 Element。";
        protected override Task<string> Execute(AddElementArguments arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Invoke(() => { AddElementOperation value = WorldOperations.AddElement(arguments.Name, arguments.Description, new ElementType(arguments.Type)); return new { stateId = Apply(service, value), elementId = value.ElementId }; }));
        }
    }

    private class AddScopeArguments : IToolArgument
    {
        [Description("整数 Quantity")] public int Quantity { get; set; } = 1;
        [Description("已注册 ScopeType 键")] public string Type { get; set; } = "core:none";
        [Description("Owner Element Id")] public Guid OwnerElementId { get; set; }
    }

    private sealed class AddScopeTool(IWorldService service) : Tool<AddScopeArguments>
    {
        public override string name => "add_scope";
        public override string description => "直接添加规则化 Scope。";
        protected override Task<string> Execute(AddScopeArguments arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Invoke(() => { AddScopeOperation value = WorldOperations.AddScope(arguments.Quantity, new ScopeType(arguments.Type), arguments.OwnerElementId); return new { stateId = Apply(service, value), scopeId = value.ScopeId }; }));
        }
    }

    private abstract class AssertionArguments : IToolArgument
    {
        [Description("整数 Quantity")] public int Quantity { get; set; } = 1;
        [Description("所属 Scope Id")] public Guid ScopeId { get; set; }
    }

    private class AddAspectArguments : AssertionArguments
    {
        [Description("已注册 AspectType 键")] public string Type { get; set; } = "core:none";
        [Description("目标 Element Id")] public Guid ElementId { get; set; }
    }

    private sealed class AddAspectTool(IWorldService service) : Tool<AddAspectArguments>
    {
        public override string name => "add_aspect";
        public override string description => "直接添加规则化 Aspect。";
        protected override Task<string> Execute(AddAspectArguments arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Invoke(() => { AddAspectOperation value = WorldOperations.AddAspect(arguments.Quantity, new AspectType(arguments.Type), arguments.ElementId, arguments.ScopeId); return new { stateId = Apply(service, value), aspectId = value.AspectId }; }));
        }
    }

    private class AddRelationArguments : AssertionArguments
    {
        [Description("已注册 RelationType 键")] public string Type { get; set; } = "core:none";
        [Description("来源 Element Id")] public Guid SourceElementId { get; set; }
        [Description("目标 Element Id")] public Guid TargetElementId { get; set; }
    }

    private sealed class AddRelationTool(IWorldService service) : Tool<AddRelationArguments>
    {
        public override string name => "add_relation";
        public override string description => "直接添加规则化 Relation。";
        protected override Task<string> Execute(AddRelationArguments arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Invoke(() => { AddRelationOperation value = WorldOperations.AddRelation(arguments.Quantity, new RelationType(arguments.Type), arguments.SourceElementId, arguments.TargetElementId, arguments.ScopeId); return new { stateId = Apply(service, value), relationId = value.RelationId }; }));
        }
    }

    private class AddLocalAspectArguments : IToolArgument
    {
        [Description("自由谓词名称")] public string Name { get; set; } = string.Empty;
        [Description("自由谓词说明")] public string Description { get; set; } = string.Empty;
        [Description("整数 Quantity")] public int Quantity { get; set; } = 1;
        [Description("目标 Element Id")] public Guid ElementId { get; set; }
        [Description("所属 Scope Id")] public Guid ScopeId { get; set; }
    }

    private sealed class AddLocalAspectTool(IWorldService service) : Tool<AddLocalAspectArguments>
    {
        public override string name => "add_local_aspect";
        public override string description => "添加不会进入 Scenario 或 Evolution 的 LocalAspect。";
        protected override Task<string> Execute(AddLocalAspectArguments arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Invoke(() => { AddLocalAspectOperation value = WorldOperations.AddLocalAspect(arguments.Name, arguments.Description, arguments.Quantity, arguments.ElementId, arguments.ScopeId); return new { stateId = Apply(service, value), localAspectId = value.LocalAspectId }; }));
        }
    }

    private class AddLocalRelationArguments : IToolArgument
    {
        [Description("自由谓词名称")] public string Name { get; set; } = string.Empty;
        [Description("自由谓词说明")] public string Description { get; set; } = string.Empty;
        [Description("整数 Quantity")] public int Quantity { get; set; } = 1;
        [Description("来源 Element Id")] public Guid SourceElementId { get; set; }
        [Description("目标 Element Id")] public Guid TargetElementId { get; set; }
        [Description("所属 Scope Id")] public Guid ScopeId { get; set; }
    }

    private sealed class AddLocalRelationTool(IWorldService service) : Tool<AddLocalRelationArguments>
    {
        public override string name => "add_local_relation";
        public override string description => "添加不会进入 Scenario 或 Evolution 的 LocalRelation。";
        protected override Task<string> Execute(AddLocalRelationArguments arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Invoke(() => { AddLocalRelationOperation value = WorldOperations.AddLocalRelation(arguments.Name, arguments.Description, arguments.Quantity, arguments.SourceElementId, arguments.TargetElementId, arguments.ScopeId); return new { stateId = Apply(service, value), localRelationId = value.LocalRelationId }; }));
        }
    }

    private sealed class UpdateElementArguments : AddElementArguments { [Description("Element Id")] public Guid Id { get; set; } }
    private sealed class UpdateElementTool(IWorldService service) : Tool<UpdateElementArguments>
    {
        public override string name => "update_element";
        public override string description => "更新 Element 文本和类型。";
        protected override Task<string> Execute(UpdateElementArguments arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Invoke(() => new { stateId = Apply(service, new UpdateElementNameOperation(arguments.Id, arguments.Name), new UpdateElementDescriptionOperation(arguments.Id, arguments.Description), new UpdateElementTypeOperation(arguments.Id, new ElementType(arguments.Type))) }));
        }
    }

    private sealed class UpdateScopeArguments : AddScopeArguments { [Description("Scope Id")] public Guid Id { get; set; } }
    private sealed class UpdateScopeTool(IWorldService service) : Tool<UpdateScopeArguments>
    {
        public override string name => "update_scope";
        public override string description => "更新规则化 Scope 的 Quantity 和 Type；Owner 不可变。";
        protected override Task<string> Execute(UpdateScopeArguments arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Invoke(() => new { stateId = Apply(service, new UpdateScopeQuantityOperation(arguments.Id, arguments.Quantity), new UpdateScopeTypeOperation(arguments.Id, new ScopeType(arguments.Type))) }));
        }
    }

    private sealed class UpdateAspectArguments : AddAspectArguments { [Description("Aspect Id")] public Guid Id { get; set; } }
    private sealed class UpdateAspectTool(IWorldService service) : Tool<UpdateAspectArguments>
    {
        public override string name => "update_aspect";
        public override string description => "更新规则化 Aspect 的 Quantity 和 Type；结构引用不可变。";
        protected override Task<string> Execute(UpdateAspectArguments arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Invoke(() => new { stateId = Apply(service, new UpdateAspectQuantityOperation(arguments.Id, arguments.Quantity), new UpdateAspectTypeOperation(arguments.Id, new AspectType(arguments.Type))) }));
        }
    }

    private sealed class UpdateRelationArguments : AddRelationArguments { [Description("Relation Id")] public Guid Id { get; set; } }
    private sealed class UpdateRelationTool(IWorldService service) : Tool<UpdateRelationArguments>
    {
        public override string name => "update_relation";
        public override string description => "更新规则化 Relation 的 Quantity 和 Type；结构引用不可变。";
        protected override Task<string> Execute(UpdateRelationArguments arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Invoke(() => new { stateId = Apply(service, new UpdateRelationQuantityOperation(arguments.Id, arguments.Quantity), new UpdateRelationTypeOperation(arguments.Id, new RelationType(arguments.Type))) }));
        }
    }

    private sealed class UpdateLocalAspectArguments : AddLocalAspectArguments { [Description("LocalAspect Id")] public Guid Id { get; set; } }
    private sealed class UpdateLocalAspectTool(IWorldService service) : Tool<UpdateLocalAspectArguments>
    {
        public override string name => "update_local_aspect";
        public override string description => "更新 LocalAspect 自由谓词文本和 Quantity；结构引用不可变。";
        protected override Task<string> Execute(UpdateLocalAspectArguments arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Invoke(() => new { stateId = Apply(service, new UpdateLocalAspectOperation(arguments.Id, arguments.Name, arguments.Description, arguments.Quantity)) }));
        }
    }

    private sealed class UpdateLocalRelationArguments : AddLocalRelationArguments { [Description("LocalRelation Id")] public Guid Id { get; set; } }
    private sealed class UpdateLocalRelationTool(IWorldService service) : Tool<UpdateLocalRelationArguments>
    {
        public override string name => "update_local_relation";
        public override string description => "更新 LocalRelation 自由谓词文本和 Quantity；结构引用不可变。";
        protected override Task<string> Execute(UpdateLocalRelationArguments arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Invoke(() => new { stateId = Apply(service, new UpdateLocalRelationOperation(arguments.Id, arguments.Name, arguments.Description, arguments.Quantity)) }));
        }
    }

    private enum EntityKind { Element, Scope, Aspect, Relation, LocalAspect, LocalRelation }
    private sealed class RemoveArguments : IToolArgument
    {
        [Description("实体类别")] public EntityKind Kind { get; set; }
        [Description("实体 Id")] public Guid Id { get; set; }
    }

    private sealed class RemoveEntityTool(IWorldService service) : Tool<RemoveArguments>
    {
        public override string name => "remove_world_entity";
        public override string description => "删除指定 World 实体；删除 Element 或 Scope 会级联其结构依赖。";
        protected override Task<string> Execute(RemoveArguments arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Invoke(() =>
            {
                WorldOperation operation = arguments.Kind switch
                {
                    EntityKind.Element => new RemoveElementOperation(arguments.Id),
                    EntityKind.Scope => new RemoveScopeOperation(arguments.Id),
                    EntityKind.Aspect => new RemoveAspectOperation(arguments.Id),
                    EntityKind.Relation => new RemoveRelationOperation(arguments.Id),
                    EntityKind.LocalAspect => new RemoveLocalAspectOperation(arguments.Id),
                    EntityKind.LocalRelation => new RemoveLocalRelationOperation(arguments.Id),
                    _ => throw new ToolArgumentException($"不支持实体类别 {arguments.Kind}。")
                };
                return new { stateId = Apply(service, operation) };
            }));
        }
    }
}
