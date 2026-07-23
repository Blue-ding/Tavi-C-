using System;

namespace Tavi.Domain.WorldGraph
{
    [Serializable]
    public record RelationId
    {
        public Guid Id = Guid.NewGuid();
    }

    [Serializable]
    public record Relation
    {
        public RelationId Id = new();
        public string Name;
        public string Description;
        public AnchorId SourceId;
        public AnchorId TargetId;

        internal Relation(string name, string description, AnchorId sourceId, AnchorId targetId)
        {
            Name=name;
            Description=description;
            SourceId=sourceId;
            TargetId=targetId;
        }
    }
}
