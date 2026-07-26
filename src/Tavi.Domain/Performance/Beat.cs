namespace Tavi.Domain.Performance;

/// <summary>表示 Performance 中一次可绑定、演绎、解决并发布的最小叙事推进。</summary>
public sealed record Beat
{
    private readonly Dictionary<string, BeatSlotSpecification> _slots;
    private readonly Dictionary<string, BeatSlotBinding> _bindings;

    public Beat(
        Guid id,
        BeatDefinitionType definitionId,
        string moduleId,
        string moduleVersion,
        Guid basedOnPerformanceStateId,
        string name,
        string description,
        BeatState state,
        IEnumerable<BeatSlotSpecification> slots,
        IEnumerable<BeatSlotBinding>? bindings = null,
        IEnumerable<BeatParagraph>? paragraphs = null,
        BeatPublicationReceipt? publication = null,
        string? writingProfileJson = null)
    {
        if (id == Guid.Empty || basedOnPerformanceStateId == Guid.Empty)
            throw new ArgumentException("Beat 标识及其依据的 Performance StateId 不能为空。");
        if (definitionId.IsEmpty)
            throw new ArgumentException("BeatDefinition 键不能为空。", nameof(definitionId));
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(slots);
        Id = id;
        DefinitionId = definitionId;
        ModuleId = moduleId;
        ModuleVersion = moduleVersion;
        BasedOnPerformanceStateId = basedOnPerformanceStateId;
        Name = name;
        Description = description;
        WritingProfileJson = writingProfileJson;
        State = state;
        _slots = slots.ToDictionary(value => value.Id, CopySlot, StringComparer.Ordinal);
        _bindings = (bindings ?? []).ToDictionary(value => value.SlotId, CopyBinding, StringComparer.Ordinal);
        Paragraphs = Array.AsReadOnly((paragraphs ?? []).Select(value => new BeatParagraph(value.Id, value.Text)).ToArray());
        Publication = publication is null ? null : new BeatPublicationReceipt(publication.ManuscriptId, publication.ManuscriptStateId);
    }

    public Guid Id { get; }
    public BeatDefinitionType DefinitionId { get; }
    public string ModuleId { get; }
    public string ModuleVersion { get; }
    public Guid BasedOnPerformanceStateId { get; }
    public string Name { get; }
    public string Description { get; }
    /// <summary>获取 Beat 创建时冻结的 Module Writing Profile；旧存档可能为空。</summary>
    public string? WritingProfileJson { get; }
    public BeatState State { get; private set; }
    public IReadOnlyList<BeatParagraph> Paragraphs { get; private set; }
    public BeatPublicationReceipt? Publication { get; private set; }

    public IReadOnlyCollection<BeatSlotSpecification> GetSlotSpecifications() => _slots.Values.Select(CopySlot).ToArray();
    public IReadOnlyCollection<BeatSlotBinding> GetBindings() => _bindings.Values.Select(CopyBinding).ToArray();
    public BeatSlotBinding? FindBinding(string slotId) => _bindings.TryGetValue(slotId, out BeatSlotBinding? value) ? CopyBinding(value) : null;
    internal bool ContainsElement(Guid id) => _bindings.Values.Any(value => value.ElementIds.Contains(id));

    internal bool SetBinding(BeatSlotBinding binding)
    {
        if (_bindings.TryGetValue(binding.SlotId, out BeatSlotBinding? current) && current.ElementIds.SequenceEqual(binding.ElementIds))
            return false;
        _bindings[binding.SlotId] = CopyBinding(binding);
        return true;
    }

    internal bool ClearBinding(string slotId) => _bindings.Remove(slotId);
    internal bool BeginProcessing() => ChangeState(BeatState.Binding, BeatState.Processing);

    internal bool Resolve(IEnumerable<BeatParagraph> paragraphs)
    {
        if (State != BeatState.Processing)
            throw new InvalidOperationException($"Beat {Id} 只有在 Processing 状态才能解决。");
        BeatParagraph[] copied = paragraphs.Select(value => new BeatParagraph(value.Id, value.Text)).ToArray();
        if (copied.Select(value => value.Id).Distinct().Count() != copied.Length)
            throw new ArgumentException("同一 Beat 不能产生重复段落标识。", nameof(paragraphs));
        Paragraphs = Array.AsReadOnly(copied);
        State = BeatState.Resolved;
        return true;
    }

    internal bool Publish(BeatPublicationReceipt receipt)
    {
        if (State != BeatState.Resolved)
            throw new InvalidOperationException($"Beat {Id} 只有在 Resolved 状态才能发布。");
        Publication = new BeatPublicationReceipt(receipt.ManuscriptId, receipt.ManuscriptStateId);
        State = BeatState.Published;
        return true;
    }

    private bool ChangeState(BeatState expected, BeatState next)
    {
        if (State != expected)
            throw new InvalidOperationException($"Beat {Id} 不能从 {State} 转换为 {next}。");
        State = next;
        return true;
    }

    internal static BeatSlotSpecification CopySlot(BeatSlotSpecification value) => new(value.Id, value.Name, value.Description, value.Minimum, value.Maximum);
    internal static BeatSlotBinding CopyBinding(BeatSlotBinding value) => new(value.SlotId, value.ElementIds);
}
