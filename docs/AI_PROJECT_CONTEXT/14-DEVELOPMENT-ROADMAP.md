# Development roadmap

Priorities for a maintainer. This is not a schedule. The project owner still has open decisions in `docs/operations/launch-checklist.md`. Do not start a payment provider implementation until that decision is explicit.

## P1 — Safe to do inside the current architecture

1. **Staff company list and companies with no members.** `SearchCompaniesHandler` should not throw. Add an API test next to `CustomersTests` or `PrivacyTests` that deletes the only member and lists `/api/v1/admin/customers`. Small change in Customers application. No schema change.
2. **Sandbox signature length.** Return invalid signature when the header length differs. Development-only, but it keeps webhook tests honest.
3. **Document staff provisioning.** Until an audited command exists, write the operational steps in the knowledge base when they are actually used. Do not invent an admin UI in the same change as a bugfix.

## P2 — Business blockers that need a decision

1. **Payment adapter** behind `IPaymentGateway`. Keep `Order.ConfirmPayment` as the only place an order becomes paid. Keep notification idempotency in `ProcessedNotifications`. Refuse the adapter in the wrong environment the same way sandbox is refused. Add contract tests with the provider's signed examples. Do not log card data (the design never receives card data; do not add it).
2. **Refund command** that writes `LedgerEntryKind.Refund` and does not delete the invoice. Needs the provider's refund API and a finance policy. Not before P2.1.
3. **Renewal.** A new order for a plan is the only purchase path today. A renewal should either be that, documented, or a command that still ends in `FulfillLicenseCommand` with a new order reference. Do not call LicensingPlatform from a controller.

## P3 — Platform contract

1. Page through LicensingPlatform lists or fail when the page is full.
2. Pass an idempotency key when the platform offers one. Until then, keep the crash window documented on the grant handler.
3. Record the platform's OpenAPI or a fixture beside `FakeLicensingPlatform` when the platform contract changes. This repo's `openapi.v1.json` is the website contract, not the platform contract.

## P4 — Operations, not code

Certificate and password stored apart from database backups, `ReverseProxy:KnownNetworks`, real `Support:DemoRequestsEmail`, `Portal:Url`, production `Email:Provider`, first admin then removal of bootstrap settings, restore drill. Follow `docs/operations/` rather than duplicating it.

## Explicit non-goals unless requested

- Splitting modules into services.
- Replacing the in-process dispatcher with MediatR or a bus.
- Merging schemas into one DbContext.
- Rewriting Angular apps.
- Translating `docs/adr` (they are the decision record in Arabic).

## Before any of the above

Read `00` through `09` and the module you will touch, then re-open the files. This roadmap is a snapshot from 3 November 2026.
