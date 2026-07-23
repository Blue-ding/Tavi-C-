using Tavi.Application.World;
using Tavi.Infrastructure.Persistence;

namespace Tavi.Cli;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        using var cancellationSource = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellationSource.Cancel();
        };
        string saveDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tavi", "Saves");
        using var store = new JsonFileWorldStore(saveDirectory);
        await using var session = new WorldSession(store);
        try
        {
            await session.InitializeAsync(cancellationSource.Token);
            Console.WriteLine($"Tavi CLI：已加载世界 {session.Current.CreateSnapshot().Id}。");
            Console.WriteLine("工程骨架已就绪，业务命令将在后续迁移阶段接入。");
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("启动已取消。");
            return 1;
        }
        catch (WorldStoreException exception)
        {
            Console.Error.WriteLine($"无法加载世界存档：{exception.Message}");
            return 1;
        }
    }
}
