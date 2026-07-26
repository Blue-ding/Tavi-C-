namespace Tavi.Domain.Scenario;

/// <summary>指定 Scene 可以进入的结算路径。</summary>
[Flags]
public enum SceneSettlementOptions
{
    /// <summary>Scene 尚未声明结算路径。</summary>
    None = 0,

    /// <summary>Scene 可以使用 Module 规则结算。</summary>
    Rules = 1,

    /// <summary>Scene 可以进入独立的 Performance 演绎路径。</summary>
    Performance = 2,

    /// <summary>旧名称；请使用 Performance。</summary>
    [Obsolete("请使用 Performance。")]
    Writing = Performance
}

/// <summary>指定 Scene 功能容器的当前生命周期状态；该状态不表示历史时间线。</summary>
public enum SceneState
{
    /// <summary>Scene 正在接受可持久化且允许不完整的 Element 槽位绑定。</summary>
    Binding,

    /// <summary>Scene 已冻结绑定并正在由规则或 Performance 流程处理。</summary>
    Processing,

    /// <summary>Scene 的结构化结果已经提交。</summary>
    Settled
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

/// <summary>保存 Scene 创建时冻结的槽位结构要求；自然语言和 Module 语义由应用层解释。</summary>
public sealed record SceneSlotSpecification
{
    /// <summary>使用稳定槽位标识、基数和语义键集合创建冻结要求。</summary>
    public SceneSlotSpecification(string id, string name, string description, int minimum, int? maximum, IEnumerable<string>? elementTypes = null, IEnumerable<string>? requiredAspectGroups = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(description);
        if (minimum < 0 || maximum < minimum)
            throw new ArgumentOutOfRangeException(nameof(minimum), "Scene 槽位基数范围无效。");
        Id = id;
        Name = name;
        Description = description;
        Minimum = minimum;
        Maximum = maximum;
        ElementTypes = Array.AsReadOnly((elementTypes ?? []).Distinct(StringComparer.Ordinal).ToArray());
        RequiredAspectGroups = Array.AsReadOnly((requiredAspectGroups ?? []).Distinct(StringComparer.Ordinal).ToArray());
    }

    /// <summary>获取稳定槽位标识。</summary>
    public string Id { get; }
    /// <summary>获取槽位显示名称。</summary>
    public string Name { get; }
    /// <summary>获取槽位说明。</summary>
    public string Description { get; }
    /// <summary>获取最少 Element 数。</summary>
    public int Minimum { get; }
    /// <summary>获取最多 Element 数；不限制时为 null。</summary>
    public int? Maximum { get; }
    /// <summary>获取允许的 ElementType 键。</summary>
    public IReadOnlyList<string> ElementTypes { get; }
    /// <summary>获取必须具备的 AspectGroup 键。</summary>
    public IReadOnlyList<string> RequiredAspectGroups { get; }
}

/// <summary>表示由 SceneDefinition 填入具体 Element 后形成的可结算功能容器。</summary>
public sealed record Scene
{
    private readonly Dictionary<string, SceneSlotBinding> _bindings;
    private readonly Dictionary<string, SceneSlotSpecification> _slotSpecifications;

    /// <summary>使用确定定义、Module 版本和独立槽位绑定创建 Scene。</summary>
    public Scene(Guid id, SceneDefinitionType definitionId, string moduleId, string moduleVersion, Guid basedOnScenarioStateId, string name, string description, SceneSettlementOptions settlementOptions, SceneState state, IEnumerable<SceneSlotSpecification>? slotSpecifications = null, IEnumerable<SceneSlotBinding>? bindings = null, bool definitionFrozen = true)
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
        DefinitionFrozen = definitionFrozen;
        _slotSpecifications = (slotSpecifications ?? []).ToDictionary(specification => specification.Id, CloneSpecification, StringComparer.Ordinal);
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

    /// <summary>获取 Scene 是否保存了完整槽位定义；旧存档缺失时为 false，不能重新开始处理。</summary>
    public bool DefinitionFrozen { get; }

    /// <summary>获取全部槽位绑定的独立副本。</summary>
    public IReadOnlyCollection<SceneSlotBinding> GetBindings() => _bindings.Values.Select(CloneBinding).ToArray();

    /// <summary>获取 Scene 创建时冻结的全部槽位要求。</summary>
    public IReadOnlyCollection<SceneSlotSpecification> GetSlotSpecifications() => _slotSpecifications.Values.Select(CloneSpecification).ToArray();

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

    /// <summary>获取指定 Element 是否保存在任意槽位的历史或活动绑定中。</summary>
    internal bool ContainsElement(Guid elementId) => _bindings.Values.Any(binding => binding.ElementIds.Contains(elementId));

    internal static SceneSlotBinding CloneBinding(SceneSlotBinding source) => new(source.SlotId, source.ElementIds);

    internal static SceneSlotSpecification CloneSpecification(SceneSlotSpecification source) => new(source.Id, source.Name, source.Description, source.Minimum, source.Maximum, source.ElementTypes, source.RequiredAspectGroups);
}
