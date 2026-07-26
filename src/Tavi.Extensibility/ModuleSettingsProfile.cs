using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tavi.Extensibility;

/// <summary>指定 Module Setting 值在何时进入活动运行配置。</summary>
public enum ModuleSettingApplyMode
{
    /// <summary>更新完成后启动的新 Module 事务使用新值；已开始事务保持原快照。</summary>
    TransactionBoundary,

    /// <summary>值只进入期望配置，并在下次进程启动时生效。</summary>
    ProcessRestart
}

/// <summary>指定 Tavi Module Setting Profile 支持的 JSON 标量类型。</summary>
public enum ModuleSettingValueType
{
    Boolean,
    Integer,
    Number,
    String
}

/// <summary>表示一项经过 Tavi Profile 校验和规范化的 Module Setting 声明。</summary>
public sealed record ModuleSettingDefinition
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = string.Empty;
    public required ModuleSettingValueType Type { get; init; }
    public required JsonElement DefaultValue { get; init; }
    public IReadOnlyList<JsonElement> AllowedValues { get; init; } = [];
    public double? Minimum { get; init; }
    public double? Maximum { get; init; }
    public int? MinimumLength { get; init; }
    public int? MaximumLength { get; init; }
    public ModuleSettingApplyMode ApplyMode { get; init; } = ModuleSettingApplyMode.ProcessRestart;
}

/// <summary>
/// 保存一份不可变的 Module Setting Schema 及其编译声明，并负责校验和物化完整 Setting 文档。
/// </summary>
public sealed class ModuleSettingsSchema
{
    private readonly JsonElement _document;
    private readonly IReadOnlyList<ModuleSettingDefinition> _settings;
    private readonly IReadOnlyDictionary<string, ModuleSettingDefinition> _settingsByKey;

    internal ModuleSettingsSchema(JsonElement document, IReadOnlyList<ModuleSettingDefinition> settings)
    {
        _document = document.Clone();
        _settings = Array.AsReadOnly(settings.Select(ModuleSettingsProfile.Copy).ToArray());
        _settingsByKey = new ReadOnlyDictionary<string, ModuleSettingDefinition>(
            _settings.ToDictionary(setting => setting.Key, StringComparer.Ordinal));
    }

    /// <summary>获取经过验证的原始 JSON Schema 独立副本。</summary>
    public JsonElement Document => _document.Clone();

    /// <summary>获取按 Schema 声明顺序排列的 Setting 定义。</summary>
    public IReadOnlyList<ModuleSettingDefinition> Settings => _settings;

    /// <summary>查找指定 Setting 声明；不存在时返回 null。</summary>
    public ModuleSettingDefinition? Find(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return _settingsByKey.GetValueOrDefault(key);
    }

    /// <summary>校验输入并物化默认值，返回只包含已声明属性的完整规范 JSON 对象。</summary>
    public JsonElement Materialize(JsonElement? value = null) =>
        ModuleSettingsProfile.Materialize(this, value);
}

/// <summary>
/// 定义 Tavi 支持的 JSON Schema 2020-12 子集。
/// Profile 仅接受适合稳定配置表单和事务快照的顶层标量 Setting。
/// </summary>
public static class ModuleSettingsProfile
{
    public const string JsonSchemaDialect = "https://json-schema.org/draft/2020-12/schema";
    public const string ApplyModeKeyword = "x-tavi-apply";

    private static readonly HashSet<string> RootKeywords =
    [
        "$schema", "$id", "title", "description", "type",
        "additionalProperties", "properties", "required"
    ];

    private static readonly HashSet<string> PropertyKeywords =
    [
        "type", "title", "description", "default", "enum",
        "minimum", "maximum", "minLength", "maxLength", ApplyModeKeyword
    ];

    /// <summary>解析并校验一份 Tavi Module Setting Schema。</summary>
    public static ModuleSettingsSchema Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using JsonDocument document = JsonDocument.Parse(json);
        return Parse(document.RootElement);
    }

    /// <summary>校验 JSON Schema 元素并编译为不可变 Setting 声明。</summary>
    public static ModuleSettingsSchema Parse(JsonElement schema)
    {
        RequireKind(schema, JsonValueKind.Object, "$");
        RejectUnknownKeywords(schema, RootKeywords, "$");
        if (schema.TryGetProperty("$schema", out JsonElement dialect) &&
            (dialect.ValueKind != JsonValueKind.String ||
             dialect.GetString() != JsonSchemaDialect))
        {
            throw Invalid("$.$schema", $"只支持 JSON Schema 方言 {JsonSchemaDialect}。");
        }

        if (!schema.TryGetProperty("type", out JsonElement rootType) ||
            rootType.ValueKind != JsonValueKind.String ||
            rootType.GetString() != "object")
        {
            throw Invalid("$.type", "Module Setting Schema 根节点必须声明 type=object。");
        }

        if (!schema.TryGetProperty("additionalProperties", out JsonElement additional) ||
            additional.ValueKind is not JsonValueKind.False)
        {
            throw Invalid("$.additionalProperties", "Module Setting Schema 必须声明 additionalProperties=false。");
        }

        if (!schema.TryGetProperty("properties", out JsonElement properties))
            throw Invalid("$.properties", "Module Setting Schema 必须声明 properties。");
        RequireKind(properties, JsonValueKind.Object, "$.properties");

        var definitions = new List<ModuleSettingDefinition>();
        foreach (JsonProperty property in properties.EnumerateObject())
            definitions.Add(ParseDefinition(property));

        ValidateRequired(schema, definitions);
        return new ModuleSettingsSchema(schema, definitions);
    }

    internal static ModuleSettingDefinition Copy(ModuleSettingDefinition source) => source with
    {
        DefaultValue = source.DefaultValue.Clone(),
        AllowedValues = Array.AsReadOnly(source.AllowedValues.Select(value => value.Clone()).ToArray())
    };

    internal static JsonElement Materialize(ModuleSettingsSchema schema, JsonElement? value)
    {
        JsonElement supplied = value ?? JsonSerializer.SerializeToElement(new { });
        RequireKind(supplied, JsonValueKind.Object, "$");

        HashSet<string> known = schema.Settings.Select(setting => setting.Key).ToHashSet(StringComparer.Ordinal);
        string? unknown = supplied.EnumerateObject().Select(property => property.Name).FirstOrDefault(name => !known.Contains(name));
        if (unknown is not null)
            throw Invalid($"$.{unknown}", $"未声明的 Module Setting {unknown}。");

        var result = new JsonObject();
        foreach (ModuleSettingDefinition definition in schema.Settings)
        {
            JsonElement selected = supplied.TryGetProperty(definition.Key, out JsonElement configured)
                ? configured
                : definition.DefaultValue;
            ValidateValue(definition, selected, $"$.{definition.Key}");
            result[definition.Key] = JsonNode.Parse(selected.GetRawText());
        }
        return JsonSerializer.SerializeToElement(result);
    }

    private static ModuleSettingDefinition ParseDefinition(JsonProperty property)
    {
        string path = $"$.properties.{property.Name}";
        ValidateKey(property.Name, path);
        RequireKind(property.Value, JsonValueKind.Object, path);
        RejectUnknownKeywords(property.Value, PropertyKeywords, path);

        ModuleSettingValueType type = ParseType(RequiredString(property.Value, "type", path), $"{path}.type");
        string name = OptionalString(property.Value, "title", path) ?? property.Name;
        if (string.IsNullOrWhiteSpace(name))
            throw Invalid($"{path}.title", "Setting title 不能为空。");
        string description = OptionalString(property.Value, "description", path) ?? string.Empty;

        if (!property.Value.TryGetProperty("default", out JsonElement defaultValue))
            throw Invalid($"{path}.default", "每个 Module Setting 都必须声明默认值。");

        double? minimum = OptionalNumber(property.Value, "minimum", path);
        double? maximum = OptionalNumber(property.Value, "maximum", path);
        int? minimumLength = OptionalNonNegativeInteger(property.Value, "minLength", path);
        int? maximumLength = OptionalNonNegativeInteger(property.Value, "maxLength", path);
        if (minimum > maximum)
            throw Invalid(path, "minimum 不能大于 maximum。");
        if (minimumLength > maximumLength)
            throw Invalid(path, "minLength 不能大于 maxLength。");
        if (type is not (ModuleSettingValueType.Integer or ModuleSettingValueType.Number) &&
            (minimum.HasValue || maximum.HasValue))
        {
            throw Invalid(path, "只有 integer 和 number Setting 可以声明 minimum/maximum。");
        }
        if (type is not ModuleSettingValueType.String &&
            (minimumLength.HasValue || maximumLength.HasValue))
        {
            throw Invalid(path, "只有 string Setting 可以声明 minLength/maxLength。");
        }

        IReadOnlyList<JsonElement> allowed = ParseAllowedValues(property.Value, path);
        ModuleSettingApplyMode applyMode = ParseApplyMode(
            OptionalString(property.Value, ApplyModeKeyword, path),
            $"{path}.{ApplyModeKeyword}");

        var definition = new ModuleSettingDefinition
        {
            Key = property.Name,
            Name = name,
            Description = description,
            Type = type,
            DefaultValue = defaultValue.Clone(),
            AllowedValues = allowed,
            Minimum = minimum,
            Maximum = maximum,
            MinimumLength = minimumLength,
            MaximumLength = maximumLength,
            ApplyMode = applyMode
        };
        for (int index = 0; index < definition.AllowedValues.Count; index++)
            ValidateValue(definition, definition.AllowedValues[index], $"{path}.enum[{index}]");
        ValidateValue(definition, definition.DefaultValue, $"{path}.default");
        return definition;
    }

    private static IReadOnlyList<JsonElement> ParseAllowedValues(JsonElement schema, string path)
    {
        if (!schema.TryGetProperty("enum", out JsonElement values))
            return [];
        RequireKind(values, JsonValueKind.Array, $"{path}.enum");
        JsonElement[] copied = values.EnumerateArray().Select(value => value.Clone()).ToArray();
        if (copied.Length == 0)
            throw Invalid($"{path}.enum", "enum 不能为空。");
        for (int index = 0; index < copied.Length; index++)
        {
            if (copied.Take(index).Any(existing => JsonValuesEqual(existing, copied[index])))
                throw Invalid($"{path}.enum", "enum 不能包含重复值。");
        }
        return Array.AsReadOnly(copied);
    }

    private static void ValidateRequired(JsonElement schema, IReadOnlyList<ModuleSettingDefinition> definitions)
    {
        if (!schema.TryGetProperty("required", out JsonElement required))
            return;
        RequireKind(required, JsonValueKind.Array, "$.required");
        string[] names = required.EnumerateArray().Select((value, index) =>
        {
            if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
                throw Invalid($"$.required[{index}]", "required 项必须是非空字符串。");
            return value.GetString()!;
        }).ToArray();
        if (names.Distinct(StringComparer.Ordinal).Count() != names.Length)
            throw Invalid("$.required", "required 不能包含重复属性。");
        string? unknown = names.FirstOrDefault(name => definitions.All(definition => definition.Key != name));
        if (unknown is not null)
            throw Invalid("$.required", $"required 引用了未声明 Setting {unknown}。");
    }

    private static void ValidateValue(ModuleSettingDefinition definition, JsonElement value, string path)
    {
        bool validType = definition.Type switch
        {
            ModuleSettingValueType.Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            ModuleSettingValueType.Integer => value.ValueKind == JsonValueKind.Number && IsInteger(value),
            ModuleSettingValueType.Number => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number) && double.IsFinite(number),
            ModuleSettingValueType.String => value.ValueKind == JsonValueKind.String,
            _ => false
        };
        if (!validType)
            throw Invalid(path, $"Setting {definition.Key} 的值不符合 {ToJsonType(definition.Type)} 类型。");

        if (value.ValueKind == JsonValueKind.Number)
        {
            double number = value.GetDouble();
            if (definition.Minimum is double minimum && number < minimum ||
                definition.Maximum is double maximum && number > maximum)
            {
                throw Invalid(path, $"Setting {definition.Key} 超出允许范围。");
            }
        }
        if (value.ValueKind == JsonValueKind.String)
        {
            int length = value.GetString()!.Length;
            if (definition.MinimumLength is int minimum && length < minimum ||
                definition.MaximumLength is int maximum && length > maximum)
            {
                throw Invalid(path, $"Setting {definition.Key} 的文本长度超出允许范围。");
            }
        }
        if (definition.AllowedValues.Count > 0 &&
            !definition.AllowedValues.Any(allowed => JsonValuesEqual(allowed, value)))
        {
            throw Invalid(path, $"Setting {definition.Key} 不接受指定值。");
        }
    }

    private static bool IsInteger(JsonElement value)
    {
        if (value.TryGetInt64(out _))
            return true;
        return value.TryGetDecimal(out decimal number) && decimal.Truncate(number) == number;
    }

    private static bool JsonValuesEqual(JsonElement left, JsonElement right)
    {
        if (left.ValueKind == JsonValueKind.Number && right.ValueKind == JsonValueKind.Number &&
            left.TryGetDecimal(out decimal leftNumber) && right.TryGetDecimal(out decimal rightNumber))
        {
            return leftNumber == rightNumber;
        }
        if (left.ValueKind != right.ValueKind)
            return false;
        return left.ValueKind switch
        {
            JsonValueKind.String => left.GetString() == right.GetString(),
            JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null => true,
            _ => left.GetRawText() == right.GetRawText()
        };
    }

    private static void RejectUnknownKeywords(JsonElement value, IReadOnlySet<string> allowed, string path)
    {
        string? unknown = value.EnumerateObject().Select(property => property.Name).FirstOrDefault(name => !allowed.Contains(name));
        if (unknown is not null)
            throw Invalid($"{path}.{unknown}", $"Tavi Module Setting Profile 不支持关键字 {unknown}。");
    }

    private static string RequiredString(JsonElement value, string name, string path)
    {
        if (!value.TryGetProperty(name, out JsonElement property) ||
            property.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw Invalid($"{path}.{name}", $"{name} 必须是非空字符串。");
        }
        return property.GetString()!;
    }

    private static string? OptionalString(JsonElement value, string name, string path)
    {
        if (!value.TryGetProperty(name, out JsonElement property))
            return null;
        if (property.ValueKind != JsonValueKind.String)
            throw Invalid($"{path}.{name}", $"{name} 必须是字符串。");
        return property.GetString();
    }

    private static double? OptionalNumber(JsonElement value, string name, string path)
    {
        if (!value.TryGetProperty(name, out JsonElement property))
            return null;
        if (property.ValueKind != JsonValueKind.Number ||
            !property.TryGetDouble(out double number) ||
            !double.IsFinite(number))
        {
            throw Invalid($"{path}.{name}", $"{name} 必须是有限数字。");
        }
        return number;
    }

    private static int? OptionalNonNegativeInteger(JsonElement value, string name, string path)
    {
        if (!value.TryGetProperty(name, out JsonElement property))
            return null;
        if (property.ValueKind != JsonValueKind.Number ||
            !property.TryGetInt32(out int number) ||
            number < 0)
        {
            throw Invalid($"{path}.{name}", $"{name} 必须是非负整数。");
        }
        return number;
    }

    private static ModuleSettingValueType ParseType(string value, string path) => value switch
    {
        "boolean" => ModuleSettingValueType.Boolean,
        "integer" => ModuleSettingValueType.Integer,
        "number" => ModuleSettingValueType.Number,
        "string" => ModuleSettingValueType.String,
        _ => throw Invalid(path, $"不支持 Setting 类型 {value}。")
    };

    private static ModuleSettingApplyMode ParseApplyMode(string? value, string path) => value switch
    {
        null or "process-restart" => ModuleSettingApplyMode.ProcessRestart,
        "transaction-boundary" => ModuleSettingApplyMode.TransactionBoundary,
        _ => throw Invalid(path, $"不支持 Setting 应用策略 {value}。")
    };

    private static string ToJsonType(ModuleSettingValueType value) => value switch
    {
        ModuleSettingValueType.Boolean => "boolean",
        ModuleSettingValueType.Integer => "integer",
        ModuleSettingValueType.Number => "number",
        ModuleSettingValueType.String => "string",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static void ValidateKey(string key, string path)
    {
        if (string.IsNullOrWhiteSpace(key) ||
            !char.IsAsciiLetterLower(key[0]) ||
            key.Any(character =>
                !char.IsAsciiLetterOrDigit(character) &&
                character is not '.' and not '_' and not '-'))
        {
            throw Invalid(path, $"Setting 键 {key} 无效。");
        }
    }

    private static void RequireKind(JsonElement value, JsonValueKind kind, string path)
    {
        if (value.ValueKind != kind)
            throw Invalid(path, $"必须是 JSON {kind.ToString().ToLowerInvariant()}。");
    }

    private static ModuleConfigurationException Invalid(string path, string message) =>
        new("TAVI.EXTENSIONS.SETTINGS_SCHEMA.INVALID", $"{path}: {message}");
}
