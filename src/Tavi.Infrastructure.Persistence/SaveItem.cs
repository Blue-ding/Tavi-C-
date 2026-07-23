using System.Reflection;
using System.Text.Json;

namespace Tavi.Infrastructure.Persistence;

/// <summary>
/// 保存一个已订阅对象，并在读取存档后将反序列化结果回填到该对象。
/// </summary>
internal sealed class SaveItem
{
    internal Type Type { get; }
    internal bool Initialized { get; private set; }
    internal object Content { get; }

    internal SaveItem(object content)
    {
        Content = content ?? throw new ArgumentNullException(nameof(content));
        Type = content.GetType();
    }

    internal JsonElement Serialize(JsonSerializerOptions options)
    {
        return JsonSerializer.SerializeToElement(Content, Type, options);
    }

    internal void Populate(JsonElement data, JsonSerializerOptions options)
    {
        object source = data.Deserialize(Type, options)
                        ?? throw new SaveException($"无法反序列化存档项 {Type.FullName}。");

        CopyPublicState(source, Content, Type);
        Initialized = true;
    }

    internal void MarkInitialized()
    {
        Initialized = true;
    }

    private static void CopyPublicState(object source, object target, Type type)
    {
        foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!field.IsInitOnly)
            {
                field.SetValue(target, field.GetValue(source));
            }
        }

        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.SetMethod is not null && property.GetIndexParameters().Length == 0)
            {
                property.SetValue(target, property.GetValue(source));
            }
        }
    }
}

