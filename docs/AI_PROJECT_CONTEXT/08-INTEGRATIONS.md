# Integrations

## LicensingPlatform (required for catalog, licences, devices)

**Adapter:** `LicensingPlatformClient` and `LicensingPlatformAuthHandler` in `src/Modules/Licenses/.../Platform/`.

**Configuration:** `Licenses:Platform:BaseUrl`, `ServiceEmail`, `ServicePassword`, `TimeoutSeconds` (default 15). Outside Development the host throws if `BaseUrl` is not an absolute URI. Empty base URL in Development is replaced with `http://licensing-platform.invalid/` so calls fail fast as `LICENSING_UNAVAILABLE`.

**Auth:** service user login, token cached in `PlatformTokens` (handler not fully line-read; class is registered as transient `LicensingPlatformAuthHandler` on the named client). ADR-007 lists required platform permissions: `catalog.read`, `customers.manage`, `subscriptions.manage`, `licenses.read`, `licenses.manage`.

**Calls confirmed in the client:**

| Local method | HTTP |
| --- | --- |
| `PublishedPaidPlansAsync` | `GET api/v1/plans?status=Published&page=1&pageSize=200` |
| `CreateCustomerAsync` | `POST api/v1/customers` |
| `StartSubscriptionAsync` | `POST api/v1/subscriptions` |
| `IssueLicenseAsync` | `POST api/v1/licenses` |
| `LicensesOfAsync` | `GET api/v1/licenses?customerId=&page=1&pageSize=200` |
| `LicenseAsync` | `GET api/v1/licenses/{id}` |
| `ResetDeviceAsync` | `POST api/v1/licenses/{licenseId}/activations/{deviceId}/reset` |

Enums are deserialized by name. A body the client cannot read is treated as failure (comment at `ReadAsync`). Page size 200 is a hard cap: additional plans or licences are not requested. **Confirmed limitation.**

No shared database. Contract tests in this repo are the OpenAPI snapshot of **this** API, not a recorded LicensingPlatform schema. `tests/MonitorAgent.Tests/Api/FakeLicensingPlatform.cs` stubs the platform for API tests.

## Payment provider

**Port:** `IPaymentGateway` with `CreateCheckoutAsync` and `ReadNotification`.

**Implemented:** `SandboxGateway` only.

- Checkout URL is a portal route `checkout/sandbox?reference&amount&currency`.
- Notification JSON, signature header `X-Sandbox-Signature`, HMAC-SHA256 hex of the body with `Commerce:Sandbox:WebhookSecret`.
- Registered only in Development, and only when the secret is non-empty. Production startup throws if the secret is set.

Paymob and Stripe are named in ADR-008 and the launch checklist. There is no class for them.

## Email

`IEmailSender` → `SmtpEmailSender` (`System.Net.Mail.SmtpClient`). Configuration `Email:Provider=Smtp`, `Email:Smtp:Host`, `Port`, `EnableSsl`, `UserName`, `Password`, `From`. Missing provider throws at startup. Development compose points SMTP at Mailpit (`localhost:1025`, UI port 8025).

Messages are plain text, Arabic then English, for registration, password, invitations, receipts, license ready, expiry reminders, ticket replies, demo requests to sales. Product keys are not emailed (`CommerceEmails` comment).

`EmailSenderExtensions.TrySendAsync` logs and swallows send failures where the business operation must still succeed (registration, invitations). Some paths (`AfterPayment.NotifyAsync`) catch similarly.

## Update clients (Monitor Agent program)

Anonymous GET of manifest bytes and signature. The private signing key stays in the external `tools/UpdateSigner` (ADR-009). This repo verifies with `Releases:PublicKey`. Package binaries are https URLs inside the manifest, not blobs in SQL Server.

## Reverse proxy

`ForwardedHeaders` for `X-Forwarded-For` and `X-Forwarded-Proto`, trusted networks only from `ReverseProxy:KnownNetworks`. Production compose sets `172.30.0.0/24`. If the list is empty, forwarded headers are not used (`Program.cs`).

## Not integrated

No Stripe/Paymob SDK, no blob storage SDK, no SMS, no Slack, no queue (Rabbit/Azure Service Bus), no OpenTelemetry exporter in the API project (health and `ILogger` only). Observability expectations are written in `docs/operations/monitoring.md` and were not re-verified against a running collector.
