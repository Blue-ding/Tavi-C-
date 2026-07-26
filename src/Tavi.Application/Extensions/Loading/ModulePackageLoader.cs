using System.Text.Json;
using System.Globalization;
using Tavi.Extensibility;

namespace Tavi.Application.Extensions.Loading;

/// <summary>从标准 Module 目录读取 Manifest、声明式语义和静态 SceneDefinition。</summary>
public static class ModulePackageLoader
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    /// <summary>读取并转换指定 Module 目录；缺少必需文件或语法无效时抛出 ScenarioApplicationException。</summary>
    public static ModulePackageDefinition Load(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        string fullDirectory = Path.GetFullPath(directory);
        string manifestPath = Path.Combine(fullDirectory, "module.json");
        string semanticsPath = Path.Combine(fullDirectory, "semantics.json");
        string scenesPath = Path.Combine(fullDirectory, "scenes.json");
        string writingPath = Path.Combine(fullDirectory, "writing.json");
        try
        {
            RawManifest manifest = ReadRequired<RawManifest>(manifestPath);
            RawSemantics semantics = File.Exists(semanticsPath) ? ReadRequired<RawSemantics>(semanticsPath) : new RawSemantics();
            RawScenes scenes = File.Exists(scenesPath) ? ReadRequired<RawScenes>(scenesPath) : new RawScenes();
            ModuleManifest convertedManifest = ConvertManifest(manifest);
            ModuleSettingsSchema? settingsSchema = LoadSettingsSchema(fullDirectory, manifest);
            ModuleWritingDefinitions writing = File.Exists(writingPath)
                ? ModuleWritingProfile.Parse(File.ReadAllText(writingPath), convertedManifest.Id)
                : ModuleWritingProfile.Empty;
            if (settingsSchema is not null)
            {
                if (convertedManifest.Parameters.Count > 0)
                    throw Invalid(nameof(Load), $"Module {convertedManifest.Id} 不能同时声明 parameters 和 settings.schema。");
                convertedManifest = convertedManifest with
                {
                    Parameters = settingsSchema.Settings.Select(ToLegacyParameter).ToArray()
                };
            }
            settingsSchema ??= convertedManifest.Parameters.Count == 0
                ? ModuleSettingsProfile.Empty
                : ModuleSettingsProfile.FromParameters(convertedManifest.Parameters);
            return new ModulePackageDefinition
            {
                Manifest = convertedManifest,
                Semantics = ConvertSemantics(semantics),
                Scenes = scenes.Scenes.Select(scene => ConvertScene(scene, convertedManifest)).ToArray(),
                SettingsSchema = settingsSchema,
                Writing = writing
            };
        }
        catch (ModuleConfigurationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException)
        {
            throw Invalid(nameof(Load), $"无法加载 Module 目录 {fullDirectory}：{exception.Message}", exception);
        }
    }

    /// <summary>按目录名称稳定排序并读取父目录中的全部直接子 Module。</summary>
    public static IReadOnlyList<ModulePackageDefinition> LoadAll(string modulesDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modulesDirectory);
        string fullDirectory = Path.GetFullPath(modulesDirectory);
        if (!Directory.Exists(fullDirectory))
            throw Invalid(nameof(LoadAll), $"Module 根目录不存在：{fullDirectory}。");
        return Directory.GetDirectories(fullDirectory).OrderBy(path => path, StringComparer.Ordinal).Where(path => File.Exists(Path.Combine(path, "module.json"))).Select(Load).ToArray();
    }

    private static T ReadRequired<T>(string path)
    {
        if (!File.Exists(path))
            throw Invalid(nameof(Load), $"缺少必需声明文件 {path}。");
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? throw Invalid(nameof(Load), $"声明文件 {path} 不能反序列化为空对象。");
    }

    private static ModuleManifest ConvertManifest(RawManifest raw)
    {
        var id = new ModuleId(Required(raw.Id, "module.id"));
        var version = new ModuleVersion(Required(raw.Version, "module.version"));
        if (raw.SchemaVersion != 1)
            throw Invalid(nameof(Load), $"Module {id} 使用不支持的 SchemaVersion {raw.SchemaVersion}。");
        return new ModuleManifest
        {
            Id = id,
            Version = version,
            SchemaVersion = raw.SchemaVersion,
            Name = Required(raw.Name, "module.name"),
            Description = raw.Description ?? string.Empty,
            Dependencies = raw.Dependencies.Select(dependency => new ModuleDependency { Id = new ModuleId(Required(dependency.Id, "dependency.id")), MinimumVersion = new ModuleVersion(Required(dependency.MinimumVersion, "dependency.minimumVersion")) }).ToArray(),
            Parameters = raw.Parameters.Select(parameter => new ModuleParameterDefinition
            {
                Key = Required(parameter.Key, "parameter.key"),
                Name = Required(parameter.Name, "parameter.name"),
                Description = parameter.Description ?? string.Empty,
                Type = Enum.TryParse(parameter.Type, true, out ModuleParameterType type) ? type : throw Invalid(nameof(Load), $"Module 参数 {parameter.Key} 的类型 {parameter.Type} 无效。"),
                DefaultValue = Required(parameter.DefaultValue, "parameter.defaultValue"),
                AllowedValues = parameter.AllowedValues.ToArray(),
                Minimum = parameter.Minimum,
                Maximum = parameter.Maximum,
                ApplyMode = ModuleSettingApplyMode.ProcessRestart
            }).ToArray()
        };
    }

    private static ModuleSettingsSchema? LoadSettingsSchema(string moduleDirectory, RawManifest manifest)
    {
        if (manifest.Settings is null)
            return null;
        string relativePath = Required(manifest.Settings.Schema, "module.settings.schema");
        if (Path.IsPathRooted(relativePath))
            throw Invalid(nameof(Load), "Module Setting Schema 必须使用 Module 目录内的相对路径。");
        string fullPath = Path.GetFullPath(Path.Combine(moduleDirectory, relativePath));
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(moduleDirectory));
        if (!fullPath.StartsWith($"{root}{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            throw Invalid(nameof(Load), "Module Setting Schema 不能位于 Module 目录之外。");
        if (!File.Exists(fullPath))
            throw Invalid(nameof(Load), $"缺少 Module Setting Schema {fullPath}。");
        return ModuleSettingsProfile.Parse(File.ReadAllText(fullPath));
    }

    private static ModuleParameterDefinition ToLegacyParameter(ModuleSettingDefinition setting) => new()
    {
        Key = setting.Key,
        Name = setting.Name,
        Description = setting.Description,
        Type = setting.Type switch
        {
            ModuleSettingValueType.Boolean => ModuleParameterType.Boolean,
            ModuleSettingValueType.Integer => ModuleParameterType.Integer,
            ModuleSettingValueType.Number => ModuleParameterType.Number,
            ModuleSettingValueType.String => ModuleParameterType.String,
            _ => throw new ArgumentOutOfRangeException(nameof(setting))
        },
        DefaultValue = ToLegacyText(setting.Type, setting.DefaultValue),
        AllowedValues = setting.AllowedValues.Select(value => ToLegacyText(setting.Type, value)).ToArray(),
        Minimum = setting.Minimum,
        Maximum = setting.Maximum,
        MinimumLength = setting.MinimumLength,
        MaximumLength = setting.MaximumLength,
        ApplyMode = setting.ApplyMode
    };

    private static string ToLegacyText(ModuleSettingValueType type, JsonElement value) => type switch
    {
        ModuleSettingValueType.Boolean => value.GetBoolean() ? "true" : "false",
        ModuleSettingValueType.Integer => value.TryGetInt64(out long integer)
            ? integer.ToString(CultureInfo.InvariantCulture)
            : value.GetDecimal().ToString(CultureInfo.InvariantCulture),
        ModuleSettingValueType.Number => value.GetDouble().ToString("R", CultureInfo.InvariantCulture),
        ModuleSettingValueType.String => value.GetString()!,
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    private static SemanticModuleDefinition ConvertSemantics(RawSemantics raw) => new()
    {
        ElementTypes = raw.ElementTypes.Select(value => new ElementTypeDefinition { Key = Key(value.Key), Name = Required(value.Name, "elementType.name"), Description = Required(value.Description, "elementType.description"), Tags = Keys(value.Tags) }).ToArray(),
        ScopeTypes = raw.ScopeTypes.Select(value => new ScopeTypeDefinition { Key = Key(value.Key), Name = Required(value.Name, "scopeType.name"), Description = Required(value.Description, "scopeType.description"), OwnerElementTypes = Keys(value.OwnerElementTypes), Tags = Keys(value.Tags) }).ToArray(),
        AspectGroups = raw.AspectGroups.Select(value => new AspectGroupDefinition { Key = Key(value.Key), Name = Required(value.Name, "aspectGroup.name"), Extensible = value.Extensible }).ToArray(),
        AspectTypes = raw.AspectTypes.Select(value => new AspectTypeDefinition { Key = Key(value.Key), Name = Required(value.Name, "aspectType.name"), Description = Required(value.Description, "aspectType.description"), Group = string.IsNullOrWhiteSpace(value.Group) ? null : Key(value.Group), SubjectElementTypes = Keys(value.SubjectElementTypes), MinimumQuantity = value.MinimumQuantity, MaximumQuantity = value.MaximumQuantity, Tags = Keys(value.Tags) }).ToArray(),
        RelationTypes = raw.RelationTypes.Select(value => new RelationTypeDefinition { Key = Key(value.Key), Name = Required(value.Name, "relationType.name"), Description = Required(value.Description, "relationType.description"), SourceElementTypes = Keys(value.SourceElementTypes), TargetElementTypes = Keys(value.TargetElementTypes), MinimumQuantity = value.MinimumQuantity, MaximumQuantity = value.MaximumQuantity, Tags = Keys(value.Tags) }).ToArray(),
        Constraints = raw.Constraints.Select(ConvertConstraint).ToArray()
    };

    private static SemanticConstraintDefinition ConvertConstraint(RawConstraint value) => new()
    {
        Key = Key(value.Key),
        Kind = value.Kind switch
        {
            "ownedScopeCardinality" => SemanticConstraintKind.OwnedScopeCardinality,
            "aspectGroupCardinality" => SemanticConstraintKind.AspectGroupCardinality,
            _ => throw Invalid(nameof(Load), $"不支持的语义约束种类 {value.Kind}。")
        },
        SubjectElementType = Key(value.SubjectElementType),
        ScopeType = Key(value.ScopeType),
        AspectGroup = string.IsNullOrWhiteSpace(value.AspectGroup) ? null : Key(value.AspectGroup),
        Minimum = value.Minimum,
        Maximum = value.Maximum,
        AspectMustTargetScopeOwner = value.AspectMustTargetScopeOwner,
        Message = value.Message ?? string.Empty
    };

    private static SceneDefinition ConvertScene(RawScene raw, ModuleManifest manifest) => new()
    {
        Id = Key(raw.Id),
        Module = manifest.Id,
        ModuleVersion = manifest.Version,
        Name = Required(raw.Name, "scene.name"),
        Description = raw.Description ?? string.Empty,
        SettlementCapabilities = ConvertCapabilities(raw.Settlement),
        Slots = raw.Slots.Select(slot => new SceneSlotDefinition
        {
            Id = Required(slot.Id, "scene.slot.id"),
            Name = Required(slot.Name, "scene.slot.name"),
            Description = slot.Description ?? string.Empty,
            Minimum = slot.Minimum,
            Maximum = slot.Maximum,
            Requirement = new SceneSlotRequirement { ElementTypes = Keys(slot.ElementTypes), RequiredAspectGroups = Keys(slot.RequiredAspectGroups) }
        }).ToArray()
    };

    private static SceneSettlementCapabilities ConvertCapabilities(IEnumerable<string> values)
    {
        SceneSettlementCapabilities result = SceneSettlementCapabilities.None;
        foreach (string value in values)
        {
            result |= value switch
            {
                "rules" => SceneSettlementCapabilities.Rules,
                "performance" => SceneSettlementCapabilities.Performance,
                "writing" => SceneSettlementCapabilities.Performance,
                _ => throw Invalid(nameof(Load), $"不支持的 Scene 结算能力 {value}。")
            };
        }
        return result;
    }

    private static HashSet<SemanticKey> Keys(IEnumerable<string> values) => values.Select(Key).ToHashSet();

    private static SemanticKey Key(string? value) => new(Required(value, "semantic.key"));

    private static string Required(string? value, string path) => string.IsNullOrWhiteSpace(value) ? throw Invalid(nameof(Load), $"{path} 不能为空。") : value;

    private static ModuleConfigurationException Invalid(string operation, string message, Exception? innerException = null) => new($"TAVI.EXTENSIONS.LOADING.{operation.ToUpperInvariant()}", message, innerException);

    private sealed class RawManifest
    {
        public string? Id { get; set; }
        public string? Version { get; set; }
        public int SchemaVersion { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? Entrypoint { get; set; }
        public List<RawDependency> Dependencies { get; set; } = [];
        public List<RawParameter> Parameters { get; set; } = [];
        public RawSettings? Settings { get; set; }
    }

    private sealed class RawSettings
    {
        public string? Schema { get; set; }
    }

    private sealed class RawDependency
    {
        public string? Id { get; set; }
        public string? MinimumVersion { get; set; }
    }

    private sealed class RawParameter
    {
        public string? Key { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? Type { get; set; }
        public string? DefaultValue { get; set; }
        public List<string> AllowedValues { get; set; } = [];
        public double? Minimum { get; set; }
        public double? Maximum { get; set; }
    }

    private sealed class RawSemantics
    {
        public List<RawElementType> ElementTypes { get; set; } = [];
        public List<RawScopeType> ScopeTypes { get; set; } = [];
        public List<RawAspectGroup> AspectGroups { get; set; } = [];
        public List<RawAspectType> AspectTypes { get; set; } = [];
        public List<RawRelationType> RelationTypes { get; set; } = [];
        public List<RawConstraint> Constraints { get; set; } = [];
    }

    private abstract class RawType
    {
        public string? Key { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public List<string> Tags { get; set; } = [];
    }

    private sealed class RawElementType : RawType;

    private sealed class RawScopeType : RawType
    {
        public List<string> OwnerElementTypes { get; set; } = [];
    }

    private sealed class RawAspectGroup
    {
        public string? Key { get; set; }
        public string? Name { get; set; }
        public bool Extensible { get; set; }
    }

    private sealed class RawAspectType : RawType
    {
        public string? Group { get; set; }
        public List<string> SubjectElementTypes { get; set; } = [];
        public int? MinimumQuantity { get; set; }
        public int? MaximumQuantity { get; set; }
    }

    private sealed class RawRelationType : RawType
    {
        public List<string> SourceElementTypes { get; set; } = [];
        public List<string> TargetElementTypes { get; set; } = [];
        public int? MinimumQuantity { get; set; }
        public int? MaximumQuantity { get; set; }
    }

    private sealed class RawConstraint
    {
        public string? Key { get; set; }
        public string? Kind { get; set; }
        public string? SubjectElementType { get; set; }
        public string? ScopeType { get; set; }
        public string? AspectGroup { get; set; }
        public int Minimum { get; set; }
        public int? Maximum { get; set; }
        public bool AspectMustTargetScopeOwner { get; set; }
        public string? Message { get; set; }
    }

    private sealed class RawScenes
    {
        public List<RawScene> Scenes { get; set; } = [];
    }

    private sealed class RawScene
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public List<string> Settlement { get; set; } = [];
        public List<RawSlot> Slots { get; set; } = [];
    }

    private sealed class RawSlot
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public int Minimum { get; set; }
        public int? Maximum { get; set; }
        public List<string> ElementTypes { get; set; } = [];
        public List<string> RequiredAspectGroups { get; set; } = [];
    }
}
