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
                _anchors.Add(anchor.Id, anchor);
                _outgoing.Add(anchor.Id,new());
                _incoming.Add(anchor.Id,new());
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
                _relations.Add(relation.Id, relation);
                _outgoing[relation.SourceId].Add(relation.Id);
                _incoming[relation.TargetId].Add(relation.Id);
            }
            catch (Exception e)
            {
                throw new GraphException("图边添加操作错误："+e.Message);
            }

        }

        internal void RemoveAnchor(AnchorId anchorId)
        {
            try
            {
                _anchors.Remove(anchorId);
                foreach (var item in _outgoing[anchorId])
                {
                    _relations.Remove(item);
                }
                _outgoing.Remove(anchorId);
                foreach (var item in _incoming[anchorId])
                {
                    _relations.Remove(item);
                }
                _incoming.Remove(anchorId);
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
                _outgoing[relation.SourceId].Remove(relation.Id);
                _incoming[relation.TargetId].Remove(relation.Id);
                _relations.Remove(relation.Id);
            }
            catch (Exception e)
            {
                throw new GraphException("图边删除操作错误："+e.Message);
            }
        }
    }
}

