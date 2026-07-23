namespace Tavi.Application.LanguageModel
{
    [UniqueInterface]
    public interface ILogger
    {
        public void Log(string msg);
        public void LogError(string msg);
        public void LogWarning(string msg);
    }
}
