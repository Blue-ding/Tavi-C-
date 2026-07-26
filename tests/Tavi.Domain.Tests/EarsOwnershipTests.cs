using Xunit;
using ScenarioAggregate = Tavi.Domain.Scenario.Scenario;
using ScenarioOperation = Tavi.Domain.Scenario.UpdateElementOperation;
using WorldAggregate = Tavi.Domain.World.World;

namespace Tavi.Domain.Tests;

public sealed class EarsOwnershipTests
{
    [Fact]
    public void EarsDefinitionsBelongToDomainRoot()
    {
        Type[] definitions =
        [
            typeof(Element),
            typeof(Scope),
            typeof(Aspect),
            typeof(Relation),
            typeof(ElementType),
            typeof(ScopeType),
            typeof(AspectType),
            typeof(RelationType)
        ];

        Assert.All(definitions, definition => Assert.Equal("Tavi.Domain", definition.Namespace));
    }

    [Fact]
    public void WorldAndScenarioIsolateTheSameEarsInput()
    {
        Guid elementId = Guid.NewGuid();
        var sharedElement = new Element(elementId, "Before", string.Empty, ElementType.None);
        var world = WorldAggregate.Create(new World.WorldSnapshot
        {
            Elements = new Dictionary<Guid, Element> { [elementId] = sharedElement }
        });
        var scenario = ScenarioAggregate.Create(new Scenario.ScenarioSnapshot
        {
            SourceWorldStateId = world.StateId,
            Elements = new Dictionary<Guid, Element> { [elementId] = sharedElement }
        });

        scenario.Apply(new Scenario.ScenarioChangeSet(
        [
            new ScenarioOperation(elementId, "After", string.Empty, ElementType.None)
        ]));

        Assert.Equal("Before", world.GetElement(elementId).Name);
        Assert.Equal("After", scenario.GetElement(elementId).Name);
        Assert.Equal("Before", sharedElement.Name);
    }
}
