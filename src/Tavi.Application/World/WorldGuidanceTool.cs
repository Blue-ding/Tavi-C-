using System.ComponentModel;
using System.Text.Json;
using Tavi.Application.LanguageModel;
using Tavi.Domain.World;

namespace Tavi.Application.World;

/// <summary>创建仅允许 Guidance 查询 World EARS 与 Local 事实的语言模型工具。</summary>
internal static class WorldGuidanceTool
{
    private const int MaxResults = 20;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    internal static IReadOnlyCollection<ITool> CreateQueryTools(IWorldBuildView service)
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

    private sealed class QueryElementsTool(IWorldBuildView service) : Tool<QueryArguments>
    {
        public override string name => "query_elements";
        public override string description => "查询 World Element。";
        protected override Task<string> Execute(QueryArguments arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Invoke(() => Limited((arguments.Clues.Length == 0 ? service.Queries.GetElements() : service.Queries.QueryElements(arguments.Clues)).Select(Output))));
        }
    }

    private sealed class QueryScopesTool(IWorldBuildView service) : Tool<QueryArguments>
    {
        public override string name => "query_scopes";
        public override string description => "按 Type、Quantity 和 Owner 查询规则化 Scope。";
        protected override Task<string> Execute(QueryArguments arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Invoke(() => Limited((arguments.Clues.Length == 0 ? service.Queries.GetScopes() : service.Queries.QueryScopes(arguments.Clues)).Select(Output))));
        }
    }

    private sealed class QueryAspectsTool(IWorldBuildView service) : Tool<ScopedQueryArguments>
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

    private sealed class QueryRelationsTool(IWorldBuildView service) : Tool<ScopedQueryArguments>
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

    private sealed class QueryLocalAspectsTool(IWorldBuildView service) : Tool<ScopedQueryArguments>
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

    private sealed class QueryLocalRelationsTool(IWorldBuildView service) : Tool<ScopedQueryArguments>
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
}
