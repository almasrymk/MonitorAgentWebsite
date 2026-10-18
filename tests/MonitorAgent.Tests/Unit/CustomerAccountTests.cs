using MonitorAgent.Modules.Customers.Domain;
using MonitorAgent.SharedKernel;

namespace MonitorAgent.Tests.Unit;

public class CustomerAccountTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Owner = Guid.NewGuid();

    [Fact]
    public void An_invitation_expires_after_seven_days()
    {
        var account = CustomerAccount.Create("Acme", Owner, Now);
        account.Invite(Owner, "late@example.test", MemberRole.Viewer, "hash", Now, []);

        var result = account.Accept("hash", Guid.NewGuid(), "late@example.test", Now + CustomerAccount.InvitationLifetime);

        Assert.Equal(CustomersErrors.InvalidInvitation, result.Error);
    }

    [Fact]
    public void An_expired_invitation_no_longer_blocks_inviting_the_same_email()
    {
        var account = CustomerAccount.Create("Acme", Owner, Now);
        account.Invite(Owner, "late@example.test", MemberRole.Viewer, "hash-1", Now, []);

        Assert.True(account.Invite(Owner, "late@example.test", MemberRole.Viewer, "hash-2", Now.AddDays(8), []).IsSuccess);
    }

    [Fact]
    public void The_team_has_a_size_limit_that_counts_pending_invitations()
    {
        var account = CustomerAccount.Create("Acme", Owner, Now);
        for (var i = 1; i < CustomerAccount.MaxMembers; i++)
            Assert.True(account.Invite(Owner, $"p{i}@example.test", MemberRole.Viewer, $"h{i}", Now, []).IsSuccess);

        Assert.Equal(CustomersErrors.TeamFull, account.Invite(Owner, "one-more@example.test", MemberRole.Viewer, "h", Now, []).Error);
    }

    [Fact]
    public void A_blank_company_name_is_rejected()
    {
        var error = Assert.Throws<DomainException>(() => CustomerAccount.Create("  ", Owner, Now)).Error;
        Assert.Equal("VALIDATION_FAILED", error.Code);
    }

    [Fact]
    public void Joining_and_leaving_raise_events()
    {
        var account = CustomerAccount.Create("Acme", Owner, Now);
        var member = Guid.NewGuid();
        account.Invite(Owner, "m@example.test", MemberRole.Technical, "hash", Now, []);
        account.Accept("hash", member, "M@Example.test", Now);
        account.Remove(member, member);

        Assert.Collection(account.DomainEvents,
            e => Assert.IsType<CompanyCreated>(e),
            e => Assert.Equal(MemberRole.Technical, Assert.IsType<MemberJoined>(e).Role),
            e => Assert.Equal(member, Assert.IsType<MemberRemoved>(e).UserId));
    }
}
