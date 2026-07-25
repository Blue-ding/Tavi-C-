namespace Tavi.Domain.World;

/// <summary>定义 World 在新增或更换开放类型时使用的只读注册策略。</summary>
public interface IWorldTypePolicy
{
    /// <summary>确定 ElementType 是否已注册。</summary>
    bool IsRegistered(ElementType type);

    /// <summary>确定 ScopeType 是否已注册。</summary>
    bool IsRegistered(ScopeType type);

    /// <summary>确定 AspectType 是否已注册。</summary>
    bool IsRegistered(AspectType type);

    /// <summary>确定 RelationType 是否已注册。</summary>
    bool IsRegistered(RelationType type);
}
