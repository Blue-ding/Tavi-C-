using System.ComponentModel;
using System.Text.Json;
using Tavi.Application.Extensions;
using Tavi.Application.LanguageModel;

namespace Tavi.Application.Guidance;

/// <summary>向 Guidance 暴露当前注册的四类 World 开放类型及其语义说明。</summary>
internal sealed class WorldTypeDiscoveryTool(ModuleCatalog catalog) : Tool<WorldTypeDiscoveryTool.Arguments>
{
    private readonly ModuleCatalog _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

    public override string name => "search_world_types";
    public override string description => "按 EARS 类别和文本查询当前注册的开放类型；创建提案前应先使用本工具选择返回的稳定类型键。";

    protected override Task<string> Execute(Arguments arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string query = arguments.Query.Trim();
        IEnumerable<TypeResult> values = arguments.Kind switch
        {
            WorldTypeKind.Element => _catalog.GetElementTypes().Select(value => new TypeResult(value.Key.Value, value.Module.Value, value.Name, value.Description)),
            WorldTypeKind.Scope => _catalog.GetScopeTypes().Select(value => new TypeResult(value.Key.Value, value.Module.Value, value.Name, value.Description)),
            WorldTypeKind.Aspect => _catalog.GetAspectTypes().Select(value => new TypeResult(value.Key.Value, value.Module.Value, value.Name, value.Description)),
            WorldTypeKind.Relation => _catalog.GetRelationTypes().Select(value => new TypeResult(value.Key.Value, value.Module.Value, value.Name, value.Description)),
            _ => throw new ToolArgumentException($"不支持的开放类型类别 {arguments.Kind}。")
        };
        if (query.Length > 0)
            values = values.Where(value => $"{value.Key}\n{value.Module}\n{value.Name}\n{value.Description}".Contains(query, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(JsonSerializer.Serialize(values));
    }

    internal sealed class Arguments : IToolArgument
    {
        [Description("要查询的 EARS 类型类别")]
        public WorldTypeKind Kind { get; set; }

        [Description("名称、稳定键、Module 或说明中的可选搜索文本；列出全部时传空字符串")]
        public string Query { get; set; } = string.Empty;
    }

    private sealed record TypeResult(string Key, string Module, string Name, string Description);
}

/// <summary>指定 Guidance 类型发现工具查询的 EARS 类别。</summary>
internal enum WorldTypeKind
{
    Element,
    Scope,
    Aspect,
    Relation
}
