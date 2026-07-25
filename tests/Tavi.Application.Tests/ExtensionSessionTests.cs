using Tavi.Application.Extensions;
using Tavi.Domain.World;
using Tavi.Extensibility;
using Xunit;

namespace Tavi.Application.Tests;

/// <summary>验证 Module 启停、依赖、参数规范化和冻结快照。</summary>
public sealed class ExtensionSessionTests
{
    /// <summary>验证四种开放类型目录保持名义隔离，并始终包含各自的 core:none。</summary>
    [Fact]
    public void TypeCatalogKeepsFourDefinitionKindsSeparate()
    {
        ModulePackageDefinition package = Package("test") with { Semantics = new SemanticModuleDefinition { ElementTypes = [new ElementTypeDefinition { Key = new SemanticKey("test:entity"), Name = "实体", Description = "仅注册为 ElementType。" }] } };

        ModuleCatalog catalog = ModuleCatalog.Create([package]);

        Assert.NotNull(catalog.FindElementType(new ElementType("test:entity")));
        Assert.Null(catalog.FindScopeType(new ScopeType("test:entity")));
        Assert.Contains(catalog.GetElementTypes(), value => value.Key.Value == "core:none");
        Assert.Contains(catalog.GetScopeTypes(), value => value.Key.Value == "core:none");
        Assert.Contains(catalog.GetAspectTypes(), value => value.Key.Value == "core:none");
        Assert.Contains(catalog.GetRelationTypes(), value => value.Key.Value == "core:none");
    }

    /// <summary>验证禁用被依赖 Module 会被拒绝。</summary>
    [Fact]
    public void CannotDisableRequiredModule()
    {
        var session = new ExtensionSession([Package("base"), Package("dependent", [new ModuleDependency { Id = new ModuleId("base"), MinimumVersion = new ModuleVersion("1.0.0") }])]);
        Assert.Throws<InvalidOperationException>(() => session.Disable(new ModuleId("base")));
    }

    /// <summary>验证冻结后的参数变化只标记需要重启且不修改既有快照。</summary>
    [Fact]
    public void FrozenParametersRemainStableUntilRestart()
    {
        ModulePackageDefinition package = Package("configurable", parameters: [new ModuleParameterDefinition { Key = "intensity", Name = "Intensity", Type = ModuleParameterType.Number, DefaultValue = "1", Minimum = 0, Maximum = 10 }]);
        var session = new ExtensionSession([package]);
        FrozenModuleRuntime frozen = session.Freeze();
        session.SetParameter(new ModuleId("configurable"), "intensity", "2.50");
        Assert.True(session.RestartRequired);
        Assert.Equal("1", frozen.Parameters[new ModuleId("configurable")]["intensity"]);
        Assert.Equal("2.5", session.GetEffectiveParameters(new ModuleId("configurable"))["intensity"]);
    }

    private static ModulePackageDefinition Package(string id, IReadOnlyList<ModuleDependency>? dependencies = null, IReadOnlyList<ModuleParameterDefinition>? parameters = null) => new()
    {
        Manifest = new ModuleManifest { Id = new ModuleId(id), Version = new ModuleVersion("1.0.0"), SchemaVersion = 1, Name = id, Dependencies = dependencies ?? [], Parameters = parameters ?? [] },
        Semantics = new SemanticModuleDefinition()
    };
}
