using MonitorAgent.BuildingBlocks.Application;

namespace MonitorAgent.Modules.Customers.Application;

/// <summary>Customer email texts, Arabic first then English.</summary>
internal static class Emails
{
    public static EmailMessage Invitation(string to, string inviter, string company, string link) => new(to,
        $"دعوة للانضمام لـ {company} | Invitation to join {company}",
        $"""
        {inviter} دعاك تنضم لحساب {company} في Monitor Agent.
        افتح الرابط ده وادخل (أو اعمل حساب) بنفس البريد ده. الدعوة صالحة 7 أيام:
        {link}
        لو مش متوقع الدعوة دي تقدر تتجاهلها.

        {inviter} invited you to join {company} on Monitor Agent.
        Open this link and sign in (or create an account) with this email. The invitation expires in 7 days:
        {link}
        If you weren't expecting this, you can ignore it.
        """);
}
