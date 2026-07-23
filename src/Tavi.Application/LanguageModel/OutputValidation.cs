using System.Text.Json;

namespace Tavi.Application.LanguageModel;

public readonly record struct ModelOutputValidationResult(
    bool IsValid,
    string? Error = null)
{
    public static ModelOutputValidationResult Valid() => new(true);

    public static ModelOutputValidationResult Invalid(string error) =>
        new(false, error);
}

public interface IModelOutputValidator
{
    string Instruction { get; }
    ModelJsonFormat? JsonFormat { get; }

    ModelOutputValidationResult Validate(string output);

    string CreateRepairPrompt(ModelOutputValidationResult failure);
}

public sealed class NoOutputValidator : IModelOutputValidator
{
    public static NoOutputValidator Instance { get; } = new();

    private NoOutputValidator()
    {
    }

    public string Instruction => string.Empty;
    public ModelJsonFormat? JsonFormat => null;

    public ModelOutputValidationResult Validate(string output) =>
        ModelOutputValidationResult.Valid();

    public string CreateRepairPrompt(ModelOutputValidationResult failure) =>
        string.Empty;
}

public class JsonOutputValidator : IModelOutputValidator
{
    public JsonOutputValidator(
        string instruction = "请只返回合法的 JSON，不要附加 Markdown 代码块或其他文字。",
        ModelJsonFormat? jsonFormat = null)
    {
        Instruction = string.IsNullOrWhiteSpace(instruction)
            ? throw new ArgumentException("JSON 输出指令不能为空。", nameof(instruction))
            : instruction;
        JsonFormat = jsonFormat ?? new ModelJsonFormat("tavi_json");
    }

    public string Instruction { get; }
    public ModelJsonFormat? JsonFormat { get; }

    public virtual ModelOutputValidationResult Validate(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return ModelOutputValidationResult.Invalid("模型输出为空。");
        try
        {
            using JsonDocument _ = JsonDocument.Parse(output);
            return ModelOutputValidationResult.Valid();
        }
        catch (JsonException exception)
        {
            return ModelOutputValidationResult.Invalid($"输出不是合法 JSON：{exception.Message}");
        }
    }

    public virtual string CreateRepairPrompt(ModelOutputValidationResult failure) =>
        $"上一次输出未通过校验：{failure.Error} 请重新生成，并严格遵守以下要求：{Instruction}";
}

public sealed class JsonOutputValidator<T> : JsonOutputValidator
{
    private readonly JsonSerializerOptions _serializerOptions;

    public JsonOutputValidator(
        string instruction = "请只返回与目标结构匹配的合法 JSON，不要附加其他文字。",
        ModelJsonFormat? jsonFormat = null,
        JsonSerializerOptions? serializerOptions = null)
        : base(instruction, jsonFormat)
    {
        _serializerOptions = serializerOptions ?? new JsonSerializerOptions();
    }

    public override ModelOutputValidationResult Validate(string output)
    {
        ModelOutputValidationResult syntaxResult = base.Validate(output);
        if (!syntaxResult.IsValid)
            return syntaxResult;
        try
        {
            T? value = JsonSerializer.Deserialize<T>(output, _serializerOptions);
            return value is null
                ? ModelOutputValidationResult.Invalid($"输出不能反序列化为 {typeof(T).Name}。")
                : ModelOutputValidationResult.Valid();
        }
        catch (JsonException exception)
        {
            return ModelOutputValidationResult.Invalid(
                $"输出与 {typeof(T).Name} 不匹配：{exception.Message}");
        }
        catch (NotSupportedException exception)
        {
            return ModelOutputValidationResult.Invalid(
                $"目标类型 {typeof(T).Name} 不支持 JSON 反序列化：{exception.Message}");
        }
    }
}
