using System;
using Tavi.Core.LanguageModel;

namespace Tavi.OpenAILlm
{
    [Serializable]
    public record OpenAILlmConfig
    {
        public Uri Uri = new Uri("https://api.openai.com/v1/responses/");
        public string Model = "Default Model";
        public string APIKey = "API Key";
        public ClientType ClientType = ClientType.Chat;
        public bool Debug = false;
        public Version Version => new Version(1, 0);
        public int MaxRound = 8;
    }
}