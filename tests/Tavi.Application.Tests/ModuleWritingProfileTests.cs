using Tavi.Application.Extensions.Loading;
using Tavi.Extensibility;
using Xunit;

namespace Tavi.Application.Tests;

public sealed class ModuleWritingProfileTests
{
    [Fact]
    public void ParseCompilesDirectAndModelParagraphs()
    {
        ModuleWritingDefinitions definitions = ModuleWritingProfile.Parse(
            """
            {
              "schemaVersion": 1,
              "beatNarrations": [
                {
                  "beatDefinition": "test:advance",
                  "paragraphs": [
                    {
                      "key": "result",
                      "template": "{{subject.name}}继续前行。{{detail}}",
                      "bindings": {
                        "subject": {
                          "source": "beatSlot",
                          "slot": "subject",
                          "cardinality": "one"
                        }
                      },
                      "fills": {
                        "detail": {
                          "kind": "model",
                          "instruction": "描写{{subject.name}}看到的景象。",
                          "context": ["subject.description", "interaction"],
                          "minimumLength": 1,
                          "maximumLength": 100
                        }
                      }
                    }
                  ]
                }
              ]
            }
            """,
            new ModuleId("test"));

        BeatNarrationDefinition narration = Assert.Single(definitions.Narrations);
        WritingParagraphDefinition paragraph = Assert.Single(narration.Paragraphs);
        Assert.Equal("test:advance", narration.BeatDefinition.Value);
        Assert.Equal(WritingBindingSource.BeatSlot, paragraph.Bindings["subject"].Source);
        Assert.Equal(100, paragraph.Fills["detail"].MaximumLength);
    }

    [Fact]
    public void ParseRejectsUnboundTemplatePlaceholder()
    {
        ModuleConfigurationException exception = Assert.Throws<ModuleConfigurationException>(
            () => ModuleWritingProfile.Parse(
                """
                {
                  "schemaVersion": 1,
                  "beatNarrations": [
                    {
                      "beatDefinition": "test:advance",
                      "paragraphs": [
                        {
                          "key": "result",
                          "template": "{{missing}}"
                        }
                      ]
                    }
                  ]
                }
                """,
                new ModuleId("test")));

        Assert.Contains("$.beatNarrations[0].paragraphs[0].template", exception.Message);
    }

    [Fact]
    public void LoaderAddsWritingWithoutChangingSettingsSchema()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"tavi-writing-profile-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(
                Path.Combine(directory, "module.json"),
                """
                {
                  "id": "test",
                  "version": "1.0.0",
                  "schemaVersion": 1,
                  "name": "Test",
                  "dependencies": [],
                  "settings": { "schema": "settings.schema.json" }
                }
                """);
            File.WriteAllText(
                Path.Combine(directory, "settings.schema.json"),
                """
                {
                  "$schema": "https://json-schema.org/draft/2020-12/schema",
                  "type": "object",
                  "additionalProperties": false,
                  "properties": {
                    "tone": {
                      "type": "string",
                      "title": "Tone",
                      "default": "plain"
                    }
                  }
                }
                """);
            File.WriteAllText(
                Path.Combine(directory, "writing.json"),
                """
                {
                  "schemaVersion": 1,
                  "beatNarrations": [
                    {
                      "beatDefinition": "test:advance",
                      "paragraphs": [
                        {
                          "key": "result",
                          "template": "Done."
                        }
                      ]
                    }
                  ]
                }
                """);

            ModulePackageDefinition package = ModulePackageLoader.Load(directory);

            Assert.NotNull(package.SettingsSchema);
            Assert.Equal("tone", Assert.Single(package.SettingsSchema!.Settings).Key);
            Assert.Single(package.Writing.Narrations);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
