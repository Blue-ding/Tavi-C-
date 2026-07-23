using System;

namespace Tavi.Domain.WorldGraph
{
    [Serializable]
    public record RelationId
    {
        public Guid ID = Guid.NewGuid();
    }
    
    [Serializable]
    public record Relation
    {
        public RelationId ID = new();
        public string Name;
        public string Description;
        public AnchorId SourceID;
        public AnchorId TargetID;

        internal Relation(string Name, string Description, AnchorId SourceID, AnchorId TargetID)
        {
            this.Name=Name;
            this.Description=Description;
            this.SourceID=SourceID;
            this.TargetID=TargetID;
        }
    }
}
