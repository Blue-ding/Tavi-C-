using System.Collections.Concurrent;
using Tavi.Application.Logging;

namespace Tavi.Application.LanguageModel;

public enum LanguageModelRunStatus
{
    Created,
    Running,
    WaitingForModel,
    ExecutingTools,
    ValidatingOutput,
    Completed,
    Failed,
    Cancelled
}

public sealed record LanguageModelRunRequest
{
    public required LanguageModelConversation Conversation { get; init; }
    public IReadOnlyList<ITool> Tools { get; init; } = [];
    public ToolCallMode ToolCallMode { get; init; } = ToolCallMode.Auto;
    public IModelOutputValidator OutputValidator { get; init; } = NoOutputValidator.Instance;
}

public sealed record LanguageModelRunResult(
    Guid RunId,
    string Output,
    LanguageModelConversation Conversation,
    int ToolRounds,
    int OutputRepairAttempts);

public sealed class LanguageModelRunStatusChangedEventArgs : EventArgs
{
    public LanguageModelRunStatusChangedEventArgs(
        LanguageModelRunStatus previous,
        LanguageModelRunStatus current)
    {
        Previous = previous;
        Current = current;
    }

    public LanguageModelRunStatus Previous { get; }
    public LanguageModelRunStatus Current { get; }
}

/// <summary>
/// 前端持有的一次独立运行，可查询状态并等待最终结果。
/// </summary>
public sealed class LanguageModelOperation
{
    private int _status = (int)LanguageModelRunStatus.Created;
    private readonly TaskCompletionSource<LanguageModelRunResult> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal LanguageModelOperation(Guid id)
    {
        Id = id;
    }

    public Guid Id { get; }
    public LanguageModelRunStatus Status => (LanguageModelRunStatus)Volatile.Read(ref _status);
    public Task<LanguageModelRunResult> Completion => _completion.Task;

    public event EventHandler<LanguageModelRunStatusChangedEventArgs>? StatusChanged;
    public event EventHandler<string>? TextReceived;

    internal void SetStatus(LanguageModelRunStatus status)
    {
        LanguageModelRunStatus previous =
            (LanguageModelRunStatus)Interlocked.Exchange(ref _status, (int)status);
        if (previous != status)
            StatusChanged?.Invoke(this, new LanguageModelRunStatusChangedEventArgs(previous, status));
    }

    internal void ReportText(string text)
    {
        if (text.Length > 0)
            TextReceived?.Invoke(this, text);
    }

    internal void Complete(LanguageModelRunResult result) => _completion.TrySetResult(result);
    internal void Fail(Exception exception) => _completion.TrySetException(exception);
    internal void Cancel(CancellationToken cancellationToken) => _completion.TrySetCanceled(cancellationToken);
}

public interface ILanguageModelService
{
    LanguageModelCapabilities Capabilities { get; }

    LanguageModelOperation Start(
        LanguageModelRunRequest request,
        CancellationToken cancellationToken = default);

    bool TryGetOperation(Guid runId, out LanguageModelOperation? operation);

    bool ForgetOperation(Guid runId);
}

/// <summary>
/// 在 Application 内执行工具循环、输出校验、重试、超时和状态跟踪。
/// </summary>
public sealed class LanguageModelRunner : ILanguageModelService
{
    private const string LogCategory = "LanguageModelRunner";
    private readonly ILanguageModelClient _client;
    private readonly LanguageModelSettings _settings;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<Guid, LanguageModelOperation> _operations = new();

    public LanguageModelRunner(
        ILanguageModelClient client,
        LanguageModelSettings settings,
        ILogger? logger = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _settings.Validate();
        _logger = logger ?? NullLogger.Instance;
        ValidateStaticCapabilities();
    }

    public LanguageModelCapabilities Capabilities => _client.Capabilities;

    public LanguageModelOperation Start(
        LanguageModelRunRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        var operation = new LanguageModelOperation(Guid.NewGuid());
        if (!_operations.TryAdd(operation.Id, operation))
            throw new InvalidOperationException("无法登记语言模型运行。");
        _ = ExecuteAndCompleteAsync(operation, request, cancellationToken);
        return operation;
    }

    public bool TryGetOperation(Guid runId, out LanguageModelOperation? operation) =>
        _operations.TryGetValue(runId, out operation);

    public bool ForgetOperation(Guid runId) =>
        _operations.TryRemove(runId, out _);

    private async Task ExecuteAndCompleteAsync(
        LanguageModelOperation operation,
        LanguageModelRunRequest request,
        CancellationToken callerCancellationToken)
    {
        operation.SetStatus(LanguageModelRunStatus.Running);
        Log(LogLevel.Information, "语言模型运行已开始。", operation.Id);

        using var timeoutSource = new CancellationTokenSource(_settings.OverallTimeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            callerCancellationToken,
            timeoutSource.Token);
        try
        {
            LanguageModelRunResult result =
                await RunCoreAsync(operation, request, linkedSource.Token);
            operation.SetStatus(LanguageModelRunStatus.Completed);
            operation.Complete(result);
            Log(LogLevel.Information, "语言模型运行已完成。", operation.Id);
        }
        catch (OperationCanceledException exception) when (
            timeoutSource.IsCancellationRequested &&
            !callerCancellationToken.IsCancellationRequested)
        {
            var timeoutException =
                new LanguageModelTimeoutException(_settings.OverallTimeout, operation.Id, exception);
            operation.SetStatus(LanguageModelRunStatus.Failed);
            operation.Fail(timeoutException);
            Log(LogLevel.Error, timeoutException.Message, operation.Id, timeoutException);
        }
        catch (OperationCanceledException)
        {
            operation.SetStatus(LanguageModelRunStatus.Cancelled);
            operation.Cancel(callerCancellationToken);
            Log(LogLevel.Information, "语言模型运行已取消。", operation.Id);
        }
        catch (Exception exception)
        {
            operation.SetStatus(LanguageModelRunStatus.Failed);
            operation.Fail(exception);
            Log(LogLevel.Error, "语言模型运行失败。", operation.Id, exception);
        }
    }

    private async Task<LanguageModelRunResult> RunCoreAsync(
        LanguageModelOperation operation,
        LanguageModelRunRequest request,
        CancellationToken cancellationToken)
    {
        Dictionary<string, ITool> tools = request.Tools.ToDictionary(tool => tool.name, StringComparer.Ordinal);
        bool nativeJson = ShouldUseNativeJson(request.OutputValidator);
        bool streaming = _settings.Streaming != FeaturePolicy.Disabled &&
                         _client.Capabilities.SupportsStreaming;
        var sessionOptions = new LanguageModelSessionOptions
        {
            Tools = request.Tools
                .Select(tool => new ModelToolDefinition(tool.name, tool.description, tool.parameterData))
                .ToArray(),
            ToolCallMode = request.ToolCallMode,
            JsonFormat = request.OutputValidator.JsonFormat,
            UseNativeJsonOutput = nativeJson,
            UseStreaming = streaming
        };

        await using ILanguageModelSession session = _client.CreateSession(sessionOptions);
        if (streaming && session is not IStreamingLanguageModelSession)
            throw LanguageModelConfigurationException.Unsupported(
                _client.Capabilities.Provider,
                "StreamingSession");

        var transcript = request.Conversation.Messages.ToList();
        if (!string.IsNullOrWhiteSpace(request.OutputValidator.Instruction))
            transcript.Insert(0, ModelMessage.System(request.OutputValidator.Instruction));
        IReadOnlyList<ModelMessage> pending = transcript.ToArray();
        int toolRounds = 0;
        int repairAttempts = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            operation.SetStatus(LanguageModelRunStatus.WaitingForModel);
            ModelTurnResult turn = streaming
                ? await CompleteStreamingAsync(
                    (IStreamingLanguageModelSession)session,
                    pending,
                    operation,
                    cancellationToken)
                : await session.CompleteAsync(pending, cancellationToken);

            switch (turn.FinishReason)
            {
                case ModelFinishReason.Completed:
                {
                    string output = turn.Text ?? string.Empty;
                    transcript.Add(ModelMessage.Assistant(output));
                    operation.SetStatus(LanguageModelRunStatus.ValidatingOutput);
                    ModelOutputValidationResult validation = request.OutputValidator.Validate(output);
                    if (validation.IsValid)
                    {
                        return new LanguageModelRunResult(
                            operation.Id,
                            output,
                            new LanguageModelConversation { Messages = transcript.ToArray() },
                            toolRounds,
                            repairAttempts);
                    }

                    if (repairAttempts >= _settings.MaxOutputRepairAttempts)
                    {
                        throw new ModelOutputValidationException(
                            validation.Error ?? "模型输出未通过校验。",
                            new LanguageModelErrorDetails
                            {
                                Provider = _client.Capabilities.Provider,
                                RunId = operation.Id,
                                Operation = "ValidateOutput"
                            });
                    }

                    repairAttempts++;
                    string repairPrompt = request.OutputValidator.CreateRepairPrompt(validation);
                    ModelMessage repairMessage = ModelMessage.User(repairPrompt);
                    transcript.Add(repairMessage);
                    pending = [repairMessage];
                    Log(
                        LogLevel.Warning,
                        $"输出校验失败，正在进行第 {repairAttempts} 次修复。",
                        operation.Id);
                    break;
                }

                case ModelFinishReason.ToolCalls:
                {
                    if (turn.ToolCalls.Count == 0)
                        throw Protocol("模型以工具调用结束，但没有返回工具调用。", operation.Id);
                    if (toolRounds >= _settings.MaxToolRounds)
                        throw new ModelRoundLimitException(_settings.MaxToolRounds, operation.Id);

                    toolRounds++;
                    transcript.Add(ModelMessage.Assistant(turn.ToolCalls));
                    operation.SetStatus(LanguageModelRunStatus.ExecutingTools);
                    ModelMessage[] toolResults = await ExecuteToolsAsync(
                        turn.ToolCalls,
                        tools,
                        operation.Id,
                        cancellationToken);
                    transcript.AddRange(toolResults);
                    pending = toolResults;
                    break;
                }

                case ModelFinishReason.LengthLimit:
                    throw new LanguageModelException(
                        LanguageModelErrorCodes.ContextLength,
                        LanguageModelErrorCategory.ContextLength,
                        "语言模型达到上下文或输出长度限制。",
                        details: RunDetails(operation.Id, "Complete"));

                case ModelFinishReason.ContentFiltered:
                    throw new LanguageModelException(
                        LanguageModelErrorCodes.ContentFiltered,
                        LanguageModelErrorCategory.ContentFilter,
                        "语言模型输出被内容策略拦截。",
                        details: RunDetails(operation.Id, "Complete"));

                default:
                    throw Protocol($"未知的模型结束原因：{turn.FinishReason}。", operation.Id);
            }
        }
    }

    private async Task<ModelMessage[]> ExecuteToolsAsync(
        IReadOnlyList<ModelToolCall> calls,
        IReadOnlyDictionary<string, ITool> tools,
        Guid runId,
        CancellationToken cancellationToken)
    {
        var results = new ModelMessage[calls.Count];
        for (int index = 0; index < calls.Count; index++)
        {
            ModelToolCall call = calls[index];
            if (!tools.TryGetValue(call.Name, out ITool? tool))
            {
                throw new ModelToolException(
                    LanguageModelErrorCodes.ToolNotFound,
                    LanguageModelErrorCategory.ToolNotFound,
                    $"模型请求了未注册的工具“{call.Name}”。",
                    ToolDetails(runId, call));
            }

            Log(LogLevel.Information, $"正在执行工具“{call.Name}”。", runId);
            try
            {
                string result = await tool.Execute(call.Arguments, cancellationToken);
                results[index] = ModelMessage.Tool(call.CallId, result);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new ModelToolException(
                    LanguageModelErrorCodes.ToolExecutionFailed,
                    LanguageModelErrorCategory.ToolExecution,
                    $"工具“{call.Name}”执行失败。",
                    ToolDetails(runId, call),
                    exception);
            }
        }
        return results;
    }

    private static async Task<ModelTurnResult> CompleteStreamingAsync(
        IStreamingLanguageModelSession session,
        IReadOnlyList<ModelMessage> pending,
        LanguageModelOperation operation,
        CancellationToken cancellationToken)
    {
        ModelTurnResult? completed = null;
        await foreach (ModelStreamUpdate update in session
                           .CompleteStreamingAsync(pending, cancellationToken)
                           .WithCancellation(cancellationToken))
        {
            operation.ReportText(update.TextDelta);
            completed = update.CompletedTurn ?? completed;
        }
        return completed ?? throw new LanguageModelProtocolException(
            "流式模型调用结束时没有返回完成结果。",
            new LanguageModelErrorDetails { RunId = operation.Id, Operation = "Stream" });
    }

    private void ValidateStaticCapabilities()
    {
        if (_settings.ToolCalls == FeaturePolicy.Required &&
            !_client.Capabilities.SupportsToolCalls)
            throw LanguageModelConfigurationException.Unsupported(
                _client.Capabilities.Provider,
                "ToolCalls");
        if (_settings.NativeJsonOutput == FeaturePolicy.Required &&
            !_client.Capabilities.SupportsNativeJsonOutput)
            throw LanguageModelConfigurationException.Unsupported(
                _client.Capabilities.Provider,
                "NativeJsonOutput");
        if (_settings.Streaming == FeaturePolicy.Required &&
            !_client.Capabilities.SupportsStreaming)
            throw LanguageModelConfigurationException.Unsupported(
                _client.Capabilities.Provider,
                "Streaming");
    }

    private void ValidateRequest(LanguageModelRunRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.Conversation);
        ArgumentNullException.ThrowIfNull(request.Tools);
        ArgumentNullException.ThrowIfNull(request.OutputValidator);
        if (!Enum.IsDefined(request.ToolCallMode))
            throw new ArgumentOutOfRangeException(nameof(request.ToolCallMode));
        if (request.Conversation.Messages.Count == 0)
            throw new ArgumentException("语言模型会话至少需要一条消息。", nameof(request));
        if (request.Tools.Any(tool => tool is null))
            throw new ArgumentException("工具集合不能包含 null。", nameof(request));
        string? duplicate = request.Tools.GroupBy(tool => tool.name, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)?.Key;
        if (duplicate is not null)
            throw new ArgumentException($"工具名称“{duplicate}”重复。", nameof(request));
        if (request.Tools.Any(tool => string.IsNullOrWhiteSpace(tool.name)))
            throw new ArgumentException("工具名称不能为空。", nameof(request));
        if (request.ToolCallMode == ToolCallMode.Required && request.Tools.Count == 0)
            throw new ArgumentException("强制工具调用模式至少需要注册一个工具。", nameof(request));
        if (_settings.ToolCalls == FeaturePolicy.Disabled &&
            (request.ToolCallMode != ToolCallMode.None || request.Tools.Count > 0))
            throw LanguageModelConfigurationException.Invalid("业务设置已禁用工具调用。");
        if (request.Tools.Count > 0 && !_client.Capabilities.SupportsToolCalls)
            throw LanguageModelConfigurationException.Unsupported(
                _client.Capabilities.Provider,
                "ToolCalls");
    }

    private bool ShouldUseNativeJson(IModelOutputValidator validator)
    {
        if (validator.JsonFormat is null || _settings.NativeJsonOutput == FeaturePolicy.Disabled)
            return false;
        if (!_client.Capabilities.SupportsNativeJsonOutput)
            return false;
        if (validator.JsonFormat.Schema is not null && !_client.Capabilities.SupportsJsonSchema)
        {
            if (_settings.NativeJsonOutput == FeaturePolicy.Required)
                throw LanguageModelConfigurationException.Unsupported(
                    _client.Capabilities.Provider,
                    "JsonSchema");
            return false;
        }
        return true;
    }

    private LanguageModelProtocolException Protocol(string message, Guid runId) =>
        new(message, RunDetails(runId, "Complete"));

    private LanguageModelErrorDetails RunDetails(Guid runId, string operation) =>
        new()
        {
            Provider = _client.Capabilities.Provider,
            RunId = runId,
            Operation = operation
        };

    private LanguageModelErrorDetails ToolDetails(Guid runId, ModelToolCall call) =>
        new()
        {
            Provider = _client.Capabilities.Provider,
            RunId = runId,
            Operation = "ExecuteTool",
            ToolName = call.Name,
            ToolCallId = call.CallId
        };

    private void Log(
        LogLevel level,
        string message,
        Guid runId,
        Exception? exception = null)
    {
        _logger.Log(
            level,
            LogCategory,
            message,
            exception,
            new Dictionary<string, object?> { ["RunId"] = runId });
    }
}

/// <summary>
/// 负责加载、验证和保存 Application 语言模型设置。
/// </summary>
public sealed class LanguageModelSettingsService
{
    private readonly ILanguageModelSettingsStore _store;

    public LanguageModelSettingsService(ILanguageModelSettingsStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<LanguageModelSettings> LoadOrDefaultAsync(
        CancellationToken cancellationToken = default)
    {
        LanguageModelSettings settings =
            await _store.LoadAsync(cancellationToken) ?? new LanguageModelSettings();
        settings.Validate();
        return settings;
    }

    public async Task SaveAsync(
        LanguageModelSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        await _store.SaveAsync(settings, cancellationToken);
    }
}
