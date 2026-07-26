using System.Text.Json;
using Tavi.Application.Extensions;
using Tavi.Extensibility;
using Xunit;

namespace Tavi.Application.Tests;

public sealed class ExtensionSessionTests
{
    [Fact]
    public void CompleteCandidateIsValidatedWithoutMutatingCurrentSession()
    {
        ModulePackageDefinition package = CreatePackage(
            "magic",
            parameters:
            [
                new ModuleParameterDefinition
                {
                    Key = "mana-cost-multiplier",
                    Name = "法力消耗倍率",
                    Type = ModuleParameterType.Number,
                    DefaultValue = "1",
                    Minimum = 0,
                    Maximum = 10,
                    ApplyMode = ModuleSettingApplyMode.ProcessRestart
                }
            ]);
        var current = new ExtensionSession([package]);
        JsonElement configured = JsonSerializer.SerializeToElement(
            new Dictionary<string, double> { ["mana-cost-multiplier"] = 1.5 });

        ExtensionSession candidate = current.CreateCandidate(
            [new ExtensionModuleConfiguration(new ModuleId("magic"), true, configured)]);

        Assert.Equal(1, current.GetEffectiveSettings(new ModuleId("magic")).GetProperty("mana-cost-multiplier").GetDouble());
        Assert.Equal(1.5, candidate.GetEffectiveSettings(new ModuleId("magic")).GetProperty("mana-cost-multiplier").GetDouble());
        Assert.Equal("number", candidate.GetSettingsSchema(new ModuleId("magic")).Document
            .GetProperty("properties")
            .GetProperty("mana-cost-multiplier")
            .GetProperty("type")
            .GetString());
    }

    [Fact]
    public void CandidateRejectsEnabledModuleWhoseDependencyIsDisabled()
    {
        ModulePackageDefinition foundation = CreatePackage("foundation");
        ModulePackageDefinition dependent = CreatePackage(
            "dependent",
            dependencies:
            [
                new ModuleDependency
                {
                    Id = new ModuleId("foundation"),
                    MinimumVersion = new ModuleVersion("1.0.0")
                }
            ]);
        var current = new ExtensionSession([foundation, dependent]);
        JsonElement empty = JsonSerializer.SerializeToElement(new { });

        ModuleConfigurationException exception = Assert.Throws<ModuleConfigurationException>(() =>
            current.CreateCandidate(
            [
                new ExtensionModuleConfiguration(new ModuleId("foundation"), false, empty),
                new ExtensionModuleConfiguration(new ModuleId("dependent"), true, empty)
            ]));

        Assert.Equal("TAVI.EXTENSIONS.SETTINGS.INVALID_DEPENDENCY", exception.Code);
        Assert.True(current.IsEnabled(new ModuleId("foundation")));
        Assert.True(current.IsEnabled(new ModuleId("dependent")));
    }

    private static ModulePackageDefinition CreatePackage(
        string id,
        IReadOnlyList<ModuleDependency>? dependencies = null,
        IReadOnlyList<ModuleParameterDefinition>? parameters = null) => new()
    {
        Manifest = new ModuleManifest
        {
            Id = new ModuleId(id),
            Version = new ModuleVersion("1.0.0"),
            SchemaVersion = 1,
            Name = id,
            Dependencies = dependencies ?? [],
            Parameters = parameters ?? []
        },
        Semantics = new SemanticModuleDefinition()
    };
}
