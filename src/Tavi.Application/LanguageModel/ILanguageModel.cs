using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Tavi.Application.LanguageModel
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
        public string SystemPrompt = string.Empty;
        public List<ITool> Tools = new();
        public string JsonOutputConstraint = string.Empty;
        public string UserContext = string.Empty;
        

        public string Result = string.Empty;
    }
}
