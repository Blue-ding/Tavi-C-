using Tavi.Application.LanguageModel;
using Tavi.Application.Logging;
using Tavi.Application.World;
using Tavi.Application.Extensions;
using Tavi.Application.Extensions.Guidance;
using Tavi.Application.Extensions.World;
using Tavi.Extensibility;
using Tavi.Domain.World;
using System.Text.Json;

namespace Tavi.Application.Guidance;

/// <summary>维护绑定到单一 World 工作区的长期 Guidance 对话、可恢复操作和消息重试状态。</summary>
internal sealed class GuidanceSession : IGuidanceService
{
    private const string SystemInstruction = """
        你是 Tavi 的 Guidance。你的目标是从玩家给出的微小叙事势能出发，协助构筑可供审阅的 World 暂存修改。
        你可以使用只读工具了解当前 World，但绝不能直接修改真实 World。
        使用 propose_element 和 propose_scope 时，临时标识与修改标识由系统生成；只有收到工具返回的标识后，才能创建引用它们的 Aspect 或 Relation。
        创建或修改 EARS 内容前必须使用 search_world_types 查询对应类别，并且只能使用工具返回的稳定类型键；不要捏造类型键。
        找到已注册类型时使用 propose_aspect 或 propose_relation；找不到合适类型时可使用 propose_local_aspect 或 propose_local_relation 保留自由语义。Local 不参与 Evolution，进入 Scenario 时不会被复制。
        当信息足够时，使用提案工具和 set_guidance_summary 构造本轮新增内容。每个断言与 Local 语义必须属于一个显式 Scope。完成工具调用后，用自然语言简要回应玩家。
        当信息不足时可以直接向玩家提出一个聚焦问题，此时不必创建提案。
        """;
    private const string LogCategory = "GuidanceSession";
    private readonly object _sync = new();
    private readonly WorldGuidanceCoordinator _world;
    private readonly ILanguageModelService _languageModels;
    private readonly ILogger _logger;
    private readonly FrozenModuleRuntime? _extensions;
    private readonly WorldAuthoringCoordinator? _authoring;
    private readonly List<GuidanceMessage> _messages = [];
    private LanguageModelConversation _conversation = new();
    private GuidanceState _state = GuidanceState.Idle;
    private GuidanceException? _failure;
    private GuidanceMessage? _retryMessage;
    private CancellationTokenSource? _activeCancellation;
    private WorldProposal? _latestProposal;
    private Guid _baseWorldStateId;

    /// <summary>创建绑定到指定 World 工作区和语言模型执行器的长期 Guidance Session。</summary>
    public GuidanceSession(WorldGuidanceCoordinator world, ILanguageModelService languageModels, ILogger? logger = null, FrozenModuleRuntime? extensions = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _languageModels = languageModels ?? throw new ArgumentNullException(nameof(languageModels));
        _logger = logger ?? NullLogger.Instance;
        _extensions = extensions;
        _authoring = extensions is null ? null : world.CreateAuthoringCoordinator(extensions);
        _baseWorldStateId = world.CurrentWorldStateId;
        Id = Guid.NewGuid();
    }

    /// <summary>获取长期稳定的 Session 标识。</summary>
    public Guid Id { get; }

    /// <inheritdoc />
    public GuidanceOperation Start(NarrativePotential potential, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(potential);
        lock (_sync)
        {
            if (_messages.Count != 0)
                throw InvalidState("Start", "Guidance 已经开始；请继续对话或先刷新 Session。");
        }
        return BeginGeneration(new GuidanceMessage(potential.Text), true, cancellationToken);
    }

    /// <inheritdoc />
    public GuidanceOperation Continue(Guid sessionId, GuidanceMessage message, CancellationToken cancellationToken = default)
    {
        RequireSession(sessionId);
        ArgumentNullException.ThrowIfNull(message);
        return BeginGeneration(message, false, cancellationToken);
    }

    /// <summary>使用上次失败消息的当前编辑文本重试生成；成功前不会丢弃原消息。</summary>
    public GuidanceOperation Retry(Guid sessionId, GuidanceMessage editedMessage, CancellationToken cancellationToken = default)
    {
        RequireSession(sessionId);
        ArgumentNullException.ThrowIfNull(editedMessage);
        lock (_sync)
        {
            if (_retryMessage is null)
                throw InvalidState("Retry", "当前没有可重试的玩家消息。");
        }
        return BeginGeneration(editedMessage, false, cancellationToken, true);
    }

    /// <inheritdoc />
    public GuidanceSnapshot GetSnapshot(Guid sessionId)
    {
        RequireSession(sessionId);
        lock (_sync)
            return CreateSnapshot();
    }

    /// <inheritdoc />
    public GuidanceCommitResult Commit(Guid sessionId, IReadOnlyCollection<string> acceptedChangeIds)
    {
        RequireSession(sessionId);
        ArgumentNullException.ThrowIfNull(acceptedChangeIds);
        lock (_sync)
        {
            if (_state == GuidanceState.Generating)
                return NotReady("Guidance 正在生成，不能提交 World。");
        }
        Guid[] ids;
        try
        {
            ids = acceptedChangeIds.Select(Guid.Parse).ToArray();
        }
        catch (FormatException)
        {
            return InvalidSelection("暂存项标识格式无效。");
        }
        try
        {
            WorldStagingCommitResult result = _world.Commit(ids);
            lock (_sync)
            {
                string committed = string.Join(", ", result.ConsumedChangeIds);
                _conversation = _conversation.Append(ModelMessage.System($"玩家已提交暂存项：{committed}。当前 WorldStateId={result.Commit.StateId}。"));
                _latestProposal = null;
                _failure = null;
                _state = GuidanceState.Idle;
            }
            return new GuidanceCommitResult { Status = GuidanceCommitStatus.Committed, WorldStateId = result.Commit.StateId };
        }
        catch (WorldStateConflictException exception)
        {
            return new GuidanceCommitResult { Status = GuidanceCommitStatus.WorldConflict, ExpectedWorldStateId = exception.ExpectedStateId, ActualWorldStateId = exception.ActualStateId, Issues = [new GuidanceIssue { Code = "world_state_conflict", Message = exception.Message }] };
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or WorldException)
        {
            return InvalidSelection(exception.Message);
        }
    }

    /// <inheritdoc />
    public void Cancel(Guid sessionId)
    {
        RequireSession(sessionId);
        lock (_sync)
        {
            if (_state != GuidanceState.Generating)
                return;
            _activeCancellation?.Cancel();
        }
    }

    /// <summary>在没有活动生成时遗忘全部 Guidance 对话、失败和运行缓存；World 暂存区不受影响。</summary>
    public void Refresh(Guid sessionId)
    {
        RequireSession(sessionId);
        lock (_sync)
        {
            if (_state == GuidanceState.Generating || _activeCancellation is not null)
                throw InvalidState("Refresh", "Guidance 正在工作；请先主动打断生成。");
            _messages.Clear();
            _conversation = new LanguageModelConversation();
            _failure = null;
            _retryMessage = null;
            _latestProposal = null;
            _baseWorldStateId = _world.CurrentWorldStateId;
            _state = GuidanceState.Idle;
        }
    }

    /// <inheritdoc />
    public bool Forget(Guid sessionId)
    {
        RequireSession(sessionId);
        Refresh(sessionId);
        return true;
    }

    private GuidanceOperation BeginGeneration(GuidanceMessage message, bool initial, CancellationToken cancellationToken, bool retry = false)
    {
        CancellationTokenSource linkedSource;
        LanguageModelConversation conversation;
        WorldStagingSnapshot workspace;
        lock (_sync)
        {
            if (_state == GuidanceState.Generating || _activeCancellation is not null)
                throw InvalidState(retry ? "Retry" : initial ? "Start" : "Continue", "Guidance 正在处理另一项操作。");
            if (!retry && _retryMessage is not null)
                throw InvalidState("Continue", "上一条失败消息尚未处理；请先编辑重试或刷新 Guidance。");
            linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _activeCancellation = linkedSource;
            _state = GuidanceState.Generating;
            _failure = null;
            if (!retry)
                _messages.Add(message);
            else
            {
                int index = _messages.FindLastIndex(item => ReferenceEquals(item, _retryMessage) || item == _retryMessage);
                if (index >= 0)
                    _messages[index] = message;
            }
            workspace = _world.CaptureBasis();
            if (initial || _messages.Count == 1)
                _baseWorldStateId = workspace.WorldStateId;
            string state = CreateWorkspaceContext(workspace);
            bool firstMessage = initial || _conversation.Messages.Count == 0;
            conversation = firstMessage ? new LanguageModelConversation { Messages = [ModelMessage.System(SystemInstruction), ModelMessage.System(state), ModelMessage.User(message.Text)] }
                : _conversation.Append(ModelMessage.System(state), ModelMessage.User(message.Text));
        }
        var operation = new GuidanceOperation(Guid.NewGuid(), Id);
        operation.SetState(GuidanceOperationState.Running);
        _ = GenerateAsync(conversation, message, operation, linkedSource, workspace);
        return operation;
    }

    private async Task GenerateAsync(LanguageModelConversation conversation, GuidanceMessage playerMessage, GuidanceOperation operation, CancellationTokenSource linkedSource, WorldStagingSnapshot workspace)
    {
        var draft = new GuidanceDraft(workspace.WorldStateId, Guid.NewGuid());
        Guid modelOperationId = Guid.Empty;
        try
        {
            ModuleCatalog catalog = _extensions?.Catalog ?? ModuleCatalog.Create([]);
            var tools = _world.CreateQueryTools().Concat(GuidanceProposalTool.CreateTools(draft, workspace.ProjectedWorld, catalog)).ToList();
            if (_extensions is not null && _authoring is not null)
            {
                IWorldView worldView = WorldExtensibilityAdapter.ToView(workspace.ProjectedWorld);
                foreach ((ModuleId module, IGuidanceExtension extension) in _extensions.GuidanceExtensions)
                {
                    IReadOnlyList<GuidanceInstructionContribution> contributions = await extension.GetInstructionsAsync(worldView, _extensions.GetParameters(module), linkedSource.Token);
                    foreach (GuidanceInstructionContribution contribution in contributions)
                    {
                        if (contribution.Id.Namespace != module)
                            throw new ModuleConfigurationException("TAVI.MODULE.GUIDANCE.INSTRUCTION_NAMESPACE", $"Guidance 贡献 {contribution.Id} 不属于 Module {module}。");
                        conversation = conversation.Append(ModelMessage.System(contribution.Content));
                    }
                }
                IReadOnlyList<WorldAuthoringAction> actions = await _authoring.GetActionsAsync(linkedSource.Token);
                tools.AddRange(actions.Select(action => new ModuleWorldAuthoringTool(_authoring, action)));
            }
            LanguageModelOperation modelOperation = _languageModels.Start(new LanguageModelRunRequest { Conversation = conversation, Tools = tools, ToolCallMode = ToolCallMode.Auto }, linkedSource.Token);
            modelOperationId = modelOperation.Id;
            modelOperation.TextReceived += (_, text) => operation.ReportText(text);
            LanguageModelRunResult result = await modelOperation.Completion;
            WorldProposal proposal = draft.CreateProposal();
            WorldProposal published = _world.StageProposal(proposal);
            lock (_sync)
            {
                _conversation = result.Conversation;
                if (!string.IsNullOrWhiteSpace(result.Output))
                    _messages.Add(new GuidanceMessage(result.Output, GuidanceMessageRole.Guidance));
                _latestProposal = published.Changes.Count > 0 ? published : null;
                _retryMessage = null;
                _failure = null;
                _state = GuidanceState.Idle;
                ClearActive(linkedSource);
                GuidanceSnapshot snapshot = CreateSnapshot();
                operation.SetState(GuidanceOperationState.Completed);
                operation.Complete(snapshot);
            }
        }
        catch (OperationCanceledException exception)
        {
            lock (_sync)
            {
                _retryMessage = playerMessage;
                _state = GuidanceState.Idle;
                ClearActive(linkedSource);
                operation.SetState(GuidanceOperationState.Cancelled);
                operation.Cancel(exception.CancellationToken);
            }
        }
        catch (Exception exception)
        {
            GuidanceException failure = exception as GuidanceException ?? CreateGenerationFailure(exception);
            lock (_sync)
            {
                _failure = failure;
                _retryMessage = playerMessage;
                _state = GuidanceState.Idle;
                ClearActive(linkedSource);
                operation.SetState(GuidanceOperationState.Failed);
                operation.Fail(failure);
            }
            _logger.Log(LogLevel.Error, LogCategory, failure.Message, failure, new Dictionary<string, object?> { ["SessionId"] = Id });
        }
        finally
        {
            if (modelOperationId != Guid.Empty)
                _languageModels.ForgetOperation(modelOperationId);
            linkedSource.Dispose();
        }
    }

    private GuidanceSnapshot CreateSnapshot() => new()
    {
        SessionId = Id,
        State = _state,
        BaseWorldStateId = _baseWorldStateId,
        Messages = Array.AsReadOnly(_messages.ToArray()),
        Proposal = _latestProposal,
        Failure = _failure,
        RetryMessage = _retryMessage?.Text
    };

    private static string CreateWorkspaceContext(WorldStagingSnapshot snapshot)
    {
        string changes = string.Join(Environment.NewLine, snapshot.Changes.Select(change => $"- {change.Id}: {change.Status}, {string.Join("; ", change.ChangeSet.Operations)}"));
        string world = JsonSerializer.Serialize(snapshot.ProjectedWorld);
        return $"当前权威状态：WorldStateId={snapshot.WorldStateId}，WorkspaceRevision={snapshot.Revision}。临时 World={world}。暂存项：{Environment.NewLine}{changes}";
    }

    private void ClearActive(CancellationTokenSource source)
    {
        if (ReferenceEquals(_activeCancellation, source))
            _activeCancellation = null;
    }

    private void RequireSession(Guid sessionId)
    {
        if (sessionId != Id)
            throw new GuidanceException(GuidanceErrorCodes.SessionNotFound, "RequireSession", $"不存在 Guidance 会话 {sessionId}。", sessionId);
    }

    private GuidanceException InvalidState(string operation, string message) => new(GuidanceErrorCodes.InvalidSessionState, operation, message, Id);
    private GuidanceException CreateGenerationFailure(Exception exception) => new(GuidanceErrorCodes.GenerationFailed, "Generate", "Guidance 无法完成本次生成；玩家消息已保留，可编辑后重试。", Id, exception is LanguageModelException model && model.IsTransient, innerException: exception);
    private static GuidanceCommitResult NotReady(string message) => new() { Status = GuidanceCommitStatus.SessionNotReady, Issues = [new GuidanceIssue { Code = "session_not_ready", Message = message }] };
    private static GuidanceCommitResult InvalidSelection(string message) => new() { Status = GuidanceCommitStatus.InvalidSelection, Issues = [new GuidanceIssue { Code = "invalid_selection", Message = message }] };
}
