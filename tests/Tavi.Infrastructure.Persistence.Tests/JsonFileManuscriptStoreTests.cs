using Tavi.Domain.Story;
using Tavi.Infrastructure.Persistence;
using Xunit;

namespace Tavi.Infrastructure.Persistence.Tests;

/// <summary>验证手稿专用目录的原子活动保存和归档恢复。</summary>
public sealed class JsonFileManuscriptStoreTests
{
    /// <summary>验证活动手稿保存后可恢复，归档后只出现在归档库。</summary>
    [Fact]
    public async Task ActiveManuscriptCanBeArchivedAndRestored()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"tavi-manuscripts-{Guid.NewGuid():N}");
        try
        {
            using var store = new JsonFileManuscriptStore(directory);
            Manuscript active = Manuscript.Create("文件测试");
            active = new Manuscript(active.Id, Guid.NewGuid(), active.Title, [new ManuscriptParagraph(Guid.NewGuid(), "正文")], ManuscriptStatus.Editing, active.CreatedAtUtc, DateTimeOffset.UtcNow);
            await store.SaveActiveAsync(active);
            Assert.Equal("正文", (await store.LoadActiveAsync())!.Paragraphs.Single().Text);

            Manuscript archived = new(active.Id, Guid.NewGuid(), active.Title, active.Paragraphs, ManuscriptStatus.Archived, active.CreatedAtUtc, DateTimeOffset.UtcNow);
            await store.ArchiveAsync(archived);
            Assert.Null(await store.LoadActiveAsync());
            Assert.Equal(archived.Id, Assert.Single(await store.ListArchivedAsync()).Id);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    /// <summary>验证存储拒绝用另一篇手稿覆盖现有活动手稿。</summary>
    [Fact]
    public async Task DifferentActiveManuscriptCannotOverwriteExistingOne()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"tavi-manuscripts-{Guid.NewGuid():N}");
        try
        {
            using var store = new JsonFileManuscriptStore(directory);
            await store.SaveActiveAsync(Manuscript.Create("第一篇"));
            await Assert.ThrowsAsync<ManuscriptStoreException>(() => store.SaveActiveAsync(Manuscript.Create("第二篇")));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }
}
