using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tavi.Infrastructure.Persistence;

internal enum SaveState
{
    Uninitialized,
    Idle,
    Running
}

public sealed class SaveData
{
    public Dictionary<string, JsonElement> Data { get; set; } = new();
}

public sealed class SaveException : Exception
{
    public SaveException(string message) : base(message)
    {
    }

    public SaveException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public static class SaveSystem
{
    private static readonly string SavePath =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Tavi",
            "save.json"
        );

    private static readonly List<SaveItem> Persistent = new();

    private static SaveState _state = SaveState.Uninitialized;
    private static int _savingFlag;
    private static SaveData _tempData = new();
    private static JsonSerializerOptions _options = new();

    public static void Init()
    {
        _options = new JsonSerializerOptions
        {
            IncludeFields = true,
            WriteIndented = true
        };
        _options.Converters.Add(new JsonStringEnumConverter());

        _tempData = new SaveData();
        _state = SaveState.Idle;
    }

    public static void Lock()
    {
        _savingFlag++;
    }

    public static void Unlock()
    {
        _savingFlag--;
    }

    public static bool CheckState()
    {
        return _state == SaveState.Idle;
    }

    public static void Subscribe(object saveItem)
    {
        ArgumentNullException.ThrowIfNull(saveItem);

        if (Persistent.Exists(item => item.Type == saveItem.GetType()))
        {
            throw new SaveException($"类型 {saveItem.GetType().FullName} 已订阅。");
        }

        Persistent.Add(new SaveItem(saveItem));
    }

    public static async Task WaitUntilInitialized(
        object saveItem,
        CancellationToken cancellationToken = default
    )
    {
        EnsureInitialized();
        ArgumentNullException.ThrowIfNull(saveItem);

        SaveItem persistent = Persistent.Find(item => item.Type == saveItem.GetType())
                              ?? throw new SaveException(
                                  $"类型 {saveItem.GetType().FullName} 尚未订阅。"
                              );

        using var timeoutSource = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using CancellationTokenSource linkedSource =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutSource.Token
            );

        while (!persistent.Initialized)
        {
            await Task.Delay(100, linkedSource.Token);
        }
    }

    public static async Task Save(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        try
        {
            if (_state == SaveState.Running || _savingFlag > 0)
            {
                return;
            }

            _state = SaveState.Running;
            await ForceSave(cancellationToken);
        }
        catch (Exception exception) when (exception is not SaveException)
        {
            throw new SaveException("保存存档异常。", exception);
        }
        finally
        {
            _state = SaveState.Idle;
        }
    }

    public static async Task Load(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        try
        {
            if (_state == SaveState.Running)
            {
                return;
            }

            _state = SaveState.Running;

            if (!File.Exists(SavePath))
            {
                await ForceSave(cancellationToken);
                return;
            }

            string json = await File.ReadAllTextAsync(SavePath, cancellationToken);
            _tempData = JsonSerializer.Deserialize<SaveData>(json, _options)
                        ?? throw new SaveException("存档内容为空。");

            foreach (SaveItem persistent in Persistent)
            {
                if (!_tempData.Data.TryGetValue(persistent.Type.Name, out JsonElement data))
                {
                    throw new SaveException($"存档中缺少类型 {persistent.Type.FullName}。");
                }

                persistent.Populate(data, _options);
            }
        }
        catch (Exception exception) when (exception is not SaveException)
        {
            throw new SaveException("读取存档异常。", exception);
        }
        finally
        {
            _state = SaveState.Idle;
        }
    }

    private static async Task ForceSave(CancellationToken cancellationToken)
    {
        EnsureInitialized();
        _tempData.Data.Clear();

        foreach (SaveItem persistent in Persistent)
        {
            _tempData.Data[persistent.Type.Name] = persistent.Serialize(_options);
        }

        string? directory = Path.GetDirectoryName(SavePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(_tempData, _options);
        await File.WriteAllTextAsync(SavePath, json, cancellationToken);

        foreach (SaveItem persistent in Persistent)
        {
            persistent.MarkInitialized();
        }
    }

    private static void EnsureInitialized()
    {
        if (_state == SaveState.Uninitialized)
        {
            throw new SaveException("SaveSystem 未初始化！");
        }
    }
}

