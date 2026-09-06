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

The dashboard lists the API and the frontend. Open the frontend URL. That is the whole of it — there is no JSON to
write first.

On the first run the app host fills in the settings the application refuses to start without, and writes them to
**this project's user secrets**, a file outside the repository:

| Setting | What it is for |
|---|---|
| `IdentityAccess:DataProtection:ApplicationName`, `:KeyRingPath` | the key ring that seals every mailed token and every stored document. On disk, so a link written before a restart still opens afterwards |
| `IdentityAccess:Email:Enabled`, `:FromAddress`, `:LocalDropPath` | delivery to a folder instead of a provider (see below) |
| `IdentityAccess:People:DocumentProtection:CurrentKeyVersion`, `:FingerprintKeys:1` | the keyed fingerprint a personal DNI is looked up by. It has no default by design: a digest under a key everybody knows is not keyed |

The key ring and the mail folder are created under `%LOCALAPPDATA%/identity-access-local` (or the equivalent on
other systems). **Nothing already configured is overwritten** — not by the first run and not by any later one —
because replacing a key ring path or a fingerprint key would silently orphan everything protected under the old
one. To change any of them, set the value yourself and it is left alone:

```bash
dotnet user-secrets --project src/AppHost set "IdentityAccess:Email:LocalDropPath" "C:/temp/identity-mail"
```

Only an interactive `dotnet run` in Development does this. The acceptance suite passes its own settings and turns
it off with `IdentityAccess:LocalSetup:Enabled=false`, so a test run never writes to your machine's.

## Reading the mail the application sends

Every onboarding link — an organization confirmation, a member invitation, the Platform owner invitation — is
carried by a token that is sealed with a Data Protection key held by the application. Nothing outside that process
can read one out of the database, so there is no useful way to fish a link out of `outbox_secrets`.

Instead, delivery points at a folder, which the first run already configured. Every message is written there as a
text file instead of being sent, with the link in it exactly as its recipient would receive it. Find the folder
with:

```bash
dotnet user-secrets --project src/AppHost list
```

The two `DataProtection` values are required whenever `Email:Enabled` is `true`, including here: without them the
API refuses to start with `Identity Data Protection configuration is incomplete`. A key ring on disk is also what
lets a link survive a restart — an in-memory one makes every envelope written before it unreadable.

A file appears within a second or two of the action that caused it, named after its outbox message:

```text
To: you@example.test
From: platform@example.test
Subject: You have been invited to Platform

You have been invited to become the Platform owner. Open this link to set up your account:
https://localhost:50889/platform/invitations/register#token=…
```

Paste the link into the browser. The origin is the frontend's own, filled in by the app host from the port it
allocated this run; `IdentityAccess:Email:PublicOrigin`, if you set it, wins over that and is what a deployment
configures.

Two things keep this local. The sender refuses to run outside a Development, Test or Testing host, and it refuses
at start-up rather than at the first message. And the drop folder holds live invitation links in plain text, which
is a mailbox with no password on it — keep it under a temporary directory and delete it when you are done.

## Bootstrapping Platform

Platform has no owner until a deployment names one. Nothing is created by default — no administrator, no password.

```bash
dotnet user-secrets --project src/AppHost set "IdentityAccess:Platform:BootstrapOwnerEmail" "you@example.test"
```

Restart. On start-up the application creates the singleton Platform tenant, its two system roles, and one pending
owner invitation for that address. Running again changes nothing, and changing the address afterwards creates no
second owner.

The owner then walks the same path any administrator does, and each step is reached from the mail the step before
it produced:

1. open the invitation link from the drop folder (`/platform/invitations/register#token=…`) and choose a password;
2. open the confirmation that arrives next (`/platform/invitations/confirm#token=…`) and confirm the address;
3. sign in normally at `/login`;
4. complete the second factor at `/platform/mfa#token=…` — the same invitation token as step 1, which is what
   binds the ceremony to the offer rather than to whoever is signed in. Enrol, enter a code from an authenticator,
   and acknowledge the recovery codes;
5. the Platform panel is then at `/platform`.

The membership becomes active only at step 4's acknowledgement. Before it, the account exists and can sign in, and
holds nothing — the panel answers that the area is for an MFA-authenticated Platform administrator.

If the owner invitation cannot be delivered, `/platform/bootstrap/recover` reissues it. It accepts no input at all
— not an address, not an identity — and answers the same way whatever the state is. Reissuing rotates the token,
so the newer file in the drop folder is the one that opens anything.

This whole journey is also what `PlatformOperations.feature` walks in the browser, from the same bootstrap
invitation and without confirming anything in the database.

## Email: simulated versus real

**This is the part to be careful about.**

- With `IdentityAccess:Email:LocalDropPath` set, messages are written to that folder and the app host does not
  start the outbox worker at all: the web application drains its own outbox, so one process seals the tokens and
  opens them. Nothing leaves the machine.
- With `IdentityAccess:Email:Enabled` unset or `false`, nothing is delivered either — a registration, invitation
  or confirmation writes an outbox message and an encrypted envelope and stops there.
- **Real sending requires the separate, deliberate activation in [EMAIL-SETUP.md](EMAIL-SETUP.md)**: a provider
  API key, a shared Data Protection key ring, a wrapping certificate, and `IdentityAccess:Email:Enabled=true` with
  no `LocalDropPath`. None of that is set by running locally, and none of it should be pointed at a real mailbox
  while trying things out.

## If nothing happens

- **The API says `Identity Data Protection configuration is incomplete`.** `Email:Enabled` is `true` without the
  two `DataProtection` values above.
- **The API cannot reach the database after the PostgreSQL container was reused.** The container is persistent and
  its password is a generated parameter, so a run without user secrets can generate a new one the existing volume
  does not know. Initialise user secrets for the app host — the start-up warning says the same — or remove the
  `dbserver-*` container and let it be recreated.
- **No file appears in the drop folder.** Check the dashboard's `webapi` logs first; delivery is refused as a
  whole when the sender cannot validate its configuration.

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
