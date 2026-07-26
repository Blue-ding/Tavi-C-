using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tavi.Extensibility;

/// <summary>指定声明式 Writing Binding 的数据来源。</summary>
public enum WritingBindingSource
{
    BeatSlot,
    Aspect,
    Parameter,
    Resolution
}

/// <summary>指定声明式 Writing Binding 期望解析出的值数量。</summary>
public enum WritingBindingCardinality
{
    One,
    Optional,
    Many
}

/// <summary>描述一个模板名称如何映射到 Performance EARS、Module Setting 或解决结果。</summary>
public sealed record WritingBindingDefinition
{
    public required WritingBindingSource Source { get; init; }
    public string? Slot { get; init; }
    public SemanticKey? Type { get; init; }
    public string? Member { get; init; }
    public string? Key { get; init; }
    public WritingBindingCardinality Cardinality { get; init; } = WritingBindingCardinality.One;
}

/// <summary>描述一个必须由语言模型填充的模板字段。</summary>
public sealed record WritingModelFillDefinition
{
    public required string Instruction { get; init; }
    public IReadOnlyList<string> Context { get; init; } = [];
    public int? MinimumLength { get; init; }
    public int? MaximumLength { get; init; }
}

/// <summary>描述一个由确定性 Binding 与可选模型 Fill 渲染的 Paragraph。</summary>
public sealed record WritingParagraphDefinition
{
    public required string Key { get; init; }
    public required string Template { get; init; }
    public IReadOnlyDictionary<string, WritingBindingDefinition> Bindings { get; init; } =
        new ReadOnlyDictionary<string, WritingBindingDefinition>(
            new Dictionary<string, WritingBindingDefinition>(StringComparer.Ordinal));
    public IReadOnlyDictionary<string, WritingModelFillDefinition> Fills { get; init; } =
        new ReadOnlyDictionary<string, WritingModelFillDefinition>(
            new Dictionary<string, WritingModelFillDefinition>(StringComparer.Ordinal));
}

/// <summary>描述一个 BeatDefinition 对应的全部声明式 Paragraph。</summary>
public sealed record BeatNarrationDefinition
{
    public required SemanticKey BeatDefinition { get; init; }
    public IReadOnlyList<WritingParagraphDefinition> Paragraphs { get; init; } = [];
}

/// <summary>保存一份已校验并编译的 Module Writing 声明。</summary>
public sealed class ModuleWritingDefinitions
{
    private readonly JsonElement _document;
    private readonly IReadOnlyDictionary<SemanticKey, BeatNarrationDefinition> _narrations;

    internal ModuleWritingDefinitions(
        JsonElement document,
        IReadOnlyList<BeatNarrationDefinition> narrations)
    {
        _document = document.Clone();
        Narrations = Array.AsReadOnly(narrations.Select(ModuleWritingProfile.Copy).ToArray());
        _narrations = new ReadOnlyDictionary<SemanticKey, BeatNarrationDefinition>(
            Narrations.ToDictionary(value => value.BeatDefinition, ModuleWritingProfile.Copy));
    }

    /// <summary>获取经过验证的原始声明独立副本。</summary>
    public JsonElement Document => _document.Clone();

    /// <summary>获取按声明顺序排列的 Beat Narration。</summary>
    public IReadOnlyList<BeatNarrationDefinition> Narrations { get; }

    /// <summary>查找指定 BeatDefinition 的声明；不存在时返回 null。</summary>
    public BeatNarrationDefinition? Find(SemanticKey beatDefinition) =>
        _narrations.TryGetValue(beatDefinition, out BeatNarrationDefinition? value)
            ? ModuleWritingProfile.Copy(value)
            : null;
}

/// <summary>解析并严格校验 Tavi 声明式 Writing Profile。</summary>
public static partial class ModuleWritingProfile
{
    private static readonly HashSet<string> RootProperties =
        ["$schema", "schemaVersion", "beatNarrations"];
    private static readonly HashSet<string> NarrationProperties =
        ["beatDefinition", "paragraphs"];
    private static readonly HashSet<string> ParagraphProperties =
        ["key", "template", "bindings", "fills"];
    private static readonly HashSet<string> BindingProperties =
        ["source", "slot", "type", "member", "key", "cardinality"];
    private static readonly HashSet<string> FillProperties =
        ["kind", "instruction", "context", "minimumLength", "maximumLength"];

    /// <summary>获取不声明任何 Paragraph 的有效空 Profile。</summary>
    public static ModuleWritingDefinitions Empty { get; } =
        Parse(
            JsonSerializer.Deserialize<JsonElement>(
                """{"schemaVersion":1,"beatNarrations":[]}"""),
            (ModuleId?)null);

    /// <summary>解析指定 Module 的 Writing 声明。</summary>
    public static ModuleWritingDefinitions Parse(string json, ModuleId module)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using JsonDocument document = JsonDocument.Parse(json);
        return Parse(document.RootElement, module);
    }

    /// <summary>解析指定 Module 的 Writing 声明。</summary>
    public static ModuleWritingDefinitions Parse(JsonElement document, ModuleId module) =>
        Parse(document, (ModuleId?)module);

    internal static BeatNarrationDefinition Copy(BeatNarrationDefinition source) =>
        source with
        {
            Paragraphs = Array.AsReadOnly(source.Paragraphs.Select(Copy).ToArray())
        };

    internal static WritingParagraphDefinition Copy(WritingParagraphDefinition source) =>
        source with
        {
            Bindings = new ReadOnlyDictionary<string, WritingBindingDefinition>(
                source.Bindings.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value with { },
                    StringComparer.Ordinal)),
            Fills = new ReadOnlyDictionary<string, WritingModelFillDefinition>(
                source.Fills.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value with
                    {
                        Context = Array.AsReadOnly(pair.Value.Context.ToArray())
                    },
                    StringComparer.Ordinal))
        };

    private static ModuleWritingDefinitions Parse(JsonElement document, ModuleId? module)
    {
        RequireKind(document, JsonValueKind.Object, "$");
        RejectUnknown(document, RootProperties, "$");
        int version = RequiredInteger(document, "schemaVersion", "$");
        if (version != 1)
            throw Invalid("$.schemaVersion", $"不支持 Writing Profile 版本 {version}。");
        JsonElement narrations = Required(document, "beatNarrations", JsonValueKind.Array, "$");
        var compiled = new List<BeatNarrationDefinition>();
        int narrationIndex = 0;
        foreach (JsonElement narration in narrations.EnumerateArray())
        {
            string path = $"$.beatNarrations[{narrationIndex++}]";
            compiled.Add(ParseNarration(narration, module, path));
        }
        SemanticKey? duplicate = compiled
            .GroupBy(value => value.BeatDefinition)
            .FirstOrDefault(group => group.Count() > 1)?.Key;
        if (duplicate.HasValue)
            throw Invalid("$.beatNarrations", $"BeatDefinition {duplicate.Value} 重复声明。");
        return new ModuleWritingDefinitions(document, compiled);
    }

    private static BeatNarrationDefinition ParseNarration(
        JsonElement value,
        ModuleId? module,
        string path)
    {
        RequireKind(value, JsonValueKind.Object, path);
        RejectUnknown(value, NarrationProperties, path);
        var beatDefinition = new SemanticKey(RequiredString(value, "beatDefinition", path));
        if (module.HasValue && beatDefinition.Namespace != module.Value)
            throw Invalid($"{path}.beatDefinition", $"BeatDefinition {beatDefinition} 不属于 Module {module.Value}。");
        JsonElement paragraphs = Required(value, "paragraphs", JsonValueKind.Array, path);
        if (paragraphs.GetArrayLength() == 0)
            throw Invalid($"{path}.paragraphs", "至少必须声明一个 Paragraph。");
        WritingParagraphDefinition[] compiled = paragraphs.EnumerateArray()
            .Select((paragraph, index) => ParseParagraph(paragraph, $"{path}.paragraphs[{index}]"))
            .ToArray();
        string? duplicate = compiled
            .GroupBy(paragraph => paragraph.Key, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)?.Key;
        if (duplicate is not null)
            throw Invalid($"{path}.paragraphs", $"Paragraph key {duplicate} 重复声明。");
        return new BeatNarrationDefinition
        {
            BeatDefinition = beatDefinition,
            Paragraphs = Array.AsReadOnly(compiled)
        };
    }

    private static WritingParagraphDefinition ParseParagraph(JsonElement value, string path)
    {
        RequireKind(value, JsonValueKind.Object, path);
        RejectUnknown(value, ParagraphProperties, path);
        string key = RequiredIdentifier(value, "key", path);
        string template = RequiredString(value, "template", path);
        IReadOnlyDictionary<string, WritingBindingDefinition> bindings =
            ParseMap(value, "bindings", path, ParseBinding);
        IReadOnlyDictionary<string, WritingModelFillDefinition> fills =
            ParseMap(value, "fills", path, ParseFill);
        string[] placeholders = PlaceholderPattern().Matches(template)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        foreach (string placeholder in placeholders)
        {
            string root = placeholder.Split('.')[0];
            if (!bindings.ContainsKey(root) && !fills.ContainsKey(root))
                throw Invalid($"{path}.template", $"占位符 {placeholder} 没有对应 Binding 或 Fill。");
        }
        string? unusedBinding = bindings.Keys.FirstOrDefault(binding =>
            !placeholders.Any(placeholder =>
                placeholder == binding ||
                placeholder.StartsWith($"{binding}.", StringComparison.Ordinal)));
        if (unusedBinding is not null)
            throw Invalid($"{path}.bindings.{unusedBinding}", "Binding 未被模板使用。");
        string? unusedFill = fills.Keys.FirstOrDefault(fill =>
            !placeholders.Contains(fill, StringComparer.Ordinal));
        if (unusedFill is not null)
            throw Invalid($"{path}.fills.{unusedFill}", "Fill 未被模板使用。");
        foreach ((string fillKey, WritingModelFillDefinition fill) in fills)
        {
            string? unknownContext = fill.Context.FirstOrDefault(reference =>
                reference != "interaction" &&
                !bindings.ContainsKey(reference.Split('.')[0]));
            if (unknownContext is not null)
                throw Invalid(
                    $"{path}.fills.{fillKey}.context",
                    $"Context 引用 {unknownContext} 没有对应 Binding。");
            string? unknownInstruction = PlaceholderPattern()
                .Matches(fill.Instruction)
                .Select(match => match.Groups[1].Value)
                .FirstOrDefault(reference =>
                    !bindings.ContainsKey(reference.Split('.')[0]));
            if (unknownInstruction is not null)
                throw Invalid(
                    $"{path}.fills.{fillKey}.instruction",
                    $"Instruction 引用 {unknownInstruction} 没有对应 Binding。");
        }
        return new WritingParagraphDefinition
        {
            Key = key,
            Template = template,
            Bindings = bindings,
            Fills = fills
        };
    }

    private static WritingBindingDefinition ParseBinding(JsonElement value, string path)
    {
        RequireKind(value, JsonValueKind.Object, path);
        RejectUnknown(value, BindingProperties, path);
        WritingBindingSource source = RequiredString(value, "source", path) switch
        {
            "beatSlot" => WritingBindingSource.BeatSlot,
            "aspect" => WritingBindingSource.Aspect,
            "parameter" => WritingBindingSource.Parameter,
            "resolution" => WritingBindingSource.Resolution,
            string unknown => throw Invalid($"{path}.source", $"不支持 Binding source {unknown}。")
        };
        WritingBindingCardinality cardinality = OptionalString(value, "cardinality", path) switch
        {
            null or "one" => WritingBindingCardinality.One,
            "optional" => WritingBindingCardinality.Optional,
            "many" => WritingBindingCardinality.Many,
            string unknown => throw Invalid($"{path}.cardinality", $"不支持 Binding cardinality {unknown}。")
        };
        string? slot = OptionalIdentifier(value, "slot", path);
        string? key = OptionalIdentifier(value, "key", path);
        string? member = OptionalIdentifier(value, "member", path);
        SemanticKey? type = OptionalString(value, "type", path) is string typeText
            ? new SemanticKey(typeText)
            : null;
        switch (source)
        {
            case WritingBindingSource.BeatSlot when slot is null || key is not null || type.HasValue:
                throw Invalid(path, "beatSlot Binding 必须声明 slot，且不能声明 key/type。");
            case WritingBindingSource.Aspect when slot is null || !type.HasValue || key is not null:
                throw Invalid(path, "aspect Binding 必须声明 slot 和 type，且不能声明 key。");
            case WritingBindingSource.Parameter
                when key is null || slot is not null || type.HasValue || member is not null:
                throw Invalid(path, "parameter Binding 必须声明 key，且不能声明 slot/type/member。");
            case WritingBindingSource.Resolution
                when key is null || slot is not null || type.HasValue:
                throw Invalid(path, $"{source} Binding 必须声明 key，且不能声明 slot/type。");
        }
        return new WritingBindingDefinition
        {
            Source = source,
            Slot = slot,
            Type = type,
            Member = member,
            Key = key,
            Cardinality = cardinality
        };
    }

    private static WritingModelFillDefinition ParseFill(JsonElement value, string path)
    {
        RequireKind(value, JsonValueKind.Object, path);
        RejectUnknown(value, FillProperties, path);
        string kind = RequiredString(value, "kind", path);
        if (kind != "model")
            throw Invalid($"{path}.kind", $"不支持 Fill kind {kind}。");
        string instruction = RequiredString(value, "instruction", path);
        string[] context = value.TryGetProperty("context", out JsonElement contextValue)
            ? ReadStringArray(contextValue, $"{path}.context")
            : [];
        int? minimumLength = OptionalNonNegativeInteger(value, "minimumLength", path);
        int? maximumLength = OptionalNonNegativeInteger(value, "maximumLength", path);
        if (minimumLength > maximumLength)
            throw Invalid(path, "minimumLength 不能大于 maximumLength。");
        return new WritingModelFillDefinition
        {
            Instruction = instruction,
            Context = Array.AsReadOnly(context),
            MinimumLength = minimumLength,
            MaximumLength = maximumLength
        };
    }

    private static IReadOnlyDictionary<string, T> ParseMap<T>(
        JsonElement parent,
        string name,
        string path,
        Func<JsonElement, string, T> parse)
    {
        if (!parent.TryGetProperty(name, out JsonElement value))
            return new ReadOnlyDictionary<string, T>(
                new Dictionary<string, T>(StringComparer.Ordinal));
        RequireKind(value, JsonValueKind.Object, $"{path}.{name}");
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (JsonProperty property in value.EnumerateObject())
        {
            ValidateIdentifier(property.Name, $"{path}.{name}.{property.Name}");
            result.Add(property.Name, parse(property.Value, $"{path}.{name}.{property.Name}"));
        }
        return new ReadOnlyDictionary<string, T>(result);
    }

    private static string[] ReadStringArray(JsonElement value, string path)
    {
        RequireKind(value, JsonValueKind.Array, path);
        string[] result = value.EnumerateArray().Select((item, index) =>
        {
            if (item.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(item.GetString()))
                throw Invalid($"{path}[{index}]", "Context 引用必须是非空字符串。");
            string reference = item.GetString()!;
            if (!ReferencePattern().IsMatch(reference))
                throw Invalid($"{path}[{index}]", $"Context 引用 {reference} 无效。");
            return reference;
        }).ToArray();
        if (result.Distinct(StringComparer.Ordinal).Count() != result.Length)
            throw Invalid(path, "Context 引用不能重复。");
        return result;
    }

    private static JsonElement Required(
        JsonElement value,
        string name,
        JsonValueKind kind,
        string path)
    {
        if (!value.TryGetProperty(name, out JsonElement property))
            throw Invalid($"{path}.{name}", "缺少必需属性。");
        RequireKind(property, kind, $"{path}.{name}");
        return property;
    }

    private static string RequiredString(JsonElement value, string name, string path)
    {
        if (!value.TryGetProperty(name, out JsonElement property) ||
            property.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(property.GetString()))
            throw Invalid($"{path}.{name}", $"{name} 必须是非空字符串。");
        return property.GetString()!;
    }

    private static string RequiredIdentifier(JsonElement value, string name, string path)
    {
        string result = RequiredString(value, name, path);
        ValidateIdentifier(result, $"{path}.{name}");
        return result;
    }

    private static string? OptionalIdentifier(JsonElement value, string name, string path)
    {
        string? result = OptionalString(value, name, path);
        if (result is not null)
            ValidateIdentifier(result, $"{path}.{name}");
        return result;
    }

    private static string? OptionalString(JsonElement value, string name, string path)
    {
        if (!value.TryGetProperty(name, out JsonElement property))
            return null;
        if (property.ValueKind != JsonValueKind.String)
            throw Invalid($"{path}.{name}", $"{name} 必须是字符串。");
        return property.GetString();
    }

    private static int RequiredInteger(JsonElement value, string name, string path)
    {
        if (!value.TryGetProperty(name, out JsonElement property) ||
            !property.TryGetInt32(out int result))
            throw Invalid($"{path}.{name}", $"{name} 必须是 Int32 整数。");
        return result;
    }

    private static int? OptionalNonNegativeInteger(
        JsonElement value,
        string name,
        string path)
    {
        if (!value.TryGetProperty(name, out JsonElement property))
            return null;
        if (!property.TryGetInt32(out int result) || result < 0)
            throw Invalid($"{path}.{name}", $"{name} 必须是非负整数。");
        return result;
    }

    private static void ValidateIdentifier(string value, string path)
    {
        if (!IdentifierPattern().IsMatch(value))
            throw Invalid(path, $"标识 {value} 无效。");
    }

    private static void RejectUnknown(
        JsonElement value,
        IReadOnlySet<string> supported,
        string path)
    {
        string? unknown = value.EnumerateObject()
            .Select(property => property.Name)
            .FirstOrDefault(name => !supported.Contains(name));
        if (unknown is not null)
            throw Invalid($"{path}.{unknown}", $"Writing Profile 不支持属性 {unknown}。");
    }

    private static void RequireKind(JsonElement value, JsonValueKind kind, string path)
    {
        if (value.ValueKind != kind)
            throw Invalid(path, $"必须是 JSON {kind.ToString().ToLowerInvariant()}。");
    }

    private static ModuleConfigurationException Invalid(string path, string message) =>
        new("TAVI.EXTENSIONS.WRITING_PROFILE.INVALID", $"{path}: {message}");

    [GeneratedRegex(@"\{\{\s*([A-Za-z][A-Za-z0-9_.-]*)\s*\}\}")]
    private static partial Regex PlaceholderPattern();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_-]*$")]
    private static partial Regex IdentifierPattern();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_.-]*$")]
    private static partial Regex ReferencePattern();
}
