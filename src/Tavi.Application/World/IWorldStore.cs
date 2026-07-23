using Tavi.Domain.World;

namespace Tavi.Application.World;

/// <summary>
/// 定义世界快照的持久化端口。
/// </summary>
public interface IWorldStore
{
    /// <summary>
    /// 读取指定存档槽；存档不存在时返回 null。
    /// </summary>
    Task<WorldSnapshot?> LoadAsync(string slot, CancellationToken cancellationToken = default);

    /// <summary>
    /// 将世界快照写入指定存档槽。
    /// </summary>
    Task SaveAsync(string slot, WorldSnapshot snapshot, CancellationToken cancellationToken = default);
}
