using SubGuard.Core.Domain;
using SubGuard.Core.Persistence;

namespace SubGuard.Core.Services;

public sealed class ApprovalWorkflowService(SubGuardRepository repository)
{
    private readonly SubGuardRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));

    public Task<ApprovalEvaluation> EvaluateAsync(int payApplicationId, CancellationToken cancellationToken = default) =>
        Task.Run(() => _repository.EvaluateApproval(payApplicationId), cancellationToken);

    public Task<ApprovalTransitionResult> TransitionAsync(
        int payApplicationId,
        ApprovalTransition transition,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => _repository.ExecuteApprovalTransition(payApplicationId, transition), cancellationToken);
}
