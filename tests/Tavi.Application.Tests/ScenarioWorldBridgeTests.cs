using Tavi.Application.Scenario;
using Tavi.Application.Extensions;
using Tavi.Domain.World;
using Xunit;

namespace Tavi.Application.Tests;

/// <summary>验证 World 吸收和 Scenario 写回提案的结构化边界。</summary>
public sealed class ScenarioWorldBridgeTests
{
    /// <summary>验证导入保留身份，写回只生成提案且不会修改来源 World。</summary>
    [Fact]
    public void ImportAndProposalPreserveWorldUntilExplicitCommit()
    {
        Guid elementId = Guid.NewGuid();
        var world = new WorldSnapshot();
        world.Elements.Add(elementId, new Element(elementId, "Alice", "", ElementType.None));
        ModuleCatalog catalog = ModuleCatalog.Create([]);
        Tavi.Domain.Scenario.ScenarioSnapshot scenario = ScenarioWorldBridge.Import(world, catalog);
        scenario.Elements[elementId] = new Tavi.Domain.Scenario.Element(elementId, "Alicia", "", Tavi.Domain.Scenario.ElementType.None);
        Guid createdId = Guid.NewGuid();
        scenario.Elements.Add(createdId, new Tavi.Domain.Scenario.Element(createdId, "Created", "", Tavi.Domain.Scenario.ElementType.None));
        ScenarioWorldProposal proposal = ScenarioWorldBridge.CreateProposal(world, scenario);
        Assert.Equal("Alice", world.Elements[elementId].Name);
        Assert.Contains(proposal.ChangeSet.Operations, operation => operation is UpdateElementNameOperation value && value.ElementId == elementId && value.Name == "Alicia");
        Assert.Contains(proposal.ChangeSet.Operations, operation => operation is AddElementOperation value && value.ElementId == createdId);
    }
}
