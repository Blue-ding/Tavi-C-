namespace Tavi.Domain.World;

/// <summary>表示可被一元或二元断言引用的世界实体；其类型不赋予任何 World Kernel 特权。</summary>
public sealed record Element
{
    /// <summary>使用确定标识和属性创建 Element。</summary>
    /// <param name="id">Element 标识。</param>
    /// <param name="name">供作者和 Module 识别的名称。</param>
    /// <param name="description">供诊断、作者或语言模型使用的说明。</param>
    /// <param name="type">由 Kernel 或 Module 定义的开放类型。</param>
    public Element(Guid id, string name, string description, ElementType type)
    {
        Id = id;
        Name = name;
        Description = description;
        Type = type;
    }

    /// <summary>获取 Element 标识。</summary>
    public Guid Id { get; }

    /// <summary>获取 Element 名称；名称不承担唯一身份语义。</summary>
    public string Name { get; private set; }

    /// <summary>获取 Element 说明。</summary>
    public string Description { get; private set; }

    /// <summary>获取由 Module 解释的开放类型。</summary>
    public ElementType Type { get; private set; }

    /// <summary>更新 Element 名称；调用方必须位于 World 事务边界内。</summary>
    internal void UpdateName(string name) => Name = name;

    /// <summary>更新 Element 说明；调用方必须位于 World 事务边界内。</summary>
    internal void UpdateDescription(string description) => Description = description;

    /// <summary>更新 Element 类型；调用方必须位于 World 事务边界内。</summary>
    internal void UpdateType(ElementType type) => Type = type;
}
