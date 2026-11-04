# Code quality issues

No code was changed. Severity is the impact if the path is hit. Confidence is how directly the source shows it.

## High — staff company search throws when a company has no owner

- **File:** `src/Modules/Customers/MonitorAgent.Modules.Customers.Application/Staff.cs`
- **Method:** `SearchCompaniesHandler.HandleAsync`, local function `Owner` (the `First()` call on owners).
- **Explanation:** `CustomerAccount.Forget` removes the last member and leaves the company so invoices and licences remain (`CustomerAccount.Forget` comment and body). `SearchCompaniesHandler` assumes every account in the page has an Owner and calls `.First()`.
- **Business impact:** After a sole member deletes their account, `GET /api/v1/admin/customers` can return 500 for any page that includes that company. Name search and the unfiltered list are affected. Email search of a deleted user may miss them (email was anonymized) but the company still appears in name or full lists.
- **Suggestion:** Treat a missing owner as an empty owner column and still return the row. Add an API test that deletes the only member and lists companies.
- **Confidence:** High. Both methods were read. Not executed.

## Medium — sandbox signature length is not checked before fixed-time compare

- **File:** `src/Modules/Commerce/MonitorAgent.Modules.Commerce.Infrastructure/Gateways/SandboxGateway.cs`
- **Method:** `Verify`
- **Explanation:** `CryptographicOperations.FixedTimeEquals` throws if the spans differ in length. A notification whose `X-Sandbox-Signature` is not the same length as the expected hex digest becomes an unhandled exception and HTTP 500, not `INVALID_SIGNATURE`.
- **Business impact:** Development and tests only. The gateway is not registered in Production. A malformed webhook is noisy and does not mark an order paid.
- **Suggestion:** If lengths differ, return false before `FixedTimeEquals`.
- **Confidence:** High for the throw behavior of `FixedTimeEquals`. Medium that every caller sees 500 (exception handler was read and would map it that way).

## Medium — manual payment check-then-act without a concurrency token

- **File:** `src/Modules/Commerce/MonitorAgent.Modules.Commerce.Domain/Order.cs` method `RecordManualPayment`; handler `RecordManualPaymentHandler` in `Payments.cs`.
- **Explanation:** Status is checked on the loaded aggregate. `Orders` has indexes but no row version. Two concurrent manual payments can both observe `PendingPayment` and both append a succeeded payment and a ledger line if both saves commit.
- **Business impact:** Duplicate ledger rows and a double fulfilment attempt. Fulfilment is idempotent per order number, so a second licence is unlikely. Money records would show two payments for one order.
- **Suggestion:** Concurrency token on `Order`, or a unique filtered index that allows only one succeeded payment per order. Do not change this without a migration review.
- **Confidence:** Medium. The race was not run.

## Medium — LicensingPlatform list calls stop at 200 rows

- **File:** `LicensingPlatformClient.PublishedPaidPlansAsync` and `LicensesOfAsync`.
- **Explanation:** A single page of 200 is requested. No follow-up page.
- **Business impact:** A catalog or a company with more than 200 rows silently drops the rest. Pricing and portal licence lists would be incomplete.
- **Suggestion:** Page until a short page is returned, or fail closed when `total` exceeds the page.
- **Confidence:** High that the client does not loop. Unverified whether the platform payload includes a total the client ignores (the `Paged<T>` shape was seen as `Items` only in the method that was read).

## Low — domain events are dead

- **Files:** aggregates that call `Raise`; every DbContext `Ignore(DomainEvents)`; no `ClearDomainEvents` usage under `src`.
- **Impact:** Future authors may think `OrderPaid` or `CompanyCreated` already notifies another module. They do not. Commerce calls licences directly.
- **Suggestion:** Leave them until a real subscriber exists, or stop raising them so the next reader is not misled. Not a behavior bug today.
- **Confidence:** High.

## Low — ADR-002 and README lag the code

- Pipeline includes `AuditBehavior` (ADR-002 omits it).
- README says Angular 21. `web/package.json` depends on Angular 22.2.x.
- **Impact:** Onboarding mistakes, not runtime defects.
- **Confidence:** High.

## Low — admin shell does not filter navigation by policy

- **File:** `web/projects/admin/src/app/app.routes.ts` `nav` data.
- **Impact:** Support sees Reports and Releases links and gets 403 from the API. Customers page checks `roles.includes('Admin')` before the manual grant action (`customers.ts`). Other pages were not all checked.
- **Confidence:** High for the route table. Medium for every page's buttons.

## Checked and not filed as defects

- Handlers are internal and unique (architecture tests).
- Webhook signature is read before the commerce store is used.
- Customer routes do not take a company id.
- Licence reveal checks platform customer id and company role.
- Audit failure does not roll back the business save (by design, ADR-014).
- No `TODO` / `FIXME` / `NotImplementedException` in `*.cs` or `*.ts`.
- Secrets are not committed in `appsettings.json` (empty strings). Bootstrap password is user-secrets or environment. `.gitleaksignore` exists; its entries were not reviewed as a finding.

## Tests that do not cover the high finding

`tests/MonitorAgent.Tests/Api/CustomersTests.cs` and `PrivacyTests.cs` exist. Whether they list staff companies after the last member is deleted was not confirmed by reading every assertion. Treat the high finding as unverified by tests until that assertion is found or added.
