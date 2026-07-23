namespace Tavi.Domain.World
{
    /// <summary>
    /// 世界图在特定时刻的独立领域快照。
    /// WorldGraph 在初始化时复制并校验该数据，之后不再受快照外部修改影响。
    /// </summary>
    public sealed record WorldSnapshot
    {
        /// <summary>
        /// 获取或设置世界标识。
        /// </summary>
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// 获取或设置按标识索引的全部锚点。
        /// </summary>
        public Dictionary<Guid, Anchor> Anchors { get; set; } = new();

        /// <summary>
        /// 获取或设置按标识索引的主世界关系。
        /// </summary>
        public Dictionary<Guid, Relation> Relations { get; set; } = new();

        /// <summary>
        /// 获取或设置全部子世界。
        /// </summary>
        public List<SubWorldSnapshot> SubWorlds { get; set; } = new();
    }

    /// <summary>
    /// 归属于一个 Character 的子世界快照。
    /// </summary>
    public sealed record SubWorldSnapshot
    {
        /// <summary>
        /// 获取或设置子世界标识。
        /// </summary>
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// 获取或设置持有该子世界的 Character 标识。
        /// </summary>
        public Guid DomainId { get; set; }

        /// <summary>
        /// 获取或设置按标识索引的子世界关系。
        /// </summary>
        public Dictionary<Guid, Relation> Relations { get; set; } = new();
    }
}
