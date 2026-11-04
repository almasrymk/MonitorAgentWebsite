# Authentication and authorization

## Mechanism

Cookie authentication from ASP.NET Core Identity. Not JWT. Session cookie `__Host-ma.session` (HttpOnly, Secure, SameSite=Strict, path `/`). The `__Host-` prefix requires Secure, path `/`, and no Domain attribute, so the browser only sends it to the exact host over HTTPS. ADR-010 and ADR-019 put the API, portal, and admin on one host (`/api`, `/portal`, `/admin`) so the cookie is shared. Development CORS allows `http://localhost:4200` with credentials; the cookie is still `Secure`, so a local HTTP origin will not store it unless the browser is treating the site as a secure context. **Inferred** from cookie options. Not executed in a browser in this analysis.

Sign-in redirect events are replaced with ProblemDetails (`UNAUTHENTICATED`, `FORBIDDEN`, `MFA_REQUIRED`). The API never redirects to a login page.

## CSRF

`AddAntiforgery`: header `X-XSRF-TOKEN`, cookie `__Host-ma.af` (HttpOnly, Secure, Strict). `GET /api/v1/auth/csrf` copies the request token into a non-HttpOnly cookie `XSRF-TOKEN` so the SPA can echo it. `ValidateCsrfFilter` runs on unsafe methods. `[NoCsrf]` is applied only to `PaymentNotificationsController`.

Front-end comment in `Csrf.cs` says Angular HttpClient copies `XSRF-TOKEN` into the header on unsafe same-origin requests. Shared client behavior was not re-read line by line; the cookie names are confirmed.

## Session invalidation

`SecurityStampValidatorOptions.ValidationInterval` is `Identity:SessionValidationSeconds` (default 60). Password change, reset, and revoke-all update the stamp. The MFA claim `amr=mfa` is copied onto the refreshed principal so a stamp check does not drop two-factor proof.

## Roles

Created by `IdentitySeeder` from `IdentityRoles.All`:

- `Customer` — every self-registration.
- `Admin`, `Support`, `Finance`, `ContentEditor`, `ContentReviewer` — staff. Not self-service. Only the bootstrap path in this repository creates an Admin. No endpoint was found that assigns staff roles. **Confirmed absence** in the controllers that were listed. Adding Support or Finance is an operational gap (database or a future admin API).

## Policies (`IdentityModule`)

| Policy | Requirement |
| --- | --- |
| `Customer` | authenticated + role Customer |
| `Staff` | authenticated + any staff role + claim `amr=mfa` |
| `Staff.Admin` | role Admin + `amr=mfa` |
| `Staff.Finance` | Admin or Finance + `amr=mfa` |
| `Staff.Customers` | Admin, Support, or Finance + `amr=mfa` |

ContentEditor and ContentReviewer are staff, so they pass `Staff` and can open admin support routes. Handlers then reject them where `IsStaff` means Support or Admin, or where content scope requires editor/reviewer.

## Second factor

Password sign-in of an MFA user returns `{ requiresMfa: true }` and does not create the full session (status `RequiresMfa` in `SignInHandler`). The follow-up uses the two-factor cookie scheme. Recovery codes drop spaces only; authenticator codes also drop dashes.

Staff cannot call `DisableMfa` (`MFA_REQUIRED_FOR_STAFF`). Customers can, with the current password.

## Account secrecy

Register, resend confirmation, and forgot-password return the same success whether the email exists. Sign-in uses one error code for unknown email and wrong password. Invitation tokens use one error for wrong, expired, used, and revoked.

## Data protection

Keys persisted in `identity` schema. In Production, `Identity:KeyProtection:CertificatePath` is mandatory or the host throws. Optional previous certificate for rotation. Development may run without the certificate (keys then protected only by the local mechanism). Product keys use a purpose string on the same provider.

## Front end versus server

| Check | Where it is real |
| --- | --- |
| Must be signed in | Portal guard and `[Authorize]` |
| Must be staff | Admin guard and staff policies |
| Must have MFA enabled on the account | Admin guard redirects to `/account` when `me.mfaEnabled` is false |
| Must have MFA **in this session** (`amr`) | Server policies only. Enabling MFA calls `MarkMfaAsync`, which reissues the session. A staff user who only has the flag from a previous session still had to pass the MFA step to get a session, because password login stops at `requiresMfa`. |
| Owner / Billing / Technical | Server handlers. Portal pages may hide buttons; that was not fully audited per template. Do not treat hidden buttons as authorization. |
| Finance versus Support pages | Admin navigation shows the same links to every staff role (`app.routes.ts` `nav` array). Server policies reject the wrong role. |

## Rate limits that matter for auth

Auth endpoints: 10/minute/IP default. Account export, delete, password change, and MFA enable/disable use the same policy. Global limiter is 300/minute/IP and is applied in addition (ASP.NET Core composes endpoint and global limiters). Payment webhooks disable rate limiting on purpose.
