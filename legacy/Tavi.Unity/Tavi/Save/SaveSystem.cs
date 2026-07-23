using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace Tavi.Save
{
    enum SaveState
    {
        UnInit, Idle, Running
    }

    [Serializable]
    public class Save
    {
        public Dictionary<string, JToken> Data = new();
    }

    public class SaveException : Exception
    {
        public SaveException(string message) : base(message)
        {
        }
    }

    public static class SaveSystem
    {
        private static SaveState _state = SaveState.UnInit;

        private static readonly string SavePath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Tavi/save.json");

        private static readonly List<SaveItem> Persistent = new();
        private static int _savingFlag;
        private static Save _tempData;
        private static JsonSerializerSettings _settings;
        private static JsonSerializer _jsonSerializer;

        public static void Init()
        {
            _settings = new();
            _settings.Formatting = Formatting.Indented;
            _settings.Converters.Add(new StringEnumConverter());
            _settings.Converters.Add(new VersionConverter());
            _jsonSerializer = JsonSerializer.Create(_settings);
            _tempData = new();
            _tempData.Data = new();
            _state = SaveState.Idle;
        }

        public static void Lock()
        {
            _savingFlag++;
        }

        public static void UnLock()
        {
            _savingFlag--;
        }

        public static bool CheckState()
        {
            return _state == SaveState.Idle;
        }

        public static void Subscribe(object saveItem)
        {
            SaveItem item = new SaveItem(saveItem);
            Persistent.Add(item);
        }

        public static async Task WaitUntilInitialized(object saveItem)
        {
            if (_state == SaveState.UnInit) throw new SaveException("SaveSystem 未初始化！");
            
            using CancellationTokenSource cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(30));
            while (!Persistent.Find(t => t.type == saveItem.GetType()).initialized)
            {
                cts.Token.ThrowIfCancellationRequested();
                await Task.Delay(500);
            }
        }
        public static async Task Save()
        {
            if (_state == SaveState.UnInit) throw new SaveException("SaveSystem 未初始化！");
            try
            {
                if (_state == SaveState.Running) return;
                _state = SaveState.Running;
                await ForceSave();
            }
            catch (Exception e)
            {
                throw new SaveException("保存存档异常：" + e.Message);
            }
            finally
            {
                _state = SaveState.Idle;
            }
        }

        private static async Task ForceSave()
        {
            if (_state == SaveState.UnInit) throw new SaveException("SaveSystem 未初始化！");
            _tempData.Data.Clear();
            foreach (SaveItem persistent in Persistent)
            {
                _tempData.Data[persistent.type.Name] =
                    JToken.FromObject(persistent.content, JsonSerializer.Create(_settings));
            }

            string directory = Path.GetDirectoryName(SavePath);
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(SavePath, JsonConvert.SerializeObject(_tempData, _settings));
        }

        public static async Task Load()
        {
            if (_state == SaveState.UnInit) throw new SaveException("SaveSystem 未初始化！");
            try
            {
                if (_state == SaveState.Running) return;
                _state = SaveState.Running;
                if (!File.Exists(SavePath))
                {
                    await ForceSave();
                }

                string json = await File.ReadAllTextAsync(SavePath);
                _tempData = JsonConvert.DeserializeObject<Save>(json, _settings);
                foreach (SaveItem persistent in Persistent)
                {
                    using JsonReader reader = _tempData.Data[persistent.type.Name].CreateReader();
                    _jsonSerializer.Populate(reader, persistent.content);
                }
            }
            catch (Exception e)
            {
                throw new SaveException("读取存档异常：" + e.Message);
            }
            finally
            {
                _state = SaveState.Idle;
            }
        }
    }
}