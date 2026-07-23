using System;

namespace Tavi.Domain.WorldGraph
{
    public enum AnchorType
    {
        Character, Item
    }

    [Serializable]
    public record AnchorId
    {
        public Guid ID = Guid.NewGuid();
    }
    
    [Serializable]
    public record Anchor
    {
        public AnchorId ID = new();
        public string name;
        public string description;
        public AnchorType type;
        
        internal Anchor(string name, string description,AnchorType type)
        {
            this.name=name;
            this.description=description;
            this.type=type;
        }
    }
    
}
