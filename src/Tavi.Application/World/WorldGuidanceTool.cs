using System.ComponentModel;
using System.Text.Json;
using Tavi.Application.LanguageModel;
using Tavi.Domain.World;

namespace Tavi.Application.World;

/// <summary>创建用于查询和编辑 Element、Aspect、Relation 与 Scope 断言图的语言模型工具。</summary>
internal static class WorldGuidanceTool
{
    private const int MaxResults = 20;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    /// <summary>创建全部 World 查询和编辑工具。</summary>
    internal static IReadOnlyCollection<ITool> CreateTools(IWorldService session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return
        [
            new QueryElementsTool(session), new QueryScopesTool(session), new QueryAspectsTool(session), new QueryRelationsTool(session),
            new AddElementTool(session), new AddScopeTool(session), new AddAspectTool(session), new AddRelationTool(session),
            new UpdateElementTool(session), new UpdateScopeTool(session), new UpdateAspectTool(session), new UpdateRelationTool(session),
            new RemoveEntityTool(session)
        ];
    }

    private static string Invoke(Func<object> action)
    {
        try
        {
            return JsonSerializer.Serialize(action(), JsonOptions);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or WorldException)
        {
            throw new ToolArgumentException(exception.Message, exception);
        }
    }

    private static object Limited<T>(IEnumerable<T> values)
    {
        T[] all = values.ToArray();
        return new { total = all.Length, returned = Math.Min(all.Length, MaxResults), truncated = all.Length > MaxResults, items = all.Take(MaxResults).ToArray() };
    }

    private static object ToOutput(Element value) => new { value.Id, value.Name, value.Description, Type = value.Type.Value };
    private static object ToOutput(ResolvedScope value) => new { value.Scope.Id, value.Scope.Name, value.Scope.Description, value.Scope.Quantity, Type = value.Scope.Type.Value, value.Scope.OwnerElementId, Owner = ToOutput(value.Owner) };
    private static object ToOutput(ResolvedAspect value) => new { value.Aspect.Id, value.Aspect.Name, value.Aspect.Description, value.Aspect.Quantity, Type = value.Aspect.Type.Value, value.Aspect.ElementId, value.Aspect.ScopeId, Element = ToOutput(value.Element), Scope = ToOutput(new ResolvedScope(value.Scope, value.ScopeOwner)) };
    private static object ToOutput(ResolvedRelation value) => new { value.Relation.Id, value.Relation.Name, value.Relation.Description, value.Relation.Quantity, Type = value.Relation.Type.Value, value.Relation.SourceElementId, value.Relation.TargetElementId, value.Relation.ScopeId, Source = ToOutput(value.Source), Target = ToOutput(value.Target), Scope = ToOutput(new ResolvedScope(value.Scope, value.ScopeOwner)) };

    private static Guid Apply(IWorldService session, WorldOperation operation) => session.Apply(WorldOperations.Single(operation), session.StateId).StateId;
    private static Guid Apply(IWorldService session, IEnumerable<WorldOperation> operations) => session.Apply(new WorldChangeSet(operations), session.StateId).StateId;

    private static void RequireFinite(double quantity)
    {
        if (!double.IsFinite(quantity))
            throw new ToolArgumentException("Quantity 必须是有限 double。");
    }

    private class QueryArguments : IToolArgument
    {
        [Description("必须同时出现的查询线索；使用空数组表示列出全部")]
        public string[] Clues { get; set; } = [];
    }

    private sealed class ScopedQueryArguments : QueryArguments
    {
        [Description("限制查询的 Scope 标识；空 Guid 表示全部 Scope")]
        public Guid ScopeId { get; set; }
    }

    private sealed class QueryElementsTool(IWorldService session) : Tool<QueryArguments>
    {
        public override string name => "query_elements";
        public override string description => "按名称、说明和开放类型查询 Element；空线索列出全部。";

        protected override Task<string> Execute(QueryArguments arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Invoke(() => Limited((arguments.Clues.Length == 0 ? session.Queries.GetElements() : session.Queries.QueryElements(arguments.Clues)).Select(ToOutput))));
        }
    }

    private sealed class QueryScopesTool(IWorldService session) : Tool<QueryArguments>
    {
        public override string name => "query_scopes";
        public override string description => "按名称、说明、开放类型和 Owner 查询 Scope；空线索列出全部。";

        protected override Task<string> Execute(QueryArguments arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Invoke(() => Limited((arguments.Clues.Length == 0 ? session.Queries.GetScopes() : session.Queries.QueryScopes(arguments.Clues)).Select(ToOutput))));
        }
    }

    private sealed class QueryAspectsTool(IWorldService session) : Tool<ScopedQueryArguments>
    {
        public override string name => "query_aspects";
        public override string description => "查询一元断言，可使用 ScopeId 限制唯一断言域。";

        protected override Task<string> Execute(ScopedQueryArguments arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Invoke(() =>
            {
                Guid? scopeId = arguments.ScopeId == Guid.Empty ? null : arguments.ScopeId;
                IReadOnlyList<ResolvedAspect> values = arguments.Clues.Length == 0 ? scopeId.HasValue ? session.Queries.GetAspectsInScope(scopeId.Value) : session.Queries.GetAspects() : session.Queries.QueryAspects(arguments.Clues, scopeId);
                return Limited(values.Select(ToOutput));
            }));
        }
    }

    private sealed class QueryRelationsTool(IWorldService session) : Tool<ScopedQueryArguments>
    {
        public override string name => "query_relations";
        public override string description => "查询有向二元断言，可使用 ScopeId 限制唯一断言域。";

        protected override Task<string> Execute(ScopedQueryArguments arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Invoke(() =>
            {
                Guid? scopeId = arguments.ScopeId == Guid.Empty ? null : arguments.ScopeId;
                IReadOnlyList<ResolvedRelation> values = arguments.Clues.Length == 0 ? scopeId.HasValue ? session.Queries.GetRelationsInScope(scopeId.Value) : session.Queries.GetRelations() : session.Queries.QueryRelations(arguments.Clues, scopeId);
                return Limited(values.Select(ToOutput));
            }));
        }
    }

    private abstract class NamedArguments : IToolArgument
    {
        [Description("名称")]
        public string Name { get; set; } = string.Empty;

        [Description("说明")]
        public string Description { get; set; } = string.Empty;

        [Description("符合 namespace:name 约定的开放类型")]
        public string Type { get; set; } = "core:none";
    }

    private sealed class AddElementArguments : NamedArguments { }

    private sealed class AddElementTool(IWorldService session) : Tool<AddElementArguments>
    {
        public override string name => "add_element";
        public override string description => "立即原子添加 Element；类型不赋予任何 World Kernel 特权。";

        protected override Task<string> Execute(AddElementArguments arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Invoke(() =>
            {
                AddElementOperation operation = WorldOperations.AddElement(arguments.Name, arguments.Description, new ElementType(arguments.Type));
                return new { stateId = Apply(session, operation), elementId = operation.ElementId };
            }));
        }
    }

    private sealed class AddScopeArguments : NamedArguments
    {
        [Description("由对应 Module 解释的有限强度")]
        public double Quantity { get; set; } = 1;

        [Description("持有该 Scope 的 Element 标识")]
        public Guid OwnerElementId { get; set; }
    }

    private sealed class AddScopeTool(IWorldService session) : Tool<AddScopeArguments>
    {
        public override string name => "add_scope";
        public override string description => "立即原子添加由一个 Element 持有的 Scope。";

        protected override Task<string> Execute(AddScopeArguments arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Invoke(() =>
            {
                RequireFinite(arguments.Quantity);
                AddScopeOperation operation = WorldOperations.AddScope(arguments.Name, arguments.Description, arguments.Quantity, new ScopeType(arguments.Type), arguments.OwnerElementId);
                return new { stateId = Apply(session, operation), scopeId = operation.ScopeId };
            }));
        }
    }

    private sealed class AddAspectArguments : NamedArguments
    {
        [Description("由对应 Module 解释的有限强度")]
        public double Quantity { get; set; } = 1;

        [Description("一元断言指向的 Element 标识")]
        public Guid ElementId { get; set; }

        [Description("断言唯一所属的 Scope 标识")]
        public Guid ScopeId { get; set; }
    }

    private sealed class AddAspectTool(IWorldService session) : Tool<AddAspectArguments>
    {
        public override string name => "add_aspect";
        public override string description => "立即原子添加唯一属于一个 Scope 的一元断言。";

        protected override Task<string> Execute(AddAspectArguments arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Invoke(() =>
            {
                RequireFinite(arguments.Quantity);
                AddAspectOperation operation = WorldOperations.AddAspect(arguments.Name, arguments.Description, arguments.Quantity, new AspectType(arguments.Type), arguments.ElementId, arguments.ScopeId);
                return new { stateId = Apply(session, operation), aspectId = operation.AspectId };
            }));
        }
    }

    private sealed class AddRelationArguments : NamedArguments
    {
        [Description("由对应 Module 解释的有限强度")]
        public double Quantity { get; set; } = 1;

        [Description("有向断言的 Source Element 标识")]
        public Guid SourceElementId { get; set; }

        [Description("有向断言的 Target Element 标识")]
        public Guid TargetElementId { get; set; }

        [Description("断言唯一所属的 Scope 标识")]
        public Guid ScopeId { get; set; }
    }

    private sealed class AddRelationTool(IWorldService session) : Tool<AddRelationArguments>
    {
        public override string name => "add_relation";
        public override string description => "立即原子添加唯一属于一个 Scope 的有向二元断言。";

        protected override Task<string> Execute(AddRelationArguments arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Invoke(() =>
            {
                RequireFinite(arguments.Quantity);
                AddRelationOperation operation = WorldOperations.AddRelation(arguments.Name, arguments.Description, arguments.Quantity, new RelationType(arguments.Type), arguments.SourceElementId, arguments.TargetElementId, arguments.ScopeId);
                return new { stateId = Apply(session, operation), relationId = operation.RelationId };
            }));
        }
    }

    private abstract class UpdateArguments : NamedArguments
    {
        [Description("需要更新的实体标识")]
        public Guid Id { get; set; }
    }

    private sealed class UpdateElementArguments : UpdateArguments { }

    private sealed class UpdateElementTool(IWorldService session) : Tool<UpdateElementArguments>
    {
        public override string name => "update_element";
        public override string description => "原子更新 Element 的名称、说明和开放类型。";

        protected override Task<string> Execute(UpdateElementArguments arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Invoke(() => new { stateId = Apply(session, [new UpdateElementNameOperation(arguments.Id, arguments.Name), new UpdateElementDescriptionOperation(arguments.Id, arguments.Description), new UpdateElementTypeOperation(arguments.Id, new ElementType(arguments.Type))]) }));
        }
    }

    private abstract class UpdateQuantifiedArguments : UpdateArguments
    {
        [Description("由对应 Module 解释的有限强度")]
        public double Quantity { get; set; }
    }

    private sealed class UpdateScopeArguments : UpdateQuantifiedArguments { }

    private sealed class UpdateScopeTool(IWorldService session) : Tool<UpdateScopeArguments>
    {
        public override string name => "update_scope";
        public override string description => "原子更新 Scope 的名称、说明、Quantity 和开放类型；Owner 是结构字段，需删除后重建。";

        protected override Task<string> Execute(UpdateScopeArguments arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Invoke(() =>
            {
                RequireFinite(arguments.Quantity);
                return new { stateId = Apply(session, [new UpdateScopeNameOperation(arguments.Id, arguments.Name), new UpdateScopeDescriptionOperation(arguments.Id, arguments.Description), new UpdateScopeQuantityOperation(arguments.Id, arguments.Quantity), new UpdateScopeTypeOperation(arguments.Id, new ScopeType(arguments.Type))]) };
            }));
        }
    }

    private sealed class UpdateAspectArguments : UpdateQuantifiedArguments { }

    private sealed class UpdateAspectTool(IWorldService session) : Tool<UpdateAspectArguments>
    {
        public override string name => "update_aspect";
        public override string description => "原子更新 Aspect 内容；Element 和 Scope 是结构字段，需删除后重建。";

        protected override Task<string> Execute(UpdateAspectArguments arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Invoke(() =>
            {
                RequireFinite(arguments.Quantity);
                return new { stateId = Apply(session, [new UpdateAspectNameOperation(arguments.Id, arguments.Name), new UpdateAspectDescriptionOperation(arguments.Id, arguments.Description), new UpdateAspectQuantityOperation(arguments.Id, arguments.Quantity), new UpdateAspectTypeOperation(arguments.Id, new AspectType(arguments.Type))]) };
            }));
        }
    }

    private sealed class UpdateRelationArguments : UpdateQuantifiedArguments { }

    private sealed class UpdateRelationTool(IWorldService session) : Tool<UpdateRelationArguments>
    {
        public override string name => "update_relation";
        public override string description => "原子更新 Relation 内容；端点和 Scope 是结构字段，需删除后重建。";

        protected override Task<string> Execute(UpdateRelationArguments arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Invoke(() =>
            {
                RequireFinite(arguments.Quantity);
                return new { stateId = Apply(session, [new UpdateRelationNameOperation(arguments.Id, arguments.Name), new UpdateRelationDescriptionOperation(arguments.Id, arguments.Description), new UpdateRelationQuantityOperation(arguments.Id, arguments.Quantity), new UpdateRelationTypeOperation(arguments.Id, new RelationType(arguments.Type))]) };
            }));
        }
    }

    private enum EntityKind
    {
        Element,
        Aspect,
        Relation,
        Scope
    }

    private sealed class RemoveEntityArguments : IToolArgument
    {
        [Description("要删除的实体类别")]
        public EntityKind Kind { get; set; }

        [Description("要删除的实体标识；删除 Element 或 Scope 会按领域规则级联")]
        public Guid Id { get; set; }
    }

    private sealed class RemoveEntityTool(IWorldService session) : Tool<RemoveEntityArguments>
    {
        public override string name => "remove_world_entity";
        public override string description => "按类别立即原子删除一个 World 实体。";

        protected override Task<string> Execute(RemoveEntityArguments arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Invoke(() =>
            {
                WorldOperation operation = arguments.Kind switch
                {
                    EntityKind.Element => new RemoveElementOperation(arguments.Id),
                    EntityKind.Aspect => new RemoveAspectOperation(arguments.Id),
                    EntityKind.Relation => new RemoveRelationOperation(arguments.Id),
                    EntityKind.Scope => new RemoveScopeOperation(arguments.Id),
                    _ => throw new ArgumentOutOfRangeException(nameof(arguments.Kind))
                };
                return new { stateId = Apply(session, operation), removed = arguments.Kind.ToString(), arguments.Id };
            }));
        }
    }
}
