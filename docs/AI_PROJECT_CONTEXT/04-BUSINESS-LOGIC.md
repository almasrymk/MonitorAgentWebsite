# Business logic

Behavior below is what the handlers and aggregates do. UI checks are called out when they differ from the server.

Legend: **Confirmed** means the method was read. **Inferred** means the rule follows from a method that was read plus a caller that was identified but not executed.

## Domains

1. Identity and sessions.
2. Company tenancy and team.
3. Catalog and license fulfilment (delegated to LicensingPlatform).
4. Orders, payments, invoices, ledger.
5. Program releases and update manifests.
6. Support tickets, suggestions, demo requests.
7. Public content with editorial review.
8. Audit and security log (records of the above, not a separate business).

## Identity

**Registration** (`RegisterHandler`): if the email exists, send "already registered" and return success. If not, create a `Customer` user, record `Registered`, send confirmation. Response is always success at the HTTP layer (`202` via `ToAccepted`) unless validation fails. Password minimum 12, max 128, Identity also requires 4 unique characters and does not require character classes (`IdentityModule` password options).

**Confirm email:** token from Identity token provider, lifespan 3 hours (`DataProtectionTokenProviderOptions`). Invalid token → `INVALID_TOKEN`.

**Sign-in** (`SignInHandler` → `ISignInSession.PasswordAsync`): outcomes `Succeeded`, `RequiresMfa`, `NotConfirmed` (`EMAIL_NOT_CONFIRMED`), `LockedOut` (`ACCOUNT_LOCKED`, HTTP 423), otherwise `INVALID_CREDENTIALS` for both unknown email and bad password. Lockout is 5 failures, 15 minutes (Identity options). Cookie name `__Host-ma.session`, 8 hours sliding, HttpOnly, Secure, SameSite Strict. MFA pending cookie `__Host-ma.mfa` expires in 5 minutes.

**MFA:** staff roles cannot disable MFA (`MfaRequiredForStaff`). Enabling MFA marks the current session with claim `amr=mfa` (`MarkMfaAsync`). Security-stamp refresh copies that claim (`OnRefreshingPrincipal`). Staff policies require the claim, not merely `TwoFactorEnabled`. A staff user who has not enabled MFA can sign in to `/api/v1/account` but receives `MFA_REQUIRED` on admin policies.

**Password reset:** unknown or unconfirmed email returns success and sends nothing. Success revokes sessions (`RevokeSessionsAsync`). Change-password refreshes the current session and revokes others via stamp.

**Delete account** (`DeleteMyAccountHandler`): staff accounts return `STAFF_ACCOUNT`. Password re-check. Every `IPersonalDataSource.CanEraseAsync` must succeed (Customers returns `LAST_OWNER` when the user is the only owner of a company that still has other members). Then every source `EraseAsync`, then `AnonymizeAsync` (name and email replaced, password and MFA removed, account locked), sign-out, security event, confirmation email to the old address. Audit action `account.delete`.

**Export:** `GET /api/v1/account/export` builds sections from every `IPersonalDataSource`. Cache-Control `no-store`.

## Customers (tenant rules)

**Confirmed** in `CustomerAccount`:

- Create company: caller must not already be a member (`ALREADY_IN_COMPANY`). Creator becomes Owner. Event `CompanyCreated` is raised and not dispatched.
- Rename, invite, revoke invitation, change role: Owner only (`NOT_COMPANY_OWNER`).
- Invite: email normalized lower case; duplicate member email `ALREADY_MEMBER`; open invite to same email `ALREADY_INVITED`; member cap 50 including open invites `TEAM_FULL`.
- Accept: token hash lookup; expired/used/revoked share `INVALID_INVITATION`; email must match the signed-in user's email (`INVITATION_EMAIL_MISMATCH`); user already in another company `ALREADY_IN_COMPANY`.
- Remove: owner may remove others; anyone may remove themselves. Last owner cannot be removed or demoted (`LAST_OWNER`).
- Forget (account deletion): same last-owner rule if other members remain. If they are the only member, the company remains with zero members. Invitations to that email are anonymized to `erased-{id}@deleted.invalid` and pending ones revoked.

**Staff search** (`SearchCompaniesHandler`): text containing `@` looks up an exact email via `IUserDirectory` and lists that user's company. Any other text is a name contains-search. Empty email match uses `Guid.Empty` so the page is empty rather than a name search.

**Inferred risk:** `Owner()` calls `.First()` on owners. A company left with no members has no owner. A staff search that includes that company throws. See `10-CODE-QUALITY-ISSUES.md`.

## Licenses

**Catalog:** `PublishedPaidPlansAsync` requests `api/v1/plans?status=Published&page=1&pageSize=200`, keeps `Kind==Paid` and `Status==Published`, and for each `(ProductCode, Code)` keeps the highest `Version`. Comment in the client: LicensingPlatform `IsCurrent` is not used for paid plans. Cache 5 minutes. If the platform throws `PlatformUnavailableException` and a previous cache exists, the stale list is returned. If there is no cache, `GetCatalog` returns `LICENSING_UNAVAILABLE` (503).

**Fulfil** (`FulfillLicenseHandler`):

1. Load grant by `OrderReference`. If it exists for a different company or plan → `ORDER_MISMATCH`. If already `Issued` → return the same license (idempotent).
2. Else create `Pending` grant after checking the company exists and the plan is still in the catalog. Save.
3. Ensure `PlatformCustomerLink` (create LicensingPlatform customer from company name and owner email).
4. If no subscription id, `StartSubscriptionAsync`, save.
5. `IssueLicenseAsync`, protect the product key with data protection purpose `MonitorAgent.Licenses.ProductKey.v1`, mark `Issued`, save.
6. `PlatformUnavailableException` returns `LICENSING_UNAVAILABLE` after whatever step was saved.

**Known gap (ADR-007, confirmed by the sequence above):** if LicensingPlatform issues a license and this process dies before `SaveAsync`, a retry calls `IssueLicenseAsync` again. The platform is not given an idempotency key.

**Customer license reads:** membership → link → platform. A license whose platform `CustomerId` is not the company's link is `LICENSE_NOT_FOUND` (`CompanyScope.OwnLicenseAsync`). Reveal key and reset device require Owner or Technical (`CompanyMembership.CanManageDevices`). Billing and Viewer can list. Reveal is `IAuditedCommand` `license.reveal-key` and also a log message.

**Reminders:** `LicenseGrant.DueReminder` uses windows 14 and 3 days, only while status is Issued and expiry is still in the future. `RenewedAsync` skips the email when a later issued grant exists for the same company and plan.

## Commerce

**Who may buy:** company role Owner or Billing (`BuyerScope`). Others get `NOT_ALLOWED` even though the controller only requires the Customer policy.

**Place order:** price, currency, product name, and plan name are copied from `IPlanCatalog.OfferAsync` at that moment. `Order.Place` rejects `total <= 0`. Order number `MA-{yyMMdd}-{6 Crockford-like chars}` (`OrderMapping.NewOrderNumber`).

**Pay:** provider name must match a registered `IPaymentGateway`. `StartPayment` only while `PendingPayment`. Checkout failure marks that payment Failed and returns `PAYMENT_UNAVAILABLE`.

**Webhook** (`HandlePaymentNotificationHandler`): signature is checked inside `ReadNotification` before the store is used. Unknown provider → error. Duplicate `(provider, eventId)` → 200 and no work. Unknown payment reference → notification row stored, 200, warning log. Amount or currency mismatch → payment Failed, order stays unpaid (`Order.ConfirmPayment`). Success → order `Paid`, ledger `Payment`, invoice, save, receipt email, then `ILicenseFulfillment`. Unique indexes on notification key and invoice number map to `SaveConflictException`. The paid path returns 503 so the provider retries. The duplicate-save path on an unknown payment swallows the conflict and returns success.

**Manual payment:** staff finance policy. `RecordManualPayment` adds a succeeded payment with provider `manual` and the same after-payment path. Reference required, max 100.

**Cancel:** only `PendingPayment`. Audited `order.cancel`.

**Invoice numbers:** `InvoiceCounter` per year, `Last` is a concurrency token, format described as `INV-yyyy-nnnnnn` in the domain comment. Gapless only as long as the counter update commits with the invoice; a rolled-back transaction does not consume a number. Confirmed mechanism: concurrency token, not a SQL sequence object.

**Fulfilment retry:** up to 20 orders in `Paid` status per tick. License issue is idempotent per order number.

**Refunds:** not implemented. Reports subtract `LedgerEntryKind.Refund` if such rows exist. No writer was found.

## Releases

`Release.Publish` only from Draft, and only if newer than the current Published release on that channel (`VERSION_NOT_NEWER`). `Withdraw` only from Published. Public manifest endpoints return the highest Published version on the channel, bytes unchanged. Signature is checked on upload with the configured public key (`INVALID_RELEASE_SIGNATURE`, `SIGNING_KEY_NOT_CONFIGURED`). Packages must already satisfy the manifest rules in `Manifest.cs` (https URL, sha256, size). Files are not stored here.

## Support

Ticket numbers and message text are stored as written. Staff display name trimming is an application concern (ADR-011); the domain stores `AuthorUserId` and `FromStaff`. Open-ticket cap is checked before create (`TOO_MANY_OPEN_TICKETS`). Another company's ticket is `TICKET_NOT_FOUND`.

**Demo request** (`DemoRequests.cs`, ADR-018): public POST, CSRF still required, rate policy `public-form` (default 3/minute/IP). Honeypot and a duplicate email inside one day return 202 without a new row. Name, company, and email reject newlines because they are placed in email headers. No email is sent to the visitor. Staff email goes to `Support:DemoRequestsEmail` when set. Default in `appsettings.json` is `sales@monitoragent.local`.

## Content

Public `GET /api/v1/content/pages/{slug}` returns the published version only (controller sets a short cache; confirmed in `ContentControllers.cs` route). Workflow commands are audited. `PAGE_CHANGED` is the concurrency failure (`ContentStore.SaveAsync` catches `DbUpdateConcurrencyException`). Slug pattern is lowercase segments (`Page.SlugPattern`). Bodies are markdown-subset text; HTML is not the storage format.

Legal routes on the site (`terms`, `privacy`, `refunds`) resolve CMS content and fall back to strings compiled into the Angular app when nothing is published (`app.routes.ts` `legalResolver`). Fallback copy was not line-audited in this pass.

## State machines (confirmed)

| Aggregate | States | Transitions |
| --- | --- | --- |
| Order | PendingPayment, Paid, Fulfilled, Cancelled | Pay or manual payment → Paid; fulfil → Fulfilled; cancel only from PendingPayment. No path back. No refund transition. |
| Payment | Pending, Succeeded, Failed | Confirm or fail once. Repeats ignored. |
| LicenseGrant | Pending, Issued | `Issued` is sticky. |
| Ticket | Open, Answered, Closed | Customer message → Open; staff message → Answered; close → Closed. |
| Suggestion | New, Planned, Done, Declined | `Answer` sets any of these. |
| Demo request | New, Contacted, Closed | Staff update. |
| Page version | Draft, InReview, Published, Superseded | One open version (Draft or InReview) at a time. |
| Release | Draft, Published, Withdrawn | Publish from Draft; withdraw from Published. |
| Invitation | Pending, Accepted, Revoked | Accept or revoke. Forget revokes pending. |

## Permissions (server)

| Action | Rule |
| --- | --- |
| Customer company and licenses | Policy `Customer` plus membership. Device key and reset: Owner or Technical. Buy, pay, cancel, invoices: Owner or Billing. |
| Admin audit, security events, release publish, manual license grant | Policy `Staff.Admin` (Admin role + `amr=mfa`) |
| Orders, manual payment, revenue, ledger CSV | Policy `Staff.Finance` (Admin or Finance + MFA) |
| Customer directory and company licenses | Policy `Staff.Customers` (Admin, Support, or Finance + MFA) |
| Tickets admin, demo requests, content routes | Policy `Staff` (any staff role + MFA). Content mutations then require ContentEditor/Admin or ContentReviewer/Admin inside the handler. Support mutations require Support or Admin inside the handler. |
| Public catalog, demo form, update manifests, published pages | Anonymous. Demo and auth writes still need CSRF except payment notifications (`[NoCsrf]`). |

Front end: admin `staff` guard (`web/projects/admin/src/app/app.routes.ts`) allows any staff role and, if `mfaEnabled` is false, only `/account`. It does not hide finance routes from Support. The API still returns 403. Portal `signedIn` only checks that a session loads.
