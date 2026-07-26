using System.Collections.ObjectModel;
using Tavi.Application.Extensions.Guidance;
using Tavi.Application.Extensions.Performance;
using Tavi.Application.Extensions.Scenario;
using Tavi.Application.Extensions.World;
using Tavi.Application.Extensions.Writing;
using Tavi.Extensibility;

namespace Tavi.Application.Extensions;

/// <summary>保存一组活动 Module 的 Definition、冻结参数和按能力建立的 Plugin 索引。</summary>
public sealed class FrozenModuleRuntime
{
    private readonly IReadOnlyDictionary<ModuleId, ITaviPlugin> _plugins;

    internal FrozenModuleRuntime(ModuleCatalog catalog, IReadOnlyDictionary<ModuleId, IReadOnlyDictionary<string, string>> parameters, IEnumerable<ITaviPlugin> plugins)
    {
        Catalog = catalog;
        Parameters = parameters;
        _plugins = new ReadOnlyDictionary<ModuleId, ITaviPlugin>(plugins.ToDictionary(plugin => plugin.Module));
    }

    /// <summary>获取活动 Module Definition Catalog。</summary>
    public ModuleCatalog Catalog { get; }
    /// <summary>获取按 Module 标识索引的规范冻结参数。</summary>
    public IReadOnlyDictionary<ModuleId, IReadOnlyDictionary<string, string>> Parameters { get; }
    /// <summary>获取按 Module 标识稳定排序的 World Authoring 能力。</summary>
    public IReadOnlyList<(ModuleId Module, IWorldAuthoringExtension Extension)> WorldAuthoringExtensions => Select<IWorldAuthoringExtension>();
    /// <summary>获取按 Module 标识稳定排序的 Scenario Definition 能力。</summary>
    public IReadOnlyList<(ModuleId Module, IScenarioDefinitionExtension Extension)> ScenarioDefinitionExtensions => Select<IScenarioDefinitionExtension>();
    /// <summary>获取按 Module 标识稳定排序的 Scenario Settlement 能力。</summary>
    public IReadOnlyList<(ModuleId Module, IScenarioSettlementExtension Extension)> ScenarioSettlementExtensions => Select<IScenarioSettlementExtension>();
    /// <summary>获取按 Module 标识稳定排序的 Guidance 能力。</summary>
    public IReadOnlyList<(ModuleId Module, IGuidanceExtension Extension)> GuidanceExtensions => Select<IGuidanceExtension>();
    /// <summary>获取按 Module 标识稳定排序的 Writing 上下文能力。</summary>
    public IReadOnlyList<(ModuleId Module, IWritingContextExtension Extension)> WritingContextExtensions => Select<IWritingContextExtension>();
    /// <summary>获取按 Module 标识稳定排序的 Writing 互动能力。</summary>
    public IReadOnlyList<(ModuleId Module, IWritingInteractionExtension Extension)> WritingInteractionExtensions => Select<IWritingInteractionExtension>();
    /// <summary>获取按 Module 标识稳定排序的 Written Scene 结果能力。</summary>
    public IReadOnlyList<(ModuleId Module, IWrittenSceneOutcomeExtension Extension)> WrittenSceneOutcomeExtensions => Select<IWrittenSceneOutcomeExtension>();
    /// <summary>获取按 Module 标识稳定排序的 Performance 演绎能力。</summary>
    public IReadOnlyList<(ModuleId Module, IPerformanceExtension Extension)> PerformanceExtensions => Select<IPerformanceExtension>();
    /// <summary>获取指定 Module 的冻结参数；没有参数时返回空字典。</summary>
    public IReadOnlyDictionary<string, string> GetParameters(ModuleId module) => Parameters.TryGetValue(module, out IReadOnlyDictionary<string, string>? values) ? values : new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
    /// <summary>获取指定 Module 的 Plugin；纯声明式 Module 返回 null。</summary>
    public ITaviPlugin? FindPlugin(ModuleId module) => _plugins.GetValueOrDefault(module);

    private IReadOnlyList<(ModuleId Module, T Extension)> Select<T>() where T : class => _plugins.OrderBy(pair => pair.Key.Value, StringComparer.Ordinal).Where(pair => pair.Value is T).Select(pair => (pair.Key, (T)pair.Value)).ToArray();
}
