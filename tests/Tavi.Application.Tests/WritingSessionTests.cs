using Tavi.Application.Writing;
using Tavi.Domain.Story;
using Xunit;

namespace Tavi.Application.Tests;

/// <summary>验证 WritingSession 的单活动约束、段落历史、保存和归档语义。</summary>
public sealed class WritingSessionTests
{
    /// <summary>验证段落可以提交、撤销、重做并在保存后清除脏状态。</summary>
    [Fact]
    public async Task ParagraphEditingSupportsHistoryAndSaving()
    {
        var store = new MemoryManuscriptStore();
        await using var session = new WritingSession(store);
        await session.InitializeAsync();
        WritingSnapshot created = await session.CreateAsync("测试手稿");
        Guid stateId = created.Manuscript!.StateId;

        WritingCommitResult inserted = session.Apply(new InsertParagraphOperation(Guid.NewGuid(), 0, "第一段"), stateId);
        Assert.True(inserted.Changed);
        Assert.True(session.GetSnapshot().IsDirty);
        WritingCommitResult undone = session.Undo(inserted.StateId);
        Assert.Empty(session.GetSnapshot().Manuscript!.Paragraphs);
        session.Redo(undone.StateId);
        Assert.Equal("第一段", session.GetSnapshot().Manuscript!.Paragraphs.Single().Text);

        await session.SaveAsync();
        Assert.False(session.GetSnapshot().IsDirty);
        Assert.Equal("第一段", store.Active!.Paragraphs.Single().Text);
    }

    /// <summary>验证未归档活动手稿阻止新建，归档后正文只读并释放编辑席位。</summary>
    [Fact]
    public async Task ArchiveReleasesOnlyActiveManuscriptSlot()
    {
        var store = new MemoryManuscriptStore();
        await using var session = new WritingSession(store);
        await session.InitializeAsync();
        WritingSnapshot created = await session.CreateAsync("第一篇");
        await Assert.ThrowsAsync<WritingException>(() => session.CreateAsync("第二篇"));

        Manuscript archived = await session.ArchiveAsync(created.Manuscript!.StateId);
        Assert.Equal(ManuscriptStatus.Archived, archived.Status);
        Assert.Null(session.GetSnapshot().Manuscript);
        Assert.Null(store.Active);
        Assert.Single(store.Archived);
        Assert.NotNull((await session.CreateAsync("第二篇")).Manuscript);
    }

    /// <summary>验证过期状态标识不会覆盖较新的正文。</summary>
    [Fact]
    public async Task StaleRevisionIsRejected()
    {
        var store = new MemoryManuscriptStore();
        await using var session = new WritingSession(store);
        await session.InitializeAsync();
        Guid initial = (await session.CreateAsync("测试")).Manuscript!.StateId;
        session.Apply(new InsertParagraphOperation(Guid.NewGuid(), 0), initial);
        WritingException exception = Assert.Throws<WritingException>(() => session.Apply(new RenameManuscriptOperation("过期修改"), initial));
        Assert.Equal(WritingErrorCodes.StateConflict, exception.ErrorCode);
    }

    private sealed class MemoryManuscriptStore : IManuscriptStore
    {
        internal Manuscript? Active { get; private set; }
        internal Dictionary<Guid, Manuscript> Archived { get; } = [];
        public Task<Manuscript?> LoadActiveAsync(CancellationToken cancellationToken = default) => Task.FromResult(Active);
        public Task<IReadOnlyList<Manuscript>> ListArchivedAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Manuscript>>(Archived.Values.ToArray());
        public Task<Manuscript?> LoadArchivedAsync(Guid manuscriptId, CancellationToken cancellationToken = default) => Task.FromResult(Archived.GetValueOrDefault(manuscriptId));
        public Task SaveActiveAsync(Manuscript manuscript, CancellationToken cancellationToken = default) { Active = manuscript; return Task.CompletedTask; }
        public Task ArchiveAsync(Manuscript manuscript, CancellationToken cancellationToken = default) { Archived[manuscript.Id] = manuscript; Active = null; return Task.CompletedTask; }
        public Task SaveArchivedAsync(Manuscript manuscript, CancellationToken cancellationToken = default) { Archived[manuscript.Id] = manuscript; return Task.CompletedTask; }
        public Task<bool> DeleteArchivedAsync(Guid manuscriptId, CancellationToken cancellationToken = default) => Task.FromResult(Archived.Remove(manuscriptId));
    }
}
