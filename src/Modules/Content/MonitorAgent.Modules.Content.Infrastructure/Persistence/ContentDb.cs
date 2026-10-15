using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using MonitorAgent.Modules.Content.Application;
using MonitorAgent.Modules.Content.Domain;
using MonitorAgent.SharedKernel;

namespace MonitorAgent.Modules.Content.Infrastructure.Persistence;

public sealed class ContentDb(DbContextOptions<ContentDb> options) : DbContext(options)
{
    public const string Schema = "content";

    public DbSet<Page> Pages => Set<Page>();

    private static readonly ValueConverter<DateTimeOffset, DateTime> Utc =
        new(v => v.UtcDateTime, v => new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)));

    private static readonly ValueConverter<DateTimeOffset?, DateTime?> NullableUtc =
        new(v => v.HasValue ? v.Value.UtcDateTime : null, v => v.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)) : null);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<Page>(p =>
        {
            p.ToTable("Pages");
            p.HasKey(x => x.Id);
            p.Property(x => x.Id).ValueGeneratedNever();
            p.Ignore(x => x.DomainEvents);
            p.Ignore(x => x.Open);
            p.Ignore(x => x.Published);
            p.Property(x => x.Slug).HasMaxLength(Page.MaxSlug);
            p.HasIndex(x => x.Slug).IsUnique();
            p.Property(x => x.CreatedAt).HasConversion(Utc);
            // Every workflow step moves UpdatedAt, so two people acting on the same page at once can't both win.
            p.Property(x => x.UpdatedAt).HasConversion(Utc).IsConcurrencyToken();
            p.OwnsMany(x => x.Versions, v =>
            {
                v.ToTable("PageVersions");
                v.WithOwner().HasForeignKey("PageId");
                v.HasKey(x => x.Id);
                v.Property(x => x.Id).ValueGeneratedNever();
                v.Ignore(x => x.Content);
                v.HasIndex("PageId", nameof(PageVersion.Number)).IsUnique();
                v.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
                v.Property(x => x.TitleAr).HasMaxLength(PageContent.MaxTitle);
                v.Property(x => x.TitleEn).HasMaxLength(PageContent.MaxTitle);
                v.Property(x => x.DescriptionAr).HasMaxLength(PageContent.MaxDescription);
                v.Property(x => x.DescriptionEn).HasMaxLength(PageContent.MaxDescription);
                v.Property(x => x.BodyAr).HasMaxLength(PageContent.MaxBody);
                v.Property(x => x.BodyEn).HasMaxLength(PageContent.MaxBody);
                v.Property(x => x.ReviewNote).HasMaxLength(Page.MaxNote);
                v.Property(x => x.EditedAt).HasConversion(Utc);
                v.Property(x => x.ReviewedAt).HasConversion(NullableUtc);
                v.Property(x => x.PublishedAt).HasConversion(NullableUtc);
            });
            p.Navigation(x => x.Versions).UsePropertyAccessMode(PropertyAccessMode.Field);
        });
    }
}

internal sealed class ContentStore(ContentDb db) : IContentStore
{
    public Task<Page?> PageAsync(Guid id, CancellationToken ct) => db.Pages.SingleOrDefaultAsync(p => p.Id == id, ct);

    public Task<Page?> PageBySlugAsync(string slug, CancellationToken ct) => db.Pages.AsNoTracking().SingleOrDefaultAsync(p => p.Slug == slug, ct);

    public Task<bool> SlugTakenAsync(string slug, CancellationToken ct) => db.Pages.AnyAsync(p => p.Slug == slug, ct);

    public async Task<IReadOnlyList<Page>> PagesAsync(CancellationToken ct) =>
        await db.Pages.AsNoTracking().OrderBy(p => p.Slug).Take(500).ToListAsync(ct);

    public void Add(Page page) => db.Pages.Add(page);

    public async Task<Result> SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return Result.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ContentErrors.Changed;
        }
        catch (DbUpdateException) when (db.ChangeTracker.Entries<Page>().Any(e => e.State == EntityState.Added))
        {
            // Two editors created the same address at once; the unique index kept one of them.
            return ContentErrors.SlugTaken;
        }
    }
}
