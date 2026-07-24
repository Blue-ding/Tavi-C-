using System.Diagnostics;

namespace Tavi.Host.Logging;

/// <summary>在正式日志系统不可用时向进程外部提供最小、无依赖的诊断输出。</summary>
internal static class EmergencyLog
{
    private static readonly object Gate = new();

    /// <summary>将应急诊断信息同步写入标准错误和调试器。</summary>
    internal static void Write(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;
        string line = $"{DateTimeOffset.Now:O} [Emergency] {message.TrimEnd()}";
        lock (Gate)
        {
            try
            {
                Console.Error.WriteLine(line);
            }
            catch
            {
                // 应急输出不得反向影响应用启动或退出。
            }
            Debug.WriteLine(line);
        }
    }

    /// <summary>将异常及其上下文写入应急诊断输出。</summary>
    internal static void Write(string message, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Write($"{message}{Environment.NewLine}{exception}");
    }
}
