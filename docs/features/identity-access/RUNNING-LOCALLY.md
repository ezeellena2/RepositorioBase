# Identity Access — running and trying it locally

Everything here is local. Nothing in this file activates an email provider or sends a real message; see
[EMAIL-SETUP.md](EMAIL-SETUP.md) for that, and read the section on delivery below before assuming a link arrived.

## Prerequisites

1. **.NET SDK 10** and **Node 20+**.
2. **Docker**, for the PostgreSQL container Aspire starts.
3. **The ASP.NET development certificate**, trusted:

   ```bash
   dotnet dev-certs https --trust
   ```

   The frontend is served over HTTPS on purpose. The design does not work over plain HTTP: a `__Host-` cookie
   cannot be stored by an insecure origin, the session and antiforgery cookies are issued `Secure`, and the API
   compares the browser's `Origin` against the scheme the request arrived on. The dev server presents the same
   certificate the API does, so both halves agree.

   The Vite config exports that certificate to `%APPDATA%/ASP.NET/https` (or `~/.aspnet/https`) the first time it
   starts, and reuses it afterwards. If the browser warns about the certificate, the trust step above has not run.

## Starting it

```bash
dotnet run --project src/AppHost
```

The dashboard lists the API and the frontend. Open the frontend URL.

## Bootstrapping Platform

Platform has no owner until a deployment names one. Nothing is created by default — no administrator, no password.

```bash
dotnet user-secrets --project src/AppHost set "IdentityAccess:Platform:BootstrapOwnerEmail" "you@example.test"
```

Restart. On start-up the application creates the singleton Platform tenant, its two system roles, and one pending
owner invitation for that address. Running again changes nothing, and changing the address afterwards creates no
second owner.

The owner then walks the same path any administrator does:

1. open the invitation link (`/platform/invitations/register#token=…`) and choose a password;
2. confirm the address (`/platform/invitations/confirm`);
3. sign in normally at `/login`;
4. complete the second factor at `/platform/mfa#token=…` — enrol, enter a code from an authenticator, and
   acknowledge the recovery codes;
5. the Platform panel is then at `/platform`.

The membership becomes active only at step 5's acknowledgement. Before it, the account exists and can sign in, and
holds nothing.

If the owner invitation cannot be delivered, `/platform/bootstrap/recover` reissues it. It accepts no input at all
— not an address, not an identity — and answers the same way whatever the state is.

## Email: simulated versus real

**This is the part to be careful about.** Local runs never send email.

- The outbox worker only runs when `IdentityAccess:Email:Enabled` is `true`; the app host does not start it
  otherwise. So a registration, invitation or confirmation writes an outbox message and an encrypted envelope, and
  nothing leaves the machine.
- To read a token locally, take it from the database rather than from an inbox. The messages are in
  `outbox_messages` and the tokens are encrypted in `outbox_secrets`, readable only by the process that wrote
  them — which is why the tests read them through the application's own `IOutboxSecretReader` rather than by
  decrypting a column.
- **Real sending requires the separate, deliberate activation in [EMAIL-SETUP.md](EMAIL-SETUP.md)**: a provider
  API key, a shared Data Protection key ring, a wrapping certificate, and `IdentityAccess:Email:Enabled=true`.
  None of that is set by running locally, and none of it should be pointed at a real mailbox while trying things
  out.

## Running the checks

```bash
dotnet build CleanArchitecture.slnx -v minimal
dotnet test CleanArchitecture.slnx --no-build
npm test --prefix src/Web/ClientApp
npm run lint --prefix src/Web/ClientApp
npm run build --prefix src/Web/ClientApp
```

The acceptance suite starts the whole application, including the frontend, so it is the slowest by a wide margin
and needs Docker running.
