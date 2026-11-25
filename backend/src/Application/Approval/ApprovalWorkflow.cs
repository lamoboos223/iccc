using Iccc.Domain;

namespace Iccc.Application.Approval;

public static class ApprovalWorkflow
{
    public static bool CanTransition(
        RequestStatus from,
        RequestStatus to,
        bool actorCanApprove,
        bool hasMeasurement,
        bool validatedCapacity,
        bool riskAccepted,
        bool calculationBlocked)
    {
        return (from, to) switch
        {
            (RequestStatus.Draft, RequestStatus.Submitted) => true,
            (RequestStatus.Submitted, RequestStatus.Testing) => true,
            (RequestStatus.Testing, RequestStatus.Analysis) => hasMeasurement,
            (RequestStatus.Analysis, RequestStatus.PendingApproval) => hasMeasurement && !calculationBlocked,
            (RequestStatus.PendingApproval, RequestStatus.Approved) =>
                actorCanApprove && !calculationBlocked && (validatedCapacity || riskAccepted),
            (RequestStatus.PendingApproval, RequestStatus.Rejected) => actorCanApprove,
            (RequestStatus.Rejected, RequestStatus.Draft) => true,
            _ => false
        };
    }
}
