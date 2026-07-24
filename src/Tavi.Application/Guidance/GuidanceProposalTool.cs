using System.ComponentModel;
using System.Text.Json;
using Tavi.Application.LanguageModel;
using Tavi.Domain.World;

namespace Tavi.Application.Guidance;

/// <summary>创建只修改 Guidance 草稿而不修改真实 World 的结构化模型工具。</summary>
internal static class GuidanceProposalTool
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    internal static IReadOnlyCollection<ITool> CreateTools(GuidanceDraft guidance, WorldSnapshot projectedWorld)
    {
        ArgumentNullException.ThrowIfNull(guidance);
        ArgumentNullException.ThrowIfNull(projectedWorld);
        return [new SetSummaryTool(guidance), new ProposeElementTool(guidance), new ProposeScopeTool(guidance, projectedWorld), new ProposeAspectTool(guidance, projectedWorld), new ProposeRelationTool(guidance, projectedWorld)];
    }

    private static ProposalElementReference ResolveElementReference(GuidanceDraft guidance, WorldSnapshot world, ReferenceKind kind, string selector)
    {
        if (string.IsNullOrWhiteSpace(selector))
            throw new ToolArgumentException("Element 引用不能为空。");
        return kind switch
        {
            ReferenceKind.Existing => new ProposalElementReference.Existing(RequireSingleElement(world, selector).Id),
            ReferenceKind.Proposed => new ProposalElementReference.Proposed(RequireProposedElement(guidance, selector)),
            _ => throw new ToolArgumentException($"不支持的 Element 引用类型：{kind}。")
        };
    }

    private static ProposalScopeReference ResolveScopeReference(GuidanceDraft guidance, WorldSnapshot world, ReferenceKind kind, string selector)
    {
        if (string.IsNullOrWhiteSpace(selector))
            throw new ToolArgumentException("Scope 引用不能为空。");
        return kind switch
        {
            ReferenceKind.Existing => new ProposalScopeReference.Existing(RequireSingleScope(world, selector).Id),
            ReferenceKind.Proposed => new ProposalScopeReference.Proposed(RequireProposedScope(guidance, selector)),
            _ => throw new ToolArgumentException($"不支持的 Scope 引用类型：{kind}。")
        };
    }

    private static ProposalElementId RequireProposedElement(GuidanceDraft guidance, string selector)
    {
        if (!Guid.TryParse(selector, out Guid id))
            throw new ToolArgumentException($"临时 Element 标识“{selector}”格式无效。");
        ProposalElementId proposalId = new(id);
        return guidance.CreateProposal().Changes.OfType<ProposeAddElement>().Any(change => change.ElementId == proposalId) ? proposalId : throw new ToolArgumentException($"当前 GuidanceOperation 不存在临时 Element {id}。");
    }

    private static ProposalScopeId RequireProposedScope(GuidanceDraft guidance, string selector)
    {
        if (!Guid.TryParse(selector, out Guid id))
            throw new ToolArgumentException($"临时 Scope 标识“{selector}”格式无效。");
        ProposalScopeId proposalId = new(id);
        return guidance.CreateProposal().Changes.OfType<ProposeAddScope>().Any(change => change.ScopeId == proposalId) ? proposalId : throw new ToolArgumentException($"当前 GuidanceOperation 不存在临时 Scope {id}。");
    }

    private static Element RequireSingleElement(WorldSnapshot world, string selector)
    {
        Element[] values = world.Elements.Values.Where(value => string.Equals(value.Name, selector, StringComparison.OrdinalIgnoreCase)).ToArray();
        return values.Length switch
        {
            1 => values[0],
            0 => throw new ToolArgumentException($"当前临时 World 不存在名称为“{selector}”的 Element。"),
            _ => throw new ToolArgumentException($"名称“{selector}”匹配多个 Element。")
        };
    }

    private static Scope RequireSingleScope(WorldSnapshot world, string selector)
    {
        Scope[] values = world.Scopes.Values.Where(value => string.Equals(value.Name, selector, StringComparison.OrdinalIgnoreCase)).ToArray();
        return values.Length switch
        {
            1 => values[0],
            0 => throw new ToolArgumentException($"当前临时 World 不存在名称为“{selector}”的 Scope。"),
            _ => throw new ToolArgumentException($"名称“{selector}”匹配多个 Scope。")
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

    private static double RequireFinite(double quantity)
    {
        if (!double.IsFinite(quantity))
            throw new ToolArgumentException("Quantity 必须是有限 double。");
        return quantity;
    }

    private enum ReferenceKind
    {
        Existing,
        Proposed
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

    private sealed class ProposeElementArguments : IToolArgument
    {
        [Description("向玩家说明这项修改如何发展叙事势能")]
        public string Rationale { get; set; } = string.Empty;

        [Description("拟添加 Element 的名称")]
        public string Name { get; set; } = string.Empty;

        [Description("拟添加 Element 的描述")]
        public string Description { get; set; } = string.Empty;

        [Description("符合 namespace:name 约定的 ElementType")]
        public string Type { get; set; } = "core:none";
    }

    private sealed class ProposeElementTool(GuidanceDraft guidance) : Tool<ProposeElementArguments>
    {
        public override string name => "propose_element";
        public override string description => "向当前 Guidance 草稿添加临时 Element；只形成待玩家审阅的修改。";

        protected override Task<string> Execute(ProposeElementArguments arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Invoke(() =>
            {
                ProposeAddElement change = guidance.ProposeElement(arguments.Rationale, arguments.Name, arguments.Description, new ElementType(arguments.Type));
                return new { changeId = change.Id, proposalElementId = change.ElementId.Value };
            }));
        }
    }

    private sealed class ProposeScopeArguments : IToolArgument
    {
        [Description("向玩家说明这项修改如何发展叙事势能")]
        public string Rationale { get; set; } = string.Empty;

        [Description("拟添加 Scope 的名称")]
        public string Name { get; set; } = string.Empty;

        [Description("拟添加 Scope 的描述")]
        public string Description { get; set; } = string.Empty;

        [Description("由对应 Module 解释的有限强度")]
        public double Quantity { get; set; } = 1;

        [Description("符合 namespace:name 约定的 ScopeType")]
        public string Type { get; set; } = "core:none";

        [Description("Owner 是现有 Element 还是 propose_element 返回的临时 Element")]
        public ReferenceKind OwnerKind { get; set; }

        [Description("现有 Element 的唯一名称或 proposalElementId")]
        public string Owner { get; set; } = string.Empty;
    }

    private sealed class ProposeScopeTool(GuidanceDraft guidance, WorldSnapshot world) : Tool<ProposeScopeArguments>
    {
        public override string name => "propose_scope";
        public override string description => "向当前 Guidance 草稿添加由一个 Element 持有的临时 Scope。";

        protected override Task<string> Execute(ProposeScopeArguments arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Invoke(() =>
            {
                ProposeAddScope change = guidance.ProposeScope(arguments.Rationale, arguments.Name, arguments.Description, RequireFinite(arguments.Quantity), new ScopeType(arguments.Type), ResolveElementReference(guidance, world, arguments.OwnerKind, arguments.Owner));
                return new { changeId = change.Id, proposalScopeId = change.ScopeId.Value };
            }));
        }
    }

    private abstract class AssertionArguments : IToolArgument
    {
        [Description("向玩家说明这项修改如何发展叙事势能")]
        public string Rationale { get; set; } = string.Empty;

        [Description("断言名称")]
        public string Name { get; set; } = string.Empty;

        [Description("断言描述")]
        public string Description { get; set; } = string.Empty;

        [Description("由对应 Module 解释的有限强度")]
        public double Quantity { get; set; } = 1;

        [Description("Scope 是现有 Scope 还是 propose_scope 返回的临时 Scope")]
        public ReferenceKind ScopeKind { get; set; }

        [Description("现有 Scope 的唯一名称或 proposalScopeId")]
        public string Scope { get; set; } = string.Empty;
    }

    private sealed class ProposeAspectArguments : AssertionArguments
    {
        [Description("符合 namespace:name 约定的 AspectType")]
        public string Type { get; set; } = "core:none";

        [Description("目标是现有 Element 还是临时 Element")]
        public ReferenceKind ElementKind { get; set; }

        [Description("现有 Element 的唯一名称或 proposalElementId")]
        public string Element { get; set; } = string.Empty;
    }

    private sealed class ProposeAspectTool(GuidanceDraft guidance, WorldSnapshot world) : Tool<ProposeAspectArguments>
    {
        public override string name => "propose_aspect";
        public override string description => "向当前 Guidance 草稿添加唯一属于一个 Scope 的一元断言。";

        protected override Task<string> Execute(ProposeAspectArguments arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Invoke(() =>
            {
                ProposeAddAspect change = guidance.ProposeAspect(arguments.Rationale, arguments.Name, arguments.Description, RequireFinite(arguments.Quantity), new AspectType(arguments.Type), ResolveElementReference(guidance, world, arguments.ElementKind, arguments.Element), ResolveScopeReference(guidance, world, arguments.ScopeKind, arguments.Scope));
                return new { changeId = change.Id, proposed = true };
            }));
        }
    }

    private sealed class ProposeRelationArguments : AssertionArguments
    {
        [Description("符合 namespace:name 约定的 RelationType")]
        public string Type { get; set; } = "core:none";

        [Description("Source 是现有 Element 还是临时 Element")]
        public ReferenceKind SourceKind { get; set; }

        [Description("现有 Source Element 的唯一名称或 proposalElementId")]
        public string Source { get; set; } = string.Empty;

        [Description("Target 是现有 Element 还是临时 Element")]
        public ReferenceKind TargetKind { get; set; }

        [Description("现有 Target Element 的唯一名称或 proposalElementId")]
        public string Target { get; set; } = string.Empty;
    }

    private sealed class ProposeRelationTool(GuidanceDraft guidance, WorldSnapshot world) : Tool<ProposeRelationArguments>
    {
        public override string name => "propose_relation";
        public override string description => "向当前 Guidance 草稿添加唯一属于一个 Scope 的有向二元断言。";

        protected override Task<string> Execute(ProposeRelationArguments arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Invoke(() =>
            {
                ProposeAddRelation change = guidance.ProposeRelation(arguments.Rationale, arguments.Name, arguments.Description, RequireFinite(arguments.Quantity), new RelationType(arguments.Type), ResolveElementReference(guidance, world, arguments.SourceKind, arguments.Source), ResolveElementReference(guidance, world, arguments.TargetKind, arguments.Target), ResolveScopeReference(guidance, world, arguments.ScopeKind, arguments.Scope));
                return new { changeId = change.Id, proposed = true };
            }));
        }
    }
}
