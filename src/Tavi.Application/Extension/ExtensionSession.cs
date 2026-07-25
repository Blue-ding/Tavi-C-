using System.Globalization;
using System.Collections.ObjectModel;
using Tavi.Application.Scenario;
using Tavi.Extensibility;

namespace Tavi.Application.Extension;

/// <summary>维护下一次 ScenarioSession 使用的 Module 启用状态和行为参数，并产生冻结快照。</summary>
public sealed class ExtensionSession
{
    private readonly Dictionary<ModuleId, ModulePackageDefinition> _packages;
    private readonly Dictionary<ModuleId, ITaviPlugin> _plugins;
    private readonly Dictionary<ModuleId, bool> _enabled;
    private readonly Dictionary<ModuleId, Dictionary<string, string>> _parameters;
    private FrozenExtensionSnapshot? _frozen;

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

    /// <summary>冻结当前已启用 Module、Plugin 和有效参数；后续设置变化不会修改已返回快照。</summary>
    public FrozenExtensionSnapshot Freeze()
    {
        ModulePackageDefinition[] enabledPackages = _packages.Values.Where(package => _enabled[package.Manifest.Id]).ToArray();
        var catalog = ScenarioModuleCatalog.Create(enabledPackages);
        foreach (ITaviPlugin plugin in _plugins.Values.Where(plugin => _enabled.GetValueOrDefault(plugin.Module)))
            catalog.RegisterPlugin(plugin);
        var parameters = enabledPackages.ToDictionary(package => package.Manifest.Id, package => GetEffectiveParameters(package.Manifest.Id));
        _frozen = new FrozenExtensionSnapshot { Catalog = catalog, Parameters = new ReadOnlyDictionary<ModuleId, IReadOnlyDictionary<string, string>>(parameters) };
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

    private ModulePackageDefinition GetPackage(ModuleId module) => _packages.TryGetValue(module, out ModulePackageDefinition? package) ? package : throw Missing(module);
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
        return canonical;
    }
}
