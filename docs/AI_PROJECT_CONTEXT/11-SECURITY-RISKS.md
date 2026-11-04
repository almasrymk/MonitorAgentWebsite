# Security risks

This is a code review, not a penetration test. No exploit was run. Items already accepted in ADRs are labeled as residual risk.

## Critical

None confirmed in source. Production refuses the sandbox gateway and refuses to start without a data-protection certificate and a LicensingPlatform base URL. Authentication is cookie-based with CSRF on state-changing routes. Staff admin routes require a second-factor claim.

## High — duplicate licence if the process dies after LicensingPlatform accepts issue

- **Where:** `FulfillLicenseHandler.HandleAsync` after `IssueLicenseAsync` and before `store.SaveAsync`.
- **Impact:** A retry creates another subscription licence. ADR-007 states this and says the fix belongs in LicensingPlatform (idempotency key). Customer could be billed once and receive two seats, or support load spikes.
- **Mitigation already present:** unique `OrderReference` prevents a second *grant row*, not a second platform licence, once the first issue succeeded and the row was not saved.
- **Confidence:** High for the sequence. Frequency is a crash window, not a normal request.

## High — no production payment adapter, and launch still depends on a correct webhook design

- **Where:** `CommerceModule` only registers `SandboxGateway`. Launch checklist section 7 is unchecked in `docs/operations/launch-checklist.md`.
- **Impact:** Taking real money on the sandbox secret would be blocked by the production throw. The residual risk is a future adapter that skips the amount check or the signature check. `Order.ConfirmPayment` already requires exact amount and currency. A new gateway must keep `ReadNotification` failing closed before `HandlePaymentNotificationHandler` touches the database.
- **Webhook endpoint:** anonymous, CSRF disabled, rate limit disabled (`CommerceControllers.cs`). That is safe only while signature verification is cheap and mandatory. A future provider that does heavy work before rejecting a bad signature becomes an unauthenticated CPU sink.
- **Confidence:** High that sandbox cannot load in Production. The future-adapter risk is a requirement, not a current bug.

## Medium — payment notification body is fully buffered with rate limits off

- **Where:** `PaymentNotificationsController.Receive`, `DisableRateLimiting`, `MaxBodyBytes` 64 KiB.
- **Impact:** An open endpoint. Today only `sandbox` is a known provider; any other name returns `UnknownProvider` after the gateway lookup, still after reading the body. In Development this is acceptable. In Production with no gateway registered, the endpoint still exists and reads up to 64 KiB per request with no IP limit.
- **Suggestion:** When no gateway is registered, reject before reading, or apply a dedicated small limiter even for signed providers (limit failures, not successes).
- **Confidence:** High for the attributes. Impact in Production today is abuse of an endpoint that cannot mark orders paid.

## Medium — staff role assignment is outside the application

- **Where:** no admin endpoint creates Support, Finance, ContentEditor, or ContentReviewer. Bootstrap creates Admin only (`IdentitySeeder`).
- **Impact:** Operators will edit `identity` tables or use a one-off script. A mistake can grant Admin. Also, those accounts cannot self-delete (`STAFF_ACCOUNT`), which is correct, but there is no audited "close staff account" command in the controllers that were listed.
- **Confidence:** High that no such route exists in the route inventory. A hidden minimal API would have shown up in `Program.cs`; there is none.

## Medium — product key disclosure

- **Where:** `RevealProductKeyHandler` unprotects and returns the full key to Owner or Technical. Audited and logged. Response is `no-store`.
- **Impact:** Any Owner or Technical session (or XSS on the portal origin) can read keys. XSS is mitigated by the site CSP described in ADR-015; the portal CSP is Caddy's, described in ADR-019, not re-read from `Caddyfile` line by line in this pass.
- **Confidence:** High for who can call the endpoint.

## Low — security log and audit store IP addresses

Forgotten on account erasure by personal-data sources (`ForgetIpAddress` on `SecurityEvent`; audit source in `AuditPersonalData.cs`, file exists, method body not fully quoted here). Until erasure, IPs are personal data in exports.

## Low — demo request and registration as email amplifiers

Registration of an existing user sends one notice to the owner (`AlreadyRegistered`). Demo requests do not email the visitor. Duplicate demo emails inside a day are dropped. Auth and form rate limits apply. Residual: distributed IPs can still send mail up to the limit. Confidence high for the single-node limits.

## Secrets handling (confirmed)

- `appsettings.json` has empty connection string, platform password, and public key.
- `.env.example` has an empty `SQL_SA_PASSWORD`.
- Bootstrap password is not in source; seeder logs a warning that an admin was created, not the password.
- CI SQL password is `Ci-${{ github.run_id }}-Aa1` inside the workflow file. It is a per-run CI password on an ephemeral service, not a production secret. Still visible in git. Severity low.

## Multi-tenant isolation (confirmed design)

Customer handlers resolve the company from `ICurrentUser` through membership. A guessed order id, ticket id, or licence id outside the company becomes not-found. Staff endpoints are allowed to pass `companyId` and are behind MFA policies. This is not a query-filter guarantee: a new handler that accepts a company id on a customer controller would break isolation, and architecture tests do not catch that. Review new customer endpoints for a company id parameter.

## Authorization gaps not found

Payment amount mismatch does not mark the order paid. Staff MFA disable is rejected. Content self-approval is rejected in the domain: `Page.cs` returns `ContentErrors.OwnVersion` when `version.EditedBy == reviewer` (lines 156 and 169). Public manifest does not accept uploads.
