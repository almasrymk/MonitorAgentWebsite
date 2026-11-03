# Project overview

Analysis date: 3 November 2026 (Africa/Cairo). Evidence is source code in this repository. No runtime database was queried.

## What the system is

Monitor Agent Website is the commercial site for the Monitor Agent desktop product. It is a separate product from LicensingPlatform. This repository owns the public marketing site, the customer portal, the staff admin console, and the API that backs them. License records, devices, and the product catalog live in LicensingPlatform and are reached only over HTTP (ADR-004, ADR-007).

Confirmed product surfaces:

| Surface | Path | Role |
| --- | --- | --- |
| Public site | `web/projects/site` | Arabic and English marketing pages, pricing, download, demo request, CMS pages. Server-rendered. |
| Customer portal | `web/projects/portal` | Company account, licenses, orders, invoices, support. Browser-only. |
| Admin console | `web/projects/admin` | Staff operations. Browser-only. |
| API | `src/Host/MonitorAgent.Api` | Single ASP.NET Core host. |

## Business outcome the code implements

A visitor can read plans, request a demo, and download a published, signed update manifest. A person can register, confirm email, sign in with optional TOTP, create one company, invite teammates, buy a published paid plan, pay through a hosted checkout (sandbox adapter only today), receive an invoice, and have a license issued in LicensingPlatform. Staff can search companies, record a manual payment, publish content through a two-person review, publish program releases, answer tickets, and read an append-only audit log.

## What is explicitly not done

Stated in `docs/operations/launch-checklist.md` and ADR-008, and confirmed by the absence of a second `IPaymentGateway` implementation:

- No Paymob or Stripe adapter. The only gateway is `SandboxGateway`, and the host refuses to register it outside Development (`CommerceModule.AddCommerceModule`).
- No refund command. `LedgerEntryKind.Refund` is readable by reports and is never written.
- No renewal purchase flow. Expiry emails exist (`ExpiryReminderWorker`); buying another term is a new order.
- No nightly provider reconciliation job.
- No ticket attachments or CMS media uploads.
- No external penetration test or production payment cutover. Those are checklist items for the project owner.

## How to read this knowledge base

- **Confirmed:** a type, method, route, or configuration line was read.
- **Inferred:** a reasonable reading of code that was not executed.
- **Unverified:** named in docs or tests but not traced in this pass.
- Existing Arabic docs under `docs/adr` and `docs/operations` were read and were not overwritten. Where they disagree with code, the code is the fact and the disagreement is recorded in `19-DECISIONS.md`.
