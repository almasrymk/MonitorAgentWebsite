# Architecture

## Verdict (verified from references, not folder names)

The running system is a **modular monolith** with **one deployable** (`MonitorAgent.Api`) and **one SQL Server database** split into **schemas per module**. Inside each module the dependency direction is the Clean Architecture shape: Domain does not reference EF or ASP.NET, Application does not reference EF or ASP.NET (enforced by `ArchitectureTests.Module_domain_and_application_layers_stay_free_of_web_and_database_code`), Infrastructure implements ports and hosts controllers.

It is **not** microservices. It is **not** a separate read/write store (no second database, no projections). It **is** a lightweight CQRS style: commands and queries are distinct types dispatched by an in-process `Dispatcher` (`BuildingBlocks.Application/Dispatcher.cs`). It uses **aggregates and invariants** in Domain (DDD tactical patterns) without a separate bounded-context process. It does **not** run an event bus: aggregates call `Raise`, EF ignores `DomainEvents`, and nothing in `src` calls `ClearDomainEvents` or publishes them. Event types are recorded intent, not runtime integration.

There is **no Unit of Work type**. Each module calls `SaveChangesAsync` on its own `DbContext`. A business step that touches two modules (pay, then issue a license) is two saves, made safe to retry by unique keys (`commerce.Orders.Number` as the license order reference, unique `licenses.Grants.OrderReference`).

Repository-style ports exist (`ICustomerAccounts`, `ICommerceStore`, `ILicenseStore`, `IContentStore`, `IReleaseStore`, `ISupportStore`, `IAuditStore`) and are implemented next to the DbContext. They are not a generic repository base.

## Dependency rule that is actually tested

`tests/MonitorAgent.Tests/Architecture/ArchitectureTests.cs`:

- SharedKernel must not reference BuildingBlocks, modules, the host, ASP.NET, or EF.
- Application building blocks must not reference modules, the host, or ASP.NET.
- Web building blocks must not reference modules or the host.
- No module assembly may reference `MonitorAgent.Api`.
- A module may reference another module only through that module's `Contracts` assembly. Domain, Application, and Infrastructure of another module are forbidden.
- Every `ICommand<>` / `IQuery<>` in a module assembly has exactly one handler.
- Handlers are `internal`. Callers use `IDispatcher`.

`AddHandlersFrom` also throws at startup if a second handler is registered for the same request type (`DependencyInjection.cs`).

## Pipeline (actual order)

`AddCqrs` registers, first to last, so the first is outermost (`Dispatcher.RunPipeline` reverses the list):

1. `LoggingBehavior` — duration and failure code. Exceptions other than cancellation are logged and rethrown.
2. `AuditBehavior` — if the request implements `IAuditedCommand` and the result is not a failed `Result`, writes `IAuditTrail`. A failed audit write is logged and does not fail the request. Uses `CancellationToken.None` so a client abort after success still tries to audit.
3. `ValidationBehavior` — FluentValidation validators for the request. Multiple validators' messages are joined. If `TResult` is `Result` or `Result<T>`, returns `VALIDATION_FAILED`. Otherwise throws `ValidationException`.

ADR-002 still says the pipeline is logging then validation and does not mention audit. The code includes audit. See `19-DECISIONS.md`.

## Host composition (`Program.cs`)

Order of middleware after `Build`:

1. Optional forwarded headers when `ReverseProxy:KnownNetworks` is non-empty.
2. Exception handler (`ApiExceptionHandler`).
3. Status code pages.
4. Security headers (nosniff, DENY framing, no-referrer, locked-down CSP).
5. HSTS and HTTPS redirection outside Development.
6. OpenAPI map in Development only.
7. CORS (configured origins, credentials allowed, any header and method).
8. Authentication.
9. Rate limiter.
10. Authorization.
11. `/health/live` (no checks), `/health/ready` (database tag), controllers.

CSRF is an MVC authorization filter on every controller (`ValidateCsrfFilter`), not middleware. GET/HEAD/OPTIONS/TRACE and `[NoCsrf]` skip it.

## Module registration order

`Program.cs` calls, in order: Identity, Customers, Licenses, Commerce, Releases, Support, Content, Audit. Each `Add*Module` registers its DbContext, stores, hosted services, `Add*Application` (handlers and validators), and `AddApplicationPart` so controllers in the infrastructure assembly are discovered.

## Cross-module contracts (confirmed references)

| Contract | Defined in | Used by |
| --- | --- | --- |
| `IUserDirectory` | Identity.Contracts | Customers, Licenses, Commerce, Support, Content, Audit (name lookup) |
| `ICompanyDirectory` | Customers.Contracts | Licenses, Commerce |
| `ILicenseFulfillment`, `IPlanCatalog`, `FulfilledLicense`, `PlanOffer` | Licenses.Contracts | Commerce |
| `IAuditTrail` | BuildingBlocks.Application | Implemented by Audit infrastructure |
| `IPersonalDataSource` | BuildingBlocks.Application | Implemented by modules that store personal data; Identity export/delete enumerates all registrations |
| `AuthPolicies`, `IdentityRoles` | Identity.Contracts | Every controller that authorizes |

## Architectural violations and debt (no redesign performed)

- Domain events are raised and then discarded. Callers must not assume `CompanyCreated` or `OrderPaid` has a subscriber.
- Commerce depends on Licenses contracts and Identity/Customers contracts. That matches ADR-001. Commerce has no Contracts project, so nothing else is allowed to call Commerce internals. Confirmed by the architecture test for Commerce layers (Application, Domain, Infrastructure only).
- Staff authorization for content is split: the controller policy is any staff with MFA (`AuthPolicies.Staff`), and editor versus reviewer is checked inside `ContentScope`. Support and Finance can hit the route and receive a domain error. That is intentional layering, not a missing policy, but it is coarser than the admin finance routes.
- Each module opens its own `DbContext` to the same connection string. There is no shared transaction across schemas.

## Patterns that were checked and are absent

Onion naming, MediatR, outbox, sagas, API gateway process, BFF, separate query database, global EF query filters for tenants, soft-delete interceptor, stored-procedure data access.
