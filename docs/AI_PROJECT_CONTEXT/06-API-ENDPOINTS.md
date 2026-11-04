# API endpoints

Base path is the API origin. In production, Caddy sends `/api/*`, `/updates/*`, and `/health/*` to this process (ADR-019, `deploy/Caddyfile` not fully quoted here). JSON enums are strings (`JsonStringEnumConverter` in `Program.cs`). Errors are `application/problem+json` with extension `code`.

Unless noted, unsafe methods require header `X-XSRF-TOKEN` matching the antiforgery cookie. Cookie session is required where a policy is listed.

## Health and CSRF

| Method | Path | Auth | Notes |
| --- | --- | --- | --- |
| GET | `/health/live` | none | No checks. Rate limit disabled. |
| GET | `/health/ready` | none | Identity database `CanConnect`. Rate limit disabled. |
| GET | `/api/v1/auth/csrf` | anonymous | Sets `XSRF-TOKEN` cookie. 204. |

OpenAPI document is mapped only when `IsDevelopment`.

## Auth (`AuthController`) — anonymous, rate policy `auth` (default 10/min/IP), except logout

| Method | Path | Success |
| --- | --- | --- |
| POST | `/api/v1/auth/register` | 202 |
| POST | `/api/v1/auth/confirm-email` | 204 |
| POST | `/api/v1/auth/resend-confirmation` | 202 |
| POST | `/api/v1/auth/login` | 200 `SignInResponse` |
| POST | `/api/v1/auth/login/mfa` | 204 |
| POST | `/api/v1/auth/login/recovery-code` | 204 |
| POST | `/api/v1/auth/logout` | 204, rate limit disabled |
| POST | `/api/v1/auth/forgot-password` | 202 |
| POST | `/api/v1/auth/reset-password` | 204 |

## Account — `[Authorize]` any signed-in user

| Method | Path | Extra limit |
| --- | --- | --- |
| GET | `/api/v1/account/me` | |
| POST | `/api/v1/account/change-password` | auth rate limit |
| POST | `/api/v1/account/sessions/revoke` | |
| GET | `/api/v1/account/export` | auth rate limit, `no-store` |
| POST | `/api/v1/account/delete` | auth rate limit |
| GET | `/api/v1/account/mfa/setup` | |
| POST | `/api/v1/account/mfa/enable` | auth rate limit |
| POST | `/api/v1/account/mfa/disable` | auth rate limit |
| POST | `/api/v1/account/mfa/recovery-codes` | |

| GET | `/api/v1/admin/security-events` | Policy `Staff.Admin` |

## Company — policy `Customer`

| Method | Path |
| --- | --- |
| GET | `/api/v1/company` |
| POST | `/api/v1/company` |
| PUT | `/api/v1/company` |
| POST | `/api/v1/company/invitations` |
| DELETE | `/api/v1/company/invitations/{invitationId}` |
| POST | `/api/v1/company/invitations/accept` |
| PUT | `/api/v1/company/members/{userId}/role` |
| DELETE | `/api/v1/company/members/{userId}` |

| GET | `/api/v1/admin/customers` | Policy `Staff.Customers` |
| GET | `/api/v1/admin/customers/{companyId}` | Policy `Staff.Customers` |

## Catalog and licenses

| Method | Path | Auth |
| --- | --- | --- |
| GET | `/api/v1/catalog/plans` | anonymous |
| GET | `/api/v1/licenses` | Customer |
| GET | `/api/v1/licenses/{licenseId}` | Customer |
| POST | `/api/v1/licenses/{licenseId}/product-key` | Customer, `no-store` |
| POST | `/api/v1/licenses/{licenseId}/devices/{deviceId}/reset` | Customer |
| POST | `/api/v1/admin/licenses/grants` | `Staff.Admin` |
| GET | `/api/v1/admin/customers/{companyId}/licenses` | `Staff.Customers` |

## Commerce

| Method | Path | Auth |
| --- | --- | --- |
| POST | `/api/v1/orders` | Customer (handler: Owner or Billing) |
| GET | `/api/v1/orders` | same |
| GET | `/api/v1/orders/{orderId}` | same |
| POST | `/api/v1/orders/{orderId}/pay` | same |
| POST | `/api/v1/orders/{orderId}/cancel` | same |
| GET | `/api/v1/invoices` | same |
| GET | `/api/v1/payments/providers` | Customer |
| POST | `/api/v1/payments/notifications/{provider}` | anonymous, **no CSRF**, rate limit **disabled**, body cap 64 KiB |
| GET | `/api/v1/admin/orders` | `Staff.Finance` |
| POST | `/api/v1/admin/orders/{orderId}/manual-payment` | `Staff.Finance` |
| GET | `/api/v1/admin/reports/revenue` | `Staff.Finance` |
| GET | `/api/v1/admin/reports/ledger.csv` | `Staff.Finance` |

## Releases

| Method | Path | Auth |
| --- | --- | --- |
| GET | `/updates/manifest.json` | anonymous, stable channel |
| GET | `/updates/manifest.json.sig` | anonymous |
| GET | `/updates/{channel}/manifest.json` | channel regex `^[a-z][a-z0-9-]*$` |
| GET | `/updates/{channel}/manifest.json.sig` | same |
| GET | `/api/v1/releases` | anonymous published list |
| GET | `/api/v1/admin/releases` | `Staff.Admin` |
| POST | `/api/v1/admin/releases` | `Staff.Admin` |
| POST | `/api/v1/admin/releases/{releaseId}/publish` | `Staff.Admin` |
| POST | `/api/v1/admin/releases/{releaseId}/withdraw` | `Staff.Admin` |

## Support

| Method | Path | Auth |
| --- | --- | --- |
| POST | `/api/v1/demo-requests` | anonymous, CSRF on, policy `public-form` |
| GET/POST | `/api/v1/support/tickets` | Customer |
| GET | `/api/v1/support/tickets/{ticketId}` | Customer |
| POST | `/api/v1/support/tickets/{ticketId}/messages` | Customer |
| POST | `/api/v1/support/tickets/{ticketId}/close` | Customer |
| GET/POST | `/api/v1/support/suggestions` | Customer |
| GET | `/api/v1/admin/support/tickets` | Staff |
| GET | `/api/v1/admin/support/tickets/{ticketId}` | Staff |
| POST | `/api/v1/admin/support/tickets/{ticketId}/messages` | Staff (handler: Support or Admin) |
| POST | `/api/v1/admin/support/tickets/{ticketId}/close` | Staff |
| GET | `/api/v1/admin/support/suggestions` | Staff |
| PUT | `/api/v1/admin/support/suggestions/{suggestionId}` | Staff |
| GET | `/api/v1/admin/support/demo-requests` | Staff |
| PUT | `/api/v1/admin/support/demo-requests/{demoRequestId}` | Staff |

## Content

| Method | Path | Auth |
| --- | --- | --- |
| GET | `/api/v1/content/pages/{slug}` | anonymous |
| GET/POST | `/api/v1/admin/content/pages` | Staff |
| GET | `/api/v1/admin/content/pages/{pageId}` | Staff |
| PUT | `/api/v1/admin/content/pages/{pageId}/draft` | Staff |
| POST | `/api/v1/admin/content/pages/{pageId}/versions/{number}/restore` | Staff |
| POST | `.../submit`, `.../withdraw`, `.../approve`, `.../reject` | Staff, role checked in handler |

## Audit

| GET | `/api/v1/admin/audit` | `Staff.Admin` |

## Contract snapshot

`tests/MonitorAgent.Tests/Contract/openapi.v1.json` is the checked-in OpenAPI document. `OpenApiContractTests` fails if an endpoint, schema, or property is removed or a property becomes required. Updating it requires `UPDATE_OPENAPI_SNAPSHOT=1`. The snapshot file itself (about 5900 lines) was not read line by line in this analysis. The route table above was taken from controller attributes.

Global rate limit default is 300 requests per minute per IP (`RateLimiting:PermitPerMinute`).
