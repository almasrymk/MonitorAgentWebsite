using MonitorAgent.SharedKernel;

namespace MonitorAgent.Modules.Support.Domain;

public enum DemoRequestStatus { New, Contacted, Closed }

/// <summary>
/// A visitor asking for a demo from the public site. The visitor has no account: sales follows up by email or phone,
/// then marks the request as contacted or closed with a note only staff see.
/// </summary>
public sealed class DemoRequest : AggregateRoot
{
    public const int MaxName = 100;
    public const int MaxCompany = 150;
    public const int MaxEmail = 254;
    public const int MaxPhone = 30;
    public const int MaxSites = 100_000;
    public const int MaxMessage = 2000;
    public const int MaxNote = 1000;

    private DemoRequest() { }

    public string Name { get; private set; } = "";
    public string Company { get; private set; } = "";
    public string Email { get; private set; } = "";
    public string? Phone { get; private set; }
    public int? Sites { get; private set; }
    public string? Message { get; private set; }
    /// <summary>The site language the visitor wrote in, so the follow-up can use the same one.</summary>
    public string Language { get; private set; } = "ar";
    public DemoRequestStatus Status { get; private set; }
    public string? StaffNote { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? HandledAt { get; private set; }

    public static DemoRequest Submit(string name, string company, string email, string? phone, int? sites, string? message, string language,
        DateTimeOffset now) => new()
    {
        Name = name.Trim(),
        Company = company.Trim(),
        Email = email.Trim(),
        Phone = Blank(phone),
        Sites = sites,
        Message = Blank(message),
        Language = language == "en" ? "en" : "ar",
        Status = DemoRequestStatus.New,
        CreatedAt = now,
    };

    public void Handle(DemoRequestStatus status, string? note, DateTimeOffset now)
    {
        Status = status;
        StaffNote = Blank(note);
        HandledAt = now;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
