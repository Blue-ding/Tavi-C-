using Tavi.Domain.World;

namespace Tavi.Application.World
{
    internal static class WorldGraphLlmFunction
    {
        private string ParseAnchor(Anchor anchor)
        {
            // TODO 将 Anchor 信息序列化为 string
        }

        /// <summary>
        /// 依据精确的名称返回 Anchor 信息
        /// </summary>
        public string GetAnchor(WorldGraph graph, string name)
        {
            // TODO
        }

        /// <summary>
        /// 依据线索查询 top-k Anchor
        /// </summary>
        public string QueryAnchor(WorldGraph graph,int k,params string[] clues)
        {}



    }
}
