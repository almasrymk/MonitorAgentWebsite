# Decisions

Architectural decisions already accepted by the previous team live in `docs/adr/README.md` (ADR-001 through ADR-019). This file records how this analysis treats them, and decisions made while writing the knowledge base. It does not add a new product decision.

## D-001 — Code wins over ADR text

When `docs/adr/README.md` and the source disagree, the source is the behavior. Disagreements found:

| Topic | ADR text | Source |
| --- | --- | --- |
| CQRS pipeline | ADR-002: logging then validation | `AddCqrs`: logging, then audit, then validation |
| Angular major | README: Angular 21 | `web/package.json`: Angular 22.2 |
| Module list in README tree | Identity, Customers, Licenses, Commerce, Releases | `Program.cs` also registers Support, Content, Audit |
| Paid plan "current" | ADR-007 says published, paid, and current plans | `LicensingPlatformClient` comment: `IsCurrent` is not used; newest published version per code is used |

ADR-007's idempotency warning, ADR-008's sandbox restriction, and ADR-005's cookie and MFA rules match the code that was read.

## D-002 — Analysis does not refactor

This pass created only `docs/AI_PROJECT_CONTEXT/`. Defects are documented in `10` and `11`. They are not fixed here.

## D-003 — One database, many schemas, no invented foreign keys

The ERD in `05-DATABASE.md` marks cross-module links as logical. They are Guid columns without database foreign keys. Do not add a foreign key in a later migration without an explicit request and a data check (orphan company ids, anonymized users).

## D-004 — Tenant boundary

Customer features must keep resolving the company from `ICompanyDirectory.MembershipOfAsync` (or `ICustomerAccounts.ForMemberAsync`). Staff features must keep using `IStaffCompanyReader` or an explicit staff policy. A new customer route with a company id is a design change, not a convenience.

## D-005 — Payment and licence extension points

New money-in goes through `IPaymentGateway` and `Order.ConfirmPayment`. New licence issuance goes through `FulfillLicenseCommand` / `ILicenseFulfillment`. Controllers do not call `HttpClient` to LicensingPlatform.

## D-006 — Knowledge base language

These files are English because the source, identifiers, and error codes are English. The ADRs stay Arabic. Do not translate the ADRs as part of a feature change.
