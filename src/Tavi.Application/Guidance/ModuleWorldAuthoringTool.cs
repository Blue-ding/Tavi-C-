using System.Text.Json;
using Tavi.Application.Extensions.World;
using Tavi.Application.LanguageModel;
using Tavi.Application.World;
using Tavi.Extensibility;

namespace Tavi.Application.Guidance;

/// <summary>把 Module World Authoring Action 适配为语言模型工具，并复用相同的提案与暂存路径。</summary>
internal sealed class ModuleWorldAuthoringTool : ITool
{
    private readonly WorldAuthoringCoordinator _coordinator;
    private readonly WorldAuthoringAction _action;

    internal ModuleWorldAuthoringTool(WorldAuthoringCoordinator coordinator, WorldAuthoringAction action)
    {
        _coordinator = coordinator;
        _action = action;
        name = $"module_{action.Id.Value.Replace(':', '_').Replace('.', '_').Replace('-', '_')}";
        description = action.Description.Length == 0 ? action.Name : $"{action.Name}：{action.Description}";
        parameterData = BinaryData.FromString(action.ParameterSchema);
    }

    /// <inheritdoc />
    public string name { get; }
    /// <inheritdoc />
    public string description { get; }
    /// <inheritdoc />
    public BinaryData parameterData { get; }

    /// <summary>执行 Module Action；参数错误作为可重试工具结果返回，其他 Module 故障向上报告。</summary>
    public async Task<string> Execute(BinaryData arguments, CancellationToken cancellationToken)
    {
        try
        {
            Guid stagedId = await _coordinator.ProposeAndStageAsync(_action.Id, arguments.ToString(), WorldStagedChangeSource.Guidance, cancellationToken);
            return JsonSerializer.Serialize(new { ok = true, stagedChangeId = stagedId, action = _action.Id.Value });
        }
        catch (ModuleArgumentException exception)
        {
            return JsonSerializer.Serialize(new { ok = false, retryable = true, code = exception.Code, parameter = exception.ParameterName, message = exception.Message });
        }
    }
}
