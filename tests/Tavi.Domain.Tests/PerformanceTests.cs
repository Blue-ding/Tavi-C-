using Tavi.Domain.Performance;
using Xunit;
using PerformanceAggregate = Tavi.Domain.Performance.Performance;

namespace Tavi.Domain.Tests;

public sealed class PerformanceTests
{
    [Fact]
    public void CreateIsolatesImportedEars()
    {
        Guid elementId = Guid.NewGuid();
        var input = new Element(elementId, "Before", string.Empty, ElementType.None);
        PerformanceAggregate performance = CreatePerformance(input);

        performance.Apply(new PerformanceChangeSet(
        [
            new UpdatePerformanceElementOperation(elementId, "After", string.Empty, ElementType.None)
        ]));

        Assert.Equal("Before", input.Name);
        Assert.Equal("After", performance.GetElement(elementId).Name);
    }

    [Fact]
    public void BeatResolvesPublishesAndCompletesInOrder()
    {
        Guid elementId = Guid.NewGuid();
        PerformanceAggregate performance = CreatePerformance(new Element(elementId, "Hero", string.Empty, ElementType.None));
        Guid beatId = AddAndBeginBeat(performance, elementId);
        Guid paragraphId = Guid.NewGuid();

        performance.Apply(new PerformanceChangeSet(
        [
            new ResolveBeatOperation(
                beatId,
                new PerformanceChangeSet(
                [
                    new UpdatePerformanceElementOperation(elementId, "Changed Hero", string.Empty, ElementType.None)
                ]),
                [new BeatParagraph(paragraphId, "The hero changed.")])
        ]));

        Beat resolved = performance.GetBeat(beatId);
        Assert.Equal(BeatState.Resolved, resolved.State);
        Assert.Equal(paragraphId, Assert.Single(resolved.Paragraphs).Id);
        Assert.Equal("Changed Hero", performance.GetElement(elementId).Name);

        Guid manuscriptId = Guid.NewGuid();
        Guid manuscriptStateId = Guid.NewGuid();
        performance.Apply(new PerformanceChangeSet(
        [
            new MarkBeatPublishedOperation(beatId, new BeatPublicationReceipt(manuscriptId, manuscriptStateId)),
            new CompletePerformanceOperation()
        ]));

        Beat published = performance.GetBeat(beatId);
        Assert.Equal(BeatState.Published, published.State);
        Assert.Equal(manuscriptStateId, published.Publication!.ManuscriptStateId);
        Assert.Equal(PerformanceStatus.Completed, performance.Status);
    }

    [Fact]
    public void BeatResolutionCannotEscapeItsBoundElementsAndRollsBack()
    {
        Guid boundId = Guid.NewGuid();
        Guid outsideId = Guid.NewGuid();
        PerformanceAggregate performance = CreatePerformance(
            new Element(boundId, "Bound", string.Empty, ElementType.None),
            new Element(outsideId, "Outside", string.Empty, ElementType.None));
        Guid beatId = AddAndBeginBeat(performance, boundId);
        Guid before = performance.StateId;

        Assert.Throws<PerformanceException>(() => performance.Apply(new PerformanceChangeSet(
        [
            new ResolveBeatOperation(
                beatId,
                new PerformanceChangeSet(
                [
                    new UpdatePerformanceElementOperation(outsideId, "Escaped", string.Empty, ElementType.None)
                ]),
                [new BeatParagraph(Guid.NewGuid(), "Invalid")])
        ])));

        Assert.Equal(before, performance.StateId);
        Assert.Equal(BeatState.Processing, performance.GetBeat(beatId).State);
        Assert.Equal("Outside", performance.GetElement(outsideId).Name);
    }

    [Fact]
    public void ResolvedBeatMustBePublishedBeforePerformanceCompletes()
    {
        Guid elementId = Guid.NewGuid();
        PerformanceAggregate performance = CreatePerformance(new Element(elementId, "Hero", string.Empty, ElementType.None));
        Guid beatId = AddAndBeginBeat(performance, elementId);
        performance.Apply(new PerformanceChangeSet(
        [
            new ResolveBeatOperation(beatId, new PerformanceChangeSet([]), [new BeatParagraph(Guid.NewGuid(), "Text")])
        ]));

        Assert.Throws<PerformanceException>(() => performance.Apply(new PerformanceChangeSet([new CompletePerformanceOperation()])));
        Assert.Equal(PerformanceStatus.Active, performance.Status);
    }

    [Fact]
    public void BindingMayRemainIncompleteUntilProcessingBegins()
    {
        Guid elementId = Guid.NewGuid();
        PerformanceAggregate performance = CreatePerformance(new Element(elementId, "Hero", string.Empty, ElementType.None));
        Guid beatId = Guid.NewGuid();
        performance.Apply(new PerformanceChangeSet(
        [
            new AddBeatOperation(
                beatId,
                new BeatDefinitionType("test:beat"),
                "test",
                "1.0.0",
                performance.StateId,
                "Beat",
                string.Empty,
                [new BeatSlotSpecification("subjects", "Subjects", string.Empty, 1, 2)])
        ]));

        performance.Apply(new PerformanceChangeSet(
        [
            new SetBeatSlotBindingOperation(beatId, new BeatSlotBinding("subjects", []))
        ]));

        Assert.Throws<PerformanceException>(() => performance.Apply(new PerformanceChangeSet([new BeginBeatProcessingOperation(beatId)])));
        Assert.Equal(BeatState.Binding, performance.GetBeat(beatId).State);
    }

    [Fact]
    public void BeatResolutionCanBuildACompleteLocalEarsStructure()
    {
        Guid elementId = Guid.NewGuid();
        PerformanceAggregate performance = CreatePerformance(new Element(elementId, "Hero", string.Empty, ElementType.None));
        Guid beatId = AddAndBeginBeat(performance, elementId);
        Guid derivedElementId = Guid.NewGuid();
        Guid scopeId = Guid.NewGuid();
        Guid aspectId = Guid.NewGuid();

        performance.Apply(new PerformanceChangeSet(
        [
            new ResolveBeatOperation(
                beatId,
                new PerformanceChangeSet(
                [
                    new AddPerformanceElementOperation(derivedElementId, "Thought", string.Empty, ElementType.None),
                    new AddPerformanceScopeOperation(scopeId, 1, ScopeType.None, derivedElementId),
                    new AddPerformanceAspectOperation(aspectId, 1, AspectType.None, derivedElementId, scopeId)
                ]),
                [new BeatParagraph(Guid.NewGuid(), "A thought appeared.")])
        ]));

        PerformanceSnapshot snapshot = performance.CreateSnapshot();
        Assert.Contains(derivedElementId, snapshot.Elements.Keys);
        Assert.Contains(scopeId, snapshot.Scopes.Keys);
        Assert.Contains(aspectId, snapshot.Aspects.Keys);
    }

    private static PerformanceAggregate CreatePerformance(params Element[] elements)
    {
        Guid sceneId = Guid.NewGuid();
        Guid scenarioStateId = Guid.NewGuid();
        return PerformanceAggregate.Create(new PerformanceSnapshot
        {
            SourceScene = new PerformanceSourceScene
            {
                Id = sceneId,
                DefinitionId = "test.scene",
                ModuleId = "test",
                ModuleVersion = "1.0.0",
                BasedOnScenarioStateId = scenarioStateId,
                State = "Processing",
                Elements = elements
            },
            SourceScenarioStateId = scenarioStateId,
            Elements = elements.ToDictionary(value => value.Id),
            ImportedElementIds = elements.Select(value => value.Id).ToHashSet()
        });
    }

    private static Guid AddAndBeginBeat(PerformanceAggregate performance, Guid elementId)
    {
        Guid beatId = Guid.NewGuid();
        performance.Apply(new PerformanceChangeSet(
        [
            new AddBeatOperation(
                beatId,
                new BeatDefinitionType("test:beat"),
                "test",
                "1.0.0",
                performance.StateId,
                "Beat",
                string.Empty,
                [new BeatSlotSpecification("subject", "Subject", string.Empty, 1, 1)])
        ]));
        performance.Apply(new PerformanceChangeSet(
        [
            new SetBeatSlotBindingOperation(beatId, new BeatSlotBinding("subject", [elementId]))
        ]));
        performance.Apply(new PerformanceChangeSet([new BeginBeatProcessingOperation(beatId)]));
        return beatId;
    }
}
