# Configure identity email delivery

Identity confirmation, invitations, invited-user confirmation, and generic sign-in notices use Resend's REST API. Local implementation and isolated tests are complete; external activation requires your Resend account, verified domain, API key, and deployment key configuration. No account, subscription, DNS record, credential, or real email is created by this change.

## Activate delivery

1. Create or use your own Resend account. Add a domain or subdomain you control and complete the DNS verification shown in its dashboard. The sender must belong to that verified domain; an ordinary Gmail address cannot serve as the sender. Keep click and open tracking disabled for these sensitive transactional messages.
2. Create a **Sending access** API key restricted to that domain. Receipt reconciliation repeats the send operation, so full access and email-read permissions are unnecessary.
3. Provision a durable key directory accessible to Web and OutboxWorker, and a deployment X.509 certificate with its private key. The certificate wraps Data Protection keys at rest in Production, Staging, and custom deployment environments. Only explicit `Development`, `Test`, and `Testing` environments may omit certificate wrapping. Supply its PFX file and optional password through your deployment's secure configuration.
4. Supply the settings below to **both Web and OutboxWorker**, preserving the existing application discriminator and key material where applicable. Supply `IdentityAccess__Email__Enabled=true` to AppHost as well when starting the worker through Aspire locally.
5. Start the hosts. Production startup rejects missing email settings. Every environment outside explicit `Development`, `Test`, and `Testing` rejects missing certificate/keyring settings before database migrations or outbox claims. Enabled local delivery also requires an explicit shared application name and key directory. Only activate the sender after the prerequisites are ready.

## Settings

Environment variable names below map to the corresponding colon-separated .NET configuration keys. Values are placeholders, not working credentials. Inject secrets from your local or deployment secret store into the processes; do not commit them, put them in chat, or paste them into shell history. Local environment configuration is supported without adding a provider SDK.

| Setting | Required value |
|---|---|
| `IdentityAccess__Email__Enabled` | `true` to run delivery; required in Production |
| `IdentityAccess__Email__ApiKey` | Your domain-scoped Sending access key, supplied externally |
| `IdentityAccess__Email__FromAddress` | A bare address on your verified domain, such as `identity@YOUR_VERIFIED_DOMAIN` |
| `IdentityAccess__Email__PublicOrigin` | Your allowlisted HTTPS origin, such as `https://YOUR_APP_HOST`; no credentials, path, query, or fragment |
| `IdentityAccess__DataProtection__ApplicationName` | The exact shared application discriminator; see compatibility below |
| `IdentityAccess__DataProtection__KeyRingPath` | Shared durable directory for Data Protection keys, accessible to both process identities |
| `IdentityAccess__DataProtection__CertificatePath` | Deployment PFX file containing the wrapping certificate and private key; required outside `Development`/`Test`/`Testing` |
| `IdentityAccess__DataProtection__CertificatePassword` | PFX password, when required, supplied externally |

The normal `ConnectionStrings__CleanArchitectureDb` setting is also required. Restrict the PFX and key-directory permissions to the deployment identities. Both hosts must retain access to the same keys across restarts and deployments. There is no plaintext key fallback outside the explicit local/test environments and no paid cloud key service requirement.

`PublicOrigin` is deployment configuration, never a request `Host` or `Origin`. Links are absolute. Confirmation and invitation tokens are URL-escaped fragments, which browsers do not send in the initial HTTP request. Generic notices carry only an identity reference in the outbox and contain no invitation or token.

## Preserve existing encrypted data

Data Protection compatibility requires **both the same application discriminator and the same key repository**. A new installation may choose one stable explicit name. An existing installation must configure the exact previous Web `DataProtectionOptions.ApplicationDiscriminator` value in both processes, including any trailing separator. Web's implicit discriminator can derive from its content root; the generic worker must not guess it.

Keep the existing key files when moving to shared durable storage. Enabling certificate wrapping protects newly generated keys; it does not automatically rewrap or migrate old keys. Existing OS/user-bound key protection must remain usable by both deployment identities. Complete any required deployment key migration before enabling delivery. This change does not rotate keys, recover lost keys, or authorize discarding old material.

Verify the pairing before trusting delivery. Neither process can detect a mismatch on its own: each validates
only that its own settings are present, and each protects and unprotects its own envelopes correctly. A wrong
discriminator, or two directories that are not the same durable volume, leaves both processes reporting
healthy while every message is claimed, fails to decrypt, exhausts its attempts and is abandoned. The only
trace is the column:

```sql
SELECT count(*) FROM outbox_messages WHERE "Status" = 'Abandoned' AND "FailureCode" = 'envelope_unreadable';
```

Any row there after enabling delivery means the two processes are not reading the same key ring. Send one
invitation to an address you control and confirm it arrives before announcing the feature.

Omitting `ApplicationName` in disabled Development preserves the previous Web default. The existing envelope purpose, `identity-access.registration.outbox-secret.v1`, is unchanged. Configure the shared discriminator before enabling the worker so existing pending envelopes remain readable.

## Development and isolated tests

Delivery is disabled by default in Development. Aspire's local run starts OutboxWorker only when `IdentityAccess:Email:Enabled` is explicitly enabled. A directly started disabled worker has no polling service. Web may still create pending outbox intents; disabled delivery is not an assertion that mail was sent.

`IdentityAccess__Email__LocalDropPath` is the third mode, and it belongs only to a developer's machine: with it
set, messages are written to that folder as text files instead of being sent, no provider is contacted, and no API
key is needed. It is refused outside an explicit `Development`, `Test` or `Testing` environment, and refused at
start-up rather than at the first message — so a deployment cannot acquire it by accident. Do not set it here.
[RUNNING-LOCALLY.md](RUNNING-LOCALLY.md#reading-the-mail-the-application-sends) is where it is documented.

Automated tests use explicit in-memory sinks or fake HTTP transports and an isolated PostgreSQL database. They do not contact Resend or require a provider account. The generic-host test uses the same worker registration as the executable and delivers a seeded notice through its isolated sink.

## Retry and delivery limits

Each message uses its UUID as the exact `Idempotency-Key` for `POST https://api.resend.com/emails`. Resend retains that key for **24 hours**. Repeating an identical request within that window returns the original provider receipt without accepting another logical email; there is no lookup-by-idempotency-key endpoint.

The worker persists a conservative first-attempt timestamp before network access and a SHA-256 request fingerprint before sending. The fingerprint covers the resolved recipient, subject, body, sender configuration, and a hash derived from the API credential. Only the combined fingerprint is persisted; the API key is never stored there. Changed content or credentials are terminalized as `request_changed`, without another HTTP send or a replacement idempotency key. This conservatively rejects any API-key rotation during an uncertain retry; it does not detect or discover provider account identity. Resolve pending/uncertain deliveries before rotating the API key. For preexisting attempted rows, the migration uses creation time as the conservative lower bound without changing their attempt counts or prior evidence.

Retries stop before the 24-hour retention boundary, including a 20-second request margin. An ambiguous delivery beyond that boundary is abandoned as `receipt_window_expired`; a message may have reached the provider even though its local state could not be settled. Do not manually reset its timestamp or create a replacement message merely to force replay.

Claims count toward an eight-attempt maximum, including crashed attempts. Transient failures release the lease and wait `min(30 seconds × 2^(attempt − 1), 30 minutes)`. HTTP 429, 5xx, and `concurrent_idempotent_requests` are retryable; changed-idempotency-payload conflicts and other permanent refusals stop retrying. A stale worker cannot settle or release a replacement worker's claim. Success and permanent cleanup update the message and any pending secret atomically, retaining non-secret evidence and clearing ciphertext.

Provider acknowledgement means that Resend accepted the request, not that the recipient's inbox received it. Bounce/webhook processing and delivery tracking are outside this slice. Provider bodies, API keys, raw tokens, and email content are not logged or persisted as error details. Worker errors use fixed codes. HTTP requests have a 20-second deadline, bounded response buffering, and redirects disabled.

## References

- [Resend send email API](https://resend.com/docs/api-reference/emails/send-email)
- [Resend idempotency keys](https://resend.com/docs/dashboard/emails/idempotency-keys)
- [Resend API-key permissions](https://resend.com/docs/dashboard/api-keys/introduction)
- [Resend domain verification](https://resend.com/docs/dashboard/domains/introduction)
- [ASP.NET Core Data Protection configuration](https://learn.microsoft.com/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0)
