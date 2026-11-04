# Technical debt

Debt here means a confirmed gap between the design the code comments claim and the design the code runs, or a shortcut that will cost the next change. It is not a request to refactor.

## Accepted by ADR, still unpaid

| Item | Where it shows up | Cost of leaving it |
| --- | --- | --- |
| Payment provider not chosen | ADR-008, launch checklist, only `SandboxGateway` | No production revenue path |
| Licence issue is not idempotent on the platform | `FulfillLicenseHandler` | Duplicate licences after a crash |
| Refunds, renewals, nightly reconciliation | `LedgerEntryKind.Refund` unused; no renewal command | Finance reports and support cannot complete the commercial loop |
| Attachments and media | ADR-011, ADR-012 | Support and CMS stay text-only |
| Domain events with no dispatcher | `AggregateRoot.Raise` | Misleading extension point |
| Catalog cache can serve stale prices for longer than 5 minutes while the platform is down | `CatalogCache.PlansAsync` catch branch | A withdrawn plan can still be offered until the process restarts if the cache never refreshes successfully. The place-order path calls `OfferAsync`, which uses the same cache. **Confirmed.** |

## Structural

- Eight `DbContext` types, eight migration histories, one database. A change that must be atomic across orders and grants cannot use one transaction without a new pattern. The current retry design depends on that split. Do not "unify" contexts as a drive-by.
- `AddCqrs` is safe to call twice (`Dispatcher` registration is skipped if present) but `AddHandlersFrom` is not idempotent for the same assembly. Modules call it once. Fine.
- Audit pipeline is in BuildingBlocks and the writer is in Audit. If Audit is not registered, `GetService<IAuditTrail>()` is null and audited commands succeed with no line (`AuditBehavior`). The host always registers Audit. A future test host that forgets it would silently drop audit.
- Identity `DatabaseInitialization=None` skips role creation as well as migration (`IdentitySeeder` returns immediately). Tests must create roles themselves or set `EnsureCreated`.

## Documentation drift

- `README.md` technology line says Angular 21. `web/package.json` has `@angular/core` `^22.2.3`.
- ADR-002 pipeline description predates `AuditBehavior`.
- README module tree omits Support, Content, and Audit. Those modules exist and are wired in `Program.cs`.

## Front end

- Admin navigation is one list for every staff role.
- Three apps share `shared` for session and account, which is the right reuse. Site SSR has its own server (`server.ts`) and must keep forwarding the visitor IP (ADR-019). `forward-visitor.ts` exists; treat IP forwarding as load-bearing and re-read it before changing the site server.

## Dependencies

Central versions are pinned, NuGet audit is `high`, transitive pins exist for `System.Security.Cryptography.Pkcs` and `Xml` with GHSA comments in `Directory.Packages.props`. Do not upgrade packages as part of an unrelated task. `Microsoft.EntityFrameworkCore.Sqlite` and `Mvc.Testing` are 10.0.12 while SQL Server EF is 10.0.13. That skew is visible and was not investigated as a vulnerability.

## Tests as debt

Coverage gate is 80% line coverage on Domain and Application plus SharedKernel (`scripts/ci/coverage-gate.py`). ADR-017 says the current figure was 92%. That number was not recomputed. UI pages rely on Playwright rather than unit coverage. The zero-member company case is a hole until a test names it.
