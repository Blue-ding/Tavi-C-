using Tavi.Application.Guidance;
using Tavi.Host.ViewModels;

namespace Tavi.Host.Mapping;

internal static class GuidanceViewModelMapper
{
    internal static GuidanceSnapshotViewModel ToSnapshot(GuidanceSnapshot snapshot)
    {
        GuidanceMessageViewModel[] messages = snapshot.Messages.Select(message => new GuidanceMessageViewModel(message.Role.ToString(), message.Text)).ToArray();
        GuidanceFailureViewModel? failure = snapshot.Failure is null ? null : new GuidanceFailureViewModel(snapshot.Failure.ErrorCode, snapshot.Failure.Message, snapshot.Failure.IsTransient);
        return new GuidanceSnapshotViewModel(snapshot.SessionId, snapshot.State.ToString(), snapshot.BaseWorldRevision, messages, snapshot.Proposal is null ? null : ToProposal(snapshot.Proposal), failure, snapshot.RetryMessage);
    }

    internal static GuidanceOperationViewModel ToOperation(GuidanceOperation operation, GuidanceSnapshot snapshot) => new(operation.Id, operation.SessionId, operation.State.ToString(), ToSnapshot(snapshot));

    internal static GuidanceCommitViewModel ToCommit(GuidanceCommitResult result, GuidanceSnapshot snapshot)
    {
        CreatedAnchorViewModel[] anchors = result.CreatedAnchorIds.Select(pair => new CreatedAnchorViewModel(pair.Key.Value, pair.Value)).ToArray();
        GuidanceIssueViewModel[] issues = result.Issues.Select(issue => new GuidanceIssueViewModel(issue.Code, issue.Message, issue.ChangeId)).ToArray();
        return new GuidanceCommitViewModel(result.Status.ToString(), result.WorldRevision, anchors, issues, result.ExpectedWorldRevision, result.ActualWorldRevision, ToSnapshot(snapshot));
    }

    private static WorldProposalViewModel ToProposal(WorldProposal proposal) => new(proposal.Id, proposal.BaseWorldRevision, proposal.Summary, proposal.Changes.Select(ToChange).ToArray());

    private static ProposalChangeViewModel ToChange(ProposalChange change)
    {
        return change switch
        {
            ProposeAddAnchor anchor => new ProposeAddAnchorViewModel { Id = anchor.Id, Rationale = anchor.Rationale, AnchorId = anchor.AnchorId.Value, Name = anchor.Name, Description = anchor.Description, Type = anchor.Type.ToString() },
            ProposeAddRelation relation => new ProposeAddRelationViewModel { Id = relation.Id, Rationale = relation.Rationale, Name = relation.Name, Description = relation.Description, Source = ToReference(relation.Source), Target = ToReference(relation.Target), Scope = ToScope(relation.Scope) },
            _ => throw new InvalidOperationException($"不支持的 Guidance 提案修改类型 {change.GetType().Name}。")
        };
    }

    private static ProposalAnchorReferenceViewModel ToReference(ProposalAnchorReference reference)
    {
        return reference switch
        {
            ProposalAnchorReference.Existing existing => new ProposalAnchorReferenceViewModel("Existing", existing.AnchorId),
            ProposalAnchorReference.Proposed proposed => new ProposalAnchorReferenceViewModel("Proposed", proposed.AnchorId.Value),
            _ => throw new InvalidOperationException($"不支持的 Guidance Anchor 引用类型 {reference.GetType().Name}。")
        };
    }

    private static ProposedRelationScopeViewModel ToScope(ProposedRelationScope scope)
    {
        return scope switch
        {
            ProposedRelationScope.World => new ProposedRelationScopeViewModel("World", null),
            ProposedRelationScope.SubWorld subWorld => new ProposedRelationScopeViewModel("SubWorld", ToReference(subWorld.Character)),
            _ => throw new InvalidOperationException($"不支持的 Guidance Relation 范围类型 {scope.GetType().Name}。")
        };
    }
}
