# Module inventory

Responsibilities below are taken from the types that module actually contains.

## SharedKernel

- `Entity.Id` is `Guid.CreateVersion7()` (time-ordered).
- `AggregateRoot` collects `IDomainEvent` in memory only.
- `Result` / `Result<T>` and `Error` with `ErrorKind`. Error `Code` strings are the public API contract (`Result.cs` summary).
- `Guard.NotEmpty` throws `DomainException` with `VALIDATION_FAILED`.
- `ValueObject` equality is component-wise. Few domain types subclass it; records are used more often (`PageContent`, `ReleasePackage`).

## BuildingBlocks.Application

Ports the host or modules must implement: `ICurrentUser`, `IEmailSender`, `IAuditTrail`, `IPersonalDataSource`. `PortalLinks` builds absolute portal URLs from `Portal:Url` (required at startup). `RateLimitPolicies`: `auth` and `public-form`.

## BuildingBlocks.Web

`ResultMapping.StatusFor` maps `ErrorKind` to 400, 401, 403, 404, 409, 410, 423, 429, 503. `ToAccepted` returns 202 so registration and password reset do not reveal whether an account exists.

## Identity

**Responsibility:** accounts, email confirmation, password, cookie session, TOTP MFA, recovery codes, roles, security event log, personal-data export and account anonymization, bootstrap admin.

**Public surface:** `IUserDirectory`, `IdentityRoles`, `AuthPolicies`. HTTP under `/api/v1/auth` and `/api/v1/account`.

**Internal:** `IUserAccounts`, `ISignInSession`, `ISecurityLog` implemented with ASP.NET Core Identity in `UserAccounts` and `SignInSession`.

**Persistence:** schema `identity`. `IdentityDb` extends `IdentityDbContext<ApplicationUser, ApplicationRole, Guid>` and `IDataProtectionKeyContext`.

**Hosted:** `IdentitySeeder` migrates, ensures every role in `IdentityRoles.All`, creates one Admin if `Identity:BootstrapAdminEmail` and password are set and no Admin exists yet. Email is pre-confirmed. MFA is not turned on by the seeder.

## Customers

**Responsibility:** the tenant. `CustomerAccount` is a company. A user is a member of at most one company (unique index on `Members.UserId`). Roles: Owner, Billing, Technical, Viewer. Invitations last 7 days, max 50 members plus open invitations, token stored as SHA-256 only (`InvitationTokens` in `Tokens.cs`).

**Public surface:** `ICompanyDirectory` (`MembershipOfAsync`, `FindAsync`, `FindManyAsync`). Customer HTTP never takes a company id (`CompanyController`). Staff HTTP `AdminCustomersController` is read-only and uses `IStaffCompanyReader`, a separate port so customer handlers cannot query arbitrary companies.

## Licenses

**Responsibility:** anti-corruption layer over LicensingPlatform. Catalog cache (5 minutes, stale-on-failure), idempotent grant per order reference, encrypted product key, device list and reset, expiry reminder emails.

**Public surface:** `IPlanCatalog`, `ILicenseFulfillment`.

**Hosted:** `LicensesDbInitializer`, `ExpiryReminderWorker` (default every 360 minutes; `0` disables).

**External:** `LicensingPlatformClient` named HttpClient `LicensingPlatform`.

## Commerce

**Responsibility:** orders at a snapshotted price, hosted checkout, signed webhooks, invoices with a gapless yearly counter, append-only ledger, manual staff payment, retry of paid-but-unfulfilled orders.

**Public surface:** HTTP only. Other modules do not call Commerce.

**Hosted:** `CommerceDbInitializer`, `FulfillmentRetryWorker` (default 60 seconds).

**Gateway:** `IPaymentGateway`. Only `SandboxGateway` (`Name == "sandbox"`), registered only when `Commerce:Sandbox:WebhookSecret` is set and the environment is Development.

## Releases

**Responsibility:** store exact manifest bytes and detached signature, verify with `Releases:PublicKey`, publish only a newer draft, serve the current published manifest for update clients. The website does not sign.

**HTTP:** public `/updates/...` and admin `/api/v1/admin/releases`. Portal/site list published releases at `/api/v1/releases`.

## Support

**Responsibility:** company tickets (Open / Answered / Closed), suggestions, public demo requests.

**Limits:** 20 open tickets per company, 200 messages per ticket (`SupportScope` in `Support.cs`). Viewer may read and may not write. Staff reply is Support or Admin (`SupportScope.IsStaff`). Customer messages reopen a closed ticket (`Ticket.CustomerReply` sets status Open).

## Content

**Responsibility:** bilingual pages with version workflow Draft → InReview → Published, previous published version becomes Superseded. `Page.UpdatedAt` is a concurrency token. Editor cannot approve their own version (enforced in domain `Page` methods; see `Page.cs` review rules). Public read is published body only.

## Audit

**Responsibility:** append-only `audit.Entries`. No update or delete API. List is Admin + MFA.

**Write path:** `AuditBehavior` → `AuditTrail.RecordAsync`. Separate `SaveChanges` from the business transaction.

## Host web adapters (`src/Host/MonitorAgent.Api/Web`)

| Type | Role |
| --- | --- |
| `HttpCurrentUser` | `ClaimTypes.NameIdentifier` as Guid, `IsInRole` |
| `ValidateCsrfFilter`, `CsrfController` | Antiforgery header `X-XSRF-TOKEN`, readable cookie `XSRF-TOKEN` |
| `ApiExceptionHandler` | `DomainException` and `ValidationException` to ProblemDetails; anything else 500 `INTERNAL_ERROR` plus trace id, no exception message to the client |
| `SecurityHeaders` | API CSP `default-src 'none'` |
| `DatabaseHealthCheck` | `IdentityDb.Database.CanConnectAsync` as the ready check for the shared database |
| `SmtpEmailSender` | Only email provider implemented (`Email:Provider=Smtp`) |

## Front-end library `web/projects/shared`

Session, API client (CSRF, cookies), i18n, shell, account page, sign-in, formatting. Portal and admin import it. The public site uses it for language helpers and content rendering.
