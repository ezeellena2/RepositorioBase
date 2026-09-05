# Identity Access code review — 2026-09-05

Do not close this as a usable B2B/B2C product yet. At `7e9eb55`, the Organization and Platform foundation has
substantial implementation and tests, but the normal email journeys and several security/session boundaries
have the defects below. Personal/B2C (IA-010), recovery/change/session management (IA-011), Google (IA-013), and
operations hardening (IA-015) remain deferred; their absence is not a defect against this increment.

This is a direct code review, not a workflow receipt or approval. It changes documentation only. Findings are
based on source and test inspection; the reproduction procedures below were not executed in this review.
The scoped runtime results below were supplied by a separate verification worker; this static review does not
rerun the full suite. Source line links refer to `7e9eb55`. Passing existing tests does not establish the missing
journeys described here.

## Resolution status (added after remediation)

R1-R7 and L1 below are corrected. The findings themselves are left exactly as written — this note only records
what happened to them. Each correction has a test that fails on `7e9eb55`, the code this review read, and passes
on the code now in the tree; [TRACEABILITY.md](TRACEABILITY.md) names them per requirement and records the full
verification run that this review deliberately did not claim. Two things are recorded there as still open, and
neither is a silent pass: R6's fix leaves the authenticated `409` observable to a caller who first probes
anonymously, which is a duplicate-CUIT policy decision rather than a defect fix, and the later-administrator
browser journey remains a coverage gap. The closing paragraph below still holds: this is not an approval, and
IA-010, IA-011, IA-013 and IA-015 remain out of scope and unimplemented.

## Scoped runtime evidence

Fresh verification reported **447 passing tests**: Domain 160/160, Application.Unit 191/191, and React 96/96
across 9 files. Frontend lint checked 39 files with zero problems. Initial .NET attempts executed zero tests
because of sandbox permissions; the same commands passed with the required execution permissions.

Infrastructure integration, Application functional, browser acceptance, Release build and EF model parity were
not rerun by this review. Local logs under gitignored `artifacts/direct-review-2026-09-05` record this run; they
are not portable acceptance evidence or a universal delivery gate.

Equivalent commands from the repository root (dependencies were already restored/installed). Paths below use
the current checkout instead of the original machine's absolute path; permissions must allow the test runner's
working directory:

```powershell
$reviewResults = Join-Path (Get-Location) 'artifacts/direct-review-2026-09-05'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
dotnet test tests/Domain.UnitTests/Domain.UnitTests.csproj --no-restore --logger 'trx;LogFileName=domain-workdir.trx' --results-directory $reviewResults -- "NUnit.WorkDirectory=$reviewResults"
dotnet test tests/Application.UnitTests/Application.UnitTests.csproj --no-restore --logger 'trx;LogFileName=application-workdir.trx' --results-directory $reviewResults -- "NUnit.WorkDirectory=$reviewResults"
Push-Location src/Web/ClientApp
npm --cache "$reviewResults/npm-cache" test -- --reporter=default --reporter=json --outputFile="$reviewResults/frontend-tests.json"
npm --cache "$reviewResults/npm-cache" run lint -- --format json --output-file "$reviewResults/frontend-lint.json"
Pop-Location
```

## Findings that prevent closure

### R1 · P1 · Organization confirmation mail opens no confirmation screen

[EmailConfirmationDeliveryHandler.cs:22](../../../src/Infrastructure/Outbox/EmailConfirmationDeliveryHandler.cs#L22)
renders `/confirm-email#token=...`, also used by invited-user confirmation. That route is absent from
[AppRoutes.jsx:27](../../../src/Web/ClientApp/src/AppRoutes.jsx#L27); the server fallback serves the SPA shell,
not the separate POST confirmation API. The client confirmation method has no production caller.

Reproduce by registering an organization and following its delivered confirmation link: no screen consumes the
token, so the identity and organization remain pending. The B2B acceptance helper directly updates confirmation
and activation state in SQL ([IdentityAccessStepDefinitions.cs:340](../../../tests/Web.AcceptanceTests/StepDefinitions/IdentityAccessStepDefinitions.cs#L340)),
which bypasses this missing step. Add the screen and verify the journey through the delivered link.

### R2 · P1 · Platform confirmation does not lead into invitation-bound MFA

[PlatformInvitationPages.jsx:87](../../../src/Web/ClientApp/src/features/platform/invitations/PlatformInvitationPages.jsx#L87)
ends confirmation with a sign-in instruction. No visible continuation carries the original invitation token into
`/platform/mfa`; registration keeps it only in component memory and erases its URL fragment. Delivered links
point to registration, confirmation or login, not that MFA continuation.

Reproduce by following those emails, confirming and signing in using visible controls: the owner still has no
membership and cannot reach the ceremony with its required token. The acceptance test supplies the missing
navigation itself ([PlatformOperationsStepDefinitions.cs:226](../../../tests/Web.AcceptanceTests/StepDefinitions/PlatformOperationsStepDefinitions.cs#L226)),
constructing the MFA URL from a retained fragment. Implement and test the actual continuation without placing
the token in server-visible query strings or persistent client storage.

### R3 · P1 · A password-only Platform session can read the operational directories

[CreateSessionHandler.cs:67](../../../src/Application/IdentityAccess/Sessions/CreateSession/CreateSessionHandler.cs#L67)
selects the sole active tenant after password login. The permission evaluator and context projection require
active membership/roles but no MFA proof for that session. [PlatformPanel.jsx:65](../../../src/Web/ClientApp/src/features/platform/PlatformPanel.jsx#L65)
checks only authentication, tenant type and permission; directory handlers add no MFA check.

Reproduce with an activated owner: log out, sign in with the password, then open Platform or read
`/api/platform/identities`, `/api/platform/admins` or `/api/platform/audit` before TOTP. The new session receives operational data,
contrary to IA-REQ-045. Mutations do check recent MFA bound to the session; preserve that control while requiring
MFA for Platform access and providing an accessible step-up flow.

### R4 · P1 · Platform TOTP verification has no attempt limit

[PlatformMfaEndpoints.cs:35](../../../src/Web/Endpoints/Platform/PlatformMfaEndpoints.cs#L35) has no rate policy;
[LoginRateLimiting.cs:29](../../../src/Web/Infrastructure/Identity/LoginRateLimiting.cs#L29) exempts non-login routes.
An invalid code in [PlatformMfaHandlers.cs:205](../../../src/Application/IdentityAccess/Platform/Mfa/PlatformMfaHandlers.cs#L205)
records no failed-attempt state. Six-digit TOTP accepts the previous, current and next time windows.

The precondition is an authenticated session for an identity with an active enrollment, for example after a
compromised password; anonymous callers cannot use this route. Repeated incorrect submissions with a valid
antiforgery pair encounter no attempt ceiling, while a correct guess grants mutation freshness. Add bounded
verification attempts and recovery tests. This review makes no measured-throughput or exploitation claim.

### R5 · P2 · Expired Platform confirmation cannot be reissued

Confirmation expires after 24 hours. [RegisterPlatformInviteeHandler.cs:61](../../../src/Application/IdentityAccess/Platform/Invitations/RegisterPlatformInviteeHandler.cs#L61)
treats every existing identity, including an unconfirmed one, as sign-in-only. The expired token is refused,
and password login refuses an unconfirmed identity. Bootstrap recovery rotates the invitation without replacing
the bound identity, so registration still takes that branch.

Reproduce by registering, leaving confirmation to expire, then retrying registration or a recovered invitation:
the only new message is a sign-in notice that cannot complete onboarding. Provide a neutral confirmation-reissue
path for that pending identity, preserving recipient binding and the prohibition on premature membership.

### R6 · P2 · An occupied CUIT makes registration reveal email existence

[RegisterOrganizationHandler.cs:60](../../../src/Application/IdentityAccess/Organizations/RegisterOrganization/RegisterOrganizationHandler.cs#L60)
returns neutral success for an existing anonymous identity before line 61 checks CUIT uniqueness. For an unknown
email with the same occupied CUIT it returns `registration_conflict` instead.

Reproduce anonymously with a valid password and a known occupied CUIT, varying only the email: existing email
gets `202`, unknown email gets a conflict. The existing-identity, conflict-replay and password-policy tests in
[RegistrationTests.cs:121](../../../tests/Application.FunctionalTests/IdentityAccess/Organizations/RegistrationTests.cs#L121)
cover the branches separately, not this combination. Align observable responses with the existing neutrality
requirement and test the cross-product. This report does not choose a new business policy for duplicate CUITs.

### R7 · P2 · A rejected expired cookie cannot be cleared through logout

[SessionCookieEvents.cs:153](../../../src/Infrastructure/Identity/SessionCookieEvents.cs#L153) rejects the principal
without deleting its cookie. [SessionEndpoints.cs:23](../../../src/Web/Endpoints/Identity/SessionEndpoints.cs#L23)
requires authentication for logout, so an already expired session never reaches the handler's cookie deletion.
[ValidatedOptionalSession.cs:16](../../../src/Infrastructure/IdentityAccess/ValidatedOptionalSession.cs#L16)
then treats that cookie as invalid, and registration refuses it.

Reproduce by signing in, allowing server-side idle expiry while the browser still holds the cookie, requesting
logout (`401`), then attempting anonymous registration with fresh antiforgery: it remains `invalid_session`.
Clear the unusable cookie at rejection or through an appropriate logout path, while retaining rejection of
invalid sessions. Existing expiry tests prove refusal, not recovery to anonymous browsing.

## Local delivery follow-up

**L1 · P2, local Development/Test only:** [LocalFolderEmailSender.cs:64](../../../src/Infrastructure/Email/LocalFolderEmailSender.cs#L64)
uses file existence as delivery proof although it writes directly to the final filename. An interrupted write
can leave an empty/truncated file; retry returns delivered and the dispatcher deletes the token ciphertext.
An empty `<message-id>.txt` is a minimal interrupted-write fixture. Publish a completed file atomically and
validate existing delivery evidence. This is not a production-provider blocker.

## Foundations retained and evidence limits

- Authorization uses tenant memberships and explicit permissions, with no global administrator bypass.
- Confirmation and credential registration do not grant Platform membership. MFA admission checks the confirmed
  identity, normalized recipient, bound identity and invitation token together; mutations bind freshness to a session.
- Projections are allowlisted; TOTP secrets are encrypted and recovery codes hashed. Last-owner revocation is refused.
- Outbox delivery has atomic claims, owner/generation settlement, expiry rechecks, bounded retries and provider
  idempotency/fingerprint tests. The older token-hash and settled-migration defects are corrected in code with
  relevant tests present; [TASKS.md](TASKS.md#ia-008-merge-gates) records the evidence and remaining limits.

There is no capacity benchmark in this review. Persisted session validation/touch and tenant authorization run
per request and need measurement at the expected workload. Login rate-limit counters are process-local;
multiple instances require an explicit strategy for consistent limits. Neither observation dictates a specific
cache or infrastructure product, nor proves a measured scaling failure.

The exact denial-audit field test and later-administrator browser journey remain coverage gaps, not additional
proven bugs. `AcceptInvitation` does not consume its delivery envelope, but normal delivery already removes
ciphertext and accepted-token replay cannot create another membership; no additional authorization defect was
established from that observation. [TRACEABILITY.md](TRACEABILITY.md) retains useful tests and marks the affected
journeys Partial. Closure requires fixes and focused regression evidence for R1–R7, not a reinterpretation of
the existing green suite as proof of those missing behaviors.
