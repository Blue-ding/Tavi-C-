namespace Tavi.Host.Logging;

/// <summary>解析、准备并维护 Host 的本地日志目录。</summary>
internal static class LogDirectoryResolver
{
    internal const string ConfigurationKey = "Tavi:Logging:Directory";
    internal const string EnvironmentVariable = "TAVI_LOG_DIRECTORY";
    private const int RetentionDays = 14;

    /// <summary>按照配置、专用环境变量和本地应用数据目录的顺序解析日志目录。</summary>
    internal static string Resolve(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        string defaultDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tavi", "Logs");
        string configuredDirectory = configuration[ConfigurationKey] ?? Environment.GetEnvironmentVariable(EnvironmentVariable) ?? defaultDirectory;
        return Path.GetFullPath(configuredDirectory);
    }

    /// <summary>创建日志目录并以尽力而为的方式清理超过保留期限的 Tavi 日志。</summary>
    internal static void Prepare(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory.CreateDirectory(directory);
        DateTime cutoffUtc = DateTime.UtcNow.AddDays(-RetentionDays);
        foreach (string path in Directory.EnumerateFiles(directory, "tavi-*.jsonl", SearchOption.TopDirectoryOnly))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(path) < cutoffUtc)
                    File.Delete(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                EmergencyLog.Write($"无法清理过期日志“{path}”。", exception);
            }
        }
    }
}
