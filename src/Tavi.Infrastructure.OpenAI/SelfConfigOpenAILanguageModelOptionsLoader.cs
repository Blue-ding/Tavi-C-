using System.Text.Json;
using Tavi.Application.LanguageModel;

namespace Tavi.Infrastructure.OpenAI;

/// <summary>从被 Git 忽略的临时 <c>SelfCongif.md</c> 文件加载 OpenAI 兼容服务配置。</summary>
public static class SelfConfigOpenAILanguageModelOptionsLoader
{
    /// <summary>尝试加载临时本地配置；文件不存在时返回 <see langword="null"/>。</summary>
    /// <param name="path">配置文件路径；为空时从当前程序目录向上定位解决方案根目录。</param>
    /// <returns>可用于创建 OpenAI 客户端的选项，或在文件不存在时返回 <see langword="null"/>。</returns>
    public static OpenAILanguageModelOptions? TryLoad(string? path = null)
    {
        string resolvedPath = string.IsNullOrWhiteSpace(path) ? FindDefaultPath() : Path.GetFullPath(path);
        if (!File.Exists(resolvedPath))
            return null;
        try
        {
            string fragment = File.ReadAllText(resolvedPath);
            using JsonDocument document = JsonDocument.Parse($"{{{fragment}}}", new JsonDocumentOptions { AllowTrailingCommas = true });
            JsonElement root = document.RootElement;
            string uri = GetRequired(root, "Uri");
            string model = GetRequired(root, "Model");
            string apiKey = GetRequired(root, "APIKey");
            if (!Uri.TryCreate(uri, UriKind.Absolute, out Uri? endpoint))
                throw new InvalidDataException("SelfCongif.md 字段 Uri 必须是绝对 URI。");
            return new OpenAILanguageModelOptions { Endpoint = endpoint, Model = model, ApiKey = apiKey, ClientType = OpenAIClientType.Chat, SupportsRequiredToolChoice = false, EnableThinking = false };
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            throw LanguageModelConfigurationException.Invalid($"无法读取 SelfCongif.md：{exception.Message}");
        }
    }

    private static string FindDefaultPath()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Tavi.sln")))
            directory = directory.Parent;
        if (directory is null)
            return Path.Combine(AppContext.BaseDirectory, "SelfCongif.md");
        return Path.Combine(directory.FullName, "src", "Tavi.Infrastructure.OpenAI", "SelfCongif.md");
    }

    private static string GetRequired(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement property) || property.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"SelfCongif.md 缺少字符串字段 {name}。");
        string? value = property.GetString();
        return string.IsNullOrWhiteSpace(value) ? throw new InvalidDataException($"SelfCongif.md 字段 {name} 不能为空。") : value;
    }
}
