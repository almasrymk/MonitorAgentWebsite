# Analysis coverage

Date: 3 November 2026 (Africa/Cairo). Workspace file count excluding `.git`, `node_modules`, `bin`, `obj`, and `dist`: **358 files**.

This pass did **not** execute the application, the test suite, or a database. Coverage is source reading.

## Totals

| Bucket | Count (approx.) | Meaning |
| --- | --- | --- |
| Discovered | 358 | All files under the workspace with those directories excluded |
| Analyzed | see below | Read enough to describe behavior and cite paths |
| Partially analyzed | see below | Opened or sampled; not every line |
| Not analyzed | none of the first-party source trees were skipped entirely | Individual files listed |
| Excluded | binaries, lockfile body, generated EF designer dumps | Not treated as behavior |

## Analyzed (behavior claimed from these)

- Solution, build props, package versions, `global.json`, README, `docker-compose.yml`, `deploy/docker-compose.yml` (services section), `appsettings.json`, `appsettings.Development.json`, `.env.example`.
- `Program.cs` and host `Web/*` (CSRF, exception handler, current user, security headers, email, health check).
- Shared kernel (all five types) and application building blocks (dispatcher, behaviors, DI, messaging, audit, privacy, abstractions).
- `ResultMapping`.
- Identity: module, roles, errors, security event, sign-in, registration, MFA, passwords, privacy, ports, queries, both controllers, seeder, `IdentityDb`.
- Customers: aggregate, errors, company/team/staff application, module, `CustomersDb`, both controllers, `CompanyDirectory` contract, privacy source (export and erase entry).
- Licenses: grant aggregate, fulfilment, catalog, my-licences handlers, module, `LicensesDb`, controllers, platform client through `ResetDeviceAsync`.
- Commerce: `Order` aggregate, `Orders.cs`, `Payments.cs` (notification, manual payment, retry, list), module, gateway, controllers, `CommerceDb` mapping through ledger indexes.
- Releases: `Release` aggregate, `ReleasesDb`.
- Support: `Ticket` and suggestion and errors, `DemoRequest` fields, `SupportDb`, role gates via grep of `Support.cs`.
- Content: `Page` model and `OwnVersion` checks, `ContentDb`, `ContentScope`.
- Audit: `AuditEntry`, `AuditDb`, `AuditTrail`.
- `ArchitectureTests.cs` in full.
- `docs/adr/README.md` in full. Launch checklist in full.
- Front-end routes for site, portal, and admin. `web/package.json`. CI workflow `ci.yml`.

## Partially analyzed

| Item | What was not finished |
| --- | --- |
| `UserAccounts.cs`, `SignInSession.cs`, `SecurityLog.cs`, `UserDirectory.cs` | Wiring and interfaces read; method bodies not all read. Password hashing is Identity's default. |
| `LicensingPlatformClient` after line 120 | Error mapping and token handler not fully read. |
| `Reports.cs`, `Releases.cs`, `Manifest.cs`, `Support.cs`, `Content.cs` remainder, `DemoRequests.cs` | Rules cited from the parts and from ADRs that matched grep. Not every validator line. |
| EF migrations `*.Designer.cs` and `*ModelSnapshot.cs` | Schema taken from `OnModelCreating`, which is the current model. Historical migration diffs were not reviewed. Migration class names were listed from the tree. |
| `openapi.v1.json` | ~5900 lines. Routes taken from controllers instead. |
| API and unit test method bodies | File names and the architecture test. Assertions not all read. |
| Angular page components and `*.spec.ts` | Routes and a few guards (`customers.ts` admin check, `app.routes.ts`). Templates not all read. |
| `deploy/Caddyfile`, `deploy/web.Dockerfile`, other workflows | Described from ADR-019 and `ci.yml`. Caddy directives not quoted line by line. |
| `package-lock.json` | Excluded as a lockfile. Direct dependencies taken from `package.json`. |
| `web/projects/*/public/favicon.ico` | Binary, ignored. |
| `docs/operations/backup-restore.md`, `deploy.md`, `monitoring.md` | Named and used for launch risks. Not copied into this knowledge base line by line. |

## Not-yet-analyzed components

No module folder was left unopened. The unread tails are the partial list above, not a hidden project.

If a later task touches one of those tails, read the file before editing and update this table.

## Excluded or generated

- `.git/`
- `node_modules/`, `bin/`, `obj/`, `dist/` (not present in the clean tree)
- EF designer and snapshot files as generated companions of migrations
- `openapi.v1.json` as a generated contract snapshot (still a source of truth for CI; update it when the API changes)

## Claim boundary

Enough of the system is understood to add a feature inside an existing module without rediscovering the host, the tenant rule, or the payment and licence pipeline. It is not enough to claim every test assertion or every Angular template was reviewed. Say which file you re-read when the next change starts.
