using System.ComponentModel;
using System.Text.Json;
using Tavi.Application.LanguageModel;
using Tavi.Domain.World;

namespace Tavi.Application.World;

internal static class WorldGuidanceTool
{
    private const int MaxResults = 20;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    /// <summary>
    /// 创建全部世界查询和写入工具。
    /// </summary>
    internal static IReadOnlyCollection<ITool> CreateTools(WorldSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return CreateQueryTools(session).Concat(CreateEditingTools(session)).ToArray();
    }

    /// <summary>创建不会修改真实 World 的全部查询工具。</summary>
    internal static IReadOnlyCollection<ITool> CreateQueryTools(WorldSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return
        [
            new ListAnchorsTool(session),
            new GetAnchorTool(session),
            new QueryAnchorTool(session),
            new GetWorldRelationTool(session),
            new GetSubWorldRelationTool(session),
            new QueryWorldRelationTool(session),
            new QuerySubWorldRelationTool(session),
            new QueryRelationTool(session),
            new ListCharactersTool(session),
            new GetAnchorRelationsTool(session),
            new GetRelationsBetweenAnchorsTool(session),
            new CompareWorldWithSubWorldTool(session)
        ];
    }

    private static IReadOnlyCollection<ITool> CreateEditingTools(WorldSession session)
    {
        return
        [
            new AddAnchorTool(session),
            new RemoveAnchorTool(session),
            new UpdateAnchorNameTool(session),
            new UpdateAnchorDescriptionTool(session),
            new UpdateAnchorTypeTool(session),
            new AddRelationTool(session),
            new RemoveRelationTool(session),
            new UpdateRelationNameTool(session),
            new UpdateRelationDescriptionTool(session),
            new CreateSubWorldTool(session),
            new RemoveSubWorldTool(session)
        ];
    }

    private static string SerializeAnchors(IEnumerable<Anchor> anchors)
    {
        return SerializeResult(anchors.Select(ToAnchorOutput));
    }

    private static string SerializeAllAnchors(IEnumerable<Anchor> anchors)
    {
        AnchorOutput[] items = anchors.Select(ToAnchorOutput).ToArray();
        return JsonSerializer.Serialize(new QueryResult<AnchorOutput>(items.Length, items.Length, false, items), JsonOptions);
    }

    private static string SerializeRelations(Func<IEnumerable<ScopedRelation>> query)
    {
        return InvokeForTool(() => SerializeResult(query().Select(ToRelationOutput)));
    }

    private static string SerializeComparison(WorldSession session, string characterName, IEnumerable<string> clues)
    {
        return InvokeForTool(() =>
        {
            WorldRelationComparison comparison = session.Queries.CompareWorldWithSubWorld(characterName, clues);
            var output = new ComparisonOutput(CreateRelationResult(comparison.World), CreateRelationResult(comparison.SubWorld));
            return JsonSerializer.Serialize(output, JsonOptions);
        });
    }

    private static string SerializeMutation(string operation, long revision, object result)
    {
        return JsonSerializer.Serialize(new MutationOutput(operation, revision, result), JsonOptions);
    }

    private static AnchorOutput ToAnchorOutput(Anchor anchor)
    {
        return new AnchorOutput(anchor.Name, anchor.Description, anchor.Type.ToString());
    }

    private static RelationOutput ToRelationOutput(ScopedRelation scopedRelation)
    {
        Relation relation = scopedRelation.Relation;
        object scope = scopedRelation.Domain is null
            ? new WorldScopeOutput("World")
            : new SubWorldScopeOutput("SubWorld", ToAnchorOutput(scopedRelation.Domain));
        return new RelationOutput(relation.Name, relation.Description, ToAnchorOutput(scopedRelation.Source), ToAnchorOutput(scopedRelation.Target), scope);
    }

    private static QueryResult<RelationOutput> CreateRelationResult(IEnumerable<ScopedRelation> relations)
    {
        RelationOutput[] items = relations.Select(ToRelationOutput).ToArray();
        return new QueryResult<RelationOutput>(items.Length, Math.Min(items.Length, MaxResults), items.Length > MaxResults, items.Take(MaxResults).ToArray());
    }

    private static string SerializeResult<T>(IEnumerable<T> items)
    {
        T[] allItems = items.ToArray();
        var result = new QueryResult<T>(allItems.Length, Math.Min(allItems.Length, MaxResults), allItems.Length > MaxResults, allItems.Take(MaxResults).ToArray());
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    private static TResult InvokeForTool<TResult>(Func<TResult> operation)
    {
        try
        {
            return operation();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or WorldException)
        {
            throw new ToolArgumentException(exception.Message, exception);
        }
    }

    private static Guid? ResolveDomainId(WorldSession session, RelationQueryScope scope, string characterName)
    {
        return scope switch
        {
            RelationQueryScope.World => null,
            RelationQueryScope.SubWorld => RequireCharacter(session, characterName).Id,
            _ => throw new ArgumentException("Relation 写操作的 Scope 只能是 World 或 SubWorld。", nameof(scope))
        };
    }

    private static Anchor RequireCharacter(WorldSession session, string characterName)
    {
        Anchor character = session.Queries.RequireSingleAnchor(characterName);
        if (character.Type != AnchorType.Character)
            throw new InvalidOperationException($"Anchor“{characterName}”不是 Character。");
        return character;
    }

    private static WorldCommitResult ApplySingle(WorldSession session, WorldOperation operation)
    {
        return session.Apply(WorldOperations.Single(operation), session.Revision);
    }

    private static ScopedRelation GetScopedRelation(WorldSession session, Guid relationId, Guid? domainId)
    {
        Relation relation = session.Queries.GetRelation(relationId);
        Anchor? domain = domainId.HasValue ? session.Queries.GetAnchor(domainId.Value) : null;
        return new ScopedRelation(relation, session.Queries.GetAnchor(relation.SourceId), session.Queries.GetAnchor(relation.TargetId), domain);
    }

    public sealed class GetAnchorToolPara : IToolArgument
    {
        [Description("需要精确查询的 Anchor 名称")]
        public string Name { get; set; } = string.Empty;
    }

    private sealed class GetAnchorTool(WorldSession session) : Tool<GetAnchorToolPara>
    {
        public override string name => "get_anchor";
        public override string description => "依据完整名称查询 Anchor；名称相同的结果会全部返回。";

        protected override Task<string> Execute(GetAnchorToolPara arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(InvokeForTool(() => SerializeAnchors(session.Queries.FindAnchors(arguments.Name))));
        }
    }

    public sealed class EmptyToolPara : IToolArgument
    {
    }

    private sealed class ListAnchorsTool(WorldSession session) : Tool<EmptyToolPara>
    {
        public override string name => "list_anchors";
        public override string description => "无条件返回世界中的全部 Anchor。需要遍历或查看所有 Anchor 时使用本工具；不要为此向 query_anchor 传递空 clues。";

        protected override Task<string> Execute(EmptyToolPara arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(SerializeAllAnchors(session.Queries.GetAnchors()));
        }
    }

    public sealed class QueryAnchorToolPara : IToolArgument
    {
        [Description("至少一个非空字符串，用于匹配 Anchor 名称、描述和类型；所有线索必须同时匹配。需要全部 Anchor 时改用 list_anchors")]
        public string[] Clues { get; set; } = [];
    }

    private sealed class QueryAnchorTool(WorldSession session) : Tool<QueryAnchorToolPara>
    {
        public override string name => "query_anchor";
        public override string description => "按至少一个非空字符串线索查询 Anchor，所有线索必须同时匹配；本工具不用于无条件遍历，需要全部 Anchor 时使用 list_anchors。";

        protected override Task<string> Execute(QueryAnchorToolPara arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(InvokeForTool(() => SerializeAnchors(session.Queries.QueryAnchors(arguments.Clues))));
        }
    }

    public class RelationNameToolPara : IToolArgument
    {
        [Description("需要精确查询的 Relation 名称")]
        public string Name { get; set; } = string.Empty;
    }

    public sealed class SubWorldRelationNameToolPara : RelationNameToolPara
    {
        [Description("持有目标子世界的 Character 名称")]
        public string CharacterName { get; set; } = string.Empty;
    }

    public class RelationCluesToolPara : IToolArgument
    {
        [Description("用于匹配 Relation 名称、描述、两端 Anchor 和所属 Character 的字符串线索")]
        public string[] Clues { get; set; } = [];
    }

    public sealed class SubWorldRelationCluesToolPara : RelationCluesToolPara
    {
        [Description("持有目标子世界的 Character 名称")]
        public string CharacterName { get; set; } = string.Empty;
    }

    private sealed class GetWorldRelationTool(WorldSession session) : Tool<RelationNameToolPara>
    {
        public override string name => "get_world_relation";
        public override string description => "依据完整名称查询事实世界中的 Relation。";

        protected override Task<string> Execute(RelationNameToolPara arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(SerializeRelations(() => session.Queries.FindRelations(arguments.Name, RelationQueryScope.World, string.Empty)));
        }
    }

    private sealed class GetSubWorldRelationTool(WorldSession session) : Tool<SubWorldRelationNameToolPara>
    {
        public override string name => "get_sub_world_relation";
        public override string description => "依据完整名称查询指定 Character 认知世界中的 Relation。";

        protected override Task<string> Execute(SubWorldRelationNameToolPara arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(SerializeRelations(() => session.Queries.FindRelations(arguments.Name, RelationQueryScope.SubWorld, arguments.CharacterName)));
        }
    }

    private sealed class QueryWorldRelationTool(WorldSession session) : Tool<RelationCluesToolPara>
    {
        public override string name => "query_world_relation";
        public override string description => "使用普通字符串包含匹配查询事实世界中的 Relation。";

        protected override Task<string> Execute(RelationCluesToolPara arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(SerializeRelations(() => session.Queries.QueryRelations(arguments.Clues, RelationQueryScope.World, string.Empty)));
        }
    }

    private sealed class QuerySubWorldRelationTool(WorldSession session) : Tool<SubWorldRelationCluesToolPara>
    {
        public override string name => "query_sub_world_relation";
        public override string description => "使用普通字符串包含匹配查询指定 Character 认知世界中的 Relation。";

        protected override Task<string> Execute(SubWorldRelationCluesToolPara arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(SerializeRelations(() => session.Queries.QueryRelations(arguments.Clues, RelationQueryScope.SubWorld, arguments.CharacterName)));
        }
    }

    private sealed class QueryRelationTool(WorldSession session) : Tool<RelationCluesToolPara>
    {
        public override string name => "query_relation";
        public override string description => "使用普通字符串包含匹配查询事实世界和全部角色认知世界中的 Relation。";

        protected override Task<string> Execute(RelationCluesToolPara arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(SerializeRelations(() => session.Queries.QueryRelations(arguments.Clues, RelationQueryScope.All, string.Empty)));
        }
    }

    private sealed class ListCharactersTool(WorldSession session) : Tool<EmptyToolPara>
    {
        public override string name => "list_characters";
        public override string description => "返回世界中的全部 Character。";

        protected override Task<string> Execute(EmptyToolPara arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(SerializeAnchors(session.Queries.GetCharacters()));
        }
    }

    public sealed class AnchorRelationsToolPara : IToolArgument
    {
        [Description("需要查询关联关系的 Anchor 完整名称")]
        public string AnchorName { get; set; } = string.Empty;

        [Description("关系方向：Incoming、Outgoing 或 Both")]
        public RelationDirection Direction { get; set; }

        [Description("查询范围：World、SubWorld 或 All")]
        public RelationQueryScope Scope { get; set; }

        [Description("Scope 为 SubWorld 时使用的 Character 名称；其它范围传空字符串")]
        public string CharacterName { get; set; } = string.Empty;
    }

    private sealed class GetAnchorRelationsTool(WorldSession session) : Tool<AnchorRelationsToolPara>
    {
        public override string name => "get_anchor_relations";
        public override string description => "按方向和世界范围查询与指定名称 Anchor 相连的 Relation。";

        protected override Task<string> Execute(AnchorRelationsToolPara arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(SerializeRelations(() => session.Queries.GetAnchorRelations(arguments.AnchorName, arguments.Direction, arguments.Scope, arguments.CharacterName)));
        }
    }

    public sealed class RelationsBetweenAnchorsToolPara : IToolArgument
    {
        [Description("第一个 Anchor 的完整名称")]
        public string FirstAnchorName { get; set; } = string.Empty;

        [Description("第二个 Anchor 的完整名称")]
        public string SecondAnchorName { get; set; } = string.Empty;

        [Description("查询范围：World、SubWorld 或 All")]
        public RelationQueryScope Scope { get; set; }

        [Description("Scope 为 SubWorld 时使用的 Character 名称；其它范围传空字符串")]
        public string CharacterName { get; set; } = string.Empty;
    }

    private sealed class GetRelationsBetweenAnchorsTool(WorldSession session) : Tool<RelationsBetweenAnchorsToolPara>
    {
        public override string name => "get_relations_between_anchors";
        public override string description => "查询两个 Anchor 之间双向存在的 Relation，可限定事实世界或角色认知世界。";

        protected override Task<string> Execute(RelationsBetweenAnchorsToolPara arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(SerializeRelations(() => session.Queries.GetRelationsBetweenAnchors(arguments.FirstAnchorName, arguments.SecondAnchorName, arguments.Scope, arguments.CharacterName)));
        }
    }

    public sealed class CompareWorldWithSubWorldToolPara : IToolArgument
    {
        [Description("需要对比其认知世界的 Character 名称")]
        public string CharacterName { get; set; } = string.Empty;

        [Description("用于匹配 Relation 的字符串线索；所有线索必须同时匹配")]
        public string[] Clues { get; set; } = [];
    }

    private sealed class CompareWorldWithSubWorldTool(WorldSession session) : Tool<CompareWorldWithSubWorldToolPara>
    {
        public override string name => "compare_world_with_sub_world";
        public override string description => "使用相同字符串线索并列查询事实世界与指定 Character 的认知世界，不对差异作推理。";

        protected override Task<string> Execute(CompareWorldWithSubWorldToolPara arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(SerializeComparison(session, arguments.CharacterName, arguments.Clues));
        }
    }

    public sealed class AddAnchorToolPara : IToolArgument
    {
        [Description("新 Anchor 名称")]
        public string Name { get; set; } = string.Empty;

        [Description("新 Anchor 描述")]
        public string Description { get; set; } = string.Empty;

        [Description("新 Anchor 类型")]
        public AnchorType Type { get; set; }
    }

    private sealed class AddAnchorTool(WorldSession session) : Tool<AddAnchorToolPara>
    {
        public override string name => "add_anchor";
        public override string description => "向世界添加一个 Anchor。";

        protected override Task<string> Execute(AddAnchorToolPara arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(InvokeForTool(() =>
            {
                AddAnchorOperation operation = WorldOperations.AddAnchor(arguments.Name, arguments.Description, arguments.Type);
                WorldCommitResult commit = ApplySingle(session, operation);
                return SerializeMutation(name, commit.Revision, ToAnchorOutput(session.Queries.GetAnchor(operation.AnchorId)));
            }));
        }
    }

    public sealed class AnchorSelectorToolPara : IToolArgument
    {
        [Description("需要唯一匹配的 Anchor 名称")]
        public string Name { get; set; } = string.Empty;
    }

    private sealed class RemoveAnchorTool(WorldSession session) : Tool<AnchorSelectorToolPara>
    {
        public override string name => "remove_anchor";
        public override string description => "删除唯一匹配的 Anchor、相连 Relation 及其子世界。";

        protected override Task<string> Execute(AnchorSelectorToolPara arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(InvokeForTool(() =>
            {
                Anchor anchor = session.Queries.RequireSingleAnchor(arguments.Name);
                WorldCommitResult commit = ApplySingle(session, new RemoveAnchorOperation(anchor.Id));
                return SerializeMutation(name, commit.Revision, new RemovedOutput("Anchor", anchor.Name));
            }));
        }
    }

    public sealed class UpdateAnchorNameToolPara : IToolArgument
    {
        [Description("需要唯一匹配的当前 Anchor 名称")]
        public string CurrentName { get; set; } = string.Empty;

        [Description("新的 Anchor 名称")]
        public string NewName { get; set; } = string.Empty;
    }

    private sealed class UpdateAnchorNameTool(WorldSession session) : Tool<UpdateAnchorNameToolPara>
    {
        public override string name => "update_anchor_name";
        public override string description => "更新唯一匹配的 Anchor 名称。";

        protected override Task<string> Execute(UpdateAnchorNameToolPara arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(InvokeForTool(() =>
            {
                Anchor anchor = session.Queries.RequireSingleAnchor(arguments.CurrentName);
                WorldCommitResult commit = ApplySingle(session, new UpdateAnchorNameOperation(anchor.Id, arguments.NewName));
                return SerializeMutation(name, commit.Revision, ToAnchorOutput(session.Queries.GetAnchor(anchor.Id)));
            }));
        }
    }

    public sealed class UpdateAnchorDescriptionToolPara : IToolArgument
    {
        [Description("需要唯一匹配的 Anchor 名称")]
        public string Name { get; set; } = string.Empty;

        [Description("新的 Anchor 描述")]
        public string Description { get; set; } = string.Empty;
    }

    private sealed class UpdateAnchorDescriptionTool(WorldSession session) : Tool<UpdateAnchorDescriptionToolPara>
    {
        public override string name => "update_anchor_description";
        public override string description => "更新唯一匹配的 Anchor 描述。";

        protected override Task<string> Execute(UpdateAnchorDescriptionToolPara arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(InvokeForTool(() =>
            {
                Anchor anchor = session.Queries.RequireSingleAnchor(arguments.Name);
                WorldCommitResult commit = ApplySingle(session, new UpdateAnchorDescriptionOperation(anchor.Id, arguments.Description));
                return SerializeMutation(name, commit.Revision, ToAnchorOutput(session.Queries.GetAnchor(anchor.Id)));
            }));
        }
    }

    public sealed class UpdateAnchorTypeToolPara : IToolArgument
    {
        [Description("需要唯一匹配的 Anchor 名称")]
        public string Name { get; set; } = string.Empty;

        [Description("新的 Anchor 类型")]
        public AnchorType Type { get; set; }
    }

    private sealed class UpdateAnchorTypeTool(WorldSession session) : Tool<UpdateAnchorTypeToolPara>
    {
        public override string name => "update_anchor_type";
        public override string description => "更新唯一匹配的 Anchor 类型。";

        protected override Task<string> Execute(UpdateAnchorTypeToolPara arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(InvokeForTool(() =>
            {
                Anchor anchor = session.Queries.RequireSingleAnchor(arguments.Name);
                WorldCommitResult commit = ApplySingle(session, new UpdateAnchorTypeOperation(anchor.Id, arguments.Type));
                return SerializeMutation(name, commit.Revision, ToAnchorOutput(session.Queries.GetAnchor(anchor.Id)));
            }));
        }
    }

    public sealed class AddRelationToolPara : IToolArgument
    {
        [Description("新 Relation 名称")]
        public string Name { get; set; } = string.Empty;

        [Description("新 Relation 描述")]
        public string Description { get; set; } = string.Empty;

        [Description("来源 Anchor 的唯一名称")]
        public string SourceAnchorName { get; set; } = string.Empty;

        [Description("目标 Anchor 的唯一名称")]
        public string TargetAnchorName { get; set; } = string.Empty;

        [Description("写入范围：World 或 SubWorld")]
        public RelationQueryScope Scope { get; set; }

        [Description("Scope 为 SubWorld 时持有该子世界的 Character 名称；World 时传空字符串")]
        public string CharacterName { get; set; } = string.Empty;
    }

    private sealed class AddRelationTool(WorldSession session) : Tool<AddRelationToolPara>
    {
        public override string name => "add_relation";
        public override string description => "向主世界或指定 Character 子世界添加 Relation。";

        protected override Task<string> Execute(AddRelationToolPara arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(InvokeForTool(() =>
            {
                Anchor source = session.Queries.RequireSingleAnchor(arguments.SourceAnchorName);
                Anchor target = session.Queries.RequireSingleAnchor(arguments.TargetAnchorName);
                Guid? domainId = ResolveDomainId(session, arguments.Scope, arguments.CharacterName);
                AddRelationOperation operation = WorldOperations.AddRelation(arguments.Name, arguments.Description, source.Id, target.Id, domainId);
                WorldCommitResult commit = ApplySingle(session, operation);
                return SerializeMutation(name, commit.Revision, ToRelationOutput(GetScopedRelation(session, operation.RelationId, domainId)));
            }));
        }
    }

    public class RelationSelectorToolPara : IToolArgument
    {
        [Description("需要唯一匹配的 Relation 名称")]
        public string Name { get; set; } = string.Empty;

        [Description("来源 Anchor 的唯一名称")]
        public string SourceAnchorName { get; set; } = string.Empty;

        [Description("目标 Anchor 的唯一名称")]
        public string TargetAnchorName { get; set; } = string.Empty;

        [Description("Relation 所在范围：World 或 SubWorld")]
        public RelationQueryScope Scope { get; set; }

        [Description("Scope 为 SubWorld 时持有该子世界的 Character 名称；World 时传空字符串")]
        public string CharacterName { get; set; } = string.Empty;
    }

    private sealed class RemoveRelationTool(WorldSession session) : Tool<RelationSelectorToolPara>
    {
        public override string name => "remove_relation";
        public override string description => "删除选择条件唯一匹配的 Relation。";

        protected override Task<string> Execute(RelationSelectorToolPara arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(InvokeForTool(() =>
            {
                ScopedRelation selected = session.Queries.RequireSingleRelation(arguments.Name, arguments.SourceAnchorName, arguments.TargetAnchorName, RequireWriteScope(arguments.Scope), arguments.CharacterName);
                WorldCommitResult commit = ApplySingle(session, new RemoveRelationOperation(selected.Relation.Id));
                return SerializeMutation(name, commit.Revision, new RemovedOutput("Relation", selected.Relation.Name));
            }));
        }
    }

    public sealed class UpdateRelationNameToolPara : RelationSelectorToolPara
    {
        [Description("新的 Relation 名称")]
        public string NewName { get; set; } = string.Empty;
    }

    private sealed class UpdateRelationNameTool(WorldSession session) : Tool<UpdateRelationNameToolPara>
    {
        public override string name => "update_relation_name";
        public override string description => "更新选择条件唯一匹配的 Relation 名称。";

        protected override Task<string> Execute(UpdateRelationNameToolPara arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(InvokeForTool(() =>
            {
                ScopedRelation selected = session.Queries.RequireSingleRelation(arguments.Name, arguments.SourceAnchorName, arguments.TargetAnchorName, RequireWriteScope(arguments.Scope), arguments.CharacterName);
                WorldCommitResult commit = ApplySingle(session, new UpdateRelationNameOperation(selected.Relation.Id, arguments.NewName));
                Guid? domainId = selected.Domain?.Id;
                return SerializeMutation(name, commit.Revision, ToRelationOutput(GetScopedRelation(session, selected.Relation.Id, domainId)));
            }));
        }
    }

    public sealed class UpdateRelationDescriptionToolPara : RelationSelectorToolPara
    {
        [Description("新的 Relation 描述")]
        public string Description { get; set; } = string.Empty;
    }

    private sealed class UpdateRelationDescriptionTool(WorldSession session) : Tool<UpdateRelationDescriptionToolPara>
    {
        public override string name => "update_relation_description";
        public override string description => "更新选择条件唯一匹配的 Relation 描述。";

        protected override Task<string> Execute(UpdateRelationDescriptionToolPara arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(InvokeForTool(() =>
            {
                ScopedRelation selected = session.Queries.RequireSingleRelation(arguments.Name, arguments.SourceAnchorName, arguments.TargetAnchorName, RequireWriteScope(arguments.Scope), arguments.CharacterName);
                WorldCommitResult commit = ApplySingle(session, new UpdateRelationDescriptionOperation(selected.Relation.Id, arguments.Description));
                Guid? domainId = selected.Domain?.Id;
                return SerializeMutation(name, commit.Revision, ToRelationOutput(GetScopedRelation(session, selected.Relation.Id, domainId)));
            }));
        }
    }

    public sealed class CharacterSelectorToolPara : IToolArgument
    {
        [Description("需要唯一匹配的 Character 名称")]
        public string CharacterName { get; set; } = string.Empty;
    }

    private sealed class CreateSubWorldTool(WorldSession session) : Tool<CharacterSelectorToolPara>
    {
        public override string name => "create_sub_world";
        public override string description => "为指定 Character 创建子世界。";

        protected override Task<string> Execute(CharacterSelectorToolPara arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(InvokeForTool(() =>
            {
                Anchor character = RequireCharacter(session, arguments.CharacterName);
                WorldCommitResult commit = ApplySingle(session, WorldOperations.CreateSubWorld(character.Id));
                return SerializeMutation(name, commit.Revision, new SubWorldOutput(character.Name));
            }));
        }
    }

    private sealed class RemoveSubWorldTool(WorldSession session) : Tool<CharacterSelectorToolPara>
    {
        public override string name => "remove_sub_world";
        public override string description => "删除指定 Character 持有的子世界及其中全部 Relation。";

        protected override Task<string> Execute(CharacterSelectorToolPara arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(InvokeForTool(() =>
            {
                Anchor character = RequireCharacter(session, arguments.CharacterName);
                WorldCommitResult commit = ApplySingle(session, new RemoveSubWorldOperation(character.Id));
                return SerializeMutation(name, commit.Revision, new RemovedOutput("SubWorld", character.Name));
            }));
        }
    }

    private static RelationQueryScope RequireWriteScope(RelationQueryScope scope)
    {
        if (scope == RelationQueryScope.All)
            throw new ArgumentException("Relation 写操作的 Scope 只能是 World 或 SubWorld。", nameof(scope));
        return scope;
    }

    private sealed record AnchorOutput(string Name, string Description, string Type);
    private sealed record WorldScopeOutput(string Type);
    private sealed record SubWorldScopeOutput(string Type, AnchorOutput Character);
    private sealed record RelationOutput(string Name, string Description, AnchorOutput Source, AnchorOutput Target, object Scope);
    private sealed record QueryResult<T>(int Total, int Returned, bool Truncated, IReadOnlyList<T> Items);
    private sealed record ComparisonOutput(QueryResult<RelationOutput> World, QueryResult<RelationOutput> SubWorld);
    private sealed record MutationOutput(string Operation, long Revision, object Result);
    private sealed record RemovedOutput(string Type, string Name);
    private sealed record SubWorldOutput(string CharacterName);
}
