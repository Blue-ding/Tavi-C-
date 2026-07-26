using System.Collections.Concurrent;
using Tavi.Application.LanguageModel;

namespace Tavi.Runtime;

/// <summary>
/// 为长期存活的 GuidanceService 提供可热替换的语言模型执行器。
/// 已启动的运行继续由原执行器完成，之后启动的运行使用最新配置。
/// </summary>
internal sealed class ReloadableLanguageModelService : ILanguageModelService
{
    private readonly ConcurrentDictionary<Guid, ILanguageModelService> _operationOwners = new();
    private ILanguageModelService _current;

    internal ReloadableLanguageModelService(ILanguageModelService current)
    {
        _current = current ?? throw new ArgumentNullException(nameof(current));
    }

    public LanguageModelCapabilities Capabilities => Volatile.Read(ref _current).Capabilities;

    public LanguageModelOperation Start(
        LanguageModelRunRequest request,
        CancellationToken cancellationToken = default)
    {
        ILanguageModelService owner = Volatile.Read(ref _current);
        LanguageModelOperation operation = owner.Start(request, cancellationToken);
        _operationOwners[operation.Id] = owner;
        return operation;
    }

    public bool TryGetOperation(Guid runId, out LanguageModelOperation? operation)
    {
        if (_operationOwners.TryGetValue(runId, out ILanguageModelService? owner))
            return owner.TryGetOperation(runId, out operation);

        operation = null;
        return false;
    }

    public bool ForgetOperation(Guid runId)
    {
        return _operationOwners.TryRemove(runId, out ILanguageModelService? owner)
            && owner.ForgetOperation(runId);
    }

    internal void Replace(ILanguageModelService replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        Interlocked.Exchange(ref _current, replacement);
    }
}
