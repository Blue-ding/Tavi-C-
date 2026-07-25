namespace Tavi.Domain.Scenario;

/// <summary>指定 Scene 可以进入的结算路径。</summary>
[Flags]
public enum SceneSettlementOptions
{
    /// <summary>Scene 尚未声明结算路径。</summary>
    None = 0,

    /// <summary>Scene 可以使用 Module 规则结算。</summary>
    Rules = 1,

    /// <summary>Scene 可以进入独立的 Writing 演绎路径。</summary>
    Writing = 2
}

/// <summary>指定 Scene 功能容器的当前生命周期状态；该状态不表示历史时间线。</summary>
public enum SceneState
{
    /// <summary>Scene 正在接受 Element 槽位绑定。</summary>
    Preparing,

    /// <summary>Scene 已通过槽位校验并可选择结算路径。</summary>
    Ready,

    /// <summary>Scene 已进入与规则结算分离的 Writing 演绎路径。</summary>
    AwaitingWriting,

    /// <summary>Scene 的结构化结果已经提交。</summary>
    Settled,

    /// <summary>Scene 已取消且不再允许结算。</summary>
    Cancelled
}

/// <summary>表示一个 Scene 槽位及其当前绑定的 Element。</summary>
public sealed record SceneSlotBinding
{
    /// <summary>使用槽位标识和 Element 标识创建独立绑定。</summary>
    public SceneSlotBinding(string slotId, IEnumerable<Guid> elementIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slotId);
        ArgumentNullException.ThrowIfNull(elementIds);
        Guid[] copied = elementIds.ToArray();
        if (copied.Any(id => id == Guid.Empty) || copied.Distinct().Count() != copied.Length)
            throw new ArgumentException("Scene 槽位绑定不能包含空或重复的 Element 标识。", nameof(elementIds));
        SlotId = slotId;
        ElementIds = Array.AsReadOnly(copied);
    }

    /// <summary>获取 SceneDefinition 内的稳定槽位标识。</summary>
    public string SlotId { get; }

    /// <summary>获取按绑定顺序排列的 Element 标识。</summary>
    public IReadOnlyList<Guid> ElementIds { get; }
}

/// <summary>表示由 SceneDefinition 填入具体 Element 后形成的可结算功能容器。</summary>
public sealed record Scene
{
    private readonly Dictionary<string, SceneSlotBinding> _bindings;

    /// <summary>使用确定定义、Module 版本和独立槽位绑定创建 Scene。</summary>
    public Scene(Guid id, SceneDefinitionType definitionId, string moduleId, string moduleVersion, Guid basedOnScenarioStateId, string name, string description, SceneSettlementOptions settlementOptions, SceneState state, IEnumerable<SceneSlotBinding>? bindings = null)
    {
        Id = id;
        DefinitionId = definitionId;
        ModuleId = moduleId;
        ModuleVersion = moduleVersion;
        BasedOnScenarioStateId = basedOnScenarioStateId;
        Name = name;
        Description = description;
        SettlementOptions = settlementOptions;
        State = state;
        _bindings = (bindings ?? []).ToDictionary(binding => binding.SlotId, CloneBinding, StringComparer.Ordinal);
    }

    /// <summary>获取 Scene 标识。</summary>
    public Guid Id { get; }

    /// <summary>获取产生该 Scene 的 SceneDefinition 稳定键。</summary>
    public SceneDefinitionType DefinitionId { get; }

    /// <summary>获取拥有该 SceneDefinition 的 Module 标识。</summary>
    public string ModuleId { get; }

    /// <summary>获取解释该 Scene 所需的 Module 版本。</summary>
    public string ModuleVersion { get; }

    /// <summary>获取创建该 SceneDefinition 时依据的 Scenario StateId。</summary>
    public Guid BasedOnScenarioStateId { get; }

    /// <summary>获取面向玩家或作者的 Scene 名称。</summary>
    public string Name { get; }

    /// <summary>获取面向玩家、作者或语言模型的 Scene 说明。</summary>
    public string Description { get; }

    /// <summary>获取 Scene 允许进入的结算路径。</summary>
    public SceneSettlementOptions SettlementOptions { get; }

    /// <summary>获取 Scene 当前功能状态。</summary>
    public SceneState State { get; private set; }

    /// <summary>获取全部槽位绑定的独立副本。</summary>
    public IReadOnlyCollection<SceneSlotBinding> GetBindings() => _bindings.Values.Select(CloneBinding).ToArray();

    /// <summary>获取指定槽位绑定的独立副本；槽位尚未绑定时返回 null。</summary>
    public SceneSlotBinding? FindBinding(string slotId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slotId);
        return _bindings.TryGetValue(slotId, out SceneSlotBinding? binding) ? CloneBinding(binding) : null;
    }

    internal bool SetBinding(SceneSlotBinding binding)
    {
        if (_bindings.TryGetValue(binding.SlotId, out SceneSlotBinding? current) && current.ElementIds.SequenceEqual(binding.ElementIds))
            return false;
        _bindings[binding.SlotId] = CloneBinding(binding);
        return true;
    }

    internal bool ClearBinding(string slotId) => _bindings.Remove(slotId);

    internal bool RemoveElementFromBindings(Guid elementId)
    {
        bool changed = false;
        foreach (SceneSlotBinding binding in _bindings.Values.ToArray())
        {
            Guid[] remaining = binding.ElementIds.Where(id => id != elementId).ToArray();
            if (remaining.Length == binding.ElementIds.Count)
                continue;
            _bindings[binding.SlotId] = new SceneSlotBinding(binding.SlotId, remaining);
            changed = true;
        }
        return changed;
    }

    internal bool UpdateState(SceneState state)
    {
        if (State == state)
            return false;
        State = state;
        return true;
    }

    internal static SceneSlotBinding CloneBinding(SceneSlotBinding source) => new(source.SlotId, source.ElementIds);
}
