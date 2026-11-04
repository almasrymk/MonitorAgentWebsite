# Solution structure

Solution file: `MonitorAgentWebsite.slnx` (SDK-style solution, not a classic `.sln`).

## Projects (31 `.csproj` files)

### Building blocks

| Project | Responsibility |
| --- | --- |
| `src/BuildingBlocks/MonitorAgent.SharedKernel` | `Entity`, `AggregateRoot`, `ValueObject`, `Result`, `Error`, `DomainException`, `Guard`, `DomainEvent` |
| `src/BuildingBlocks/MonitorAgent.BuildingBlocks.Application` | In-process CQRS: `IDispatcher`, handlers, logging / audit / validation pipeline, `ICurrentUser`, `IEmailSender`, `IPersonalDataSource`, `IAuditedCommand` |
| `src/BuildingBlocks/MonitorAgent.BuildingBlocks.Web` | `Result` to ProblemDetails, `NoCsrfAttribute` |

### Host

| Project | Responsibility |
| --- | --- |
| `src/Host/MonitorAgent.Api` | Composition root. `Program.cs` is the only process entry point (`public partial class Program`). |

### Modules

Each module is a folder under `src/Modules/<Name>/`. Projects follow `MonitorAgent.Modules.<Name>.<Layer>`.

| Module | Projects present |
| --- | --- |
| Identity | Domain, Application, Contracts, Infrastructure |
| Customers | Domain, Application, Contracts, Infrastructure |
| Licenses | Domain, Application, Contracts, Infrastructure |
| Commerce | Domain, Application, Infrastructure (no Contracts project; other modules call `ILicenseFulfillment` in Licenses.Contracts, not Commerce) |
| Releases | Domain, Application, Infrastructure |
| Support | Domain, Application, Infrastructure |
| Content | Domain, Application, Infrastructure |
| Audit | Application, Infrastructure (no Domain project; ADR-014) |

### Tests and front end

| Path | Responsibility |
| --- | --- |
| `tests/MonitorAgent.Tests` | xUnit: unit, NetArchTest architecture, `WebApplicationFactory` API, OpenAPI snapshot |
| `tests/load` | k6 script `website.js` and a load compose file |
| `web/` | One Angular workspace: applications `site`, `portal`, `admin`, library `shared` |
| `deploy/` | Production compose, Caddy, web image Dockerfile |
| `scripts/ci`, `scripts/ops` | Coverage gate, stack smoke, restore drill |
| `.github/workflows` | CI, CodeQL, security scan, load test, restore drill |

## Entry points

| Entry | File | What starts |
| --- | --- | --- |
| API | `src/Host/MonitorAgent.Api/Program.cs` | `WebApplication.CreateBuilder`, module registration, `app.RunAsync` |
| Public site browser | `web/projects/site/src/main.ts` | Angular bootstrap |
| Public site SSR | `web/projects/site/src/main.server.ts`, `server.ts` | Angular SSR / Express |
| Portal | `web/projects/portal/src/main.ts` | Angular bootstrap |
| Admin | `web/projects/admin/src/main.ts` | Angular bootstrap |
| First-admin seed | `IdentitySeeder.StartingAsync` | Hosted lifecycle service, runs during host start when `Identity:DatabaseInitialization` is `Migrate` or `EnsureCreated` |
| Schema migration | One `*DbInitializer` per module, same lifecycle hook | `Database.MigrateAsync` when that module's `DatabaseInitialization` is `Migrate` |

There is no second API host, no worker process, and no message bus project. Background work is `BackgroundService` / `IHostedLifecycleService` inside the API process.

## Namespace pattern

`MonitorAgent.Modules.<Module>.<Layer>[.<Feature>]`. HTTP controllers live in `*.Infrastructure.Http`. Persistence lives in `*.Infrastructure.Persistence`. Cross-module types that are allowed to be referenced live only in `*.Contracts`.

## Configuration files

| File | Role |
| --- | --- |
| `global.json` | .NET SDK `10.0.100`, `rollForward: latestFeature` |
| `Directory.Build.props` | `net10.0`, nullable, warnings as errors, central package versions, NuGet audit high |
| `Directory.Packages.props` | Package versions |
| `src/Host/MonitorAgent.Api/appsettings.json` | Empty connection string and origins; per-module `DatabaseInitialization: Migrate` |
| `appsettings.Development.json` | Local SQL Server trusted connection, CORS `http://localhost:4200`, SMTP to Mailpit |
| `.env.example` | `SQL_SA_PASSWORD` only |
| `deploy/env.example` | Production compose secrets template (not copied into this file; see deploy docs) |
| `web/angular.json`, `web/package.json` | Front-end workspace |

User secrets are documented for `Identity:BootstrapAdminEmail` and `Identity:BootstrapAdminPassword`. They are not in the repository.
