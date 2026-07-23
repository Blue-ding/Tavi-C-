using System.Collections.Concurrent;
using Tavi.Application.LanguageModel;
using Tavi.Application.Logging;
using Tavi.Application.World;
using Tavi.Domain.World;

namespace Tavi.Application.Guidance;

/// <summary>在单个 WorldSession 上编排 Guidance 对话、草稿和提交。</summary>
internal sealed class GuidanceService : IGuidanceService
{
    private const string LogCategory = "GuidanceService";
    private const string SystemInstruction = """
        你是 Tavi 的 Guidance。你的目标是从玩家给出的微小叙事势能出发，协助构筑可供审阅的 World 提案。
        你可以使用只读工具了解当前 World，但绝不能直接修改真实 World。
        当信息足够时，使用 propose_anchor、propose_relation 和 set_guidance_summary 构造一份完整草稿；每次生成都必须从完整草稿开始，并为每项修改提供本轮唯一的 ChangeId。
        Relation 引用本轮 Anchor 时必须使用对应 propose_anchor 的 ChangeId。完成工具调用后，用自然语言简要回应玩家。
        当信息不足时可以直接向玩家提出一个聚焦问题，此时不必创建提案。
        """;

    private readonly WorldSession _world;
    private readonly ILanguageModelService _languageModels;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<Guid, GuidanceContext> _sessions = new();

    internal GuidanceService(WorldSession world, ILanguageModelService languageModels, ILogger? logger = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _languageModels = languageModels ?? throw new ArgumentNullException(nameof(languageModels));
        _logger = logger ?? NullLogger.Instance;
    }

    /// <inheritdoc />
    public GuidanceOperation Start(NarrativePotential potential, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(potential);
        var context = new GuidanceContext(Guid.NewGuid(), _world.Revision);
        if (!_sessions.TryAdd(context.Id, context))
            throw Failure(GuidanceErrorCodes.InternalFailure, "Start", "无法登记 Guidance 会话。", context.Id);
        return BeginGeneration(context, new GuidanceMessage(potential.Text), true, cancellationToken);
    }

    /// <inheritdoc />
    public GuidanceOperation Continue(Guid sessionId, GuidanceMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        return BeginGeneration(RequireContext(sessionId, "Continue"), message, false, cancellationToken);
    }

    /// <inheritdoc />
    public GuidanceSnapshot GetSnapshot(Guid sessionId)
    {
        GuidanceContext context = RequireContext(sessionId, "GetSnapshot");
        lock (context.Sync)
            return CreateSnapshot(context);
    }

    /// <inheritdoc />
    public GuidanceCommitResult Commit(Guid sessionId, IReadOnlyCollection<string> acceptedChangeIds)
    {
        ArgumentNullException.ThrowIfNull(acceptedChangeIds);
        GuidanceContext context = RequireContext(sessionId, "Commit");
        GuidanceSession? draft;
        WorldProposal? proposal;
        lock (context.Sync)
        {
            if (context.State != GuidanceState.ReadyForReview || context.Draft is null)
                return NotReady(context.State);
            context.State = GuidanceState.Committing;
            draft = context.Draft;
            proposal = draft.CreateProposal();
        }

        try
        {
            ProposalCompilationResult compilation = WorldProposalCompiler.Compile(proposal, acceptedChangeIds, _world);
            WorldCommitResult commit = _world.Apply(compilation.ChangeSet, proposal.BaseWorldRevision);
            lock (context.Sync)
            {
                draft.Complete();
                context.State = GuidanceState.Completed;
            }
            Log(LogLevel.Information, "Guidance 提案已提交。", context.Id);
            return new GuidanceCommitResult { Status = GuidanceCommitStatus.Committed, WorldRevision = commit.Revision, CreatedAnchorIds = compilation.AnchorIds };
        }
        catch (WorldRevisionConflictException exception)
        {
            lock (context.Sync)
                context.State = GuidanceState.ReadyForReview;
            return new GuidanceCommitResult
            {
                Status = GuidanceCommitStatus.WorldConflict,
                ExpectedWorldRevision = exception.ExpectedRevision,
                ActualWorldRevision = exception.ActualRevision,
                Issues = [new GuidanceIssue { Code = "world_revision_conflict", Message = exception.Message }]
            };
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or WorldException)
        {
            lock (context.Sync)
                context.State = GuidanceState.ReadyForReview;
            return new GuidanceCommitResult
            {
                Status = GuidanceCommitStatus.InvalidSelection,
                Issues = [new GuidanceIssue { Code = "invalid_selection", Message = exception.Message }]
            };
        }
        catch (Exception exception)
        {
            GuidanceException failure = Failure(GuidanceErrorCodes.CommitFailed, "Commit", "Guidance 提案提交失败。", context.Id, exception: exception);
            lock (context.Sync)
            {
                context.State = GuidanceState.Failed;
                context.Failure = failure;
            }
            Log(LogLevel.Error, failure.Message, context.Id, failure);
            throw failure;
        }
    }

    /// <inheritdoc />
    public void Cancel(Guid sessionId)
    {
        GuidanceContext context = RequireContext(sessionId, "Cancel");
        lock (context.Sync)
        {
            if (context.State is GuidanceState.Completed or GuidanceState.Cancelled or GuidanceState.Failed)
                return;
            context.State = GuidanceState.Cancelled;
            if (context.Draft?.State == GuidanceSessionState.Draft)
                context.Draft.Cancel();
            context.ActiveCancellation?.Cancel();
        }
        Log(LogLevel.Information, "Guidance 会话已取消。", context.Id);
    }

    /// <inheritdoc />
    public bool Forget(Guid sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out GuidanceContext? context))
            return false;
        lock (context.Sync)
        {
            if (context.ActiveCancellation is not null || context.State is not (GuidanceState.Completed or GuidanceState.Cancelled or GuidanceState.Failed))
                return false;
            return _sessions.TryRemove(new KeyValuePair<Guid, GuidanceContext>(sessionId, context));
        }
    }

    private GuidanceOperation BeginGeneration(GuidanceContext context, GuidanceMessage message, bool initial, CancellationToken cancellationToken)
    {
        CancellationTokenSource linkedSource;
        LanguageModelConversation conversation;
        lock (context.Sync)
        {
            bool validState = initial ? context.State == GuidanceState.Created : context.State is GuidanceState.AwaitingPlayer or GuidanceState.ReadyForReview;
            if (!validState || context.ActiveCancellation is not null)
                throw Failure(GuidanceErrorCodes.InvalidSessionState, initial ? "Start" : "Continue", $"Guidance 会话当前状态为 {context.State}，不能开始新的生成。", context.Id, details: new Dictionary<string, string> { ["State"] = context.State.ToString() });
            linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            context.ActiveCancellation = linkedSource;
            context.State = GuidanceState.Generating;
            context.Messages.Add(message);
            conversation = initial
                ? new LanguageModelConversation { Messages = [ModelMessage.System(SystemInstruction), ModelMessage.User(message.Text)] }
                : context.Conversation.Append(ModelMessage.User("请依据以下玩家反馈重新考虑并在需要时生成完整提案：" + Environment.NewLine + message.Text));
        }

        var operation = new GuidanceOperation(Guid.NewGuid(), context.Id);
        operation.SetState(GuidanceOperationState.Running);
        _ = GenerateAsync(context, conversation, operation, linkedSource);
        return operation;
    }

    private async Task GenerateAsync(GuidanceContext context, LanguageModelConversation conversation, GuidanceOperation operation, CancellationTokenSource linkedSource)
    {
        var candidate = new GuidanceSession(context.BaseWorldRevision, context.Id);
        try
        {
            IReadOnlyList<ITool> tools = WorldGuidanceTool.CreateQueryTools(_world).Concat(GuidanceProposalTool.CreateTools(candidate, _world)).ToArray();
            LanguageModelOperation modelOperation = _languageModels.Start(new LanguageModelRunRequest { Conversation = conversation, Tools = tools, ToolCallMode = ToolCallMode.Auto }, linkedSource.Token);
            modelOperation.TextReceived += (_, text) => operation.ReportText(text);
            LanguageModelRunResult result = await modelOperation.Completion;
            WorldProposal candidateProposal = candidate.CreateProposal();
            lock (context.Sync)
            {
                if (context.State == GuidanceState.Cancelled)
                    throw new OperationCanceledException(linkedSource.Token);
                context.Conversation = result.Conversation;
                if (!string.IsNullOrWhiteSpace(result.Output))
                    context.Messages.Add(new GuidanceMessage(result.Output));
                if (candidateProposal.Changes.Count > 0)
                    context.Draft = candidate;
                context.State = context.Draft?.CreateProposal().Changes.Count > 0 ? GuidanceState.ReadyForReview : GuidanceState.AwaitingPlayer;
                context.Failure = null;
                ClearActive(context, linkedSource);
                GuidanceSnapshot snapshot = CreateSnapshot(context);
                operation.SetState(GuidanceOperationState.Completed);
                operation.Complete(snapshot);
            }
            Log(LogLevel.Information, "Guidance 生成已完成。", context.Id);
        }
        catch (OperationCanceledException exception)
        {
            lock (context.Sync)
            {
                context.State = GuidanceState.Cancelled;
                if (candidate.State == GuidanceSessionState.Draft)
                    candidate.Cancel();
                ClearActive(context, linkedSource);
                operation.SetState(GuidanceOperationState.Cancelled);
                operation.Cancel(exception.CancellationToken);
            }
        }
        catch (Exception exception)
        {
            GuidanceException failure = exception as GuidanceException ?? CreateGenerationFailure(context.Id, exception);
            lock (context.Sync)
            {
                context.State = GuidanceState.Failed;
                context.Failure = failure;
                if (candidate.State == GuidanceSessionState.Draft)
                    candidate.Cancel();
                ClearActive(context, linkedSource);
                operation.SetState(GuidanceOperationState.Failed);
                operation.Fail(failure);
            }
            Log(LogLevel.Error, failure.Message, context.Id, failure);
        }
        finally
        {
            linkedSource.Dispose();
        }
    }

    private static void ClearActive(GuidanceContext context, CancellationTokenSource source)
    {
        if (ReferenceEquals(context.ActiveCancellation, source))
            context.ActiveCancellation = null;
    }

    private GuidanceContext RequireContext(Guid sessionId, string operation)
    {
        if (_sessions.TryGetValue(sessionId, out GuidanceContext? context))
            return context;
        throw Failure(GuidanceErrorCodes.SessionNotFound, operation, $"不存在 Guidance 会话 {sessionId}。", sessionId, details: new Dictionary<string, string> { ["RequestedSessionId"] = sessionId.ToString() });
    }

    private static GuidanceSnapshot CreateSnapshot(GuidanceContext context)
    {
        return new GuidanceSnapshot
        {
            SessionId = context.Id,
            State = context.State,
            BaseWorldRevision = context.BaseWorldRevision,
            Messages = Array.AsReadOnly(context.Messages.ToArray()),
            Proposal = context.Draft?.CreateProposal(),
            Failure = context.Failure
        };
    }

    private static GuidanceCommitResult NotReady(GuidanceState state)
    {
        return new GuidanceCommitResult
        {
            Status = GuidanceCommitStatus.SessionNotReady,
            Issues = [new GuidanceIssue { Code = "session_not_ready", Message = $"Guidance 会话当前状态为 {state}，没有可提交的提案。" }]
        };
    }

    private static GuidanceException CreateGenerationFailure(Guid sessionId, Exception exception)
    {
        var details = new Dictionary<string, string> { ["ExceptionType"] = exception.GetType().FullName ?? exception.GetType().Name };
        bool isTransient = false;
        if (exception is LanguageModelException languageModelException)
        {
            details["LanguageModelErrorCode"] = languageModelException.ErrorCode;
            details["LanguageModelCategory"] = languageModelException.Category.ToString();
            isTransient = languageModelException.IsTransient;
        }
        return Failure(GuidanceErrorCodes.GenerationFailed, "Generate", "Guidance 无法完成本次生成。", sessionId, isTransient, details, exception);
    }

    private static GuidanceException Failure(string errorCode, string operation, string message, Guid? sessionId = null, bool isTransient = false, IReadOnlyDictionary<string, string>? details = null, Exception? exception = null)
    {
        return new GuidanceException(errorCode, operation, message, sessionId, isTransient, details, exception);
    }

    private void Log(LogLevel level, string message, Guid sessionId, Exception? exception = null)
    {
        _logger.Log(level, LogCategory, message, exception, new Dictionary<string, object?> { ["SessionId"] = sessionId });
    }

    private sealed class GuidanceContext(Guid id, long baseWorldRevision)
    {
        internal object Sync { get; } = new();
        internal Guid Id { get; } = id;
        internal long BaseWorldRevision { get; } = baseWorldRevision;
        internal GuidanceState State { get; set; } = GuidanceState.Created;
        internal List<GuidanceMessage> Messages { get; } = [];
        internal LanguageModelConversation Conversation { get; set; } = new();
        internal GuidanceSession? Draft { get; set; }
        internal GuidanceException? Failure { get; set; }
        internal CancellationTokenSource? ActiveCancellation { get; set; }
    }
}
