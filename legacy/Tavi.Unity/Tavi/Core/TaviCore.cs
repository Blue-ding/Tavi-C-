using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Tavi.Core.LanguageModel;
using Tavi.Core.WorldGraph;

namespace Tavi.Core
{
    public class TaviCore
    {
        public static ILanguageModel languageModel { get; private set; }

        private static ILogger _logger;
        public static ILogger logger => _logger ??= UniqueInterfaceUtility.Instantiate<ILogger>();
        
        public static async Task Init(object llmConfig)
        {
            languageModel = UniqueInterfaceUtility.Instantiate<ILanguageModel>();
            languageModel.Init(llmConfig);
            
            logger.Log("TaviCore 初始化完成");
        }

        public static async Task Test()
        {
            string output = await languageModel.GenerateAsync(new Message() { UserContext = "你好啊！" });
            logger.Log(output);
        }

    }
}
