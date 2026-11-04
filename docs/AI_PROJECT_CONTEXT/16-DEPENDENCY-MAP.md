# Dependency map

## Runtime package versions (`Directory.Packages.props`)

| Package | Version |
| --- | --- |
| FluentValidation | 12.2.0 |
| FluentValidation.DependencyInjectionExtensions | 12.2.0 |
| Microsoft.AspNetCore.Identity.EntityFrameworkCore | 10.0.13 |
| Microsoft.AspNetCore.DataProtection.EntityFrameworkCore | 10.0.13 |
| Microsoft.AspNetCore.OpenApi | 10.0.13 |
| Microsoft.EntityFrameworkCore.SqlServer | 10.0.13 |
| Microsoft.EntityFrameworkCore.Design | 10.0.13 |
| Microsoft.EntityFrameworkCore.Sqlite | 10.0.12 |
| Microsoft.AspNetCore.Mvc.Testing | 10.0.12 |
| Microsoft.Extensions.DependencyInjection.Abstractions | 10.0.13 |
| Microsoft.Extensions.Logging.Abstractions | 10.0.13 |
| System.Security.Cryptography.Pkcs | 10.0.13 (transitive pin) |
| System.Security.Cryptography.Xml | 10.0.13 (transitive pin) |
| xunit | 2.9.3 |
| xunit.runner.visualstudio | 4.0.1 |
| Microsoft.NET.Test.Sdk | 18.11.0 |
| NetArchTest.Rules | 1.3.2 |
| coverlet.collector | 10.1.0 |

SDK: `global.json` `10.0.100` with latest feature roll-forward. Target framework `net10.0`.

## Project references (logical)

```
Api
  → every *.Infrastructure
  → BuildingBlocks.Application
  → BuildingBlocks.Web (via ResultMapping in the host exception handler and CSRF)

*.Infrastructure
  → own Application, own Domain
  → other modules' Contracts only
  → BuildingBlocks.Application
  → BuildingBlocks.Web (controllers)

*.Application
  → own Domain
  → Contracts it is allowed to call
  → BuildingBlocks.Application
  → SharedKernel

*.Domain
  → SharedKernel

*.Contracts
  → SharedKernel where results are returned (Licenses contracts use Result)

SharedKernel
  → nothing in this solution
```

The architecture test loads module assemblies by walking references from the host, so a module that exists on disk but is not referenced by the host is not part of the running system. All eight modules are referenced from `Program.cs`.

## Front-end (`web/package.json`)

| Package | Version range |
| --- | --- |
| Angular packages | ^22.2.3 (SSR `@angular/ssr` ^22.2.2) |
| rxjs | ~7.8.0 |
| express | ^5.1.0 |
| compression | ^1.8.2 |
| qrcode-generator | 2.0.4 |
| TypeScript | ~6.0.3 |
| Vitest | ^4.0.8 |
| Playwright | 1.64.1 |
| Node in CI | 22 |
| npm | 10.9.4 (`packageManager`) |

Applications depend on the `shared` library (`public-api.ts`). There is no NgRx and no component library beyond the project's own SCSS.

## External systems at runtime

| System | Required |
| --- | --- |
| SQL Server 2022 | Yes |
| SMTP | Yes, or the host throws |
| LicensingPlatform HTTP API | Required outside Development for a useful catalog; Development degrades to unavailable |
| Payment provider | Only sandbox, Development only |
| Caddy / reverse proxy | Production compose, not required for `dotnet run` |

## CI actions

Pinned by commit SHA in `.github/workflows/ci.yml` (checkout, setup-dotnet, setup-node, upload-artifact, Trivy, SBOM). Dependabot config is `.github/dependabot.yml` (not fully read; ADR-015 says weekly NuGet, npm, Actions, Docker).
