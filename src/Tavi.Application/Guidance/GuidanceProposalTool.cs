using System.ComponentModel;
using System.Text.Json;
using Tavi.Application.LanguageModel;
using Tavi.Application.World;
using Tavi.Domain.World;

namespace Tavi.Application.Guidance;

/// <summary>创建只修改 Guidance 草稿而不修改真实 World 的模型工具。</summary>
internal static class GuidanceProposalTool
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    internal static IReadOnlyCollection<ITool> CreateTools(GuidanceDraft guidance, WorldSnapshot projectedWorld)
    {
        ArgumentNullException.ThrowIfNull(guidance);
        ArgumentNullException.ThrowIfNull(projectedWorld);
        return [new SetSummaryTool(guidance), new ProposeAnchorTool(guidance), new ProposeRelationTool(guidance, projectedWorld)];
    }

    private static ProposalAnchorReference ResolveReference(GuidanceDraft guidance, WorldSnapshot projectedWorld, ReferenceKind kind, string selector)
    {
        if (string.IsNullOrWhiteSpace(selector))
            throw new ToolArgumentException("Anchor 引用不能为空。");
        return kind switch
        {
            ReferenceKind.Existing => new ProposalAnchorReference.Existing(RequireSingleProjectedAnchor(projectedWorld, selector).Id),
            ReferenceKind.Proposed => new ProposalAnchorReference.Proposed(RequireProposedAnchor(guidance, selector)),
            _ => throw new ToolArgumentException($"不支持的 Anchor 引用类型：{kind}。")
        };
    }

    private static ProposalAnchorId RequireProposedAnchor(GuidanceDraft guidance, string selector)
    {
        if (!Guid.TryParse(selector, out Guid id))
            throw new ToolArgumentException($"临时 Anchor 标识“{selector}”格式无效。");
        ProposalAnchorId anchorId = new(id);
        bool exists = guidance.CreateProposal().Changes.OfType<ProposeAddAnchor>().Any(change => change.AnchorId == anchorId);
        return exists ? anchorId : throw new ToolArgumentException($"当前 GuidanceOperation 不存在临时 Anchor {id}。");
    }

    private static Anchor RequireSingleProjectedAnchor(WorldSnapshot projectedWorld, string selector)
    {
        Anchor[] anchors = projectedWorld.Anchors.Values.Where(anchor => string.Equals(anchor.Name, selector, StringComparison.OrdinalIgnoreCase)).ToArray();
        return anchors.Length switch
        {
            1 => anchors[0],
            0 => throw new ToolArgumentException($"当前临时 World 不存在名称为“{selector}”的 Anchor。"),
            _ => throw new ToolArgumentException($"名称“{selector}”匹配多个 Anchor。")
        };
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

    private enum ReferenceKind
    {
        Existing,
        Proposed
    }

    private enum ProposalScope
    {
        World,
        SubWorld
    }

    private sealed class SetSummaryArguments : IToolArgument
    {
        [Description("面向玩家概括当前完整提案的简短文本")]
        public string Summary { get; set; } = string.Empty;
    }

    private sealed class SetSummaryTool(GuidanceDraft guidance) : Tool<SetSummaryArguments>
    {
        public override string name => "set_guidance_summary";
        public override string description => "设置当前 Guidance 草稿面向玩家的提案摘要；不会修改真实 World。";

        protected override Task<string> Execute(SetSummaryArguments arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Invoke(() =>
            {
                guidance.SetSummary(arguments.Summary);
                return new { updated = true };
            }));
        }
    }

    private sealed class ProposeAnchorArguments : IToolArgument
    {
        [Description("向玩家说明这项修改如何发展叙事势能")]
        public string Rationale { get; set; } = string.Empty;

        [Description("拟添加 Anchor 的名称")]
        public string Name { get; set; } = string.Empty;

        [Description("拟添加 Anchor 的描述")]
        public string Description { get; set; } = string.Empty;

        [Description("拟添加 Anchor 的领域类型")]
        public AnchorType Type { get; set; }
    }

    private sealed class ProposeAnchorTool(GuidanceDraft guidance) : Tool<ProposeAnchorArguments>
    {
        public override string name => "propose_anchor";
        public override string description => "向当前 Guidance 草稿添加一个临时 Anchor；只形成待玩家审阅的修改，不会修改真实 World。";

        protected override Task<string> Execute(ProposeAnchorArguments arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Invoke(() =>
            {
                ProposeAddAnchor change = guidance.ProposeAnchor(arguments.Rationale, arguments.Name, arguments.Description, arguments.Type);
                return new { changeId = change.Id, proposalAnchorId = change.AnchorId.Value };
            }));
        }
    }

    private sealed class ProposeRelationArguments : IToolArgument
    {
        [Description("向玩家说明这项修改如何发展叙事势能")]
        public string Rationale { get; set; } = string.Empty;

        [Description("拟添加 Relation 的名称")]
        public string Name { get; set; } = string.Empty;

        [Description("拟添加 Relation 的描述")]
        public string Description { get; set; } = string.Empty;

        [Description("Source 是现有 Anchor 名称还是系统返回的临时 Anchor 标识")]
        public ReferenceKind SourceKind { get; set; }

        [Description("SourceKind 为 Existing 时填写唯一 Anchor 名称，为 Proposed 时填写 propose_anchor 返回的 proposalAnchorId")]
        public string Source { get; set; } = string.Empty;

        [Description("Target 是现有 Anchor 名称还是系统返回的临时 Anchor 标识")]
        public ReferenceKind TargetKind { get; set; }

        [Description("TargetKind 为 Existing 时填写唯一 Anchor 名称，为 Proposed 时填写 propose_anchor 返回的 proposalAnchorId")]
        public string Target { get; set; } = string.Empty;

        [Description("Relation 位于事实 World 还是某个 Character 的 SubWorld")]
        public ProposalScope Scope { get; set; }

        [Description("Scope 为 SubWorld 时说明 Character 是现有 Anchor 还是本轮临时 Anchor；Scope 为 World 时填写 Existing")]
        public ReferenceKind CharacterKind { get; set; }

        [Description("Scope 为 SubWorld 时填写 Character 的唯一名称或 propose_anchor 返回的 proposalAnchorId；Scope 为 World 时填写空字符串")]
        public string Character { get; set; } = string.Empty;
    }

    private sealed class ProposeRelationTool(GuidanceDraft guidance, WorldSnapshot projectedWorld) : Tool<ProposeRelationArguments>
    {
        public override string name => "propose_relation";
        public override string description => "向当前 Guidance 草稿添加 Relation，可引用现有 Anchor 或本轮临时 Anchor；不会修改真实 World。";

        protected override Task<string> Execute(ProposeRelationArguments arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Invoke(() =>
            {
                ProposalAnchorReference source = ResolveReference(guidance, projectedWorld, arguments.SourceKind, arguments.Source);
                ProposalAnchorReference target = ResolveReference(guidance, projectedWorld, arguments.TargetKind, arguments.Target);
                ProposedRelationScope scope = arguments.Scope switch
                {
                    ProposalScope.World => new ProposedRelationScope.World(),
                    ProposalScope.SubWorld => new ProposedRelationScope.SubWorld(ResolveReference(guidance, projectedWorld, arguments.CharacterKind, arguments.Character)),
                    _ => throw new ToolArgumentException($"不支持的 Relation Scope：{arguments.Scope}。")
                };
                ProposeAddRelation change = guidance.ProposeRelation(arguments.Rationale, arguments.Name, arguments.Description, source, target, scope);
                return new { changeId = change.Id, proposed = true };
            }));
        }
    }
}
