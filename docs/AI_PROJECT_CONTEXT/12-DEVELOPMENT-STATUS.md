# Development status

Classification uses executed code paths as far as they can be read. Nothing was run (`dotnet test` was not executed in this analysis). "Completed" means the server flow and a UI route both exist and the rules are in domain or handlers, not that a browser was exercised.

## Completed (code present end to end)

| Feature | Evidence |
| --- | --- |
| Register, confirm email, sign-in, MFA, recovery codes, logout, forgot/reset/change password, session revoke | Identity application + `AuthController` + `AccountController` + portal/admin auth routes |
| Personal data export and customer account deletion | `Privacy.cs`, `IPersonalDataSource` implementations, ADR-016 |
| Company create, rename, invite, accept, role change, remove | `CustomerAccount` + `CompanyController` + portal company routes |
| Staff company search and detail (read-only) | `AdminCustomersController` + admin customers pages |
| Public catalog | `CatalogController` + site pricing route |
| Orders, sandbox checkout wiring, webhook, invoices, ledger, manual payment, fulfilment retry | Commerce module + portal order routes + admin orders |
| Licence list, detail, key reveal, device reset, admin grant, staff list | Licenses controllers + portal licenses pages |
| Expiry reminder worker | `ExpiryReminderWorker` + `SendExpiryRemindersCommand` (command type referenced by the worker; handler file not fully quoted) |
| Releases upload, publish, withdraw, public manifest | Releases module + admin releases page + site download page |
| Tickets, suggestions, demo requests | Support module + portal and admin and site `/demo` |
| CMS draft/review/publish and public page route | Content module + admin content pages + site `pages/:slug` |
| Audit read API and admin page | Audit module |
| Security event list for admins | `SecurityEventsController` + admin security page |
| Health checks, security headers, rate limits, CSRF, forwarded headers | `Program.cs` |
| CI: tests, SQL Server job, web unit and Playwright and Lighthouse, Docker Trivy, stack smoke, Semgrep, gitleaks | `.github/workflows/ci.yml` |

## Partially implemented

| Feature | What is missing |
| --- | --- |
| Payments | Sandbox only. No Paymob or Stripe. Refunds and renewal checkout are absent. Reports already understand refunds. |
| Staff user administration | Roles can be seeded and one Admin bootstrapped. No API to assign Support, Finance, or content roles, or to close a staff account. |
| LicensingPlatform paging | First 200 plans and 200 licences only. |
| Legal pages | CMS overrides compiled copy; publishing terms, privacy, and refunds is an operations task (`launch-checklist.md`). |
| Email | SMTP only. No provider failover. |
| Observability | Logs and health endpoints. No metrics exporter in the API project. |
| Domain events | Raised, never handled. |

## Not implemented

- Real card payment and the launch checklist items that are still open (hosting choice, domain, SPF/DKIM, external pentest, written go-live approval).
- Nightly settlement against the payment provider (ADR-008 "لسه").
- Ticket attachments and CMS media (explicitly deferred until malware scanning).
- Navigation menus driven by the CMS (ADR-012).
- Idempotent licence issue on the platform side.

## Broken

No compile failure was observed because the solution was not built in this run. Do not treat that as a green build.

The staff customer list can throw when a member-less company is included. That path is a defect, not an unfinished feature. See `10-CODE-QUALITY-ISSUES.md`. Until a test run proves otherwise, status of that endpoint is **needs verification** against a database that contains a zero-member company.

## Needs verification

- Every `*.spec.ts` and API test assertion. Files exist and CI runs them. This pass did not execute them and did not read every test method.
- OpenAPI snapshot matches the current controllers. The test would fail CI if it did not; CI on `main` was not queried.
- SSR pricing and download actually call the API (`forward-visitor.ts` and page components exist; the HTTP path from the Node server was not traced line by line).
- Caddy CSP for `/portal` and `/admin` matches ADR-019.

## Legacy or deprecated

No obsolete module or feature flag was found. `LedgerEntryKind.Refund` is forward-looking, not legacy. Sandbox is development-only, not deprecated.

## Suggested order of remaining work

1. Payment provider adapter and webhook verification tests against the provider's test mode (business blocker, depends on owner decision).
2. Fix or confirm the zero-member company staff search (security and support operations; small code change, needs a regression test).
3. Staff provisioning that is audited (Support and Finance cannot use the admin product until someone can create them safely).
4. LicensingPlatform idempotency and paging (duplicate licence and silent truncation).
5. Refund and renewal only after the provider exists.
6. Launch checklist environment items (certificate, `Portal:Url`, known proxy networks, demo mailbox, backups). Those are operations, not new modules.
