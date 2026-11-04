# Testing strategy

## Backend

Project: `tests/MonitorAgent.Tests/MonitorAgent.Tests.csproj`.

| Area | Path | What it locks |
| --- | --- | --- |
| Unit | `tests/MonitorAgent.Tests/Unit` | `CustomerAccountTests`, `DispatcherTests`, `LicensingPlatformClientTests`, `ResultTests` |
| Architecture | `tests/MonitorAgent.Tests/Architecture/ArchitectureTests.cs` | Layering, single handler, internal handlers. Read in full for this analysis. |
| API | `tests/MonitorAgent.Tests/Api` | One class per module plus host, privacy, errors, key protection, demo requests. `ApiFactory` boots the real host. `FakeLicensingPlatform` replaces the platform. |
| Contract | `tests/MonitorAgent.Tests/Contract` | `OpenApiContractTests` against `openapi.v1.json` |

`dotnet test` is the local command from the README. CI runs Release tests with XPlat coverage and `scripts/ci/coverage-gate.py` at 80% lines for Domain, Application, and SharedKernel. A second job runs the same tests against SQL Server 2022 when `TEST_SQLSERVER` is set, one database per test class (comment in `ci.yml`).

This analysis did not run `dotnet test`. Do not claim the suite is green from these documents alone.

## Front end

| Layer | Command (from `web/`) | Notes |
| --- | --- | --- |
| Unit | `npx ng test site|portal|admin|shared` | CI runs all four, shared with coverage. `angular.json` threshold was not re-read; ADR-017 says 70% gate and a past 95% result. |
| E2E | `npx playwright test` | `web/e2e` specs for site, portal, admin. Fakes: `fake-admin-api.ts`, `fake-portal-api.ts`, `mock-api.mjs`. Playwright config uses Chromium in CI. Axe is a dependency (`@axe-core/playwright`). |
| Lighthouse | `npx @lhci/cli autorun` | `web/lighthouserc.json`. CI fails under performance 90, and the workflow comment cites LCP and CLS limits. |

## Load and security automation

- `tests/load/website.js` k6. Workflow `.github/workflows/load-test.yml` (not read line by line).
- `codeql.yml`, `security-scan.yml` (ZAP is described in ADR-015 as nightly).
- `restore-drill.yml` and `scripts/ops/restore-drill.sh`.
- Semgrep and gitleaks are jobs inside `ci.yml`.

## How to extend tests without breaking the rules

- Domain rule: unit test the aggregate (`CustomerAccountTests` is the pattern).
- HTTP rule: API test through `ApiFactory`, assert the error `code`, not only the status.
- New endpoint: update the OpenAPI snapshot in the same change or CI fails.
- New project reference from a module to another module's Infrastructure: `ArchitectureTests` fails.
- SQL-specific behavior (filtered unique indexes, `datetime2`) belongs in the suite that runs under `TEST_SQLSERVER`, because SQLite is the default.

## Gaps (confirmed by absence of a test name, not by running coverage)

- No test file name mentions a company with zero members after account deletion.
- No payment provider contract tests beyond sandbox (no provider exists).
- LicensingPlatform client tests exist (`LicensingPlatformClientTests.cs`) and were not read assertion by assertion. Paging past 200 is unlikely to be covered if the client never requests page 2.
