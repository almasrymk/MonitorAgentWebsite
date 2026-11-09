namespace MonitorAgent.Modules.Licenses.Domain;

/// <summary>
/// INT-017: a plan change the company asked for that waits for the next paid renewal. Changing plan never happens for
/// free in the middle of a term: a cheaper or equal plan is applied when the renewal is paid, and the renewal is priced
/// at that plan. One schedule per subscription; asking again replaces it.
/// </summary>
public sealed class ScheduledPlanChange
{
    private ScheduledPlanChange() { }

    public ScheduledPlanChange(Guid subscriptionId, Guid companyId, Guid planId, Guid requestedBy, DateTimeOffset at)
    {
        SubscriptionId = subscriptionId;
        CompanyId = companyId;
        Replace(planId, requestedBy, at);
    }

    public Guid SubscriptionId { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid PlanId { get; private set; }
    public Guid RequestedBy { get; private set; }
    public DateTimeOffset RequestedAt { get; private set; }

    public void Replace(Guid planId, Guid requestedBy, DateTimeOffset at)
    {
        PlanId = planId;
        RequestedBy = requestedBy;
        RequestedAt = at;
    }
}
