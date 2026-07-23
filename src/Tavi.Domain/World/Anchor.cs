using System.Text.Json.Serialization;

namespace Tavi.Domain.World
{
    /// <summary>
    /// 锚点类型。
    /// Character 是唯一具有特殊领域行为的类型：它可以持有一个子世界。
    /// </summary>
    public enum AnchorType
    {
        Character,
        Item
    }

    /// <summary>
    /// 世界数据的锚点，表征一个叙事要素。
    /// </summary>
    public sealed record Anchor
    {
        /// <summary>
        /// 根据持久化数据创建 Anchor。
        /// </summary>
        [JsonConstructor]
        public Anchor(Guid id, string name, string description, AnchorType type)
        {
            Id = id;
            Name = name;
            Description = description;
            Type = type;
        }

        public Guid Id { get; }
        public string Name { get; private set; }
        public string Description { get; private set; }
        public AnchorType Type { get; private set; }

        internal static Anchor Create(string name, string description, AnchorType type)
        {
            return new Anchor(Guid.NewGuid(), name, description, type);
        }

        internal void UpdateName(string name)
        {
            Name = name;
        }

        internal void UpdateDescription(string description)
        {
            Description = description;
        }

        internal void UpdateType(AnchorType type)
        {
            Type = type;
        }
    }
}
