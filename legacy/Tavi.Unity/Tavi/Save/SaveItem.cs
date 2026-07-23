using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace Tavi.Save
{
    /// <summary>
    /// 可持久化数据
    /// 注意其中任何写操作需要锁 SaveSystem
    /// 同时需要检查 SaveSystem 状态
    /// 建议配置默认值
    /// </summary>
    internal class SaveItem
    {
        internal Type type { get; }
        internal bool initialized { get; private set; }
        internal object content { get; private set; }

        internal SaveItem(object content)
        {
            type = content.GetType();
            this.content = content;
            initialized = false;
        }
    }
    
}
