using System;
using System.Collections.Generic;

namespace Tavi.Domain.WorldGraph
{
    public class GraphException : Exception
    {
        public GraphException(string message) : base(message)
        {
            
        }
    }
    
    [Serializable]
    public record WorldGraph
    {
        private readonly Dictionary<AnchorId, Anchor> _anchors = new();
        private readonly Dictionary<RelationId, Relation> _relations = new();

        private readonly Dictionary<AnchorId, HashSet<RelationId>> _outgoing = new();
        private readonly Dictionary<AnchorId, HashSet<RelationId>> _incoming = new();
        internal IReadOnlyDictionary<AnchorId, Anchor> anchors => _anchors;
        internal IReadOnlyDictionary<RelationId, Relation> relations => _relations;

        internal void AddAnchor(Anchor anchor)
        {
            try
            {
                _anchors.Add(anchor.ID, anchor);
                _outgoing.Add(anchor.ID,new());
                _incoming.Add(anchor.ID,new());
            }
            catch (Exception e)
            {
                throw new GraphException("图节点添加操作错误："+e.Message);
            }
        }

        internal void AddRelation(Relation relation)
        {
            try
            {
                _relations.Add(relation.ID, relation);
                _outgoing[relation.SourceID].Add(relation.ID);
                _incoming[relation.TargetID].Add(relation.ID);
            }
            catch (Exception e)
            {
                throw new GraphException("图边添加操作错误："+e.Message);
            }
            
        }

        internal void RemoveAnchor(AnchorId anchorID)
        {
            try
            {
                _anchors.Remove(anchorID);
                foreach (var item in _outgoing[anchorID])
                {
                    _relations.Remove(item);
                }
                _outgoing.Remove(anchorID);
                foreach (var item in _incoming[anchorID])
                {
                    _relations.Remove(item);
                }
                _incoming.Remove(anchorID);
            }
            catch (Exception e)
            {
                throw new GraphException("图节点删除操作错误："+e.Message);
            }
        }

        internal void RemoveRelation(RelationId relationId)
        {
            try
            {
                var relation = _relations[relationId];
                _outgoing[relation.SourceID].Remove(relation.ID);
                _incoming[relation.TargetID].Remove(relation.ID);
                _relations.Remove(relation.ID);
            }
            catch (Exception e)
            {
                throw new GraphException("图边删除操作错误："+e.Message);
            }
        }
    }
}

