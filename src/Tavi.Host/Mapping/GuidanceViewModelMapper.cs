using Tavi.Application.Guidance;
using Tavi.Host.ViewModels;
using Tavi.Runtime;

namespace Tavi.Host.Mapping;

/// <summary>将 Guidance Application 契约转换为可序列化的 Host ViewModel。</summary>
internal static class GuidanceViewModelMapper
{
    internal static GuidanceSnapshotViewModel ToSnapshot(GuidanceSnapshot snapshot)
    {
        GuidanceMessageViewModel[] messages = snapshot.Messages.Select(message => new GuidanceMessageViewModel(message.Role.ToString(), message.Text)).ToArray();
        GuidanceFailureViewModel? failure = snapshot.Failure is null ? null : new GuidanceFailureViewModel(snapshot.Failure.ErrorCode, snapshot.Failure.Message, snapshot.Failure.IsTransient);
        return new GuidanceSnapshotViewModel(snapshot.SessionId, snapshot.State.ToString(), snapshot.BaseWorldStateId, messages, snapshot.Proposal is null ? null : ToProposal(snapshot.Proposal), failure, snapshot.RetryMessage);
    }

    internal static GuidanceOperationViewModel ToOperation(GuidanceRuntimeOperation operation) =>
        new(operation.OperationId, operation.SessionId, operation.State.ToString(), ToSnapshot(operation.Snapshot));

    internal static GuidanceCommitViewModel ToCommit(GuidanceRuntimeCommit commit)
    {
        GuidanceCommitResult result = commit.Result;
        CreatedWorldEntityViewModel[] elements = result.CreatedElementIds.Select(pair => new CreatedWorldEntityViewModel(pair.Key.Value, pair.Value)).ToArray();
        CreatedWorldEntityViewModel[] scopes = result.CreatedScopeIds.Select(pair => new CreatedWorldEntityViewModel(pair.Key.Value, pair.Value)).ToArray();
        GuidanceIssueViewModel[] issues = result.Issues.Select(issue => new GuidanceIssueViewModel(issue.Code, issue.Message, issue.ChangeId)).ToArray();
        return new GuidanceCommitViewModel(result.Status.ToString(), result.WorldStateId, elements, scopes, issues, result.ExpectedWorldStateId, result.ActualWorldStateId, ToSnapshot(commit.Snapshot));
    }

    private static WorldProposalViewModel ToProposal(WorldProposal proposal) => new(proposal.Id, proposal.BaseWorldStateId, proposal.Summary, proposal.Changes.Select(ToChange).ToArray());

    private static ProposalChangeViewModel ToChange(ProposalChange change) => change switch
    {
        ProposeAddElement value => new ProposeAddElementViewModel { Id = value.Id, Rationale = value.Rationale, ElementId = value.ElementId.Value, Name = value.Name, Description = value.Description, Type = value.Type.Value },
        ProposeAddScope value => new ProposeAddScopeViewModel { Id = value.Id, Rationale = value.Rationale, ScopeId = value.ScopeId.Value, Quantity = value.Quantity, Type = value.Type.Value, Owner = ToReference(value.Owner) },
        ProposeAddAspect value => new ProposeAddAspectViewModel { Id = value.Id, Rationale = value.Rationale, Quantity = value.Quantity, Type = value.Type.Value, Element = ToReference(value.Element), Scope = ToReference(value.Scope) },
        ProposeAddRelation value => new ProposeAddRelationViewModel { Id = value.Id, Rationale = value.Rationale, Quantity = value.Quantity, Type = value.Type.Value, Source = ToReference(value.Source), Target = ToReference(value.Target), Scope = ToReference(value.Scope) },
        ProposeAddLocalAspect value => new ProposeAddLocalAspectViewModel { Id = value.Id, Rationale = value.Rationale, Name = value.Name, Description = value.Description, Quantity = value.Quantity, Element = ToReference(value.Element), Scope = ToReference(value.Scope) },
        ProposeAddLocalRelation value => new ProposeAddLocalRelationViewModel { Id = value.Id, Rationale = value.Rationale, Name = value.Name, Description = value.Description, Quantity = value.Quantity, Source = ToReference(value.Source), Target = ToReference(value.Target), Scope = ToReference(value.Scope) },
        _ => throw new InvalidOperationException($"不支持的 Guidance 提案修改类型 {change.GetType().Name}。")
    };

    private static ProposalElementReferenceViewModel ToReference(ProposalElementReference reference) => reference switch
    {
        ProposalElementReference.Existing value => new ProposalElementReferenceViewModel("Existing", value.ElementId),
        ProposalElementReference.Proposed value => new ProposalElementReferenceViewModel("Proposed", value.ElementId.Value),
        _ => throw new InvalidOperationException($"不支持的 Guidance Element 引用类型 {reference.GetType().Name}。")
    };

    private static ProposalScopeReferenceViewModel ToReference(ProposalScopeReference reference) => reference switch
    {
        ProposalScopeReference.Existing value => new ProposalScopeReferenceViewModel("Existing", value.ScopeId),
        ProposalScopeReference.Proposed value => new ProposalScopeReferenceViewModel("Proposed", value.ScopeId.Value),
        _ => throw new InvalidOperationException($"不支持的 Guidance Scope 引用类型 {reference.GetType().Name}。")
    };
}
