using System.ComponentModel;
using System.Text.Json;
using Tavi.Application.LanguageModel;
using Xunit;

namespace Tavi.Application.Tests;

public sealed class ToolUtilityTests
{
    [Fact]
    public void GeneratesStrictJsonSchema()
    {
        BinaryData schema = ToolUtility.GetParameterData<ExampleArguments>();
        using JsonDocument document = JsonDocument.Parse(schema);

        JsonElement root = document.RootElement;
        Assert.Equal("object", root.GetProperty("type").GetString());
        Assert.False(root.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            "示例名称",
            root.GetProperty("properties")
                .GetProperty("Name")
                .GetProperty("description")
                .GetString()
        );
    }

    private sealed class ExampleArguments : IToolArgument
    {
        [Description("示例名称")]
        public string Name { get; set; } = string.Empty;
    }
}
