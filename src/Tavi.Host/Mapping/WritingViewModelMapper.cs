using Tavi.Application.Writing;
using Tavi.Domain.Story;
using Tavi.Host.ViewModels;

namespace Tavi.Host.Mapping;

internal static class WritingViewModelMapper
{
    internal static ManuscriptViewModel ToManuscript(Manuscript manuscript) => new(manuscript.Id, manuscript.StateId, manuscript.Title, manuscript.Status.ToString(), manuscript.Paragraphs.Select(paragraph => new ManuscriptParagraphViewModel(paragraph.Id, paragraph.Text)).ToArray(), manuscript.CreatedAtUtc, manuscript.UpdatedAtUtc);
    internal static ManuscriptSummaryViewModel ToSummary(ManuscriptSummary summary) => new(summary.Id, summary.Title, summary.Status.ToString(), summary.ParagraphCount, summary.Preview, summary.CreatedAtUtc, summary.UpdatedAtUtc);
    internal static WritingSnapshotViewModel ToSnapshot(WritingSnapshot snapshot) => new(snapshot.Manuscript is null ? null : ToManuscript(snapshot.Manuscript), snapshot.StagingRevision, snapshot.IsDirty, snapshot.CanUndo, snapshot.CanRedo, snapshot.StagedChanges.Count, snapshot.LastAutoSaveException?.Message);
}
