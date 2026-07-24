namespace Tavi.Domain.World;

/// <summary>表示世界断言图在特定时刻的独立领域快照；World 会在初始化时深复制并完整校验该数据。</summary>
public sealed record WorldSnapshot
{
    /// <summary>获取或设置不透明的世界状态标识；每次实际领域写入后都会生成新值，但该值不表达时间顺序。</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>获取或设置按标识索引的全部 Element。</summary>
    public Dictionary<Guid, Element> Elements { get; set; } = new();

    /// <summary>获取或设置按标识索引的全部一元断言。</summary>
    public Dictionary<Guid, Aspect> Aspects { get; set; } = new();

    /// <summary>获取或设置按标识索引的全部二元断言。</summary>
    public Dictionary<Guid, Relation> Relations { get; set; } = new();

    /// <summary>获取或设置按标识索引的全部断言域。</summary>
    public Dictionary<Guid, Scope> Scopes { get; set; } = new();
}
