using Tavi.Domain.Story;

namespace Tavi.Application.Writing;

/// <summary>提供始终通过 WritingSession 同步边界读取最新手稿状态的查询。</summary>
public sealed class WritingQueries
{
    private readonly WritingSession _session;

    internal WritingQueries(WritingSession session) => _session = session;

    /// <summary>获取当前活动手稿及暂存投影的权威快照。</summary>
    public WritingSnapshot CreateSnapshot() => _session.GetSnapshot();

    /// <summary>列出全部手稿摘要，包含当前活动手稿和归档手稿。</summary>
    public Task<IReadOnlyList<ManuscriptSummary>> ListAsync(CancellationToken cancellationToken = default)
        => _session.ListAsync(cancellationToken);

    /// <summary>读取指定手稿；活动手稿返回当前已提交内容，归档手稿返回只读正文。</summary>
    public Task<Manuscript> GetAsync(Guid manuscriptId, CancellationToken cancellationToken = default)
        => _session.GetAsync(manuscriptId, cancellationToken);
}
