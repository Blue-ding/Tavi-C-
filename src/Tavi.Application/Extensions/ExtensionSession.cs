using System.Globalization;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tavi.Extensibility;

namespace Tavi.Application.Extensions;

/// <summary>维护下一次 ScenarioSession 使用的 Module 启用状态和行为参数，并产生冻结快照。</summary>
public sealed class ExtensionSession
{
    private readonly Dictionary<ModuleId, ModulePackageDefinition> _packages;
    private readonly Dictionary<ModuleId, ITaviPlugin> _plugins;
    private readonly Dictionary<ModuleId, bool> _enabled;
    private readonly Dictionary<ModuleId, Dictionary<string, string>> _parameters;
    private FrozenModuleRuntime? _frozen;

    /// <summary>从已校验的 Module 包、持久化设置和受信 Plugin 创建 ExtensionSession。</summary>
    public ExtensionSession(IEnumerable<ModulePackageDefinition> packages, ExtensionSettings? settings = null, IEnumerable<ITaviPlugin>? plugins = null)
    {
        ArgumentNullException.ThrowIfNull(packages);
        ModulePackageDefinition[] copied = packages.Select(ExtensibilityCopies.Package).ToArray();
        _packages = copied.ToDictionary(package => package.Manifest.Id);
        _plugins = (plugins ?? []).ToDictionary(plugin => plugin.Module);
        _enabled = _packages.Keys.ToDictionary(id => id, _ => true);
        _parameters = _packages.Keys.ToDictionary(id => id, _ => new Dictionary<string, string>(StringComparer.Ordinal));
        ApplySettings(settings);
        ValidateEnabledDependencies();
    }

    /// <summary>获取 Module Manifest 的稳定排序副本。</summary>
    public IReadOnlyList<ModuleManifest> Modules => _packages.Values.Select(package => ExtensibilityCopies.Manifest(package.Manifest)).OrderBy(manifest => manifest.Id.Value, StringComparer.Ordinal).ToArray();

    /// <summary>获取指定 Module 的声明式 Setting Schema；未声明时返回 null。</summary>
    public ModuleSettingsSchema? FindSettingsSchema(ModuleId module) => GetPackage(module).SettingsSchema;

    /// <summary>获取指定 Module 的有效 Setting Schema；旧式参数会被转换为等价 Profile。</summary>
    public ModuleSettingsSchema GetSettingsSchema(ModuleId module) => GetSettingsSchema(GetPackage(module));

    /// <summary>获取设置是否已在冻结后改变，因而需要重建 ScenarioSession。</summary>
    public bool RestartRequired { get; private set; }

    /// <summary>获取指定 Module 当前期望的启用状态。</summary>
    public bool IsEnabled(ModuleId module) => _enabled.TryGetValue(module, out bool enabled) ? enabled : throw Missing(module);

    /// <summary>启用 Module；其依赖必须已经启用。</summary>
    public void Enable(ModuleId module)
    {
        ModulePackageDefinition package = GetPackage(module);
        ModuleDependency? missing = package.Manifest.Dependencies.FirstOrDefault(dependency => !_enabled.GetValueOrDefault(dependency.Id));
        if (missing is not null)
            throw new InvalidOperationException($"启用 Module {module} 前必须先启用依赖 {missing.Id}。");
        SetEnabled(module, true);
    }

    /// <summary>禁用 Module；仍被已启用 Module 依赖时拒绝。</summary>
    public void Disable(ModuleId module)
    {
        _ = GetPackage(module);
        ModuleManifest? dependent = _packages.Values.Where(package => _enabled.GetValueOrDefault(package.Manifest.Id)).Select(package => package.Manifest).FirstOrDefault(manifest => manifest.Dependencies.Any(dependency => dependency.Id == module));
        if (dependent is not null)
            throw new InvalidOperationException($"Module {module} 仍被已启用 Module {dependent.Id} 依赖。");
        SetEnabled(module, false);
    }

    /// <summary>设置 Module 参数并规范化文本；参数变化只影响下一份冻结快照。</summary>
    public void SetParameter(ModuleId module, string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        ModuleParameterDefinition definition = GetPackage(module).Manifest.Parameters.SingleOrDefault(parameter => parameter.Key == key) ?? throw new ArgumentException($"Module {module} 未声明参数 {key}。", nameof(key));
        string canonical = Canonicalize(definition, value);
        if (_parameters[module].GetValueOrDefault(key) == canonical)
            return;
        _parameters[module][key] = canonical;
        MarkChanged();
    }

    /// <summary>清除参数覆盖并恢复 Module 默认值。</summary>
    public void ResetParameter(ModuleId module, string key)
    {
        _ = GetPackage(module).Manifest.Parameters.SingleOrDefault(parameter => parameter.Key == key) ?? throw new ArgumentException($"Module {module} 未声明参数 {key}。", nameof(key));
        if (_parameters[module].Remove(key))
            MarkChanged();
    }

    /// <summary>获取指定 Module 的当前有效参数，结果为独立只读副本。</summary>
    public IReadOnlyDictionary<string, string> GetEffectiveParameters(ModuleId module)
    {
        ModuleManifest manifest = GetPackage(module).Manifest;
        return new ReadOnlyDictionary<string, string>(manifest.Parameters.ToDictionary(parameter => parameter.Key, parameter => _parameters[module].GetValueOrDefault(parameter.Key) ?? Canonicalize(parameter, parameter.DefaultValue), StringComparer.Ordinal));
    }

    /// <summary>获取指定 Module 经过 Schema 校验并物化默认值的完整 JSON Setting 快照。</summary>
    public JsonElement GetEffectiveSettings(ModuleId module)
    {
        ModulePackageDefinition package = GetPackage(module);
        ModuleSettingsSchema schema = GetSettingsSchema(package);
        var configured = new JsonObject();
        foreach (ModuleParameterDefinition definition in package.Manifest.Parameters)
        {
            if (_parameters[module].TryGetValue(definition.Key, out string? value))
                configured[definition.Key] = ToJsonValue(definition, value);
        }
        return schema.Materialize(JsonSerializer.SerializeToElement(configured));
    }

    /// <summary>
    /// 原子校验并替换指定 Module 的完整 JSON Setting。
    /// 当前 Runtime 尚未切换 Setting 代际，因此所有变化仍标记为需要重建。
    /// </summary>
    public JsonElement SetSettings(ModuleId module, JsonElement settings)
    {
        ModulePackageDefinition package = GetPackage(module);
        ModuleSettingsSchema schema = GetSettingsSchema(package);
        JsonElement materialized = schema.Materialize(settings);
        Dictionary<string, ModuleParameterDefinition> definitions = package.Manifest.Parameters.ToDictionary(value => value.Key, StringComparer.Ordinal);
        Dictionary<string, string> candidate = materialized.EnumerateObject().ToDictionary(
            property => property.Name,
            property => ToCanonicalText(definitions[property.Name], property.Value),
            StringComparer.Ordinal);
        if (candidate.Count == _parameters[module].Count &&
            candidate.All(pair => _parameters[module].GetValueOrDefault(pair.Key) == pair.Value))
            return materialized;
        _parameters[module] = candidate;
        MarkChanged();
        return materialized;
    }

    /// <summary>
    /// 从完整候选配置创建独立 Session；当前 Session 在任一步验证失败时保持不变。
    /// 候选必须精确覆盖全部已安装 Module。
    /// </summary>
    public ExtensionSession CreateCandidate(IEnumerable<ExtensionModuleConfiguration> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);
        ExtensionModuleConfiguration[] copied = modules.ToArray();
        if (copied.Any(configuration => configuration is null))
            throw new ArgumentException("Extension 候选配置不能包含空 Module。", nameof(modules));
        if (copied.GroupBy(configuration => configuration.Module).Any(group => group.Count() > 1))
            throw new ArgumentException("Extension 候选配置不能包含重复 Module。", nameof(modules));
        ModuleId[] expected = _packages.Keys.OrderBy(module => module.Value, StringComparer.Ordinal).ToArray();
        ModuleId[] actual = copied.Select(configuration => configuration.Module).OrderBy(module => module.Value, StringComparer.Ordinal).ToArray();
        if (!expected.SequenceEqual(actual))
            throw new ArgumentException("Extension 候选配置必须精确覆盖全部已安装 Module。", nameof(modules));

        ExtensionSession candidate;
        try
        {
            candidate = new ExtensionSession(
                _packages.Values,
                new ExtensionSettings
                {
                    Modules = copied.Select(configuration => new ExtensionModuleSettings
                    {
                        Module = configuration.Module,
                        Enabled = configuration.Enabled
                    }).ToArray()
                },
                _plugins.Values);
        }
        catch (InvalidDataException exception)
        {
            throw new ModuleConfigurationException(
                "TAVI.EXTENSIONS.SETTINGS.INVALID_DEPENDENCY",
                exception.Message,
                exception);
        }

        foreach (ExtensionModuleConfiguration configuration in copied)
            _ = candidate.SetSettings(configuration.Module, configuration.Settings);
        return candidate;
    }

    /// <summary>冻结当前已启用 Module、Plugin 和有效参数；后续设置变化不会修改已返回快照。</summary>
    public FrozenModuleRuntime Freeze()
    {
        ModulePackageDefinition[] enabledPackages = _packages.Values.Where(package => _enabled[package.Manifest.Id]).ToArray();
        var catalog = ModuleCatalog.Create(enabledPackages);
        ITaviPlugin[] activePlugins = _plugins.Values.Where(plugin => _enabled.GetValueOrDefault(plugin.Module)).ToArray();
        ValidatePlugins(catalog, activePlugins);
        var parameters = enabledPackages.ToDictionary(package => package.Manifest.Id, package => GetEffectiveParameters(package.Manifest.Id));
        _frozen = new FrozenModuleRuntime(catalog, new ReadOnlyDictionary<ModuleId, IReadOnlyDictionary<string, string>>(parameters), activePlugins);
        RestartRequired = false;
        return _frozen;
    }

    /// <summary>创建可持久化的期望设置副本。</summary>
    public ExtensionSettings CreateSettings() => new()
    {
        Modules = _packages.Keys.OrderBy(id => id.Value, StringComparer.Ordinal).Select(id => new ExtensionModuleSettings { Module = id, Enabled = _enabled[id], Parameters = new Dictionary<string, string>(_parameters[id], StringComparer.Ordinal) }).ToArray()
    };

    private void ApplySettings(ExtensionSettings? settings)
    {
        if (settings is null)
            return;
        if (settings.Version != 1)
            throw new InvalidDataException($"不支持 Extension 设置版本 {settings.Version}。");
        foreach (ExtensionModuleSettings value in settings.Modules)
        {
            if (!_packages.ContainsKey(value.Module))
                continue;
            _enabled[value.Module] = value.Enabled;
            foreach ((string key, string parameterValue) in value.Parameters)
            {
                ModuleParameterDefinition definition = _packages[value.Module].Manifest.Parameters.SingleOrDefault(parameter => parameter.Key == key) ?? throw new InvalidDataException($"Module {value.Module} 未声明参数 {key}。");
                _parameters[value.Module][key] = Canonicalize(definition, parameterValue);
            }
        }
    }

    private void ValidateEnabledDependencies()
    {
        foreach (ModulePackageDefinition package in _packages.Values.Where(package => _enabled[package.Manifest.Id]))
        {
            ModuleDependency? missing = package.Manifest.Dependencies.FirstOrDefault(dependency => !_enabled.GetValueOrDefault(dependency.Id));
            if (missing is not null)
                throw new InvalidDataException($"已启用 Module {package.Manifest.Id} 缺少已启用依赖 {missing.Id}。");
        }
    }

    private void SetEnabled(ModuleId module, bool enabled)
    {
        if (_enabled[module] == enabled)
            return;
        _enabled[module] = enabled;
        MarkChanged();
    }

    private void MarkChanged()
    {
        if (_frozen is not null)
            RestartRequired = true;
    }

    private void ValidatePlugins(ModuleCatalog catalog, IEnumerable<ITaviPlugin> plugins)
    {
        Dictionary<ModuleId, ModuleManifest> modules = catalog.Modules.ToDictionary(module => module.Id);
        foreach (ITaviPlugin plugin in plugins)
        {
            if (!modules.TryGetValue(plugin.Module, out ModuleManifest? module))
                throw new ModuleConfigurationException("TAVI.EXTENSIONS.PLUGIN.MODULE_MISSING", $"Plugin 所属 Module {plugin.Module} 未加载。");
            if (plugin.Version != module.Version)
                throw new ModuleConfigurationException("TAVI.EXTENSIONS.PLUGIN.VERSION_MISMATCH", $"Plugin {plugin.Module} 版本 {plugin.Version} 与 Module 版本 {module.Version} 不一致。");
            if (plugin is Application.Extensions.Scenario.IScenarioSettlementExtension settlement)
            {
                SemanticKey? foreign = settlement.Definitions.Cast<SemanticKey?>().FirstOrDefault(definition => definition!.Value.Namespace != plugin.Module);
                if (foreign.HasValue)
                    throw new ModuleConfigurationException("TAVI.EXTENSIONS.PLUGIN.FOREIGN_DEFINITION", $"Plugin {plugin.Module} 声明了其他 Module 的 SceneDefinition {foreign.Value}。");
            }
        }
    }

    private ModulePackageDefinition GetPackage(ModuleId module) => _packages.TryGetValue(module, out ModulePackageDefinition? package) ? package : throw Missing(module);
    private static ModuleSettingsSchema GetSettingsSchema(ModulePackageDefinition package) =>
        package.SettingsSchema ?? ModuleSettingsProfile.FromParameters(package.Manifest.Parameters);
    private static KeyNotFoundException Missing(ModuleId module) => new($"未发现 Module {module}。");

    private static string Canonicalize(ModuleParameterDefinition definition, string value)
    {
        string canonical = definition.Type switch
        {
            ModuleParameterType.Boolean when bool.TryParse(value, out bool parsed) => parsed ? "true" : "false",
            ModuleParameterType.Integer when long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed) => parsed.ToString(CultureInfo.InvariantCulture),
            ModuleParameterType.Number when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) && double.IsFinite(parsed) => parsed.ToString("R", CultureInfo.InvariantCulture),
            ModuleParameterType.String => value,
            _ => throw new ArgumentException($"参数 {definition.Key} 的值“{value}”不符合 {definition.Type} 类型。", nameof(value))
        };
        if (definition.AllowedValues.Count > 0 && !definition.AllowedValues.Contains(canonical, StringComparer.Ordinal))
            throw new ArgumentOutOfRangeException(nameof(value), $"参数 {definition.Key} 不接受值“{canonical}”。");
        if (definition.Type is ModuleParameterType.Integer or ModuleParameterType.Number)
        {
            double number = double.Parse(canonical, CultureInfo.InvariantCulture);
            if (definition.Minimum is double minimum && number < minimum || definition.Maximum is double maximum && number > maximum)
                throw new ArgumentOutOfRangeException(nameof(value), $"参数 {definition.Key} 超出允许范围。");
        }
        if (definition.Type == ModuleParameterType.String)
        {
            if (definition.MinimumLength is int minimum && canonical.Length < minimum ||
                definition.MaximumLength is int maximum && canonical.Length > maximum)
                throw new ArgumentOutOfRangeException(nameof(value), $"参数 {definition.Key} 的文本长度超出允许范围。");
        }
        return canonical;
    }

    private static JsonNode? ToJsonValue(ModuleParameterDefinition definition, string value)
    {
        string canonical = Canonicalize(definition, value);
        return definition.Type switch
        {
            ModuleParameterType.Boolean => JsonValue.Create(bool.Parse(canonical)),
            ModuleParameterType.Integer => JsonValue.Create(long.Parse(canonical, NumberStyles.Integer, CultureInfo.InvariantCulture)),
            ModuleParameterType.Number => JsonValue.Create(double.Parse(canonical, NumberStyles.Float, CultureInfo.InvariantCulture)),
            ModuleParameterType.String => JsonValue.Create(canonical),
            _ => throw new ArgumentOutOfRangeException(nameof(definition))
        };
    }

    private static string ToCanonicalText(ModuleParameterDefinition definition, JsonElement value) =>
        Canonicalize(definition, definition.Type switch
        {
            ModuleParameterType.Boolean => value.GetBoolean() ? "true" : "false",
            ModuleParameterType.Integer => value.TryGetInt64(out long integer)
                ? integer.ToString(CultureInfo.InvariantCulture)
                : value.GetDecimal().ToString(CultureInfo.InvariantCulture),
            ModuleParameterType.Number => value.GetDouble().ToString("R", CultureInfo.InvariantCulture),
            ModuleParameterType.String => value.GetString()!,
            _ => throw new ArgumentOutOfRangeException(nameof(definition))
        });
}
