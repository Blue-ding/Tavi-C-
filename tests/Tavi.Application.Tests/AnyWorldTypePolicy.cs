using Tavi.Domain.World;

namespace Tavi.Application.Tests;

internal sealed class AnyWorldTypePolicy : IWorldTypePolicy
{
    internal static AnyWorldTypePolicy Instance { get; } = new();

    private AnyWorldTypePolicy()
    {
    }

    bool IWorldTypePolicy.IsRegistered(ElementType type) => true;
    bool IWorldTypePolicy.IsRegistered(ScopeType type) => true;
    bool IWorldTypePolicy.IsRegistered(AspectType type) => true;
    bool IWorldTypePolicy.IsRegistered(RelationType type) => true;
}
