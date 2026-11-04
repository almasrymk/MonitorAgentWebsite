# Execution flows

## Startup

1. `Program.cs` builds the service collection: controllers with CSRF filter and string enums, antiforgery, OpenAPI, problem details, exception handler, health check, `TimeProvider.System`, `AddCqrs`, `HttpCurrentUser`, email sender, `PortalLinks` (throws if `Portal:Url` is empty).
2. Each module registers DbContext, ports, hosted services.
3. Identity, in Production, throws if the data-protection certificate path is missing. Licenses, outside Development, throws if the platform base URL is missing. Commerce throws if the sandbox secret is set outside Development.
4. `Build`. Hosted lifecycle `StartingAsync` migrates schemas and seeds roles (and maybe the bootstrap admin) before the server accepts traffic.
5. Middleware order is documented in `02-ARCHITECTURE.md`.
6. `MapControllers` discovers controllers from each module's application part.

Background loops start with the host: fulfilment retry, expiry reminders. They no-op when their interval is `<= 0`.

## A command

Controller → `IDispatcher.SendAsync` → logging behavior → audit behavior (after) → validation behavior → internal handler → port → `DbContext.SaveChangesAsync` → `Result` → `ResultMapping` → ProblemDetails or 200/204/202.

Queries use `QueryAsync` and the same pipeline. Audit behavior ignores requests that are not `IAuditedCommand`.

## Sign-in

`POST /api/v1/auth/login` → `SignInCommand` → `ISignInSession.PasswordAsync` → security event on success, lockout, or failure. If MFA is required, the client calls `login/mfa` or `login/recovery-code` with the short-lived two-factor cookie. Logout is `SignOutCommand` and does not require the user to still be authenticated to clear cookies (handler signs out, then logs if `ICurrentUser` had an id).

## Buy a plan (happy path, Development sandbox)

1. Anonymous or SSR `GET /api/v1/catalog/plans` → cache → LicensingPlatform.
2. Owner or Billing `POST /api/v1/orders` with `planId` → price copied → `commerce.Orders` row `PendingPayment`.
3. `GET /api/v1/payments/providers` returns `["sandbox"]` only when the gateway is registered.
4. `POST /api/v1/orders/{id}/pay` with `{ "provider": "sandbox" }` → pending payment saved → checkout URL on the portal.
5. Portal sandbox page (front-end `SandboxCheckoutPage`) is expected to post a signed notification. The page source was not line-audited.
6. `POST /api/v1/payments/notifications/sandbox` with HMAC → `ConfirmPayment` (amount and currency must match) → ledger + invoice → save → receipt email → `FulfillLicenseCommand`.
7. Licenses module creates or resumes `licenses.Grants`, calls LicensingPlatform, stores protected product key, returns license number.
8. Commerce sets order `Fulfilled` and emails again without the key.
9. If step 7 fails, order stays `Paid`. `FulfillmentRetryWorker` calls `FulfillPaidOrdersCommand`.

## Manual bank payment

Finance or Admin with MFA: `POST /api/v1/admin/orders/{id}/manual-payment` with a reference. Same after-payment path, provider name `manual`. Audited `order.manual-payment`.

## Team invite

Owner `POST /api/v1/company/invitations` → domain `Invite` → save hash → email link `Portal:Url/invitations/accept?token=`. Accept is `POST /api/v1/company/invitations/accept` by a signed-in Customer whose email matches. Raw token is not stored.

## Content publish

Editor saves draft, submits. Reviewer (not the last editor) approves or rejects with a note. `UpdatedAt` concurrency: loser gets `PAGE_CHANGED`. Public site resolver reads `GET /api/v1/content/pages/{slug}`.

## Release publish

Admin uploads manifest bytes and signature. Handler verifies signature, then stores a Draft. Publish checks newer-than-current. Clients fetch `/updates/manifest.json`.

## Account deletion

Password → all personal-data sources `CanErase` → `Erase` (membership, invitation emails, IPs on security and audit rows — implementations in `*PersonalData.cs`) → identity anonymize → sign-out → email. Orders and invoices are kept. Ticket messages stay with a deleted-user display (ADR-016; display mapping not re-read in the support DTO mapper).

## Exceptions

`DomainException` and FluentValidation `ValidationException` become ProblemDetails with a code. Any other exception, including a handler bug, becomes 500 `INTERNAL_ERROR` and a server log line with the trace id only (the handler comment says request path is not placed in the log message). `logger.LogError(exception, ...)` still records the exception object, which may include data the exception captured. Confidence: the message template does not include the path; exception details depend on what was thrown.

## Validation versus authorization

FluentValidation runs before the handler for annotated commands (email shape, lengths, page bounds, enum). Authorization is the cookie plus policies, then handler checks (company role, content role, tenant match). A validation failure is 400 `VALIDATION_FAILED`. A missing session is 401. A wrong role is 403.

## Where rules are not on the server

Admin navigation visibility. Portal button visibility. Marketing copy in Angular when CMS has no published legal page. None of these replace the handlers above.
