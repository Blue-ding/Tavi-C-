using Tavi.Domain.Performance;

namespace Tavi.Application.Performance;

/// <summary>提供始终通过 PerformanceSession 同步边界读取最新状态的查询。</summary>
public sealed class PerformanceQueries
{
    private readonly PerformanceSession _session;

    internal PerformanceQueries(PerformanceSession session) => _session = session;

    public PerformanceSnapshot CreateSnapshot() => _session.CreateSnapshot();

    public Beat GetBeat(Guid beatId)
    {
        PerformanceSnapshot snapshot = _session.CreateSnapshot();
        return snapshot.Beats.TryGetValue(beatId, out Beat? beat)
            ? beat
            : throw new KeyNotFoundException($"Performance 中不存在 Beat {beatId}。");
    }

    public IReadOnlyList<Beat> GetBeats()
        => _session.CreateSnapshot().Beats.Values.OrderBy(value => value.Name, StringComparer.Ordinal).ThenBy(value => value.Id).ToArray();
}
