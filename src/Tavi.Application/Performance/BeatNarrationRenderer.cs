using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Tavi.Application.Extensions;
using Tavi.Application.LanguageModel;
using Tavi.Domain.Performance;
using Tavi.Extensibility;

namespace Tavi.Application.Performance;

/// <summary>
/// 将 Module 的直接 Paragraph 与声明式 Writing Profile 编译为最终 BeatParagraph。
/// LanguageModel 只用于显式声明的 model Fill。
/// </summary>
internal sealed partial class BeatNarrationRenderer
{
    private const string BaseInstruction =
        "你负责填充 Tavi Beat 正文中的指定字段。不得添加上下文没有提供的事实。每个字段只返回可直接嵌入正文的纯文本。";
    private readonly FrozenModuleRuntime _extensions;
    private readonly ILanguageModelService? _languageModels;

    internal BeatNarrationRenderer(
        FrozenModuleRuntime extensions,
        ILanguageModelService? languageModels)
    {
        _extensions = extensions ?? throw new ArgumentNullException(nameof(extensions));
        _languageModels = languageModels;
    }

    internal async Task<IReadOnlyList<BeatParagraph>> RenderAsync(
        PerformanceSnapshot performance,
        Beat beat,
        BeatResolutionProposal proposal,
        string interaction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(performance);
        ArgumentNullException.ThrowIfNull(beat);
        ArgumentNullException.ThrowIfNull(proposal);
        BeatParagraph[] direct = proposal.Paragraphs
            .Select(value => new BeatParagraph(value.Id, value.Text))
            .ToArray();
        var module = new ModuleId(beat.ModuleId);
        BeatNarrationDefinition? narration =
            string.IsNullOrWhiteSpace(beat.WritingProfileJson)
                ? _extensions.FindBeatNarration(module, beat.DefinitionId)
                : ModuleWritingProfile.Parse(beat.WritingProfileJson, module)
                    .Find(new SemanticKey(beat.DefinitionId.Value));
        if (narration is null)
            return Array.AsReadOnly(direct);

        var prepared = new List<PreparedParagraph>(narration.Paragraphs.Count);
        var requestedFills =
            new Dictionary<string, RequestedFill>(StringComparer.Ordinal);
        foreach (WritingParagraphDefinition paragraph in narration.Paragraphs)
        {
            Dictionary<string, JsonElement> bindings =
                ResolveBindings(performance, beat, proposal, paragraph);
            foreach ((string fillKey, WritingModelFillDefinition fill) in paragraph.Fills)
            {
                string modelKey = $"{paragraph.Key}.{fillKey}";
                if (!requestedFills.TryAdd(
                        modelKey,
                        new RequestedFill(
                            RenderKnownTemplate(fill.Instruction, bindings),
                            ResolveContext(fill.Context, bindings, interaction),
                            fill.MinimumLength,
                            fill.MaximumLength)))
                    throw new InvalidOperationException($"重复的 Beat Narration Fill {modelKey}。");
            }
            prepared.Add(new PreparedParagraph(paragraph, bindings));
        }

        IReadOnlyDictionary<string, string> generated =
            requestedFills.Count == 0
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : await GenerateFillsAsync(
                    requestedFills,
                    interaction,
                    cancellationToken);
        var rendered = new List<BeatParagraph>(direct.Length + prepared.Count);
        rendered.AddRange(direct);
        foreach (PreparedParagraph paragraph in prepared)
        {
            Dictionary<string, JsonElement> values =
                new(paragraph.Bindings, StringComparer.Ordinal);
            foreach (string fillKey in paragraph.Definition.Fills.Keys)
            {
                string modelKey = $"{paragraph.Definition.Key}.{fillKey}";
                values[fillKey] = JsonSerializer.SerializeToElement(
                    generated[modelKey]);
            }
            string text = RenderTemplate(paragraph.Definition.Template, values);
            rendered.Add(new BeatParagraph(
                CreateParagraphId(
                    performance.PerformanceId,
                    beat.Id,
                    paragraph.Definition.Key),
                text));
        }
        return Array.AsReadOnly(rendered.ToArray());
    }

    private Dictionary<string, JsonElement> ResolveBindings(
        PerformanceSnapshot performance,
        Beat beat,
        BeatResolutionProposal proposal,
        WritingParagraphDefinition paragraph)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach ((string name, WritingBindingDefinition binding) in paragraph.Bindings)
        {
            JsonElement value = binding.Source switch
            {
                WritingBindingSource.BeatSlot =>
                    ResolveBeatSlot(performance, beat, binding),
                WritingBindingSource.Aspect =>
                    ResolveAspect(performance, beat, binding),
                WritingBindingSource.Parameter =>
                    ResolveParameter(beat, binding),
                WritingBindingSource.Resolution =>
                    ResolveResolution(proposal, binding),
                _ => throw new ArgumentOutOfRangeException(nameof(binding))
            };
            result.Add(name, value);
        }
        return result;
    }

    private static JsonElement ResolveBeatSlot(
        PerformanceSnapshot performance,
        Beat beat,
        WritingBindingDefinition binding)
    {
        BeatSlotBinding? slot = beat.FindBinding(binding.Slot!);
        Element[] elements = (slot?.ElementIds ?? [])
            .Select(id => performance.Elements.TryGetValue(id, out Element? element)
                ? element
                : throw new InvalidOperationException(
                    $"Beat Slot {binding.Slot} 引用了不存在的 Element {id}。"))
            .ToArray();
        JsonElement[] values = elements.Select(element =>
            JsonSerializer.SerializeToElement(new
            {
                id = element.Id,
                name = element.Name,
                description = element.Description,
                type = element.Type.Value
            })).ToArray();
        return ApplyMemberAndCardinality(values, binding, $"Beat Slot {binding.Slot}");
    }

    private static JsonElement ResolveAspect(
        PerformanceSnapshot performance,
        Beat beat,
        WritingBindingDefinition binding)
    {
        BeatSlotBinding? slot = beat.FindBinding(binding.Slot!);
        HashSet<Guid> elementIds = (slot?.ElementIds ?? []).ToHashSet();
        JsonElement[] values = performance.Aspects.Values
            .Where(aspect =>
                elementIds.Contains(aspect.ElementId) &&
                aspect.Type.Value == binding.Type!.Value.Value)
            .Select(aspect => JsonSerializer.SerializeToElement(new
            {
                id = aspect.Id,
                quantity = aspect.Quantity,
                type = aspect.Type.Value,
                elementId = aspect.ElementId,
                scopeId = aspect.ScopeId
            }))
            .ToArray();
        WritingBindingDefinition effective = binding.Member is null
            ? binding with { Member = "quantity" }
            : binding;
        return ApplyMemberAndCardinality(
            values,
            effective,
            $"Aspect {binding.Type} in Slot {binding.Slot}");
    }

    private JsonElement ResolveParameter(
        Beat beat,
        WritingBindingDefinition binding)
    {
        IReadOnlyDictionary<string, string> parameters =
            _extensions.GetParameters(new ModuleId(beat.ModuleId));
        if (!parameters.TryGetValue(binding.Key!, out string? value))
            return ApplyCardinality(
                [],
                binding.Cardinality,
                $"Module Setting {binding.Key}");
        return ApplyCardinality(
            [JsonSerializer.SerializeToElement(value)],
            binding.Cardinality,
            $"Module Setting {binding.Key}");
    }

    private static JsonElement ResolveResolution(
        BeatResolutionProposal proposal,
        WritingBindingDefinition binding)
    {
        if (!proposal.Values.TryGetValue(binding.Key!, out JsonElement value))
            return ApplyCardinality(
                [],
                binding.Cardinality,
                $"Resolution value {binding.Key}");
        JsonElement[] values = value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Select(item => item.Clone()).ToArray()
            : [value.Clone()];
        return ApplyMemberAndCardinality(
            values,
            binding,
            $"Resolution value {binding.Key}");
    }

    private static JsonElement ApplyMemberAndCardinality(
        IEnumerable<JsonElement> values,
        WritingBindingDefinition binding,
        string description)
    {
        JsonElement[] selected = binding.Member is null
            ? values.Select(value => value.Clone()).ToArray()
            : values.Select(value =>
                SelectMember(value, binding.Member, description)).ToArray();
        return ApplyCardinality(selected, binding.Cardinality, description);
    }

    private static JsonElement ApplyCardinality(
        IReadOnlyList<JsonElement> values,
        WritingBindingCardinality cardinality,
        string description)
    {
        return cardinality switch
        {
            WritingBindingCardinality.One when values.Count == 1 =>
                values[0].Clone(),
            WritingBindingCardinality.One =>
                throw new InvalidOperationException(
                    $"{description} 期望恰好一个值，实际为 {values.Count}。"),
            WritingBindingCardinality.Optional when values.Count <= 1 =>
                values.Count == 0
                    ? JsonSerializer.SerializeToElement<object?>(null)
                    : values[0].Clone(),
            WritingBindingCardinality.Optional =>
                throw new InvalidOperationException(
                    $"{description} 期望至多一个值，实际为 {values.Count}。"),
            WritingBindingCardinality.Many =>
                JsonSerializer.SerializeToElement(values),
            _ => throw new ArgumentOutOfRangeException(nameof(cardinality))
        };
    }

    private static JsonElement SelectMember(
        JsonElement value,
        string member,
        string description)
    {
        if (value.ValueKind != JsonValueKind.Object ||
            !value.TryGetProperty(member, out JsonElement selected))
            throw new InvalidOperationException($"{description} 不包含成员 {member}。");
        return selected.Clone();
    }

    private static IReadOnlyDictionary<string, JsonElement> ResolveContext(
        IReadOnlyList<string> references,
        IReadOnlyDictionary<string, JsonElement> bindings,
        string interaction)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (string reference in references)
        {
            result.Add(
                reference,
                reference == "interaction"
                    ? JsonSerializer.SerializeToElement(interaction)
                    : ResolveReference(reference, bindings));
        }
        return result;
    }

    private async Task<IReadOnlyDictionary<string, string>> GenerateFillsAsync(
        IReadOnlyDictionary<string, RequestedFill> fills,
        string interaction,
        CancellationToken cancellationToken)
    {
        ILanguageModelService languageModels = _languageModels ??
            throw LanguageModelConfigurationException.Invalid(
                "当前 Beat Writing Profile 包含 model Fill，但尚未配置语言模型。");
        var requestBody = new
        {
            interaction,
            fields = fills.ToDictionary(
                pair => pair.Key,
                pair => new
                {
                    instruction = pair.Value.Instruction,
                    context = pair.Value.Context
                },
                StringComparer.Ordinal)
        };
        var validator = new BeatFillOutputValidator(fills);
        LanguageModelOperation operation = languageModels.Start(
            new LanguageModelRunRequest
            {
                Conversation = new LanguageModelConversation
                {
                    Messages =
                    [
                        ModelMessage.System(BaseInstruction),
                        ModelMessage.User(JsonSerializer.Serialize(requestBody))
                    ]
                },
                ToolCallMode = ToolCallMode.None,
                OutputValidator = validator
            },
            cancellationToken);
        try
        {
            LanguageModelRunResult result = await operation.Completion;
            return validator.Parse(result.Output);
        }
        finally
        {
            languageModels.ForgetOperation(operation.Id);
        }
    }

    private static string RenderKnownTemplate(
        string template,
        IReadOnlyDictionary<string, JsonElement> values)
    {
        return PlaceholderPattern().Replace(template, match =>
        {
            string reference = match.Groups[1].Value;
            return Format(ResolveReference(reference, values));
        });
    }

    private static string RenderTemplate(
        string template,
        IReadOnlyDictionary<string, JsonElement> values)
    {
        string result = RenderKnownTemplate(template, values);
        if (PlaceholderPattern().IsMatch(result))
            throw new InvalidOperationException("Paragraph 模板仍包含未解析占位符。");
        return result;
    }

    private static JsonElement ResolveReference(
        string reference,
        IReadOnlyDictionary<string, JsonElement> values)
    {
        string[] path = reference.Split('.');
        if (!values.TryGetValue(path[0], out JsonElement value))
            throw new InvalidOperationException($"Writing 引用 {reference} 没有对应值。");
        JsonElement current = value;
        for (int index = 1; index < path.Length; index++)
            current = SelectMember(current, path[index], reference);
        return current.Clone();
    }

    private static string Format(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => string.Empty,
        JsonValueKind.Array => string.Join(
            "、",
            value.EnumerateArray().Select(Format)),
        JsonValueKind.Object => value.GetRawText(),
        _ => string.Empty
    };

    private static Guid CreateParagraphId(
        Guid performanceId,
        Guid beatId,
        string paragraphKey)
    {
        byte[] input = Encoding.UTF8.GetBytes(
            $"{performanceId:N}:{beatId:N}:{paragraphKey}");
        byte[] hash = SHA256.HashData(input);
        Span<byte> bytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(bytes);
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }

    private sealed record PreparedParagraph(
        WritingParagraphDefinition Definition,
        Dictionary<string, JsonElement> Bindings);

    private sealed record RequestedFill(
        string Instruction,
        IReadOnlyDictionary<string, JsonElement> Context,
        int? MinimumLength,
        int? MaximumLength);

    private sealed class BeatFillOutputValidator : IModelOutputValidator
    {
        private readonly IReadOnlyDictionary<string, RequestedFill> _fills;

        internal BeatFillOutputValidator(
            IReadOnlyDictionary<string, RequestedFill> fills)
        {
            _fills = fills;
            JsonFormat = new ModelJsonFormat(
                "tavi_beat_fills",
                BinaryData.FromString(CreateSchema(fills)),
                true);
        }

        public string Instruction =>
            "只返回一个 JSON object，根属性必须且只能是 fills；fills 必须且只能包含请求的字段，所有值必须是字符串。";

        public ModelJsonFormat? JsonFormat { get; }

        public ModelOutputValidationResult Validate(string output)
        {
            try
            {
                _ = Parse(output);
                return ModelOutputValidationResult.Valid();
            }
            catch (Exception exception) when (
                exception is JsonException or InvalidOperationException)
            {
                return ModelOutputValidationResult.Invalid(exception.Message);
            }
        }

        public string CreateRepairPrompt(ModelOutputValidationResult failure) =>
            $"输出未通过校验：{failure.Error} 请严格按指定 JSON Schema 重新生成。";

        internal IReadOnlyDictionary<string, string> Parse(string output)
        {
            using JsonDocument document = JsonDocument.Parse(output);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                root.EnumerateObject().Any(property => property.Name != "fills") ||
                !root.TryGetProperty("fills", out JsonElement fills) ||
                fills.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("模型输出必须只包含 fills object。");
            string[] actual = fills.EnumerateObject()
                .Select(property => property.Name)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            string[] expected = _fills.Keys
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
                throw new InvalidOperationException("模型输出的 Fill 字段与请求不一致。");
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach ((string key, RequestedFill request) in _fills)
            {
                JsonElement value = fills.GetProperty(key);
                if (value.ValueKind != JsonValueKind.String)
                    throw new InvalidOperationException($"Fill {key} 必须是字符串。");
                string text = value.GetString()!;
                if (request.MinimumLength is int minimum &&
                    text.Length < minimum ||
                    request.MaximumLength is int maximum &&
                    text.Length > maximum)
                    throw new InvalidOperationException($"Fill {key} 长度超出声明范围。");
                result.Add(key, text);
            }
            return result;
        }

        private static string CreateSchema(
            IReadOnlyDictionary<string, RequestedFill> fills)
        {
            var properties = new JsonObject();
            foreach ((string key, RequestedFill fill) in fills)
            {
                var property = new JsonObject { ["type"] = "string" };
                if (fill.MinimumLength.HasValue)
                    property["minLength"] = fill.MinimumLength.Value;
                if (fill.MaximumLength.HasValue)
                    property["maxLength"] = fill.MaximumLength.Value;
                properties[key] = property;
            }
            var schema = new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["required"] = new JsonArray("fills"),
                ["properties"] = new JsonObject
                {
                    ["fills"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["additionalProperties"] = false,
                        ["required"] = new JsonArray(
                            fills.Keys
                                .Select(key => (JsonNode?)JsonValue.Create(key))
                                .ToArray()),
                        ["properties"] = properties
                    }
                }
            };
            return schema.ToJsonString();
        }
    }

    [GeneratedRegex(@"\{\{\s*([A-Za-z][A-Za-z0-9_.-]*)\s*\}\}")]
    private static partial Regex PlaceholderPattern();
}
