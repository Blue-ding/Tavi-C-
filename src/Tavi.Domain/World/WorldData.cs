using System.Text.Json.Serialization;

namespace Tavi.Domain.World
{
    /// <summary>
    /// 可序列化的世界数据快照。
    /// WorldGraph 在初始化时复制并校验该数据，之后不再受快照外部修改影响。
    /// </summary>
    [Serializable]
    public sealed record WorldData
    {
        [JsonInclude]
        [JsonPropertyName("id")]
        public Guid Id { get; set; } = Guid.NewGuid();

        [JsonInclude]
        [JsonPropertyName("anchors")]
        public Dictionary<Guid, Anchor> Anchors { get; set; } = new();

        [JsonInclude]
        [JsonPropertyName("relations")]
        public Dictionary<Guid, Relation> Relations { get; set; } = new();

        [JsonInclude]
        [JsonPropertyName("sub_world_data")]
        public List<SubWorldData> SubWorldData { get; set; } = new();
    }

    /// <summary>
    /// 归属于一个 Character 的可序列化子世界快照。
    /// </summary>
    [Serializable]
    public sealed record SubWorldData
    {
        [JsonInclude]
        [JsonPropertyName("id")]
        public Guid Id { get; set; } = Guid.NewGuid();

        [JsonInclude]
        [JsonPropertyName("domain")]
        public Guid DomainId { get; set; }

        [JsonInclude]
        [JsonPropertyName("relations")]
        public Dictionary<Guid, Relation> Relations { get; set; } = new();
    }
}
