using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Tavi.Host.ViewModels;
using Tavi.Extensibility;

namespace Tavi.Host.Errors;

/// <summary>将展示层可观察到的所有异常统一转换为稳定、脱敏的 HTTP 错误响应。</summary>
public sealed class TaviExceptionHandler : IExceptionHandler
{
    private readonly ILogger<TaviExceptionHandler> _logger;

    /// <summary>创建使用指定日志记录器的全局异常处理器。</summary>
    public TaviExceptionHandler(ILogger<TaviExceptionHandler> logger) => _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>处理请求管线异常，并保证未知异常不会向前端泄露内部信息。</summary>
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
            return false;
        (int status, ErrorViewModel error) = CreateError(httpContext, exception);
        if (status >= StatusCodes.Status500InternalServerError)
            _logger.LogError(exception, "Host 请求失败，TraceId={TraceId}，ErrorCode={ErrorCode}，Operation={Operation}", httpContext.TraceIdentifier, error.Code, error.Operation);
        else
            _logger.LogWarning("Host 请求被拒绝，TraceId={TraceId}，ErrorCode={ErrorCode}，Operation={Operation}", httpContext.TraceIdentifier, error.Code, error.Operation);
        httpContext.Response.StatusCode = status;
        httpContext.Response.ContentType = "application/problem+json";
        var problem = new ProblemDetails { Status = status, Title = error.Message, Type = $"https://tavi.local/errors/{error.Code.ToLowerInvariant().Replace('.', '-')}", Extensions = { ["error"] = error } };
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }

    private static (int Status, ErrorViewModel Error) CreateError(HttpContext context, Exception exception)
    {
        if (exception is TaviException taviException)
        {
            int status = taviException.Category switch
            {
                TaviErrorCategory.Validation => StatusCodes.Status400BadRequest,
                TaviErrorCategory.NotFound => StatusCodes.Status404NotFound,
                TaviErrorCategory.Conflict or TaviErrorCategory.InvalidState => StatusCodes.Status409Conflict,
                TaviErrorCategory.Configuration => StatusCodes.Status422UnprocessableEntity,
                TaviErrorCategory.Timeout => StatusCodes.Status504GatewayTimeout,
                TaviErrorCategory.Storage or TaviErrorCategory.ExternalService or TaviErrorCategory.Protocol => StatusCodes.Status503ServiceUnavailable,
                _ => StatusCodes.Status500InternalServerError
            };
            return (status, new ErrorViewModel(taviException.ErrorCode, taviException.Message, taviException.Category.ToString(), taviException.Operation, taviException.IsTransient, taviException.Details, context.TraceIdentifier));
        }
        if (exception is ModuleException moduleException)
        {
            int status = moduleException switch
            {
                ModuleArgumentException => StatusCodes.Status400BadRequest,
                ModuleSemanticException or ModuleConfigurationException => StatusCodes.Status422UnprocessableEntity,
                _ => StatusCodes.Status500InternalServerError
            };
            IReadOnlyDictionary<string, string> details = moduleException switch
            {
                ModuleArgumentException { ParameterName: not null } value => new Dictionary<string, string> { ["parameter"] = value.ParameterName },
                ModuleSemanticException value => CreateModuleSemanticDetails(value),
                _ => EmptyDetails
            };
            return (status, new ErrorViewModel(moduleException.Code, moduleException.Message, moduleException.GetType().Name, null, moduleException.Retryable, details, context.TraceIdentifier));
        }
        if (exception is ArgumentException argumentException)
            return (StatusCodes.Status400BadRequest, new ErrorViewModel("TAVI.HOST.REQUEST.INVALID_ARGUMENT", argumentException.Message, "Validation", null, false, EmptyDetails, context.TraceIdentifier));
        if (exception is KeyNotFoundException)
            return (StatusCodes.Status404NotFound, new ErrorViewModel("TAVI.HOST.RESOURCE.NOT_FOUND", exception.Message, "NotFound", null, false, EmptyDetails, context.TraceIdentifier));
        if (exception is InvalidOperationException invalidOperationException)
            return (StatusCodes.Status409Conflict, new ErrorViewModel("TAVI.HOST.REQUEST.INVALID_STATE", invalidOperationException.Message, "InvalidState", null, false, EmptyDetails, context.TraceIdentifier));
        return (StatusCodes.Status500InternalServerError, new ErrorViewModel("TAVI.HOST.INTERNAL.UNEXPECTED", "Tavi 遇到了未预期的内部错误。", "InternalFailure", null, false, EmptyDetails, context.TraceIdentifier));
    }

    private static IReadOnlyDictionary<string, string> EmptyDetails { get; } = new Dictionary<string, string>();
    private static IReadOnlyDictionary<string, string> CreateModuleSemanticDetails(ModuleSemanticException exception)
    {
        var details = new Dictionary<string, string>();
        if (exception.DefinitionId is SemanticKey definitionId)
            details["definitionId"] = definitionId.Value;
        if (exception.EntityId is Guid entityId)
            details["entityId"] = entityId.ToString();
        return details;
    }
}
