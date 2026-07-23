using System.ClientModel;
using System.ClientModel.Primitives;
using System.Globalization;
using System.Text.Json;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Responses;
using Tavi.Application.LanguageModel;

namespace Tavi.Infrastructure.OpenAI;

/// <summary>
/// 将 OpenAI SDK 映射为 Application 的单轮会话端口。
/// </summary>
public sealed class OpenAILanguageModelClient : ILanguageModelClient
{
    private readonly OpenAILanguageModelOptions _options;
    private readonly ChatClient? _chatClient;
    private readonly ResponsesClient? _responsesClient;

    /// <summary>使用强类型适配器配置创建 OpenAI 客户端。</summary>
    public OpenAILanguageModelClient(OpenAILanguageModelOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        var credential = new ApiKeyCredential(_options.ApiKey);
        if (_options.ClientType == OpenAIClientType.Chat)
        {
            _chatClient = new ChatClient(
                _options.Model,
                credential,
                new OpenAIClientOptions { Endpoint = _options.Endpoint });
            Capabilities = new LanguageModelCapabilities
            {
                Provider = "OpenAI.Chat",
                SupportsToolCalls = true,
                SupportsRequiredToolChoice = _options.SupportsRequiredToolChoice,
                SupportsParallelToolCalls = true,
                SupportsNativeJsonOutput = true,
                SupportsJsonSchema = true,
                SupportsStreaming = false
            };
        }
        else
        {
            _responsesClient = new ResponsesClient(
                credential,
                new ResponsesClientOptions { Endpoint = _options.Endpoint });
            Capabilities = new LanguageModelCapabilities
            {
                Provider = "OpenAI.Responses",
                SupportsToolCalls = true,
                SupportsRequiredToolChoice = _options.SupportsRequiredToolChoice,
                SupportsParallelToolCalls = true,
                SupportsNativeJsonOutput = true,
                SupportsJsonSchema = true,
                SupportsStreaming = false
            };
        }
    }

    /// <inheritdoc />
    public LanguageModelCapabilities Capabilities { get; }

    /// <inheritdoc />
    public ILanguageModelSession CreateSession(LanguageModelSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateSessionOptions(options);
        return _options.ClientType == OpenAIClientType.Chat
            ? new OpenAIChatSession(
                _chatClient ?? throw new InvalidOperationException("Chat 客户端尚未初始化。"),
                options,
                _options.EnableThinking)
            : new OpenAIResponsesSession(
                _responsesClient ?? throw new InvalidOperationException("Responses 客户端尚未初始化。"),
                _options.Model,
                options);
    }

    private void ValidateSessionOptions(LanguageModelSessionOptions options)
    {
        if (options.UseStreaming)
            throw LanguageModelConfigurationException.Unsupported(Capabilities.Provider, "Streaming");
        if (options.Tools.Count > 0 && !Capabilities.SupportsToolCalls)
            throw LanguageModelConfigurationException.Unsupported(Capabilities.Provider, "ToolCalls");
        if (options.ToolCallMode == ToolCallMode.Required && !Capabilities.SupportsRequiredToolChoice)
            throw LanguageModelConfigurationException.Unsupported(Capabilities.Provider, "RequiredToolChoice");
        if (options.UseNativeJsonOutput &&
            options.JsonFormat?.Schema is not null &&
            !Capabilities.SupportsJsonSchema)
            throw LanguageModelConfigurationException.Unsupported(Capabilities.Provider, "JsonSchema");
    }
}

internal sealed class OpenAIChatSession : ILanguageModelSession
{
    private readonly ChatClient _client;
    private readonly ChatCompletionOptions _options = new();
    private readonly List<ChatMessage> _history = [];
    private readonly bool _requireToolOnFirstTurn;
    private bool _completedFirstTurn;
    private bool _disposed;

    internal OpenAIChatSession(
        ChatClient client,
        LanguageModelSessionOptions options,
        bool? enableThinking)
    {
        _client = client;
        _requireToolOnFirstTurn = options.ToolCallMode == ToolCallMode.Required;
        if (enableThinking.HasValue)
            _options.Patch.Set("$.enable_thinking"u8, enableThinking.Value);
        foreach (ModelToolDefinition tool in options.Tools)
        {
            _options.Tools.Add(ChatTool.CreateFunctionTool(
                tool.Name,
                tool.Description,
                tool.ParameterSchema,
                true));
        }
        _options.ToolChoice = options.ToolCallMode switch
        {
            ToolCallMode.None => ChatToolChoice.CreateNoneChoice(),
            ToolCallMode.Required => ChatToolChoice.CreateRequiredChoice(),
            _ => ChatToolChoice.CreateAutoChoice()
        };
        if (options.UseNativeJsonOutput && options.JsonFormat is not null)
        {
            _options.ResponseFormat = options.JsonFormat.Schema is null
                ? ChatResponseFormat.CreateJsonObjectFormat()
                : ChatResponseFormat.CreateJsonSchemaFormat(
                    options.JsonFormat.Name,
                    options.JsonFormat.Schema,
                    null,
                    options.JsonFormat.Strict);
        }
    }

    /// <inheritdoc />
    public async Task<ModelTurnResult> CompleteAsync(
        IReadOnlyList<ModelMessage> messages,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(messages);
        foreach (ModelMessage message in messages)
            _history.Add(ConvertMessage(message));
        try
        {
            ChatCompletion completion =
                await _client.CompleteChatAsync(_history, _options, cancellationToken);
            if (_requireToolOnFirstTurn && !_completedFirstTurn)
                _options.ToolChoice = ChatToolChoice.CreateAutoChoice();
            _completedFirstTurn = true;
            _history.Add(new AssistantChatMessage(completion));
            return ConvertCompletion(completion);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ClientResultException exception)
        {
            throw OpenAIExceptionMapper.Map(exception, "CompleteChat");
        }
        catch (Exception exception) when (exception is HttpRequestException or TimeoutException)
        {
            throw OpenAIExceptionMapper.MapTransport(exception, "CompleteChat");
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _disposed = true;
        _history.Clear();
        return ValueTask.CompletedTask;
    }

    private static ChatMessage ConvertMessage(ModelMessage message)
    {
        return message.Role switch
        {
            ModelMessageRole.System => new SystemChatMessage(message.Text ?? string.Empty),
            ModelMessageRole.User => new UserChatMessage(message.Text ?? string.Empty),
            ModelMessageRole.Assistant when message.ToolCalls.Count > 0 =>
                new AssistantChatMessage(message.ToolCalls.Select(call =>
                    ChatToolCall.CreateFunctionToolCall(call.CallId, call.Name, call.Arguments))),
            ModelMessageRole.Assistant => new AssistantChatMessage(message.Text ?? string.Empty),
            ModelMessageRole.Tool => new ToolChatMessage(
                message.ToolCallId ?? throw new LanguageModelProtocolException(
                    "工具消息缺少 ToolCallId。"),
                message.Text ?? string.Empty),
            _ => throw new LanguageModelProtocolException($"不支持的消息角色：{message.Role}。")
        };
    }

    private static ModelTurnResult ConvertCompletion(ChatCompletion completion)
    {
        if (completion.FinishReason == ChatFinishReason.Stop)
        {
            string text = string.Concat(completion.Content.Select(part => part.Text));
            return ModelTurnResult.Completed(text);
        }
        if (completion.FinishReason == ChatFinishReason.ToolCalls)
        {
            return ModelTurnResult.CallingTools(
                completion.ToolCalls.Select(call =>
                    new ModelToolCall(call.Id, call.FunctionName, call.FunctionArguments)).ToArray());
        }
        if (completion.FinishReason == ChatFinishReason.Length)
            return new ModelTurnResult { FinishReason = ModelFinishReason.LengthLimit };
        if (completion.FinishReason == ChatFinishReason.ContentFilter)
            return new ModelTurnResult { FinishReason = ModelFinishReason.ContentFiltered };
        throw new LanguageModelProtocolException(
            $"OpenAI Chat 返回了不支持的结束原因：{completion.FinishReason}。",
            new LanguageModelErrorDetails
            {
                Provider = "OpenAI.Chat",
                Operation = "ConvertCompletion"
            });
    }
}

internal sealed class OpenAIResponsesSession : ILanguageModelSession
{
    private readonly ResponsesClient _client;
    private readonly string _model;
    private readonly LanguageModelSessionOptions _sessionOptions;
    private string? _previousResponseId;
    private bool _completedFirstTurn;
    private bool _disposed;

    internal OpenAIResponsesSession(
        ResponsesClient client,
        string model,
        LanguageModelSessionOptions sessionOptions)
    {
        _client = client;
        _model = model;
        _sessionOptions = sessionOptions;
    }

    /// <inheritdoc />
    public async Task<ModelTurnResult> CompleteAsync(
        IReadOnlyList<ModelMessage> messages,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(messages);
        CreateResponseOptions options = CreateOptions();
        foreach (ModelMessage message in messages)
        {
            foreach (ResponseItem item in ConvertMessage(message))
                options.InputItems.Add(item);
        }
        try
        {
            ResponseResult response =
                await _client.CreateResponseAsync(options, cancellationToken);
            _completedFirstTurn = true;
            _previousResponseId = response.Id;
            if (response.Status == ResponseStatus.Incomplete)
            {
                return response.IncompleteStatusDetails?.Reason ==
                       ResponseIncompleteStatusReason.ContentFilter
                    ? new ModelTurnResult { FinishReason = ModelFinishReason.ContentFiltered }
                    : new ModelTurnResult { FinishReason = ModelFinishReason.LengthLimit };
            }
            if (response.Status == ResponseStatus.Failed)
                throw OpenAIExceptionMapper.MapResponseFailure(response);
            if (response.Status == ResponseStatus.Cancelled)
                throw OpenAIExceptionMapper.MapCancelledResponse(response);
            if (response.Status is not null and not ResponseStatus.Completed)
            {
                throw new LanguageModelProtocolException(
                    $"OpenAI Responses 返回了非终态状态：{response.Status}。",
                    new LanguageModelErrorDetails
                    {
                        Provider = "OpenAI.Responses",
                        RequestId = response.Id,
                        Operation = "CreateResponse"
                    });
            }
            ModelToolCall[] calls = response.OutputItems
                .OfType<FunctionCallResponseItem>()
                .Select(call => new ModelToolCall(
                    call.CallId,
                    call.FunctionName,
                    call.FunctionArguments))
                .ToArray();
            return calls.Length > 0
                ? ModelTurnResult.CallingTools(calls)
                : ModelTurnResult.Completed(response.GetOutputText());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ClientResultException exception)
        {
            throw OpenAIExceptionMapper.Map(exception, "CreateResponse");
        }
        catch (Exception exception) when (exception is HttpRequestException or TimeoutException)
        {
            throw OpenAIExceptionMapper.MapTransport(exception, "CreateResponse");
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _disposed = true;
        _previousResponseId = null;
        return ValueTask.CompletedTask;
    }

    private CreateResponseOptions CreateOptions()
    {
        var options = new CreateResponseOptions
        {
            Model = _model,
            PreviousResponseId = _previousResponseId,
            ToolChoice = _sessionOptions.ToolCallMode switch
            {
                ToolCallMode.None => ResponseToolChoice.CreateNoneChoice(),
                ToolCallMode.Required when !_completedFirstTurn =>
                    ResponseToolChoice.CreateRequiredChoice(),
                _ => ResponseToolChoice.CreateAutoChoice()
            }
        };
        foreach (ModelToolDefinition tool in _sessionOptions.Tools)
        {
            options.Tools.Add(ResponseTool.CreateFunctionTool(
                tool.Name,
                tool.ParameterSchema,
                true,
                tool.Description));
        }
        if (_sessionOptions.UseNativeJsonOutput && _sessionOptions.JsonFormat is not null)
        {
            options.TextOptions = new ResponseTextOptions
            {
                TextFormat = _sessionOptions.JsonFormat.Schema is null
                    ? ResponseTextFormat.CreateJsonObjectFormat()
                    : ResponseTextFormat.CreateJsonSchemaFormat(
                        _sessionOptions.JsonFormat.Name,
                        _sessionOptions.JsonFormat.Schema,
                        null,
                        _sessionOptions.JsonFormat.Strict)
            };
        }
        return options;
    }

    private static IEnumerable<ResponseItem> ConvertMessage(ModelMessage message)
    {
        switch (message.Role)
        {
            case ModelMessageRole.System:
                yield return ResponseItem.CreateSystemMessageItem(message.Text ?? string.Empty);
                yield break;
            case ModelMessageRole.User:
                yield return ResponseItem.CreateUserMessageItem(message.Text ?? string.Empty);
                yield break;
            case ModelMessageRole.Assistant when message.ToolCalls.Count > 0:
                foreach (ModelToolCall call in message.ToolCalls)
                    yield return ResponseItem.CreateFunctionCallItem(call.CallId, call.Name, call.Arguments);
                yield break;
            case ModelMessageRole.Assistant:
                yield return ResponseItem.CreateAssistantMessageItem(message.Text ?? string.Empty, []);
                yield break;
            case ModelMessageRole.Tool:
                yield return ResponseItem.CreateFunctionCallOutputItem(
                    message.ToolCallId ?? throw new LanguageModelProtocolException(
                        "工具消息缺少 ToolCallId。"),
                    message.Text ?? string.Empty);
                yield break;
            default:
                throw new LanguageModelProtocolException($"不支持的消息角色：{message.Role}。");
        }
    }
}

internal static class OpenAIExceptionMapper
{
    internal static LanguageModelProviderException Map(
        ClientResultException exception,
        string operation)
    {
        int status = exception.Status;
        (string code, LanguageModelErrorCategory category, bool transient) = status switch
        {
            401 => (LanguageModelErrorCodes.Authentication, LanguageModelErrorCategory.Authentication, false),
            403 => (LanguageModelErrorCodes.Authorization, LanguageModelErrorCategory.Authorization, false),
            429 => (LanguageModelErrorCodes.RateLimited, LanguageModelErrorCategory.RateLimit, true),
            408 => (LanguageModelErrorCodes.Transport, LanguageModelErrorCategory.Transport, true),
            >= 500 => (LanguageModelErrorCodes.ServiceUnavailable, LanguageModelErrorCategory.ServiceUnavailable, true),
            _ => (LanguageModelErrorCodes.InvalidRequest, LanguageModelErrorCategory.InvalidRequest, false)
        };
        PipelineResponse? response = exception.GetRawResponse();
        string? requestId = GetFirstHeader(
            response,
            "x-request-id",
            "request-id",
            "apim-request-id");
        TimeSpan? retryAfter = ParseRetryAfter(response);
        string? providerErrorCode = TryGetProviderErrorCode(response);
        return new LanguageModelProviderException(
            code,
            category,
            $"OpenAI 请求失败，HTTP 状态码 {status}。",
            transient,
            new LanguageModelErrorDetails
            {
                Provider = "OpenAI",
                ProviderErrorCode = providerErrorCode,
                HttpStatusCode = status,
                RequestId = requestId,
                RetryAfter = retryAfter,
                Operation = operation
            },
            exception);
    }

    internal static LanguageModelProviderException MapTransport(
        Exception exception,
        string operation) =>
        new(
            LanguageModelErrorCodes.Transport,
            LanguageModelErrorCategory.Transport,
            "无法连接 OpenAI 服务。",
            true,
            new LanguageModelErrorDetails { Provider = "OpenAI", Operation = operation },
            exception);

    internal static LanguageModelProviderException MapResponseFailure(
        ResponseResult response)
    {
        string? providerCode = response.Error?.Code.ToString();
        bool rateLimited = response.Error?.Code == ResponseErrorCode.RateLimitExceeded;
        bool serverError = response.Error?.Code == ResponseErrorCode.ServerError;
        return new LanguageModelProviderException(
            rateLimited
                ? LanguageModelErrorCodes.RateLimited
                : serverError
                    ? LanguageModelErrorCodes.ServiceUnavailable
                    : LanguageModelErrorCodes.InvalidRequest,
            rateLimited
                ? LanguageModelErrorCategory.RateLimit
                : serverError
                    ? LanguageModelErrorCategory.ServiceUnavailable
                    : LanguageModelErrorCategory.InvalidRequest,
            "OpenAI Responses 返回失败状态。",
            rateLimited || serverError,
            new LanguageModelErrorDetails
            {
                Provider = "OpenAI.Responses",
                ProviderErrorCode = providerCode,
                RequestId = response.Id,
                Operation = "CreateResponse"
            });
    }

    internal static LanguageModelProviderException MapCancelledResponse(
        ResponseResult response) =>
        new(
            LanguageModelErrorCodes.Transport,
            LanguageModelErrorCategory.Transport,
            "OpenAI Responses 在服务端取消了请求。",
            true,
            new LanguageModelErrorDetails
            {
                Provider = "OpenAI.Responses",
                RequestId = response.Id,
                Operation = "CreateResponse"
            });

    private static string? GetFirstHeader(
        PipelineResponse? response,
        params string[] names)
    {
        if (response is null)
            return null;
        foreach (string name in names)
        {
            if (response.Headers.TryGetValue(name, out string? value) &&
                !string.IsNullOrWhiteSpace(value))
                return value;
        }
        return null;
    }

    private static TimeSpan? ParseRetryAfter(PipelineResponse? response)
    {
        if (response is null)
            return null;
        if (!response.Headers.TryGetValue("retry-after", out string? value) ||
            string.IsNullOrWhiteSpace(value))
            return null;
        if (double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double seconds) &&
            seconds >= 0)
            return TimeSpan.FromSeconds(seconds);
        if (DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out DateTimeOffset retryAt))
            return retryAt > DateTimeOffset.UtcNow
                ? retryAt - DateTimeOffset.UtcNow
                : TimeSpan.Zero;
        return null;
    }

    private static string? TryGetProviderErrorCode(PipelineResponse? response)
    {
        if (response is null)
            return null;
        try
        {
            BinaryData? content = response.Content;
            if (content is null)
                return null;
            using JsonDocument document = JsonDocument.Parse(content);
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("error", out JsonElement error))
                return null;
            if (error.TryGetProperty("code", out JsonElement code) &&
                code.ValueKind == JsonValueKind.String)
                return code.GetString();
            if (error.TryGetProperty("type", out JsonElement type) &&
                type.ValueKind == JsonValueKind.String)
                return type.GetString();
        }
        catch (JsonException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        return null;
    }
}
