# Identity Access — running and trying it locally

Everything here is local. By default nothing sends a real message; the one exception is
[Gmail SMTP](#sending-real-mail-through-gmail-smtp), which you switch on deliberately. See
[EMAIL-SETUP.md](EMAIL-SETUP.md) for a deployment's provider, and read the section on delivery below before
assuming a link arrived.

Two neighbours: [OPERATIONS.md](OPERATIONS.md) is what an operator configures and watches once this run is behind
them, and [RECOVERY-AND-RETENTION.md](RECOVERY-AND-RETENTION.md) is what happens to a lost second factor and to
stored personal data.

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

## The journeys you can walk today

Every link below is reachable from the navigation once you are signed in. Each mailed step is a file in the drop
folder, as described above.

**Register as a person.** `/register` asks which of the two you are registering. `/personal/register` answers the
same neutral `202` whatever address you type, and the confirmation lands in the drop folder as
`/confirm-email#token=…`. After confirming, sign in at `/login`.

**Register an organization.** `/organizations/register` is the other branch, with the same neutral answer and the
same mailed confirmation. **Read the limitation below before you rely on it.**

**Your profile.** `/identity/profile` shows the name, display name and address, and the masked document if one
was recorded. Only the two names are editable. The document is not: correcting one takes two parties, and all
this screen does is ask for a review. If you signed in without a personal context — because you arrived through
an organization — this is also where you claim one, with a name and a document rather than by registering again.
After creating it, **Organizations** offers the personal context without reloading the application. Accepting a
member invitation refreshes that same selector; if refreshing fails, the saved membership is not submitted again.

**Your account.** `/identity/account` deactivates your account after a deliberate confirmation and the existing
identity proof. It ends every session and sends a deactivation notice. From **Reactivate your account** on the
login page, request a link; the neutral acknowledgement does not reveal account status. Follow the delivered
`/account/reactivate#token=…` link and enter your current password. Reactivation creates no session: sign in
afterwards. If you have forgotten your password or never set one, use **Reset or set a password** first, then
request reactivation. This flow cannot lift an administrative suspension, and a last administrator or Platform
owner must give someone else that responsibility before deactivating.

**Your devices.** `/identity/sessions` lists where you are signed in, marks the one you are using, and ends
another one or all the others. Both ask for your password first, which buys a single-use server-side proof. Five
sessions is the cap; a sixth sign-in ends the oldest.

**Your password.** `/identity/password` changes it after the same proof, and signs your other devices out.
`/credentials/forgot` is the way in when you cannot sign in at all — the reset link arrives in the drop folder as
`/credentials/reset#token=…`, works once, and issues no session, so you sign in afterwards with what you chose.

**Your sign-in providers.** `/identity/external` links and unlinks Google. It is inert until the section above is
configured.

**Your organization's roles.** `/roles` builds and edits custom roles and shows what each one confers. You may
only put a permission into a role that you hold yourself, so the catalogue shows which codes you could actually
grant. Editing a role asks for your password first. Retiring one is permanent, and it stops conferring anything
on the very next request.

**Your organization's members.** `/members` lists everybody, what they hold, and which one owns the organization.
You can change somebody's roles — with your password — and suspend, reactivate or remove them without one. The
owner's own membership cannot be suspended or removed; transferring the organization first is what makes it
possible, and the transfer asks for your password and a deliberate confirmation.

**Inviting somebody.** `/members/invite` offers your organization's roles by name, lists every standing offer,
and lets you reissue or withdraw one. A reissue rotates the token and the previous one stops working, so the link
that matters is always the newest file in the drop folder.

### What a real registered owner holds

Registering an organization makes you its `Owner`, and that role is provisioned with the `Organization` codes the
C5 acceptance decided it holds (amendment D1) — role and member administration, invitations, tenant management
and ownership transfer. Existing organizations were backfilled by the same decision. A permission added by a
later feature does **not** join that set automatically: the catalogue records the answer per code, and a test
fails until somebody gives it.

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
4. open the **invitation link from step 1 again** and take "Set up your second factor". There is no URL to type:
   the ceremony is bound to the offer rather than to whoever is signed in, and the page you already have is the
   one holding the token that says so. Enrol, enter a code from an authenticator, and acknowledge the recovery
   codes;
5. the Platform panel is then at `/platform`.

If the authenticator is later lost, `/platform/mfa/recover` replaces the factor for a caller who proves a
password a moment beforehand and spends one unused recovery code. The replacement has been proved by nobody, so
the next Platform change asks for a step-up with the new authenticator.

The membership becomes active only at step 4's acknowledgement. Before it, the account exists and can sign in, and
holds nothing — the panel answers that the area is for an MFA-authenticated Platform administrator.

If the owner invitation cannot be delivered, `/platform/bootstrap/recover` reissues it. It accepts no input at all
— not an address, not an identity — and answers the same way whatever the state is. Reissuing rotates the token,
so the newer file in the drop folder is the one that opens anything.

This whole journey is also what `PlatformOperations.feature` walks in the browser, from the same bootstrap
invitation and without confirming anything in the database.

## Signing in with Google

The button is on the sign-in screen and the account screen is at `/identity/external`, but **both are inert until
a deployment is given an OAuth client**. Nothing is stubbed and nothing is simulated: with no client configured,
the middleware is not registered at all, `POST /api/identity/external/Google/login/start` answers
`400 invalid_external_login`, and the screens say so rather than pretending.

Turning it on is one action only you can take, in your own Google account. Nobody else can do it for you, and
none of it belongs in this repository.

1. Open the [Google Cloud console](https://console.cloud.google.com/), create (or pick) a project, and configure
   its **OAuth consent screen**. External user type, in Testing mode, with your own address added as a test user
   is enough for a local run; the only scopes needed are `openid` and `email`.
2. Under **APIs & Services → Credentials**, create an **OAuth client ID** of type **Web application**.
3. Give it the **authorized redirect URI** — exactly this, with your own frontend port from the Aspire dashboard:

   ```text
   https://localhost:<port>/api/identity/external/google/callback
   ```

   It has to match character for character, including the scheme and the trailing path. There is no wildcard, and
   a different port is a different URI, so add one line per port you actually use. The authorized JavaScript
   origins list can stay empty: the browser never talks to Google from a script here.
4. Google shows you a **client ID** and a **client secret**. Put them in this project's user secrets, which live
   outside the repository — run these two commands in a terminal and paste each value at its prompt rather than
   into a chat window or a file:

   ```bash
   dotnet user-secrets --project src/AppHost set "IdentityAccess:ExternalLogins:Google:ClientId"
   ```

   ```bash
   dotnet user-secrets --project src/AppHost set "IdentityAccess:ExternalLogins:Google:ClientSecret"
   ```

5. Restart `dotnet run --project src/AppHost`.

Two settings and nothing else. `IdentityAccess:ExternalLogins:Google:Authority` exists so the automated tests can
stand a controlled provider up in place of Google; leave it unset and the real `https://accounts.google.com` is
used. The client secret is never written to `appsettings*.json`, never logged, and never reaches the browser: the
authorization code is exchanged for the identity token by the server, on its own connection.

What works once it is on: signing in with a Google account creates or finds the identity that account is linked
to, and linking from `/identity/external` attaches a Google account to the one you are already signed in as. A
matching email address never links anything on its own — that is the point of the rule — so an address that
already has a local account answers "sign in and link it from your account" instead.

**Verified against the protocol is not the same as verified against Google.** The automated suite drives the real
ASP.NET Core OpenID Connect handler against a controlled provider that signs its own tokens, so state, nonce,
PKCE, issuer, audience, signature and expiry are all genuinely exercised. Whether *Google's* consent screen,
redirect registration and token endpoint behave as expected for your project is a separate thing, and it can only
be established by doing the five steps above and signing in once.

## Email: simulated versus real

**This is the part to be careful about.**

- With `IdentityAccess:Email:LocalDropPath` set, messages are written to that folder and the app host does not
  start the outbox worker at all: the web application drains its own outbox, so one process seals the tokens and
  opens them. Nothing leaves the machine.
- With `IdentityAccess:Email:Enabled` unset or `false`, nothing is delivered either — a registration, invitation
  or confirmation writes an outbox message and an encrypted envelope and stops there.
- With `IdentityAccess:Email:Provider=GmailSmtp`, messages really leave the machine through your Gmail account —
  see [the section below](#sending-real-mail-through-gmail-smtp). It is never selected unless you name it.
- **Deployment sending requires the separate, deliberate activation in [EMAIL-SETUP.md](EMAIL-SETUP.md)**: a
  provider API key, a shared Data Protection key ring, a wrapping certificate, and `IdentityAccess:Email:Enabled=true`
  with no `LocalDropPath`. None of that is set by running locally.

## Sending real mail through Gmail SMTP

The drop folder is the default because it cannot reach anybody. When you need to see a message arrive in a real
inbox — how a client renders it, whether a link survives the trip — switch to Gmail SMTP. It is a development
option, not a deployment one; [EMAIL-SETUP.md](EMAIL-SETUP.md#gmail-smtp--local-and-development-only) explains why.

**Before you start**, in your own Google account: turn on 2-Step Verification, then create an app password at
<https://myaccount.google.com/apppasswords>. Google shows it once, as 16 letters. That is the SMTP password; your
normal Google password will be refused.

**Switch.** Run these in a local terminal, not in a chat window, from the repository root. They write to this
project's user secrets, outside the repository:

```bash
dotnet user-secrets --project src/AppHost remove "IdentityAccess:Email:LocalDropPath"
```

```bash
dotnet user-secrets --project src/AppHost set "IdentityAccess:Email:Provider" "GmailSmtp"
```

```bash
dotnet user-secrets --project src/AppHost set "IdentityAccess:Email:FromAddress" "you@gmail.com"
```

```bash
dotnet user-secrets --project src/AppHost set "IdentityAccess:Email:Smtp:Host" "smtp.gmail.com"
```

```bash
dotnet user-secrets --project src/AppHost set "IdentityAccess:Email:Smtp:Port" "587"
```

```bash
dotnet user-secrets --project src/AppHost set "IdentityAccess:Email:Smtp:UseStartTls" "true"
```

```bash
dotnet user-secrets --project src/AppHost set "IdentityAccess:Email:Smtp:Username" "you@gmail.com"
```

```bash
dotnet user-secrets --project src/AppHost set "IdentityAccess:Email:Smtp:Password" "your-16-letter-app-password"
```

`IdentityAccess:Email:Enabled` is already `true` from the first run. `PublicOrigin` needs nothing: the app host
fills in the frontend's origin for both processes. Set it only if links must point somewhere else:

```bash
dotnet user-secrets --project src/AppHost set "IdentityAccess:Email:PublicOrigin" "https://localhost:5173"
```

The same keys work as environment variables of the app host process, with `__` in place of `:` — for example
`IdentityAccess__Email__Provider=GmailSmtp` and `IdentityAccess__Email__Smtp__Password=…`. The app host forwards
them to the API and the worker.

Restart `dotnet run --project src/AppHost`. What changes:

- **The outbox worker starts.** Without a drop folder the web application no longer delivers in-process; the
  `outboxworker` resource in the dashboard is the process that sends, through the same outbox, lease and retry
  rules as a deployment. Its logs are where a refusal shows up.
- **`FromAddress` must be the Gmail account or a verified alias.** Otherwise Gmail rewrites it to the account.
- **The links point at `https://localhost:…`.** They open only on the machine running the app, so send to an
  address you read on that machine.
- **A retry can send twice.** SMTP has no idempotency key; a copy after an uncertain attempt carries the same
  `Message-Id`.

If start-up says `Identity email delivery is not configured`, a setting above is missing, `Enabled` is `false`, or
`LocalDropPath` is still present — `GmailSmtp` and a drop folder together are refused rather than guessed between.

**Switch back** to the folder by removing the provider. The next run fills in the drop path again:

```bash
dotnet user-secrets --project src/AppHost remove "IdentityAccess:Email:Provider"
```

The SMTP keys can stay; they are ignored unless `GmailSmtp` is selected. Remove the password when you no longer
need it, and revoke the app password in your Google account.

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
and needs Docker running. It is repeatable: it walks a cold start the Platform bootstrap never repeats, so it
creates a database of its own inside the `dbserver-*` container each run and drops it at the end. Your own
`CleanArchitectureDb` is not touched, and nothing needs recreating between runs.
