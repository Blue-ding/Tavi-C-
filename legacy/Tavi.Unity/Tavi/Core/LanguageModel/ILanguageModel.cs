using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Tavi.Core.LanguageModel
{
    [UniqueInterface]
    public interface ILanguageModel
    {
        public void Init(object config);
        public Task<string> GenerateAsync(Message message, CancellationToken cancellationToken = default);
    }
    
    public sealed class LanguageModelException : Exception
    {
        public LanguageModelException(string message) : base(message)
        {
            
        }
    }

    public class Message
    {
        public string SystemPrompt;
        public List<ITool> Tools;
        public string JsonOutputConstraint;
        public string UserContext;
        

        public string Result;
    }
}