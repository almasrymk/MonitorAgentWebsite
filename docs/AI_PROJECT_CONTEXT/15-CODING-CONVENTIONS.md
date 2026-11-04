# Coding conventions

Observed from the code. Follow these on the next change.

## C#

- Nullable enabled, warnings as errors, `AnalysisLevel` latest-recommended (`Directory.Build.props`). Several CA rules are suppressed globally (`NoWarn`). Do not add a new suppression to hide a local warning without a reason.
- File-scoped namespaces. One main type family per file; a file often holds the command, validator, and handler together (`Orders.cs`, `SignIn.cs`).
- Handlers and ports that are module-private are `internal`. Public types are DTOs, commands, and contracts.
- Domain invariants throw `DomainException` or return `Result` / `Error` constants on a static `*Errors` class. Error `Code` values are API contract. Do not rename them.
- Aggregates use private setters, private EF constructors, and `Guard.NotEmpty` for required strings.
- EF configurations live in `OnModelCreating`, not in separate `IEntityTypeConfiguration` classes (Identity, Customers, Commerce, and the others that were read).
- DateTimeOffset is stored as UTC `datetime2` through a value converter so SQLite tests can sort them. New date columns need the same converter.
- Guid keys are client-generated (`Guid.CreateVersion7` on `Entity`), `ValueGeneratedNever`.
- Logging of hot paths uses `[LoggerMessage]` source generators.
- Async methods take `CancellationToken`. Audit write after success uses `CancellationToken.None` on purpose.
- Comments explain why (idempotency, enumeration attacks, PCI), not what a setter does. Match that tone. Existing comments are English. ADRs are Arabic. New code comments should stay English so they match the source.

## HTTP

- Routes are `api/v1/...` except `/updates`, `/health`, and the CSRF path under `api/v1/auth`.
- Controllers are thin: map the body to a command and call `IDispatcher`. Business rules do not go in the controller.
- Authorize with `AuthPolicies` constants, not magic strings.
- New anonymous POSTs need a rate-limit policy and CSRF, unless they are a signed server callback, in which case use `[NoCsrf]` and verify the signature before any database write.
- Success with no body is 204 via `ToActionResult`. Deliberately vague success (email sent or not) is 202 via `ToAccepted`.

## Front end

- Angular workspace, standalone components, `loadComponent` routes.
- User-visible strings go through the i18n dictionaries (`texts.ts` and shared i18n). ADR-010 says a missing key fails the build. Do not hardcode a new Arabic or English sentence in a template if the neighboring page uses the dictionary.
- Logical CSS, not left/right, so RTL keeps working (`projects/shared/styles`).
- Do not render ticket or CMS body through `innerHTML`. The content renderer builds elements. Keep that when editing `content.ts` in shared or site.
- Money displayed in the browser comes from the API. Do not recompute prices in TypeScript (ADR-010).

## Tests

- xUnit facts, `WebApplicationFactory` in `tests/MonitorAgent.Tests/Api`, architecture rules in `ArchitectureTests`. A new module assembly whose name starts with `MonitorAgent.Modules.` is picked up by those rules once the host references it.
- A new command needs exactly one handler or the architecture test and startup registration fail.
- OpenAPI snapshot must be updated consciously (`UPDATE_OPENAPI_SNAPSHOT=1`) when the contract changes.
- Front-end unit tests are `*.spec.ts` (Vitest via Angular). End-to-end tests are Playwright under `web/e2e`.

## What not to introduce

- A second dispatcher, a generic repository base, AutoMapper, or MediatR.
- A customer endpoint that accepts `companyId`.
- Secrets in `appsettings.json` or source.
- Cross-module references to another module's Domain, Application, or Infrastructure. Add a type to `Contracts` instead.
