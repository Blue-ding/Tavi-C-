using Tavi.Application.Writing;
using Tavi.Domain.Performance;
using Tavi.Domain.Story;
using Xunit;

namespace Tavi.Application.Tests;

public sealed class WritingBeatPublicationTests
{
    [Fact]
    public async Task PublishBeatIsIdempotentAfterLaterManuscriptChanges()
    {
        var store = new MemoryManuscriptStore();
        await using var session = new WritingSession(store);
        await session.InitializeAsync();
        WritingSnapshot created = await session.CreateAsync("Story");
        Guid initialStateId = created.Manuscript!.StateId;
        Guid performanceId = Guid.NewGuid();
        Guid beatId = Guid.NewGuid();
        var paragraph = new BeatParagraph(Guid.NewGuid(), "Generated paragraph.");

        BeatPublicationResult first = session.PublishBeat(performanceId, beatId, [paragraph], initialStateId);
        Assert.False(session.GetSnapshot().CanUndo);
        WritingCommitResult renamed = session.Apply(new RenameManuscriptOperation("Renamed"), first.ManuscriptStateId);
        session.Undo(renamed.StateId);
        BeatPublicationResult repeated = session.PublishBeat(performanceId, beatId, [paragraph], initialStateId);

        Assert.False(first.AlreadyPublished);
        Assert.True(repeated.AlreadyPublished);
        Manuscript manuscript = session.GetSnapshot().Manuscript!;
        Assert.Single(manuscript.Paragraphs);
        Assert.Single(manuscript.BeatPublications);
    }

    internal sealed class MemoryManuscriptStore : IManuscriptStore
    {
        private Manuscript? _active;
        private readonly Dictionary<Guid, Manuscript> _archived = new();

        public Task<Manuscript?> LoadActiveAsync(CancellationToken cancellationToken = default) => Task.FromResult(_active);
        public Task<IReadOnlyList<Manuscript>> ListArchivedAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Manuscript>>(_archived.Values.ToArray());
        public Task<Manuscript?> LoadArchivedAsync(Guid manuscriptId, CancellationToken cancellationToken = default) => Task.FromResult(_archived.GetValueOrDefault(manuscriptId));
        public Task SaveActiveAsync(Manuscript manuscript, CancellationToken cancellationToken = default) { _active = manuscript; return Task.CompletedTask; }
        public Task ArchiveAsync(Manuscript manuscript, CancellationToken cancellationToken = default) { _archived[manuscript.Id] = manuscript; _active = null; return Task.CompletedTask; }
        public Task SaveArchivedAsync(Manuscript manuscript, CancellationToken cancellationToken = default) { _archived[manuscript.Id] = manuscript; return Task.CompletedTask; }
        public Task<bool> DeleteArchivedAsync(Guid manuscriptId, CancellationToken cancellationToken = default) => Task.FromResult(_archived.Remove(manuscriptId));
    }
}
