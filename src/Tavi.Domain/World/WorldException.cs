namespace Tavi.Domain.World
{
    /// <summary>
    /// World 错误类型。
    /// </summary>
    public enum WorldErrorCode
    {
        InvalidArgument,
        NotFound,
        Duplicate,
        InvalidOperation,
        InvalidWorldSnapshot
    }

    /// <summary>
    /// World 操作或初始化校验失败时抛出的异常。
    /// </summary>
    public sealed class WorldException : Exception
    {
        /// <summary>
        /// 创建包含操作上下文和校验详情的 World 异常。
        /// </summary>
        public WorldException(
            WorldErrorCode errorCode,
            string operation,
            string detail,
            Guid? entityId = null,
            IReadOnlyList<string>? validationErrors = null,
            Exception? innerException = null
        ) : base(BuildMessage(operation, detail, entityId, validationErrors), innerException)
        {
            ErrorCode = errorCode;
            Operation = operation;
            EntityId = entityId;
            ValidationErrors = validationErrors?.ToArray() ?? Array.Empty<string>();
        }

        public WorldErrorCode ErrorCode { get; }
        public string Operation { get; }
        public Guid? EntityId { get; }
        public IReadOnlyList<string> ValidationErrors { get; }

        private static string BuildMessage(string operation, string detail, Guid? entityId, IReadOnlyList<string>? validationErrors)
        {
            var message = $"World 操作“{operation}”失败：{detail}";
            if (entityId.HasValue)
                message += $" 实体 Id：{entityId.Value}。";
            if (validationErrors is { Count: > 0 })
                message += $"{Environment.NewLine}- {string.Join($"{Environment.NewLine}- ", validationErrors)}";
            return message;
        }
    }
}
