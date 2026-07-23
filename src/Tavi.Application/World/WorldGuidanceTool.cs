using System.ComponentModel;
using System.Text.Json;
using Tavi.Application.LanguageModel;
using Tavi.Domain.World;

namespace Tavi.Application.World
{
    internal static class WorldGuidanceTool
    {
        private const int MaxResults = 20;
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        /// <summary>
        /// 创建全部世界引导查询工具。
        /// </summary>
        internal static IReadOnlyCollection<ITool> CreateTools(WorldGraph graph)
        {
            return
            [
                new GetAnchorTool(graph),
                new QueryAnchorTool(graph),
                new GetWorldRelationTool(graph),
                new GetSubWorldRelationTool(graph),
                new QueryWorldRelationTool(graph),
                new QuerySubWorldRelationTool(graph),
                new QueryRelationTool(graph),
                new ListCharactersTool(graph),
                new GetAnchorRelationsTool(graph),
                new GetRelationsBetweenAnchorsTool(graph),
                new CompareWorldWithSubWorldTool(graph)
            ];
        }

        private static string ParseAnchor(Anchor anchor)
        {
            return JsonSerializer.Serialize(ToAnchorOutput(anchor), JsonOptions);
        }

        private static string ParseRelation(WorldGraph graph, Relation relation, Anchor? domain)
        {
            return JsonSerializer.Serialize(ToRelationOutput(graph, new ScopedRelation(relation, domain)), JsonOptions);
        }

        private static AnchorOutput ToAnchorOutput(Anchor anchor)
        {
            return new AnchorOutput(anchor.Name, anchor.Description, anchor.Type.ToString());
        }

        private static RelationOutput ToRelationOutput(WorldGraph graph, ScopedRelation scopedRelation)
        {
            Relation relation = scopedRelation.Relation;
            Anchor source = graph.GetAnchor(relation.SourceId);
            Anchor target = graph.GetAnchor(relation.TargetId);
            object scope = scopedRelation.Domain is null
                ? new WorldScopeOutput("World")
                : new SubWorldScopeOutput("SubWorld", ToAnchorOutput(scopedRelation.Domain));
            return new RelationOutput(relation.Name, relation.Description,
                ToAnchorOutput(source), ToAnchorOutput(target), scope);
        }

        private static IReadOnlyList<ScopedRelation> GetWorldRelations(WorldGraph graph)
        {
            return graph.GetWorldRelations().Select(relation => new ScopedRelation(relation, null)).ToArray();
        }

        private static IReadOnlyList<ScopedRelation> GetSubWorldRelations(WorldGraph graph, string characterName)
        {
            Anchor character = RequireCharacter(graph, characterName);
            SubWorldSnapshot? subWorld = graph.GetSubWorlds().SingleOrDefault(item => item.DomainId == character.Id);
            return subWorld is null
                ? Array.Empty<ScopedRelation>()
                : subWorld.Relations.Values.Select(relation => new ScopedRelation(relation, character)).ToArray();
        }

        private static IReadOnlyList<ScopedRelation> GetAllRelations(WorldGraph graph)
        {
            var relations = new List<ScopedRelation>(GetWorldRelations(graph));
            foreach (SubWorldSnapshot subWorld in graph.GetSubWorlds())
            {
                Anchor character = graph.GetAnchor(subWorld.DomainId);
                relations.AddRange(subWorld.Relations.Values.Select(relation => new ScopedRelation(relation, character)));
            }
            return relations;
        }

        private static IReadOnlyList<ScopedRelation> GetRelationsByScope(
            WorldGraph graph, RelationQueryScope scope, string characterName)
        {
            return scope switch
            {
                RelationQueryScope.World => GetWorldRelations(graph),
                RelationQueryScope.SubWorld => GetSubWorldRelations(graph, characterName),
                RelationQueryScope.All => GetAllRelations(graph),
                _ => throw new ToolArgumentException($"不支持的 Relation 查询范围：{scope}。")
            };
        }

        private static Anchor RequireCharacter(WorldGraph graph, string characterName)
        {
            RequireText(characterName, nameof(characterName));
            Anchor? exactCharacter = graph.GetCharacters().SingleOrDefault(anchor =>
                string.Equals(anchor.Name, characterName, StringComparison.Ordinal));
            if (exactCharacter is not null)
                return exactCharacter;
            Anchor[] characters = graph.GetCharacters().Where(anchor =>
                string.Equals(anchor.Name, characterName, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (characters.Length == 0)
                throw new ToolArgumentException($"不存在名称为“{characterName}”的 Character。");
            if (characters.Length > 1)
                throw new ToolArgumentException($"Character 名称“{characterName}”存在大小写歧义，请使用准确大小写。");
            return characters[0];
        }

        private static Anchor[] FindAnchors(WorldGraph graph, string name)
        {
            RequireText(name, nameof(name));
            return graph.GetAnchors()
                .Where(anchor => string.Equals(anchor.Name, name, StringComparison.OrdinalIgnoreCase))
                .OrderBy(anchor => anchor.Type)
                .ThenBy(anchor => anchor.Description, StringComparer.Ordinal)
                .ToArray();
        }

        private static string[] NormalizeClues(string[] clues)
        {
            if (clues is null)
                throw new ToolArgumentException("clues 不能为 null。");
            string[] normalized = clues
                .Where(clue => !string.IsNullOrWhiteSpace(clue))
                .Select(clue => clue.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (normalized.Length == 0)
                throw new ToolArgumentException("clues 至少需要包含一个非空字符串。");
            return normalized;
        }

        private static bool MatchesAll(string candidate, IReadOnlyCollection<string> clues)
        {
            return clues.All(clue => candidate.Contains(clue, StringComparison.OrdinalIgnoreCase));
        }

        private static bool MatchesAnchor(Anchor anchor, IReadOnlyCollection<string> clues)
        {
            return MatchesAll($"{anchor.Name}\n{anchor.Description}\n{anchor.Type}", clues);
        }

        private static bool MatchesRelation(
            WorldGraph graph, ScopedRelation scopedRelation, IReadOnlyCollection<string> clues)
        {
            Relation relation = scopedRelation.Relation;
            Anchor source = graph.GetAnchor(relation.SourceId);
            Anchor target = graph.GetAnchor(relation.TargetId);
            string domainName = scopedRelation.Domain?.Name ?? "World";
            return MatchesAll(
                $"{relation.Name}\n{relation.Description}\n{source.Name}\n{target.Name}\n{domainName}", clues);
        }

        private static IEnumerable<ScopedRelation> SortRelations(
            WorldGraph graph, IEnumerable<ScopedRelation> relations)
        {
            return relations
                .OrderBy(item => item.Relation.Name, StringComparer.Ordinal)
                .ThenBy(item => item.Domain?.Name ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(item => graph.GetAnchor(item.Relation.SourceId).Name, StringComparer.Ordinal)
                .ThenBy(item => graph.GetAnchor(item.Relation.TargetId).Name, StringComparer.Ordinal)
                .ThenBy(item => item.Relation.Description, StringComparer.Ordinal);
        }

        private static string SerializeAnchors(IEnumerable<Anchor> anchors)
        {
            return SerializeResult(anchors.Select(ToAnchorOutput));
        }

        private static string SerializeRelations(WorldGraph graph, IEnumerable<ScopedRelation> relations)
        {
            return SerializeResult(SortRelations(graph, relations).Select(item => ToRelationOutput(graph, item)));
        }

        private static string SerializeResult<T>(IEnumerable<T> items)
        {
            T[] allItems = items.ToArray();
            var result = new QueryResult<T>(
                allItems.Length,
                Math.Min(allItems.Length, MaxResults),
                allItems.Length > MaxResults,
                allItems.Take(MaxResults).ToArray());
            return JsonSerializer.Serialize(result, JsonOptions);
        }

        private static void RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ToolArgumentException($"参数 {parameterName} 不能为空或只包含空白字符。");
        }

        public sealed class GetAnchorToolPara : IToolArgument
        {
            [Description("需要精确查询的 Anchor 名称")]
            public string Name { get; set; } = string.Empty;
        }

        /// <summary>
        /// 依据精确名称返回 Anchor 信息。
        /// </summary>
        private sealed class GetAnchorTool(WorldGraph graph) : Tool<GetAnchorToolPara>
        {
            public override string name => "get_anchor";
            public override string description => "依据完整名称查询 Anchor；名称相同的结果会全部返回。";

            protected override Task<string> Execute(
                GetAnchorToolPara arguments, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(SerializeAnchors(FindAnchors(graph, arguments.Name)));
            }
        }

        public sealed class QueryAnchorToolPara : IToolArgument
        {
            [Description("用于匹配 Anchor 名称、描述和类型的字符串线索；所有线索必须同时匹配")]
            public string[] Clues { get; set; } = [];
        }

        /// <summary>
        /// 依据字符串线索模糊查询 Anchor。
        /// </summary>
        private sealed class QueryAnchorTool(WorldGraph graph) : Tool<QueryAnchorToolPara>
        {
            public override string name => "query_anchor";
            public override string description => "使用普通字符串包含匹配查询 Anchor；所有有效线索必须同时匹配。";

            protected override Task<string> Execute(
                QueryAnchorToolPara arguments, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string[] clues = NormalizeClues(arguments.Clues);
                IEnumerable<Anchor> anchors = graph.GetAnchors()
                    .Where(anchor => MatchesAnchor(anchor, clues))
                    .OrderBy(anchor => anchor.Name, StringComparer.Ordinal)
                    .ThenBy(anchor => anchor.Type);
                return Task.FromResult(SerializeAnchors(anchors));
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

        /// <summary>
        /// 依据精确名称查询主世界 Relation。
        /// </summary>
        private sealed class GetWorldRelationTool(WorldGraph graph) : Tool<RelationNameToolPara>
        {
            public override string name => "get_world_relation";
            public override string description => "依据完整名称查询事实世界中的 Relation。";

            protected override Task<string> Execute(
                RelationNameToolPara arguments, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                RequireText(arguments.Name, nameof(arguments.Name));
                IEnumerable<ScopedRelation> relations = GetWorldRelations(graph).Where(item =>
                    string.Equals(item.Relation.Name, arguments.Name, StringComparison.OrdinalIgnoreCase));
                return Task.FromResult(SerializeRelations(graph, relations));
            }
        }

        /// <summary>
        /// 依据精确名称查询指定 Character 子世界中的 Relation。
        /// </summary>
        private sealed class GetSubWorldRelationTool(WorldGraph graph) : Tool<SubWorldRelationNameToolPara>
        {
            public override string name => "get_sub_world_relation";
            public override string description => "依据完整名称查询指定 Character 认知世界中的 Relation。";

            protected override Task<string> Execute(
                SubWorldRelationNameToolPara arguments, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                RequireText(arguments.Name, nameof(arguments.Name));
                IEnumerable<ScopedRelation> relations = GetSubWorldRelations(graph, arguments.CharacterName)
                    .Where(item => string.Equals(
                        item.Relation.Name, arguments.Name, StringComparison.OrdinalIgnoreCase));
                return Task.FromResult(SerializeRelations(graph, relations));
            }
        }

        /// <summary>
        /// 模糊查询主世界 Relation。
        /// </summary>
        private sealed class QueryWorldRelationTool(WorldGraph graph) : Tool<RelationCluesToolPara>
        {
            public override string name => "query_world_relation";
            public override string description => "使用普通字符串包含匹配查询事实世界中的 Relation。";

            protected override Task<string> Execute(
                RelationCluesToolPara arguments, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string[] clues = NormalizeClues(arguments.Clues);
                return Task.FromResult(SerializeRelations(
                    graph, GetWorldRelations(graph).Where(item => MatchesRelation(graph, item, clues))));
            }
        }

        /// <summary>
        /// 模糊查询指定 Character 子世界中的 Relation。
        /// </summary>
        private sealed class QuerySubWorldRelationTool(WorldGraph graph) : Tool<SubWorldRelationCluesToolPara>
        {
            public override string name => "query_sub_world_relation";
            public override string description => "使用普通字符串包含匹配查询指定 Character 认知世界中的 Relation。";

            protected override Task<string> Execute(
                SubWorldRelationCluesToolPara arguments, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string[] clues = NormalizeClues(arguments.Clues);
                return Task.FromResult(SerializeRelations(graph,
                    GetSubWorldRelations(graph, arguments.CharacterName)
                        .Where(item => MatchesRelation(graph, item, clues))));
            }
        }

        /// <summary>
        /// 模糊查询主世界和全部子世界中的 Relation。
        /// </summary>
        private sealed class QueryRelationTool(WorldGraph graph) : Tool<RelationCluesToolPara>
        {
            public override string name => "query_relation";
            public override string description => "使用普通字符串包含匹配查询事实世界和全部角色认知世界中的 Relation。";

            protected override Task<string> Execute(
                RelationCluesToolPara arguments, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string[] clues = NormalizeClues(arguments.Clues);
                return Task.FromResult(SerializeRelations(
                    graph, GetAllRelations(graph).Where(item => MatchesRelation(graph, item, clues))));
            }
        }

        public sealed class EmptyToolPara : IToolArgument
        {
        }

        /// <summary>
        /// 遍历全部 Character。
        /// </summary>
        private sealed class ListCharactersTool(WorldGraph graph) : Tool<EmptyToolPara>
        {
            public override string name => "list_characters";
            public override string description => "返回世界中的全部 Character。";

            protected override Task<string> Execute(
                EmptyToolPara arguments, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                IEnumerable<Anchor> characters = graph.GetCharacters()
                    .OrderBy(anchor => anchor.Name, StringComparer.Ordinal);
                return Task.FromResult(SerializeAnchors(characters));
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

        /// <summary>
        /// 查询指定 Anchor 的入边、出边或全部关联 Relation。
        /// </summary>
        private sealed class GetAnchorRelationsTool(WorldGraph graph) : Tool<AnchorRelationsToolPara>
        {
            public override string name => "get_anchor_relations";
            public override string description => "按方向和世界范围查询与指定名称 Anchor 相连的 Relation。";

            protected override Task<string> Execute(
                AnchorRelationsToolPara arguments, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Anchor[] anchors = FindAnchors(graph, arguments.AnchorName);
                HashSet<Guid> anchorIds = anchors.Select(anchor => anchor.Id).ToHashSet();
                IEnumerable<ScopedRelation> relations = GetRelationsByScope(
                    graph, arguments.Scope, arguments.CharacterName).Where(item =>
                    arguments.Direction switch
                    {
                        RelationDirection.Incoming => anchorIds.Contains(item.Relation.TargetId),
                        RelationDirection.Outgoing => anchorIds.Contains(item.Relation.SourceId),
                        RelationDirection.Both => anchorIds.Contains(item.Relation.SourceId)
                                                  || anchorIds.Contains(item.Relation.TargetId),
                        _ => throw new ToolArgumentException($"不支持的 Relation 方向：{arguments.Direction}。")
                    });
                return Task.FromResult(SerializeRelations(graph, relations));
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

        /// <summary>
        /// 查询两个名称对应的 Anchor 集合之间的 Relation。
        /// </summary>
        private sealed class GetRelationsBetweenAnchorsTool(WorldGraph graph)
            : Tool<RelationsBetweenAnchorsToolPara>
        {
            public override string name => "get_relations_between_anchors";
            public override string description => "查询两个 Anchor 之间双向存在的 Relation，可限定事实世界或角色认知世界。";

            protected override Task<string> Execute(
                RelationsBetweenAnchorsToolPara arguments, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                HashSet<Guid> firstIds = FindAnchors(graph, arguments.FirstAnchorName)
                    .Select(anchor => anchor.Id).ToHashSet();
                HashSet<Guid> secondIds = FindAnchors(graph, arguments.SecondAnchorName)
                    .Select(anchor => anchor.Id).ToHashSet();
                IEnumerable<ScopedRelation> relations = GetRelationsByScope(
                    graph, arguments.Scope, arguments.CharacterName).Where(item =>
                    (firstIds.Contains(item.Relation.SourceId) && secondIds.Contains(item.Relation.TargetId))
                    || (secondIds.Contains(item.Relation.SourceId) && firstIds.Contains(item.Relation.TargetId)));
                return Task.FromResult(SerializeRelations(graph, relations));
            }
        }

        public sealed class CompareWorldWithSubWorldToolPara : IToolArgument
        {
            [Description("需要对比其认知世界的 Character 名称")]
            public string CharacterName { get; set; } = string.Empty;

            [Description("用于匹配 Relation 的字符串线索；所有线索必须同时匹配")]
            public string[] Clues { get; set; } = [];
        }

        /// <summary>
        /// 并列查询事实世界和指定 Character 子世界中的 Relation。
        /// </summary>
        private sealed class CompareWorldWithSubWorldTool(WorldGraph graph)
            : Tool<CompareWorldWithSubWorldToolPara>
        {
            public override string name => "compare_world_with_sub_world";
            public override string description => "使用相同字符串线索并列查询事实世界与指定 Character 的认知世界，不对差异作推理。";

            protected override Task<string> Execute(
                CompareWorldWithSubWorldToolPara arguments, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string[] clues = NormalizeClues(arguments.Clues);
                ScopedRelation[] world = GetWorldRelations(graph)
                    .Where(item => MatchesRelation(graph, item, clues)).ToArray();
                ScopedRelation[] subWorld = GetSubWorldRelations(graph, arguments.CharacterName)
                    .Where(item => MatchesRelation(graph, item, clues)).ToArray();
                var result = new ComparisonOutput(
                    CreateRelationResult(graph, world),
                    CreateRelationResult(graph, subWorld));
                return Task.FromResult(JsonSerializer.Serialize(result, JsonOptions));
            }
        }

        private static QueryResult<RelationOutput> CreateRelationResult(
            WorldGraph graph, IEnumerable<ScopedRelation> relations)
        {
            RelationOutput[] items = SortRelations(graph, relations)
                .Select(item => ToRelationOutput(graph, item)).ToArray();
            return new QueryResult<RelationOutput>(
                items.Length, Math.Min(items.Length, MaxResults), items.Length > MaxResults,
                items.Take(MaxResults).ToArray());
        }

        public enum RelationDirection
        {
            Incoming,
            Outgoing,
            Both
        }

        public enum RelationQueryScope
        {
            World,
            SubWorld,
            All
        }

        private sealed record ScopedRelation(Relation Relation, Anchor? Domain);
        private sealed record AnchorOutput(string Name, string Description, string Type);
        private sealed record WorldScopeOutput(string Type);
        private sealed record SubWorldScopeOutput(string Type, AnchorOutput Character);
        private sealed record RelationOutput(
            string Name,
            string Description,
            AnchorOutput Source,
            AnchorOutput Target,
            object Scope);
        private sealed record QueryResult<T>(int Total, int Returned, bool Truncated, IReadOnlyList<T> Items);
        private sealed record ComparisonOutput(
            QueryResult<RelationOutput> World,
            QueryResult<RelationOutput> SubWorld);
    }
}
