namespace Tavi.Domain.World
{
    public enum WorldGraphErrorCode
    {
        InvalidArgument,
        NotFound,
        Duplicate,
        InvalidOperation,
        InvalidWorldData
    }

    public sealed class WorldGraphException : Exception
    {
        public WorldGraphException(
            WorldGraphErrorCode errorCode,
            string operation,
            string detail,
            Guid? entityId = null,
            IReadOnlyList<string>? validationErrors = null,
            Exception? innerException = null
        ) : base(
            BuildMessage(operation, detail, entityId, validationErrors),
            innerException
        )
        {
            ErrorCode = errorCode;
            Operation = operation;
            EntityId = entityId;
            ValidationErrors = validationErrors?.ToArray()
                               ?? Array.Empty<string>();
        }

        public WorldGraphErrorCode ErrorCode { get; }
        public string Operation { get; }
        public Guid? EntityId { get; }
        public IReadOnlyList<string> ValidationErrors { get; }

        private static string BuildMessage(
            string operation,
            string detail,
            Guid? entityId,
            IReadOnlyList<string>? validationErrors
        )
        {
            var message = $"WorldGraph 操作“{operation}”失败：{detail}";
            if (entityId.HasValue)
                message += $" 实体 Id：{entityId.Value}。";
            if (validationErrors is { Count: > 0 })
                message += $"{Environment.NewLine}- {string.Join($"{Environment.NewLine}- ", validationErrors)}";
            return message;
        }
    }
}
