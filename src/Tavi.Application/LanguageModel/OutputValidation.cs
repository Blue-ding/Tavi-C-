using System.Text.Json;

namespace Tavi.Application.LanguageModel;

/// <summary>表示模型输出校验结果以及可反馈给模型的错误原因。</summary>
public readonly record struct ModelOutputValidationResult(
    bool IsValid,
    string? Error = null)
{
    /// <summary>创建成功结果。</summary>
    public static ModelOutputValidationResult Valid() => new(true);

    /// <summary>创建失败结果。</summary>
    public static ModelOutputValidationResult Invalid(string error) =>
        new(false, error);
}

/// <summary>定义本地输出校验、原生 JSON 格式和修复提示。</summary>
public interface IModelOutputValidator
{
    /// <summary>获取加入会话的输出约束指令。</summary>
    string Instruction { get; }
    /// <summary>获取可由适配器转换为原生格式的 JSON 定义。</summary>
    ModelJsonFormat? JsonFormat { get; }

    /// <summary>校验一次完整模型输出。</summary>
    ModelOutputValidationResult Validate(string output);

    /// <summary>根据失败原因创建下一轮修复提示。</summary>
    string CreateRepairPrompt(ModelOutputValidationResult failure);
}

/// <summary>接受任意输出且不创建修复提示的校验器。</summary>
public sealed class NoOutputValidator : IModelOutputValidator
{
    /// <summary>获取共享实例。</summary>
    public static NoOutputValidator Instance { get; } = new();

    private NoOutputValidator()
    {
    }

    public string Instruction => string.Empty;
    public ModelJsonFormat? JsonFormat => null;

    /// <inheritdoc />
    public ModelOutputValidationResult Validate(string output) =>
        ModelOutputValidationResult.Valid();

    /// <inheritdoc />
    public string CreateRepairPrompt(ModelOutputValidationResult failure) =>
        string.Empty;
}

/// <summary>校验输出是否为不带额外文本的合法 JSON。</summary>
public class JsonOutputValidator : IModelOutputValidator
{
    /// <summary>创建 JSON 输出校验器。</summary>
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

    /// <inheritdoc />
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

    /// <inheritdoc />
    public virtual string CreateRepairPrompt(ModelOutputValidationResult failure) =>
        $"上一次输出未通过校验：{failure.Error} 请重新生成，并严格遵守以下要求：{Instruction}";
}

/// <summary>校验输出是否能反序列化为指定目标类型。</summary>
public sealed class JsonOutputValidator<T> : JsonOutputValidator
{
    private readonly JsonSerializerOptions _serializerOptions;

    /// <summary>创建强类型 JSON 输出校验器。</summary>
    public JsonOutputValidator(
        string instruction = "请只返回与目标结构匹配的合法 JSON，不要附加其他文字。",
        ModelJsonFormat? jsonFormat = null,
        JsonSerializerOptions? serializerOptions = null)
        : base(instruction, jsonFormat)
    {
        _serializerOptions = serializerOptions ?? new JsonSerializerOptions();
    }

    /// <inheritdoc />
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
