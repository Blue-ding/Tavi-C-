#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Tavi.Application.LanguageModel
{
    /// <summary>
    /// 工具参数数据结构的标记接口
    /// 使用 DescriptionAttribute 标记描述
    /// </summary>
    public interface IToolArgument
    {
        // [Description("需要查询的名字")]
        // public string name;
    }

    /// <summary>定义可由模型按名称调用并以 JSON 参数执行的 Application 工具。</summary>
    public interface ITool
    {
        string name { get; }

        string description { get; }

        BinaryData parameterData { get; }

        Task<string> Execute(BinaryData arguments, CancellationToken cancellationToken);
    }

    /// <summary>将 BinaryData 参数严格转换为指定类型后执行工具。</summary>
    public abstract class Tool<T> : ITool where T : class, IToolArgument
    {
        private static readonly BinaryData CachedParameterData = ToolUtility.GetParameterData<T>();

        public abstract string name { get; }

        public abstract string description { get; }

        public BinaryData parameterData => CachedParameterData;

        protected abstract Task<string> Execute(T arguments, CancellationToken cancellationToken);

        Task<string> ITool.Execute(BinaryData arguments, CancellationToken cancellationToken)
        {
            T typedArguments = ToolUtility.GetArgument<T>(arguments);
            return Execute(typedArguments, cancellationToken);
        }
    }

    /// <summary>
    /// 表示 AI 返回的工具参数无法通过校验。
    /// </summary>
    public sealed class ToolArgumentException : Exception
    {
        /// <summary>创建工具参数异常。</summary>
        public ToolArgumentException(string message) : base(message)
        {
        }

        /// <summary>创建保留原始异常的工具参数异常。</summary>
        public ToolArgumentException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

    /// <summary>生成工具参数 JSON Schema，并严格反序列化模型返回的工具参数。</summary>
    public static class ToolUtility
    {
        private static readonly JsonSerializerOptions ArgumentJsonOptions = CreateArgumentJsonOptions();

        /// <summary>
        /// 根据工具参数类型生成用于 Function Calling 的 JSON Schema。
        /// </summary>
        public static BinaryData GetParameterData<T>() where T : class, IToolArgument
        {
            Dictionary<string, object?> schema = CreateObjectSchema(typeof(T));

            byte[] json =
                JsonSerializer.SerializeToUtf8Bytes(schema, new JsonSerializerOptions { WriteIndented = true });

            return BinaryData.FromBytes(json);
        }

        /// <summary>
        /// 将 AI 返回的 JSON 参数转换为指定的工具参数类型。
        /// 缺少字段、多余字段或字段类型错误时抛出异常。
        /// </summary>
        public static T GetArgument<T>(BinaryData arguments) where T : class, IToolArgument
        {
            if (arguments == null) throw new ArgumentNullException($"{nameof(arguments)} 为空。");

            try
            {
                ValidateArgumentShape<T>(arguments);

                T? result = arguments.ToObjectFromJson<T>(ArgumentJsonOptions);

                return result ?? throw new ToolArgumentException($"工具参数不能被反序列化为 {typeof(T).Name}。");
            }
            catch (JsonException e)
            {
                throw new ToolArgumentException($"AI 返回的参数与 {typeof(T).Name} 不匹配。" + $"原始参数：{arguments}", e);
            }
            catch (NotSupportedException e)
            {
                throw new ToolArgumentException($"工具参数类型 {typeof(T).Name} 包含不支持的成员。", e);
            }
        }

        private static JsonSerializerOptions CreateArgumentJsonOptions()
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = false };

            // JSON Schema 中枚举使用字符串表示。
            options.Converters.Add(new JsonStringEnumConverter());

            return options;
        }

        private static void ValidateArgumentShape<T>(BinaryData arguments) where T : class, IToolArgument
        {
            using JsonDocument document = JsonDocument.Parse(arguments.ToMemory());

            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new ToolArgumentException("工具参数必须是 JSON object，" + $"但实际为 {root.ValueKind}。");
            }

            Dictionary<string, PropertyInfo> expectedProperties =
                GetArgumentProperties(typeof(T)).ToDictionary(GetJsonPropertyName, property => property);

            var receivedProperties = new HashSet<string>();

            foreach (JsonProperty jsonProperty in root.EnumerateObject())
            {
                if (!expectedProperties.ContainsKey(jsonProperty.Name))
                {
                    throw new ToolArgumentException($"参数类型 {typeof(T).Name} " + $"中不存在字段“{jsonProperty.Name}”。");
                }

                receivedProperties.Add(jsonProperty.Name);
            }

            foreach (string expectedName in expectedProperties.Keys)
            {
                if (!receivedProperties.Contains(expectedName))
                {
                    throw new ToolArgumentException($"AI 返回的参数缺少必需字段“{expectedName}”。");
                }
            }
        }

        private static Dictionary<string, object?> CreateObjectSchema(Type objectType)
        {
            var properties = new Dictionary<string, object?>();

            var required = new List<string>();

            foreach (PropertyInfo property in GetArgumentProperties(objectType))
            {
                string jsonName = GetJsonPropertyName(property);

                Dictionary<string, object?> propertySchema = CreateTypeSchema(property.PropertyType);

                DescriptionAttribute? description = property.GetCustomAttribute<DescriptionAttribute>();

                if (description is not null)
                {
                    propertySchema["description"] = description.Description;
                }

                properties.Add(jsonName, propertySchema);
                required.Add(jsonName);
            }

            return new Dictionary<string, object?>
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = required,
                ["additionalProperties"] = false
            };
        }

        private static Dictionary<string, object?> CreateTypeSchema(Type type)
        {
            Type actualType = Nullable.GetUnderlyingType(type) ?? type;

            if (actualType == typeof(string) ||
                actualType == typeof(char) ||
                actualType == typeof(Guid) ||
                actualType == typeof(DateTime) ||
                actualType == typeof(DateTimeOffset) ||
                actualType == typeof(Uri))
            {
                return new Dictionary<string, object?> { ["type"] = "string" };
            }

            if (actualType == typeof(bool))
            {
                return new Dictionary<string, object?> { ["type"] = "boolean" };
            }

            if (IsIntegerType(actualType))
            {
                return new Dictionary<string, object?> { ["type"] = "integer" };
            }

            if (IsNumberType(actualType))
            {
                return new Dictionary<string, object?> { ["type"] = "number" };
            }

            if (actualType.IsEnum)
            {
                return new Dictionary<string, object?> { ["type"] = "string", ["enum"] = Enum.GetNames(actualType) };
            }

            Type? elementType = GetEnumerableElementType(actualType);

            if (elementType is not null)
            {
                return new Dictionary<string, object?>
                    { ["type"] = "array", ["items"] = CreateTypeSchema(elementType) };
            }

            if (actualType.IsClass || actualType.IsValueType)
            {
                return CreateObjectSchema(actualType);
            }

            throw new NotSupportedException($"无法为类型 {actualType.FullName} 生成 JSON Schema。");
        }

        private static PropertyInfo[] GetArgumentProperties(Type type)
        {
            return type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property =>
                    property.GetMethod is not null &&
                    property.SetMethod is not null &&
                    property.GetIndexParameters().Length == 0 &&
                    !IsIgnored(property)
                ).ToArray();
        }

        private static bool IsIgnored(PropertyInfo property)
        {
            JsonIgnoreAttribute? attribute = property.GetCustomAttribute<JsonIgnoreAttribute>();
            return attribute?.Condition == JsonIgnoreCondition.Always;
        }

        private static string GetJsonPropertyName(PropertyInfo property)
        {
            return property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? property.Name;
        }

        private static bool IsIntegerType(Type type)
        {
            return type == typeof(byte) ||
                   type == typeof(sbyte) ||
                   type == typeof(short) ||
                   type == typeof(ushort) ||
                   type == typeof(int) ||
                   type == typeof(uint) ||
                   type == typeof(long) ||
                   type == typeof(ulong);
        }

        private static bool IsNumberType(Type type)
        {
            return type == typeof(float) ||
                   type == typeof(double) ||
                   type == typeof(decimal);
        }

        private static Type? GetEnumerableElementType(Type type)
        {
            if (type == typeof(string)) return null;

            if (type.IsArray) return type.GetElementType();

            Type? enumerableInterface = type.GetInterfaces().Append(type).FirstOrDefault(candidate =>
                candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>));

            return enumerableInterface?.GetGenericArguments()[0];
        }
    }
}
