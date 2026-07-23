namespace Tavi.Domain.World
{
    /// <summary>
    /// Anchor 之间的联系。除描述外，结构性变化应通过删除后重建完成。
    /// </summary>
    public sealed record Relation
    {
        /// <summary>
        /// 使用指定标识和属性创建 Relation。
        /// </summary>
        public Relation(Guid id, string name, string description, Guid sourceId, Guid targetId)
        {
            Id = id;
            Name = name;
            Description = description;
            SourceId = sourceId;
            TargetId = targetId;
        }

        public Guid Id { get; }
        public string Name { get; private set; }
        public string Description { get; private set; }
        public Guid SourceId { get; }
        public Guid TargetId { get; }

        internal static Relation Create(string name, string description, Guid sourceId, Guid targetId)
        {
            return new Relation(Guid.NewGuid(), name, description, sourceId, targetId);
        }
        internal void UpdateName(string name)
        {
            Name = name;
        }

        internal void UpdateDescription(string description)
        {
            Description = description;
        }
    }
}
