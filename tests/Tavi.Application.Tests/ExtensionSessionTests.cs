using Tavi.Application.Extension;
using Tavi.Extensibility;
using Xunit;

namespace Tavi.Application.Tests;

/// <summary>验证 Module 启停、依赖、参数规范化和冻结快照。</summary>
public sealed class ExtensionSessionTests
{
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
        FrozenExtensionSnapshot frozen = session.Freeze();
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
