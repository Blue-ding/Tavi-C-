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
        public Guid Id = Guid.NewGuid();
    }

    [Serializable]
    public record Anchor
    {
        public AnchorId Id = new();
        public string Name;
        public string Description;
        public AnchorType Type;

        internal Anchor(string name, string description,AnchorType type)
        {
            this.Name=name;
            this.Description=description;
            this.Type=type;
        }
    }

}
