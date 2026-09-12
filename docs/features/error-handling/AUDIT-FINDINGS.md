# Error handling — audit findings

Read-only audit run on 2026-09-10 against `main` at `497e087` plus the uncommitted working tree. Six layers —
backend model and HTTP mapping, backend validation coverage, the error-code catalogue, frontend presentation,
cross-cutting runtime, and error-path tests. Each layer's findings were checked by an adversarial verifier that
opened every cited line and tried to refute it; the 2 refuted findings are excluded.

Confirmed: 61 — 0 critical, 13 high, 25 medium, 23 low.
The work these findings drive is in [REMEDIATION-PLAN.md](REMEDIATION-PLAN.md); the rules are in
[error-handling-standards](../../../.agents/skills/error-handling-standards/SKILL.md).

Line numbers are as of the audit. Re-read a cited file before acting on it.

## High (13)

<a id="backend-model-and-mapping-1"></a>

### backend-model-and-mapping-1 — Unexpected exceptions leave no usable log: .NET 10 suppresses the middleware's diagnostics and the app logs only the exception type name

- **Layer:** backend-model-and-mapping
- **User impact:** A user can report the traceId from a 500, but an operator cannot find the cause. A fault outside the MediatR handlers leaves no application log line at all: endpoint code, session-cookie validation (SessionCookieEvents), antiforgery, the denial-audit write, the login-budget middleware, SignInAsync. The only exception is a framework component that logs on its own, such as EF Core command failures. A fault inside a handler records only its type name, with no stack and no failing component. Whole-system error handling is not complete when a 500 cannot be diagnosed.
- **Evidence:**
  - src/Web/Infrastructure/ProblemDetailsExceptionHandler.cs:30-37 returns true for every exception, including the unexpected branch that calls WriteUnexpectedAsync
  - src/Web/Program.cs:47 calls app.UseExceptionHandler(options => { }) and sets no SuppressDiagnosticsCallback; Directory.Build.props:6 targets net10.0
  - C:/Program Files/dotnet/packs/Microsoft.AspNetCore.App.Ref/10.0.11/ref/net10.0/Microsoft.AspNetCore.Diagnostics.xml:149-159 documents that, with the callback null, ExceptionHandlerMiddleware suppresses diagnostics for any exception an IExceptionHandler service handled
  - src/Application/Common/Behaviours/UnhandledExceptionBehaviour.cs:21-22 logs only ex.GetType().Name and the trace id, without passing the exception (so no stack), and only for exceptions inside the MediatR pipeline
  - src/Web/appsettings.json:8 sets "Microsoft": "Warning", which filters out the hosting 'request finished 500' Information line; src/ServiceDefaults/Extensions.cs:62-68 adds ASP.NET Core tracing without exception recording
  - src/OutboxWorker/Worker.cs:36-41 catches everything and logs a fixed string with neither type nor stack; src/Web/HostedServices/LocalOutboxDeliveryService.cs:53, src/Web/HostedServices/PlatformBootstrapHostedService.cs:38 and src/Infrastructure/IdentityAccess/Lifecycle/LifecycleMaintenanceService.cs:54 log the type name only
  - tests/Application.FunctionalTests/IdentityAccess/Api/ProblemDetailsContractTests.cs:210-255 checks the safe 500 body, but no test checks that the fault was recorded anywhere
- **Recommendation:** Keep the suppression, which avoids full exception messages that IA-REQ-029 forbids. Instead, write one sanitized Error record from the handler's unexpected branch, before WriteUnexpectedAsync. It should hold the exception type and inner types, the stack frames, the endpoint display name, the traceId and provider error codes such as PostgresException.SqlState, but never Message. Give UnhandledExceptionBehaviour and the background loops the same record, and turn on exception recording in OpenTelemetry only behind a redacting processor. Add a functional test: ForceUnexpectedFailure should produce exactly one Error record with the response's traceId and exception type, and none of the secret sentinels.
- **Verifier:** Holds. I found nothing that refutes it. - ProblemDetailsExceptionHandler.cs:30-37 returns true on every branch. - Program.cs:47 calls UseExceptionHandler(options => { }). A grep of src for ExceptionHandlerOptions or SuppressDiagnosticsCallback finds nothing. - The 10.0.11 ref-pack doc (Microsoft.AspNetCore.Diagnostics.xml:149-173) confirms the default. When an IExceptionHandler handled the exception, the middleware suppresses three things: the UnhandledException log, the HandledException event, and the error.type metric tag. - UnhandledExceptionBehaviour.cs:21-22 logs only ex.GetType().Name and does not pass the exception, so there is no stack trace. - appsettings.json:8 sets "Microsoft": "Warning", and src/Web has no appsettings.Development.json. That filters out the hosting 'request finished' line. - ServiceDefaults Extensions.cs:62-68 has no exception recording and no EF or Npgsql tracing source. - Background loops: Worker.cs:41 logs a fixed string. PlatformBootstrapHostedService.cs:38, LocalOutboxDeliveryService.cs:53 and LifecycleMaintenanceService.cs:54 log the type name only. - SensitiveRequestLoggingTests.cs:50-62 proves only that the exception message stays out of the logs. Nothing proves that a fault gets recorded. - One caveat, which does not change the conclusion: when an OTLP exporter is configured, the ASP.NET Core trace still shows a 500 span with its route for that traceId. An operator can therefore find which route failed, but not why.

<a id="backend-model-and-mapping-2"></a>

### backend-model-and-mapping-2 — Malformed registration input cannot produce field-level errors: every input problem collapses into invalid_registration, and the CUIT check digit is never validated

- **Layer:** backend-model-and-mapping
- **User impact:** A person who mistypes the email, picks a password the policy refuses, or enters a CUIT of the wrong length gets one generic message with no hint of which field is wrong. A CUIT with a wrong check digit is not refused at all, so the typo is stored and only surfaces later, if ever. None of these cases is enumeration-sensitive. This is the field-level feedback the product owner expects from the form.
- **Evidence:**
  - src/Application/IdentityAccess/Organizations/RegisterOrganization/RegisterOrganizationHandler.cs:48 and 173-186: a missing field, an over-long value, an email without '@', a malformed CUIT or a blank legal name all return the same IdentityAccessErrors.InvalidRegistration()
  - RegisterOrganizationHandler.cs:53-58: a password the policy refuses also returns InvalidRegistration(); the comment there says the check depends on the password alone, so a field-level answer would not be an enumeration oracle
  - src/Infrastructure/IdentityAccess/IdentityAccountService.cs:163-173: ValidatePasswordAsync returns a bare bool and discards the rule descriptions that IdentityCredentialService.cs:41-42 already turns into field messages for password reset and change
  - src/Application/IdentityAccess/Common/IdentityAccessErrors.cs:17: invalid_registration has a generic detail and no ValidationErrors
  - There are only 8 AbstractValidator classes in src/Application, all under IdentityAccess/Platform (e.g. Platform/Invitations/RegisterPlatformInvitee.cs:20-26); RegisterOrganizationCommand (RegisterOrganization.cs:7-11) has none
  - src/Domain/IdentityAccess/Organizations/NormalizedCuit.cs:9-38 checks for exactly 11 digits and nothing more; there is no mod-11 check digit
  - tests/Application.FunctionalTests/IdentityAccess/Api/ProblemDetailsContractTests.cs:123,129 registers CUIT 30-12345678-9 and expects 202; the check digit for 3012345678 is 1 (weights 5,4,3,2,7,6,5,4,3,2 give 153; 153 mod 11 = 10; 11 - 10 = 1)
  - docs/features/identity-access/SPEC.md:244 prescribes 400 invalid_registration for malformed input and refused passwords on /personal/register; SPEC.md:243 says nothing about malformed input for organization registration; SPEC.md:252 already uses field-indexed validation_failed for a refused password on reset
  - src/Web/ClientApp/src/features/identity/ProblemMessage.jsx:17: the user sees only 'Check the details and try again.'
- **Recommendation:** Add shape-only validators for RegisterOrganizationCommand and its personal and invitation equivalents. They should check required fields, lengths, email syntax, CUIT format and mod-11 check digit, and the password policy (with a ValidatePasswordAsync that returns the rule descriptions). They should answer validation_failed with errors keyed by wire field names. Keep invalid_registration for decisions that depend on state, such as the session-email mismatch. Enforce the check digit inside NormalizedCuit so every path gets it. Amend the /personal/register row of SPEC section 6 to allow field-level validation_failed for input-only failures, and declare validation_failed on those routes.
- **Verifier:** Holds, but the user impact is overstated for missing fields and email syntax. Verified: - RegisterOrganizationHandler.cs:48 and :173-186 collapse every normalization failure into InvalidRegistration(). - :53-58 do the same for a refused password. The comment there confirms the check depends only on the password. - IdentityAccountService.cs:163-173 returns a validity flag and discards the rule descriptions. IdentityCredentialService.cs:41-42 does turn them into field messages for reset and change. - All 8 AbstractValidators are under Platform; none covers registration. - NormalizedCuit.cs:9-38 checks only for 11 digits. - The fixture CUIT 30-12345678-9 fails mod-11: the weighted sum is 153, and 11 - (153 mod 11) = 1, not 9. - SPEC.md:243 says nothing about malformed input on org registration. SPEC.md:244 prescribes invalid_registration for personal register, so the fix needs a SPEC amendment. Correction: RegisterOrganizationPage.jsx:46-87 marks all four inputs required and makes email type="email", with no noValidate. The browser therefore blocks empty fields and malformed email syntax before any request. The real gaps that remain: - Password policy refusal. The policy is 12+ chars with an uppercase letter, a digit, a symbol and 4 unique chars (Infrastructure/DependencyInjection.cs:232-237). The page shows no hint of it, and a refusal only says 'Check the details and try again.' (ProblemMessage.jsx:17). - A CUIT of the wrong length or with letters gets the same generic message. - A wrong CUIT check digit is accepted silently. None of these is enumeration-sensitive.

<a id="backend-validation-coverage-1"></a>

### backend-validation-coverage-1 — Public organization and personal registration answer every malformed field with one field-less `invalid_registration`

- **Layer:** backend-validation-coverage
- **User impact:** A person registering who types a 10-digit CUIT, leaves the legal name blank, enters a 6-digit DNI or picks a password the policy refuses sees only 'Check the details and try again.' Nothing says which of the 4-5 fields is wrong, and the password rules are never stated, so the user has to guess and resubmit.
- **Evidence:**
  - C:/Users/ezequ/source/repos/RepositorioBase/src/Application/IdentityAccess/Organizations/RegisterOrganization/RegisterOrganizationHandler.cs:48 and :173-186 — TryNormalize returns a bool. A null or over-length field, a blank legal name, an email without '@' or a CUIT rejected by NormalizedCuit all become Result.Failure(InvalidRegistration()).
  - C:/Users/ezequ/source/repos/RepositorioBase/src/Application/IdentityAccess/Organizations/RegisterOrganization/RegisterOrganizationHandler.cs:56-59 — a password the policy refuses returns the same InvalidRegistration.
  - C:/Users/ezequ/source/repos/RepositorioBase/src/Application/IdentityAccess/People/RegisterPersonal/RegisterPersonalHandler.cs:48, :58-61, :129-143 — the same pattern for email, password, fullName, displayName and documentNumber (DNI).
  - C:/Users/ezequ/source/repos/RepositorioBase/src/Application/IdentityAccess/Common/IdentityAccessErrors.cs:17 — InvalidRegistration carries no validationErrors.
  - C:/Users/ezequ/source/repos/RepositorioBase/src/Application/IdentityAccess/Organizations/IIdentityAccountService.cs:14 and C:/Users/ezequ/source/repos/RepositorioBase/src/Infrastructure/IdentityAccess/IdentityAccountService.cs:163-174 — ValidatePasswordAsync returns only a bool. The IdentityResult describing which rule failed is discarded, so no layer can explain the refusal.
  - C:/Users/ezequ/source/repos/RepositorioBase/src/Web/Endpoints/Identity.cs:31-34 and C:/Users/ezequ/source/repos/RepositorioBase/src/Web/Endpoints/Identity/PersonalEndpoints.cs:28-37 — malformed JSON collapses into `invalid_registration` too.
  - C:/Users/ezequ/source/repos/RepositorioBase/src/Web/ClientApp/src/features/identity/ProblemMessage.jsx:17 — the user sees 'Check the details and try again.' with no field.
  - C:/Users/ezequ/source/repos/RepositorioBase/docs/features/identity-access/SPEC.md:244 — the SPEC itself prescribes this single code for 'malformed input, for a password the policy refuses'. The code complies with the SPEC; the SPEC is below the standard the product owner is asking for.
  - C:/Users/ezequ/source/repos/RepositorioBase/src/Application/IdentityAccess/Organizations/RegisterOrganization/RegisterOrganizationHandler.cs:181 vs :56 — a non-blank password is also required from signed-in callers, even though it is never checked or used for them.
- **Recommendation:** Keep the current ordering: all of these checks already run before any email lookup (lines 48-59) and read no stored data, so field-level answers disclose nothing about accounts. Replace the bool TryNormalize with a field-error collector, or add FluentValidation validators for RegisterOrganizationCommand and RegisterPersonalCommand. Return 400 `validation_failed` with camelCase keys email/password/legalName/cuit/fullName/displayName/documentNumber and messages that describe the rule, never the value. Make ValidatePasswordAsync return the failing rule descriptions, as IdentityCredentialService.Describe already does. Amend SPEC.md:243-244 so syntactic failures use `validation_failed` with errors, keeping `invalid_registration` only for the signed-in email-mismatch case.
- **Verifier:** I confirmed every cited line. - RegisterOrganizationHandler.cs:173-186: TryNormalize returns a bool. Line 48 maps it to InvalidRegistration(), and lines 56-59 do the same for a password the policy refuses. - RegisterPersonalHandler.cs:48, 58-61 and 129-143 follow the same pattern. - IIdentityAccountService.cs:14 and :34, with IdentityAccountService.cs:163-174: ValidatePasswordAsync returns only IdentityAccountValidationResult(bool), and the IdentityResult is discarded. - IdentityAccessErrors.cs:17: InvalidRegistration carries no errors. - Malformed JSON: Identity.cs:34 and PersonalEndpoints.cs:37. This really reaches the handler, because DependencyInjection.cs:33-34 sets ThrowOnBadRequest=true. - The UI shows 'Check the details and try again.' (ProblemMessage.jsx:17). End to end, the browser's native required and type=email checks block blank fields and '@'-less emails. No form sets noValidate. So the failures that realistically reach the server are these: - The password policy: 12 characters, uppercase, digit, symbol, 4 unique (Infrastructure/DependencyInjection.cs:232-237). It is never stated on the page; RegisterOrganizationPage.jsx:77-87 and PersonalPages.jsx:115-125 have no helperText. - A CUIT of the wrong length or with other characters (RegisterOrganizationPage.jsx:55-63 has no pattern). - A 6-digit DNI. - Whitespace-only names. With a policy this strict, the most common refusal gets a field-less, rule-less answer. High is right. One nuance: the auditor cites SPEC:244, but that row is personal registration. The organization row, SPEC:243, says 'neutral bodyless 202 for every anonymous request, whatever the address and whatever the CUIT'. It lists invalid_registration only for the signed-in email mismatch. So for anonymous malformed input, the organization contract is under-documented, not prescribed.

<a id="backend-validation-coverage-2"></a>

### backend-validation-coverage-2 — On invitation sign-up, a password the policy refuses is reported as an unusable invitation

- **Layer:** backend-validation-coverage
- **User impact:** An invited member or Platform administrator who picks a too-short password is told the invitation is unusable, which reads as 'your link is dead'. They are likely to ask the inviter for a new invitation instead of choosing a stronger password, and the policy is never stated.
- **Evidence:**
  - C:/Users/ezequ/source/repos/RepositorioBase/src/Application/IdentityAccess/Invitations/RegisterInvitedUser/RegisterInvitedUserHandler.cs:49-52 — a null, >256-character or policy-refused password returns IdentityAccessErrors.InvalidInvitation().
  - C:/Users/ezequ/source/repos/RepositorioBase/src/Application/IdentityAccess/Platform/Invitations/RegisterPlatformInviteeHandler.cs:41-44 — the same for Platform invitees.
  - C:/Users/ezequ/source/repos/RepositorioBase/src/Application/IdentityAccess/Common/IdentityAccessErrors.cs:28 — `invalid_invitation` carries no field errors.
  - C:/Users/ezequ/source/repos/RepositorioBase/src/Web/ClientApp/src/features/identity/ProblemMessage.jsx:19 — shown as 'That invitation is not usable.' on C:/Users/ezequ/source/repos/RepositorioBase/src/Web/ClientApp/src/features/identity/invitations/InvitationPages.jsx:62, a page whose only input is the password.
  - C:/Users/ezequ/source/repos/RepositorioBase/src/Application/IdentityAccess/Invitations/RegisterInvitedUser/RegisterInvitedUserHandler.cs:16-19 — the handler's own comment says the password gate reads no stored data and answers identically in all four token/account combinations, so a field-level password error would disclose nothing either.
  - C:/Users/ezequ/source/repos/RepositorioBase/src/Web/Endpoints/Identity/InvitationEndpoints.cs:60-66 — the endpoint contract declares only `antiforgery_validation_failed`, `invalid_invitation` and 500.
- **Recommendation:** From the password gate in both handlers, return 400 `validation_failed` with errors { password: [rule descriptions] }, keeping the gate where it is, before any token lookup. Reserve `invalid_invitation` for token problems. Declare `validation_failed` on POST /api/invitations/register. The Platform route already declares it (PlatformInvitationEndpoints.cs:18-25).
- **Verifier:** Verified. - A null, over-256 or policy-refused password returns InvalidInvitation() in both handlers: RegisterInvitedUserHandler.cs:49-52 and RegisterPlatformInviteeHandler.cs:41-44. - Both handlers' doc comments (RegisterInvitedUserHandler.cs:16-19; RegisterPlatformInviteeHandler.cs:16-19) confirm the gate reads no state. A field-level answer is therefore enumeration-safe. - The organization route declares only antiforgery_validation_failed, invalid_invitation and 500 (InvitationEndpoints.cs:60-66). The Platform route also declares validation_failed (PlatformInvitationEndpoints.cs:20-24). - InvitationPages.jsx:62-74 renders ProblemMessage above a form whose only input is the password. ProblemMessage.jsx:19 says 'That invitation is not usable.' PlatformInvitationPages.jsx:95 and :109-120 do the same for Platform. - The policy (Infrastructure/DependencyInjection.cs:232-237) is strict and never shown, so this path is hit often. The damage compounds: useFragmentToken.js:15-21 erases the token from the URL on mount. A user who reloads the page after this misleading error loses the token entirely (see the missed finding).

<a id="backend-validation-coverage-3"></a>

### backend-validation-coverage-3 — CUIT is checked for length only; the modulo-11 check digit is never verified

- **Layer:** backend-validation-coverage
- **User impact:** A one-digit typo in the CUIT, the most common data-entry error, is accepted. The neutral 202 hides it, and after email confirmation an organization is created with a tax ID that cannot exist. The wrong value then sits in a column with a uniqueness constraint.
- **Evidence:**
  - C:/Users/ezequ/source/repos/RepositorioBase/src/Domain/IdentityAccess/Organizations/NormalizedCuit.cs:9-38 — accepts digits, hyphens and whitespace and requires exactly 11 digits; there is no other rule.
  - A search for check-digit logic ('check digit', 'verificador', 'mod 11', the weights 5,4,3,2,7) across src/, backend and ClientApp, returned no match.
  - C:/Users/ezequ/source/repos/RepositorioBase/src/Application/IdentityAccess/Organizations/RegisterOrganization/RegisterOrganizationHandler.cs:135 and :97 — the normalized CUIT is stored in the PendingRegistrationIntent and then in the OrganizationProfile.
  - C:/Users/ezequ/source/repos/RepositorioBase/docs/features/identity-access/SPEC.md:186 (IA-REQ-033) — CUIT is a unique database claim. The SPEC names no check-digit rule.
  - C:/Users/ezequ/source/repos/RepositorioBase/docs/features/identity-access/SPEC.md:243 — anonymous registration answers a neutral 202 whatever the CUIT, so a typo that passes the length check is never surfaced.
- **Recommendation:** Add the AFIP check-digit rule (weights 5,4,3,2,7,6,5,4,3,2; mod 11; a result of 11 means digit 0, a result of 10 means invalid) and the valid-prefix rule to NormalizedCuit.From. Throw an ArgumentException whose message names the rule only. Surface it as `validation_failed` { cuit: [...] } as in finding 1; this is input-only and cannot reveal accounts. Mirror the rule client-side for instant feedback, and record it in SPEC section 4.
- **Verifier:** Verified. - NormalizedCuit.cs:9-38 accepts only digits, hyphens and whitespace and requires 11 digits. There is no other rule. - A search of src (excluding node_modules) for verificador, checkdigit, 'check digit', '% 11', mod11 and '5, 4, 3, 2, 7' returned no files. The client's CUIT field also has no pattern or check (RegisterOrganizationPage.jsx:55-63). - Anonymous registration answers 202 whatever the CUIT (SPEC:243). The value is stored in the intent (RegisterOrganizationHandler.cs:135) and later in OrganizationProfile. OrganizationProfileConfiguration.cs:16 caps the column at 11, and IA-REQ-033 makes it unique. A check-digit failure is input-only and not enumeration-sensitive. Yet today it produces no feedback at all, just a success message, and invalid data is persisted. The SPEC is silent on the rule, so this is below what the product owner is asking for rather than a SPEC violation. I keep high because the invalid value is silent and permanent.

<a id="error-code-catalogue-1"></a>

### error-code-catalogue-1 — Seven declared and emitted codes are missing from ProblemMessage, so users get "That request could not be completed."

- **Layer:** error-code-catalogue
- **User impact:** Most users who click an expired or already-used password-reset link see "That request could not be completed." and are not told to ask for a new link. A person whose personal signup confirmation hits the documentary-identity conflict gets the same generic line. Their account was in fact created, but nothing tells them to sign in. Revoking a session that already ended in another tab gives no hint to refresh. Editing the profile in two tabs gives no "refresh and try again".
- **Evidence:**
  - ProblemMessage.jsx:9-57 has 43 keys and falls back at :65. The OpenAPI x-problem-codes union in src/Web/wwwroot/openapi/v1.json has 50. The 7 missing are listed below.
  - invalid_credential_token (400) comes from IdentityAccessErrors.cs:138-139, returned at PasswordRecoveryHandlers.cs:109,112,124,146 and as the binding code at PasswordEndpoints.cs:33. SPEC.md:252 names it. ResetPasswordPage shows it at PasswordPages.jsx:101,125.
  - personal_registration_conflict (409) comes from IdentityAccessErrors.cs:74-75, returned at CreatePersonalContextHandler.cs:51-52, RegisterPersonalHandler.cs:120,125 and ConfirmEmailHandler.cs:288,294. SPEC.md:245 names it. It is shown by PersonalPages.jsx:262 and ConfirmEmailPage.jsx:46. At ConfirmEmailHandler.cs:250-254 the account has already been created when this is returned.
  - session_not_found (404) comes from IdentityAccessErrors.cs:102-103, returned at SessionManagementHandlers.cs:66,83,87, and is shown by SessionsPage.jsx:89,147.
  - personal_profile_concurrency_conflict (409) comes from IdentityAccessErrors.cs:81-82 and PersonalProfileHandlers.cs:58,70,79. SPEC.md:247 names it. It is shown by PersonalPages.jsx:442.
  - profile_field_not_editable (400) comes from IdentityAccessErrors.cs:85-86 and PersonalProfileHandlers.cs:54,56, and is the binding code at PersonalEndpoints.cs:75.
  - email_confirmation_required (403) comes from IdentityAccessErrors.cs:131-132, returned at ReauthenticateHandler.cs:29 and ExternalLoginHandlers.cs:113.
  - invalid_request (400) comes from ProblemDetailsExceptionHandler.cs:44, SelectTenantHandler.cs:28 and ContextEndpoints.cs:37, and is the binding code at SessionEndpoints.cs:25, PasswordEndpoints.cs:26 and AccountLifecycleEndpoints.cs:46. TenantSelector.jsx:48 shows it.
- **Recommendation:** Add MESSAGES entries for all seven. Keep them as neutral as the codes themselves. Suggested wording: - invalid_credential_token: "That reset link has expired or was already used. Ask for a new one." - personal_registration_conflict: "Your personal account could not be created. If this address is confirmed, sign in and try again." Never mention whose document it is (SPEC 14.3). - session_not_found: "That session has already ended. Refresh the list." - personal_profile_concurrency_conflict: "Your profile changed while you were editing. Refresh and try again." - profile_field_not_editable: "That detail cannot be changed here." - email_confirmation_required: "Confirm your email address first." - invalid_request: a reload-and-retry message. Back this with the contract test described in error-code-catalogue-7.
- **Verifier:** Confirmed. The OpenAPI dump (src/Web/wwwroot/openapi/v1.json) has 50 distinct codes. MESSAGES has 43 keys (ProblemMessage.jsx:9-57) and falls back at :65. Exactly the 7 listed are missing. Each emitter and screen checked: - invalid_credential_token: PasswordRecoveryHandlers.cs:109,112,124,146; binding code at PasswordEndpoints.cs:33; shown at PasswordPages.jsx:101,125. - session_not_found: SessionManagementHandlers.cs:66,83,87; shown at SessionsPage.jsx:89,147. - personal_profile_concurrency_conflict: PersonalProfileHandlers.cs:58,70,79; shown at PersonalPages.jsx:442. - profile_field_not_editable: PersonalProfileHandlers.cs:54,56. It is reachable because the inputs have no maxLength and a whitespace-only value passes HTML required. - personal_registration_conflict: ConfirmEmailHandler.cs:288,294, where the account was already created and activated at :247-248; CreatePersonalContextHandler.cs:51-52; shown at ConfirmEmailPage.jsx:46 and PersonalPages.jsx:262. Two of the seven are hard for the SPA to reach: - email_confirmation_required fires only for an identity that is not Active (ReauthenticateHandler.cs:29; ExternalLoginHandlers.cs:113). RegisterOrganizationHandler.cs:80-82 states a validated session always belongs to an Active identity. I did not verify the cookie validation itself. - invalid_request needs an empty tenant id or a malformed body. TenantSelector.jsx:83 only submits ids taken from context. The finding stands on the five that are reachable. An expired or used reset link is a very common path, so high is justified.

<a id="error-code-catalogue-2"></a>

### error-code-catalogue-2 — A wrong authenticator or recovery code comes back under a code that names something else

- **Layer:** error-code-catalogue
- **User impact:** A Platform administrator who mistypes a 6-digit code is told their session is invalid and to sign in again. Signing in again only costs another attempt from a 5-attempt budget. An invitee who mistypes during enrollment is told the invitation is unusable and may give up on a valid invitation. An operator replacing a lost factor is told their password was wrong when it was the recovery code.
- **Evidence:**
  - Step-up with a wrong TOTP code returns invalid_session (401) at PlatformMfaHandlers.cs:222-225. An unfinished enrollment does the same at :217-219.
  - The functional test pins the 401 at SharedAbuseControlTests.cs:119-120, commenting "guess ... is a wrong code, not a refusal".
  - usePlatformStepUp.js:23-26 passes the problem through unchanged. It is rendered at PlatformIdentitiesPage.jsx:208,249 and PlatformRetentionPage.jsx:182 as ProblemMessage.jsx:13, "Your session is no longer valid. Sign in again."
  - SPEC.md:179 (IA-REQ-030) says 401 means an absent or invalid identity. The same code is what the authorization handler sends for a genuinely dead session (ApiAuthorizationMiddlewareResultHandler.cs:19-20), so the client cannot tell the two apart.
  - Enrollment verify with a wrong code returns invalid_invitation at PlatformMfaHandlers.cs:100. That becomes "That invitation is not usable." (ProblemMessage.jsx:19), rendered at PlatformInvitationPages.jsx:258. The UI test mocks exactly this at PlatformInvitationPages.test.jsx:215.
  - MFA recovery with a wrong, spent or superseded recovery code returns invalid_credential_proof at RecoverPlatformMfaHandler.cs:62-63 and 69-76. That becomes "That password was not accepted. Try again." (ProblemMessage.jsx:27) at MfaRecoveryPage.jsx:138, although the password was just accepted by reauthenticate in the same submit (MfaRecoveryPage.jsx:148-149).
- **Recommendation:** Give wrong factor codes their own Validation-category codes: - For verify and step-up, one code such as invalid_mfa_code (400) covering wrong code, no enrollment and unfinished enrollment together. This stays non-disclosing, since the caller is already authenticated and bound. - For recover, one code such as invalid_recovery_code. Keep invalid_session for real session failures only. Declare the new codes in ApiProblemMetadata and the MFA endpoint contracts, map them in MESSAGES, and update SharedAbuseControlTests.
- **Verifier:** Confirmed. Each wrong code comes back under a code that names something else: - Step-up: a wrong code returns invalid_session (PlatformMfaHandlers.cs:222-225), and an enrollment that is not Active does the same (:217-219). - Enrollment verify: a wrong code returns invalid_invitation (:99-100). - MFA recovery: a wrong or spent recovery code returns invalid_credential_proof (RecoverPlatformMfaHandler.cs:62-63, 71-76). The client passes these through unchanged. identityClient.js:13 re-exports ApiProblem as IdentityProblem, and platformClient.js:25-27 uses the same transport, so useSubmit keeps the server code. No client code treats a 401 specially (apiTransport.js:49-54). What each screen then shows: - Step-up renders at PlatformIdentitiesPage.jsx:208,249 and PlatformRetentionPage.jsx:182 as "Your session is no longer valid. Sign in again." - Enrollment verify renders at PlatformInvitationPages.jsx:258 as "That invitation is not usable." The test mock at PlatformInvitationPages.test.jsx:215 pins it. - Recovery renders at MfaRecoveryPage.jsx:138 as the password message. reauthenticate runs first in the same submit (:148-150). SharedAbuseControlTests.cs:119-120 pins the 401. SPEC.md:269 does not name a code for a wrong step-up code, only "Problem Details, including 429", so a new code stays within the SPEC. One small correction: signing in again costs nothing. It is the retry that spends from the same identity-keyed budget of 5 per 15 minutes (PlatformAttemptBudgets.cs:31; PlatformMfaHandlers.cs:92-95).

<a id="error-code-catalogue-3"></a>

### error-code-catalogue-3 — Malformed input on the public forms gets one field-less code; on invitation registration a weak password reads as "invitation not usable"

- **Layer:** error-code-catalogue
- **User impact:** The owner's wider question gets no answer. With a bad CUIT, a bad email or a password the policy refuses, nothing says which of the four fields is wrong, or what the password rule is. An invitee who picks a weak password is told the invitation itself is dead.
- **Evidence:**
  - Organization registration sends every input failure to invalid_registration with no field errors: a missing field, an address without '@', an over-long value, a CUIT that NormalizedCuit.From rejects, and a password the policy refuses. See RegisterOrganizationHandler.cs:48, 173-186 (CUIT at :182, caught at :185) and :56-59.
  - IdentityAccessErrors.cs:17 builds the error without validationErrors, so the field list at ProblemMessage.jsx:69-72 stays empty. The screen shows only "Check the details and try again." (ProblemMessage.jsx:17). The form itself has no per-field error state (RegisterOrganizationPage.jsx:36, 46-87).
  - Personal registration does the same at RegisterPersonalHandler.cs:48, 58-61 and 129-143.
  - POST /api/invitations/register returns invalid_invitation only for a password the policy refuses (RegisterInvitedUserHandler.cs:49-53) and for an unreadable body (InvitationEndpoints.cs:66). Every token failure is answered with the neutral 202 (:62-64). The screen then says "That invitation is not usable." (ProblemMessage.jsx:19).
  - The Platform equivalent does the same at RegisterPlatformInviteeHandler.cs:41-45, before the token lookup at :52.
  - All of these are decided from the request alone, before any address or token is looked up (RegisterOrganizationHandler.cs:53-59 says so explicitly; RegisterInvitedUserHandler.cs:49 runs before :60). So they are not enumeration-sensitive.
  - ApplicationError already allows field errors for Validation (ApplicationError.cs:23-26), and the mapper already emits them (ApiProblemDetailsMapper.cs:28-30). SPEC.md:195 (IA-REQ-038) permits field-indexed errors for validation.
- **Recommendation:** Keep the SPEC-named codes (SPEC.md:243-244 fixes invalid_registration). Attach field-indexed errors built from the input-only checks: email, cuit, legalName, fullName, displayName, documentNumber, and password with the policy's own messages from ValidatePasswordAsync. On /api/invitations/register and /api/platform/invitations/register, answer a password-policy refusal as validation_failed (or invalid_registration) with an errors.password entry instead of invalid_invitation. This is enumeration-safe because the decision precedes every lookup. Then render the errors beside the fields rather than only in the flat list.
- **Verifier:** Confirmed. Organization and personal registration: - RegisterOrganizationHandler.cs:48 and :173-186 send every malformed input to invalid_registration with no field errors. The password policy does the same at :56-59. - RegisterPersonalHandler.cs does the same at :48, :58-61 and :129-143. - No FluentValidation validator exists for RegisterOrganizationCommand, RegisterPersonalCommand or RegisterInvitedUserCommand (grep for AbstractValidator<...> found none). - The organization form has no error or helperText props (RegisterOrganizationPage.jsx:46-87). Invitation registration: - /api/invitations/register answers a weak password with invalid_invitation (RegisterInvitedUserHandler.cs:49-53), before the token lookup at :60. It is rendered at InvitationPages.jsx:62 as "That invitation is not usable." The auditor's open questions are resolved: - RegisterPlatformInviteeCommandValidator (RegisterPlatformInvitee.cs:20-26) answers an empty or over-long token or password with validation_failed and field errors. A weak password still gets invalid_invitation (RegisterPlatformInviteeHandler.cs:41-45). - NormalizedCuit.From (NormalizedCuit.cs:9-38) checks only the allowed characters and a count of exactly 11 digits. It has no check-digit validation (see missed). The mapper and ApplicationError already support field errors (ApiProblemDetailsMapper.cs:28-30; ApplicationError.cs:23-26). Every one of these decisions is made before any address or token is looked up, so field errors are enumeration-safe.

<a id="frontend-presentation-1"></a>

### frontend-presentation-1 — Organization and personal registration: malformed input gets one generic message with no field marked, there is no client-side validation, and a CUIT with a wrong check digit is accepted everywhere

- **Layer:** frontend-presentation
- **User impact:** This is the product owner's form. An 11-digit CUIT with a wrong check digit gets the neutral success, and an organization with an invalid CUIT can later be created. A short CUIT, a policy-refused password or a whitespace-only legal name all get the same 'Check the details and try again.' with no hint which of the four fields is wrong. 'a@b' passes the browser and the server. None of these errors is enumeration-sensitive: the server decides the password policy before looking up the address (RegisterOrganizationHandler.cs:53-59).
- **Evidence:**
  - src/Web/ClientApp/src/features/identity/register/RegisterOrganizationPage.jsx:55-63 (the CUIT TextField has only `required`; no pattern or check), :77-87 (password only `required`), :42 (onSubmit sends the form unchecked), :36 (ProblemMessage at the top of the card)
  - src/Application/IdentityAccess/Organizations/RegisterOrganization/RegisterOrganizationHandler.cs:48, 56-59, 173-186 (bad CUIT shape, missing '@', blank legal name, over-long fields and a policy-refused password all return InvalidRegistration())
  - src/Application/IdentityAccess/Common/IdentityAccessErrors.cs:17 (InvalidRegistration carries no validationErrors); src/Web/Infrastructure/ApiProblemDetailsMapper.cs:28-30 (so `errors` is omitted)
  - src/Web/ClientApp/src/features/identity/ProblemMessage.jsx:17 (invalid_registration -> 'Check the details and try again.')
  - src/Domain/IdentityAccess/Organizations/NormalizedCuit.cs:9-37 (checks digits and length 11 only; the mod-11 check digit is never verified)
  - src/Web/ClientApp/src/features/identity/people/PersonalPages.jsx:93-101 (DNI: inputMode numeric only); src/Application/IdentityAccess/People/RegisterPersonal/RegisterPersonalHandler.cs:48, 58-60, 134-138 (same generic collapse)
  - No SPA test exercises invalid_registration (a grep of *.test.* finds none; AppRoutes.test.jsx:20 only checks the heading)
- **Recommendation:** Add client validators in a shared module: CUIT as 11 digits with the mod-11 check digit, DNI as 7-8 digits, a trimmed non-empty legal name, and password rules mirrored from PasswordOptions. Show each failure on the field (MUI error, helperText, aria-invalid) and block submission. Coordinate with the backend: return validation_failed with field-indexed errors (cuit, email, password, legalName) for malformed input, and verify the CUIT check digit server-side. Add RTL tests for each case.
- **Verifier:** Confirmed. RegisterOrganizationPage.jsx:55-63 and :77-87 have only `required`, and :42 submits unchecked. RegisterOrganizationHandler.cs:48 and :173-186 collapse every malformed input into InvalidRegistration(): a null field, an over-long field, no '@', a blank legal name, or an ArgumentException from NormalizedCuit. Lines :56-59 do the same for a policy-refused password, before any address lookup, so field-level feedback would be enumeration-safe. IdentityAccessErrors.cs:17 attaches no validationErrors, so ApiProblemDetailsMapper.cs:28-30 omits `errors`. ProblemMessage.jsx:17 shows 'Check the details and try again.' NormalizedCuit.cs:9-37 checks only for digits, hyphens or whitespace and a length of 11; no check digit is verified anywhere. A grep for mod-11 or check-digit logic finds nothing, and no validator exists for RegisterOrganizationCommand or RegisterPersonalCommand. The SPEC does not require a check digit either (it only mentions a normalized CUIT, e.g. IA-REQ-033/048). The personal side is the same: PersonalPages.jsx:93-101 adds only inputMode numeric, and RegisterPersonalHandler.cs:48, 58-61 and 134-140 collapse the same way. The only SPA hit for invalid_registration is ProblemMessage.jsx:17, so no test exercises it. The 'a@b' point is also correct: the WHATWG type=email grammar accepts it, and the server only checks Contains('@').

<a id="frontend-presentation-2"></a>

### frontend-presentation-2 — A session that expires mid-flow never leads back to sign-in: the stale context stays, the message has no action, and /login bounces the user back into the app

- **Layer:** frontend-presentation
- **User impact:** After the 30-minute idle limit (IA-REQ-023), any action shows a red 'Sign in to continue.' with no way to act on it. The menu and screens still look signed in. Going to /login sends the user back into the stale shell. Only a full reload or 'Log out' recovers, and the page they were on is not kept as returnUrl.
- **Evidence:**
  - src/Web/ClientApp/src/features/identity/api/apiTransport.js:49-54 (every problem is thrown as ApiProblem; nothing reacts to 401)
  - src/Web/ClientApp/src/features/identity/context/IdentityProvider.jsx:29-56, 75-82, 91-104 (the context is cleared only by the context read, sign-out or deactivation)
  - src/Web/ClientApp/src/components/api-authorization/ProtectedRoute.jsx:13-17 (redirects only when isAuthenticated is already false)
  - src/Web/ClientApp/src/features/identity/ProblemMessage.jsx:12-13, 59-76 ('Sign in to continue.' / 'Your session is no longer valid. Sign in again.' with no link or action)
  - src/Web/ClientApp/src/features/identity/login/LoginPage.jsx:58 (while the stale context says authenticated, /login redirects straight back to returnUrl); src/Web/ClientApp/src/components/Home.jsx:28
  - src/Web/Infrastructure/ApiAuthorizationMiddlewareResultHandler.cs:16-21 (401 authentication_required / invalid_session); docs/features/identity-access/SPEC.md:281 (the cookie is deleted in the same response), :307 (the client always handles 401)
- **Recommendation:** Handle authentication failures centrally. When any call other than the context read answers authentication_required, invalid_session or credential_superseded, call identity.reload() or clear the context and keep the problem. ProtectedRoute will then redirect to /login?returnUrl=<current path>, and LoginPage already shows contextProblem. Give ProblemMessage a 'Sign in again' action for those codes. Add a test covering a 401 mid-flow.
- **Verifier:** The core claim holds. apiTransport.js:49-54 throws every problem as ApiProblem and nothing reacts to 401. IdentityProvider.jsx clears the context only in loadContext's catch (37-53), in signOut's finally (75-82) and in deactivateAccount (91-104). ProtectedRoute.jsx:13-17 redirects only when isAuthenticated is already false. ProblemMessage.jsx:12-13 has no action, NavMenu.jsx:266 hides 'Log in' for a signed-in context, and SPEC.md:307 requires the client to always handle 401. Two parts of the userImpact are overstated. (a) The page is kept as returnUrl on the realistic recovery paths. 'Log out' calls DELETE /sessions/current, which RequireAuthorization answers with 401 (SessionEndpoints.cs:33-36). signOut's finally clears the context before rethrowing, so NavMenu.jsx:91's navigate is skipped and ProtectedRoute redirects to /login?returnUrl=<current path>. A full reload does the same through loadContext's 401. (b) /login bounces the user back (LoginPage.jsx:58, Home.jsx:28) only on in-app navigation. Typing /login in the address bar reloads the app, re-reads the context, gets a 401 and shows the sign-in card. Even so, after every idle expiry (IA-REQ-023) every protected screen shows an actionless refusal inside a shell that still looks signed in. The spec explicitly asks for 401 handling, so I kept high.

<a id="cross-cutting-runtime-1"></a>

### cross-cutting-runtime-1 — There is no React error boundary anywhere, so any exception thrown during render blanks the whole application

- **Layer:** cross-cutting-runtime
- **User impact:** If any component throws while rendering, the whole page goes blank, navigation included. The user gets no message, no way to reload or go back, and no reference to give support. Nothing is reported anywhere.
- **Evidence:**
  - src/Web/ClientApp/src/main.jsx:13-19: createRoot(document.getElementById('root')) is called with no options (no onUncaughtError or onCaughtError), and root.render(<BrowserRouter><App /></BrowserRouter>) has nothing wrapping App.
  - src/Web/ClientApp/src/App.jsx:9-25: MaterialThemeProvider, then IdentityProvider, then Layout, then Routes; there is no boundary at any level.
  - A grep of src/Web/ClientApp/src for ErrorBoundary|componentDidCatch|getDerivedStateFromError|onUncaughtError|onCaughtError|errorElement|unhandledrejection found nothing in source files (test fixtures matched only on traceId).
  - main.jsx:16 and App.jsx:15-20 use BrowserRouter with <Routes>, not a data router, so React Router's route-level errorElement is not available as a fallback either.
  - src/Web/ClientApp/package.json: react and react-dom ^19.1.0. With no boundary, an error thrown during render removes the whole root.
- **Recommendation:** Add a top-level error boundary in App.jsx, inside MaterialThemeProvider so the fallback is styled. Add a second boundary around <Routes>, keyed on location.pathname, so navigating away recovers. The fallback shows a neutral message and a Reload action. Pass onUncaughtError and onCaughtError to createRoot so errors are reported. Add tests that render a component that throws.
- **Verifier:** Confirmed. main.jsx:13 calls createRoot with no options, and :15-19 renders BrowserRouter > App with nothing around App. App.jsx:9-25 has no boundary at any level. A grep of src/Web/ClientApp for ErrorBoundary|componentDidCatch|getDerivedStateFromError|onUncaughtError|onCaughtError|errorElement|unhandledrejection|onRecoverableError found nothing. The app uses BrowserRouter with <Routes>, not a data router, so errorElement is unavailable. package.json:14-16 pins react ^19.1.0 and react-router-dom ^7.6.1. The finding needs a render-time bug to trigger. For example, ProblemMessage.jsx:71 calls messages.join on whatever problemDetails.js:45 accepted as `errors`. But when it triggers, the whole application goes blank with no recovery and no report, and this is the most basic frontend safety net. High stands.

<a id="error-path-tests-1"></a>

### error-path-tests-1 — The field-indexed 'errors' member is never proven at the HTTP boundary; the FluentValidation failure path is untested end to end, and field-key casing is unpinned with two conventions in play

- **Layer:** error-path-tests
- **User impact:** Field-level feedback is the thing the product owner is asking for, and nothing guarantees it works. A validator refusal could arrive with PascalCase keys ('Token', 'Password') on one route and camelCase ('newPassword') on another. The UI would then show raw C# member names to the user or fail to match form fields, and no test would notice. A regression that drops 'errors' entirely would also pass every test.
- **Evidence:**
  - tests/Application.FunctionalTests/IdentityAccess/Api/ProblemDetailsContractTests.cs:297,308 - AssertProblemAsync takes hasErrors=false and checks TryGetProperty("errors") == hasErrors; no caller passes true (grep 'hasErrors: true' finds nothing), so the helper only ever proves absence
  - No test in tests/ reads GetProperty("errors"). The only validation_failed runtime assertion is PasswordLifecycleTests.cs:345-348, which checks the code only
  - src/Web/Infrastructure/ProblemDetailsExceptionHandler.cs:19-22 copies ValidationException.Errors keys verbatim; src/Application/Common/Exceptions/ValidationException.cs:16-18 groups by FluentValidation PropertyName (C# member names, e.g. RuleFor(command => command.Token/Password) at src/Application/IdentityAccess/Platform/Invitations/RegisterPlatformInvitee.cs:24-25)
  - src/Web/Infrastructure/ApiProblemDetailsMapper.cs:43-47 serializes with JsonSerializerDefaults.Web, which camel-cases properties but not dictionary keys. Meanwhile src/Infrastructure/IdentityAccess/IdentityCredentialService.cs:16,42 and src/Application/IdentityAccess/Credentials/ChangePassword/ChangePasswordHandler.cs:29 hand-write 'newPassword'
  - 8 AbstractValidator classes exist (Platform invitee/MFA/administrator commands) and ValidationBehaviour is registered (src/Application/DependencyInjection.cs:11,18). Endpoints declare ValidationFailed (PlatformEndpoints.cs:26,44; PlatformInvitationEndpoints.cs:22,31; PlatformMfaEndpoints.cs:56; PlatformRetentionEndpoints.cs:30; PasswordEndpoints.cs:32,43), yet grep finds no test that references ValidationException outside the unit test and none that sends an empty-field command
  - src/Web/ClientApp/src/features/identity/api/problemDetails.test.js:30-36 is the only test of 'errors', using a lowercase 'email' key the backend never produces. ProblemMessage.jsx:62-72 renders errors as a flat 'field: messages' list, and no *.test.jsx contains an 'errors' stub
- **Recommendation:** Decide on one key convention (e.g. set DictionaryKeyPolicy = JsonNamingPolicy.CamelCase in ApiProblemDetailsMapper, or normalize keys in ProblemDetailsExceptionHandler) and pin it. For each producer (ValidationBehaviour through one Platform route, e.g. POST /api/platform/mfa/verify with an empty code; the password policy through PUT /api/identity/credentials/password), add an HTTP test that calls AssertProblemAsync(..., hasErrors: true) and asserts the exact keys and non-empty messages. Add a SPA test that stubs problem(400,'validation_failed',{errors:{newPassword:[...]}}) and asserts the per-field presentation.
- **Verifier:** Holds. AssertProblemAsync defaults hasErrors=false and checks TryGetProperty("errors") == hasErrors (ProblemDetailsContractTests.cs:297,308). No caller passes true, and grep finds no GetProperty("errors") anywhere in tests/. ValidationException is referenced only in Application.UnitTests. The one runtime validation_failed check reads the code only (PasswordLifecycleTests.cs:345-348, production harness). ValidationException groups by FluentValidation PropertyName (ValidationException.cs:16-18), and src sets no ValidatorOptions or PropertyNameResolver. The mapper serializes with JsonSerializerDefaults.Web, whose DictionaryKeyPolicy is null (ApiProblemDetailsMapper.cs:43-47). So validator keys come out as C# member names (Token, Password, Code, RecoveryCode: RegisterPlatformInvitee.cs:24-25, PlatformMfaCommands.cs:30-104), while the password policy hand-writes 'newPassword' (IdentityCredentialService.cs:16,42; ChangePasswordHandler.cs:29). There are 8 validator classes. In the SPA, only problemDetails.test.js:30-36 uses 'errors', with a key the backend never emits. PlatformRetentionPage.test.jsx:357 stubs validation_failed without errors and asserts only that an alert exists. Nuance: the validator producers are hard to reach from the UI because the MUI forms mark fields required. The producer users actually hit is the password policy, whose raw key ProblemMessage.jsx:71 prints as 'newPassword: ...'. High is still warranted, because field-level feedback is exactly what the product owner asked for and nothing pins it.

<a id="error-path-tests-2"></a>

### error-path-tests-2 — Registration's malformed-input tests pin one fieldless code; the CUIT check digit is neither implemented nor tested (the suite's 'valid' CUITs fail mod-11), and no SPA test submits the registration form

- **Layer:** error-path-tests
- **User impact:** This is the exact form the product owner complained about. Malformed input is not enumeration-sensitive, since it depends only on the submitted values, yet the only answer the tests guarantee is 'Check the details and try again.' with no indication of which field. A CUIT with a wrong check digit is accepted as valid, and adding the rule today would break the existing fixtures instead of being caught by a failing test for the rule.
- **Evidence:**
  - tests/Application.FunctionalTests/IdentityAccess/Api/RegistrationHttpValidationTests.cs:79-108 - the malformed email, weak password, 'bad-cuit' and oversized cases assert only code == 'invalid_registration' (lines 101-102); never which field was wrong, never 'errors'
  - src/Application/IdentityAccess/Organizations/RegisterOrganization/RegisterOrganizationHandler.cs:48,56-59,173-186 - every malformed field, and the password policy, collapse to IdentityAccessErrors.InvalidRegistration(), which carries no validationErrors (src/Application/IdentityAccess/Common/IdentityAccessErrors.cs:17)
  - src/Domain/IdentityAccess/Organizations/NormalizedCuit.cs:9-38 checks characters and an 11-digit length only; there is no check-digit validation
  - tests/Application.FunctionalTests/IdentityAccess/Api/ProblemDetailsContractTests.cs:123,129 and Organizations/RegistrationInputValidationTests.cs:48-50 treat '30-12345678-9' as valid input. Its mod-11 check digit (weights 5,4,3,2,7,6,5,4,3,2; sum 153; 11 - 153 mod 11 = 1) is 1, not 9. tests/Domain.UnitTests/IdentityAccess/OrganizationProfileTests.cs:13 uses '30-71234567-4', whose check digit is also 1
  - src/Web/ClientApp/src/AppRoutes.test.jsx:20 only checks that the route renders the heading, and identityFiles.contract.test.js:20,70 only checks that the file exists. No test submits RegisterOrganizationPage (RegisterOrganizationPage.jsx:36-42) or asserts its refusal, which would show the generic ProblemMessage.jsx:17 text 'Check the details and try again.'
- **Recommendation:** Specify and test field-level registration refusals: either validation_failed with errors.{email,password,legalName,cuit}, or invalid_registration plus errors, evaluated before any address lookup so neutrality holds. Add a mod-11 check to NormalizedCuit, switch fixtures to valid CUITs (e.g. 30-12345678-1, 30-71234567-1), and add wrong-check-digit cases to RegistrationHttpValidationTests.InvalidPayloads and OrganizationProfileTests. Add RegisterOrganizationPage.test.jsx covering a malformed-input refusal shown per field and the neutral 202 acknowledgement.
- **Verifier:** Evidence holds. RegistrationHttpValidationTests.cs:79-108 asserts only code == invalid_registration (line 102). Every malformed field and the password policy collapse to InvalidRegistration() (RegisterOrganizationHandler.cs:48,56-59,173-186), which carries no validationErrors (IdentityAccessErrors.cs:17). NormalizedCuit.cs:9-38 checks only characters and length. I recomputed mod-11: 30-12345678-9 should end in 1 (sum 153), and 30-71234567-4 should also end in 1 (sum 142). It is worse than cited: IdentityAccessCuit.Next() always emits a '-9' check digit (RegistrationPrivacySequenceTests.cs:157-160), and '30-12345678-9' appears 40 times in tests and src. One correction: a Playwright acceptance test does submit the form (Web.AcceptanceTests/Pages/IdentityAccessPages.cs:92-102), but only for the neutral 202. No UI test anywhere covers a refusal, and no vitest submits it (AppRoutes.test.jsx:20 renders the heading only). Context: the SPEC has no check-digit rule, and SPEC:244 specifies a fieldless '400 invalid_registration for malformed input' pattern for personal registration. Fixing this therefore needs a spec and implementation change, not only tests. Neutrality stays safe if field errors are added: the password policy is already evaluated before any address lookup (handler:53-59), and RegistrationTests.cs:342 proves a weak password is refused for a taken address as well as a free one. High is kept because this is the form that triggered the audit, and an invalid CUIT is accepted into durable records.

## Medium (25)

<a id="backend-model-and-mapping-3"></a>

### backend-model-and-mapping-3 — Field-error keys use two naming conventions (C# PascalCase vs camelCase wire names), and the mapper passes them through unchanged

- **Layer:** backend-model-and-mapping
- **User impact:** The client cannot reliably put an error under the input that caused it, because it would have to know which casing each endpoint uses. Errors end up in a flat list at the top, showing C# property names and generic framework wording, which falls short of clear field-level feedback.
- **Evidence:**
  - src/Application/Common/Exceptions/ValidationException.cs:16-18 keys the errors by FluentValidation's PropertyName
  - No PropertyNameResolver or DisplayNameResolver is configured anywhere in src (grep: no matches). So RuleFor(command => command.Token) and RuleFor(command => command.Password) in src/Application/IdentityAccess/Platform/Invitations/RegisterPlatformInvitee.cs:24-25 produce keys 'Token' and 'Password', while the request fields are token/password (src/Web/Endpoints/Platform/PlatformInvitationEndpoints.cs:47,53)
  - src/Infrastructure/IdentityAccess/IdentityCredentialService.cs:16,42 and src/Application/IdentityAccess/Credentials/ChangePassword/ChangePasswordHandler.cs:29 use the camelCase key 'newPassword'
  - src/Web/Infrastructure/ProblemDetailsExceptionHandler.cs:19-22 and src/Web/Infrastructure/ApiProblemDetailsMapper.cs:28-30,43-47 copy keys verbatim; the Web JSON defaults camel-case property names but not dictionary keys
  - src/Web/ClientApp/src/features/identity/ProblemMessage.jsx:62,71 renders the raw key, e.g. 'Token: ...' followed by FluentValidation's default English message
  - No test checks the key format of errors; the only assertions check that errors is absent (ProblemDetailsContractTests.cs:308; InvitationHttpContractTests.cs:106; SessionTests.cs:1458)
- **Recommendation:** Make the wire (JSON) field name the only key format. Configure ValidatorOptions.Global.PropertyNameResolver to camel-case the member path, and normalize keys in the mapper as a safety net. Replace FluentValidation's default messages with stable user-facing text or message codes. Add a contract test asserting that every errors key names a property of the endpoint's request schema.
- **Verifier:** Verified. - ValidationException.cs:16-18 groups the errors by PropertyName. - A grep for PropertyNameResolver, DisplayNameResolver or DictionaryKeyPolicy finds nothing. - RegisterPlatformInvitee.cs:24-25 therefore produces the keys 'Token' and 'Password', while the wire DTO uses token/password (PlatformInvitationEndpoints.cs:47,53). - The credential paths use 'newPassword' (IdentityCredentialService.cs:16,42; ChangePasswordHandler.cs:29). - The mapper copies keys unchanged (ApiProblemDetailsMapper.cs:28-30), and JsonSerializerDefaults.Web does not camel-case dictionary keys. - ProblemMessage.jsx:62,71 renders the raw key next to FluentValidation's default English text. - The only key assertion in tests is a unit test that expects 'Password' (ValidationExceptionTests.cs:55). No contract test checks the key format. Today the PascalCase keys are reachable only on the Platform invitee and MFA routes. Every new FluentValidation validator would inherit them, though, so medium fits a system-wide standard.

<a id="backend-model-and-mapping-4"></a>

### backend-model-and-mapping-4 — Declared problem codes drift from what routes can actually return, and no generic check catches it

- **Layer:** backend-model-and-mapping
- **User impact:** The OpenAPI document, and any client generated from it, tells the frontend that confirm-email cannot be rate-limited or unavailable. A person confirming a personal registration during an outage, or after too many attempts, can get a response the UI never planned for. The same applies to malformed bodies on the password-recovery and reactivation-request routes, and to a bad limit on list pages. IA-REQ-038 says contract tests must reject this kind of drift.
- **Evidence:**
  - src/Web/Endpoints/Identity.cs:35-38: POST /api/identity/confirm-email declares only antiforgery_validation_failed, invalid_confirmation, registration_conflict and internal_server_error
  - src/Application/IdentityAccess/Organizations/ConfirmEmail/ConfirmEmailHandler.cs:218-222 and 297-304 can return 429 rate_limit_exceeded or 503 service_unavailable (with Retry-After) from the personal DocumentClaim budget, and :288 and :294 can return 409 personal_registration_conflict; none of these is declared. SPEC.md:182 (IA-REQ-057) requires every route that spends a budget to declare both 429 and 503
  - src/Web/Endpoints/Identity/PasswordEndpoints.cs:25-26 (/credentials/password/recovery) and src/Web/Endpoints/Identity/AccountLifecycleEndpoints.cs:41-46 (/account/reactivation-requests) set invalid_request as their binding-failure code without declaring it
  - An `int? limit` query parameter is bound at src/Web/Endpoints/Platform/PlatformEndpoints.cs:146,156,166,176, src/Web/Endpoints/Identity/RoleEndpoints.cs:64 and src/Web/Endpoints/Identity/MembershipEndpoints.cs:75,83. A non-numeric value throws BadHttpRequestException (DependencyInjection.cs:33-34), which becomes 400 invalid_request (ProblemDetailsExceptionHandler.cs:26,40-45). The declarations at PlatformEndpoints.cs:36-46, RoleEndpoints.cs:27-30 and MembershipEndpoints.cs:22-30 do not list invalid_request
  - src/Web/Infrastructure/ApiProblemMetadata.cs:104-134: WithApiProblemDetails and WithBodyBindingFailureCode are set independently, and nothing checks that the binding code is among the declared ones
  - tests/Application.FunctionalTests/IdentityAccess/Api/OpenApiContractTests.cs:56-61 checks only the listed codes for confirm-email; exact-set checks cover only a handful of routes
- **Recommendation:** Declare 409 personal_registration_conflict, 429 and 503 on confirm-email, reusing the BoundedClaim set from PersonalEndpoints.cs:20-24. Make WithBodyBindingFailureCode add its code to the declared contracts, or fail at startup when it is missing. Declare invalid_request on every route that binds query, route or body values. Add one reflection-driven test over all endpoints asserting that: the binding code is declared; RequireAuthorization routes declare 401 authentication_required and invalid_session plus 403 permission_denied; and RequireLoginAttemptBudgets routes declare both 429 and 503.
- **Verifier:** Verified. - Identity.cs:35-38 declares only antiforgery, invalid_confirmation, registration_conflict and 500 for confirm-email. - ConfirmEmailHandler.cs:97-99 sends personal intents to FinalizePersonalAsync. There, :218-222 with :297-304 can return 429 rate_limit_exceeded or 503 service_unavailable with Retry-After, and :288 and :294 return 409 personal_registration_conflict. None of these is declared, which contradicts SPEC.md:182 ('Every route that names a budget carries both answers'). - PasswordEndpoints.cs:25-26 and AccountLifecycleEndpoints.cs:41-46 set invalid_request as the binding code without declaring it. - The directory set at PlatformEndpoints.cs:36-46 and the list routes at RoleEndpoints.cs:27-30 and MembershipEndpoints.cs:22-30 bind int? limit (PlatformEndpoints.cs:146-176, RoleEndpoints.cs:64, MembershipEndpoints.cs:75,83) but do not declare invalid_request. - Nothing adds these codes automatically. ApiExceptionOperationTransformer.cs:14-40 publishes only declared contracts, and RequireLoginAttemptBudgets (LoginRateLimiting.cs:40-41) adds only a marker. - OpenApiContractTests.cs:56-61 checks only the codes it lists. I did not run a bad limit value. It relies on standard ThrowOnBadRequest binding behaviour.

<a id="backend-model-and-mapping-5"></a>

### backend-model-and-mapping-5 — Unknown /api routes do not answer problem+json: the SPA fallback returns HTML or an empty 404

- **Layer:** backend-model-and-mapping
- **User impact:** A typo in a client route, a stale client after a rename, or a removed endpoint gets either a 200 HTML page or an empty 404 with no code and no traceId. With the HTML, the client's JSON parsing fails and its RFC 9457 reader has nothing to read, so the UI cannot show a real error. This breaks IA-REQ-038's rule that every non-success is problem+json with a stable code.
- **Evidence:**
  - src/Web/Program.cs:62-64: app.MapFallbackToFile("index.html") is compiled in, because build/ClientFramework.props sets ClientFramework to React and Directory.Build.props:15-23 defines UseApiOnly only for ApiOnly
  - Microsoft.AspNetCore.StaticFiles.xml (10.0.11 ref pack), lines 581-582: the fallback registers the pattern {*path:nonfile} at order int.MaxValue, which matches any extension-less path, /api included
  - src/Web/Web.csproj:55-58 copies the React build into wwwroot when publishing, so a published GET /api/<typo> returns index.html with 200 text/html. The local checkout's src/Web/wwwroot has no index.html, so there the request falls through to an empty 404
  - Program.cs (read in full) has no UseStatusCodePages and no /api-scoped fallback; no test sends a request to an unknown /api path (grep over tests found none)
- **Recommendation:** Map an /api catch-all ahead of the SPA fallback, for example app.MapFallback("/api/{**path}", ...), that writes 404 not_found through IProblemDetailsService. Alternatively, restrict MapFallbackToFile to paths outside /api. Add tests for GET and POST to an unknown /api route, and decide how a known path called with the wrong method should answer.
- **Verifier:** Verified. - Program.cs:62-64 is compiled in: build/ClientFramework.props sets React, so UseApiOnly is undefined. - StaticFiles.xml:582 documents the fallback pattern {*path:nonfile} at order int.MaxValue. - Web.csproj:56-58 publishes ClientApp/build into wwwroot. - The local src/Web/wwwroot holds only openapi/ and favicon.ico. - There is no UseStatusCodePages, and no test sends a request to an unknown /api path. The reach is wider than the auditor said. Routes use :guid constraints (e.g. RoleEndpoints.cs:27, PlatformEndpoints.cs:80), so a malformed id matches no route and also drops into the fallback, instead of getting a 400 or 404 problem. On the client, a 200 HTML response makes problemDetails.js readSuccess throw 'The success response was not valid JSON.' (lines 59-67).

<a id="backend-validation-coverage-4"></a>

### backend-validation-coverage-4 — Field-error keys and messages follow two conventions: PascalCase library defaults vs camelCase `newPassword`

- **Layer:** backend-validation-coverage
- **User impact:** The frontend cannot attach an error to the input that caused it, because the key 'Token' does not match the input 'token'. What it shows is FluentValidation's default English text built from C# member names instead of the product's own wording. On the same form, the next rule on the same field switches to a different code with no field at all.
- **Evidence:**
  - C:/Users/ezequ/source/repos/RepositorioBase/src/Application/Common/Exceptions/ValidationException.cs:16-18 — errors are grouped by ValidationFailure.PropertyName, which is the C# member name ('Token', 'Password', 'Code', 'Email', 'ConfirmationToken', 'RecoveryCode').
  - C:/Users/ezequ/source/repos/RepositorioBase/src/Application/IdentityAccess/Platform/Mfa/PlatformMfaCommands.cs:30,42-43,58,90,104; RegisterPlatformInvitee.cs:24-25; ConfirmPlatformInvitee.cs:26; PlatformAdministrators.cs:26 — no WithName, OverridePropertyName or WithMessage, and a search of src/ found no PropertyNameResolver or ValidatorOptions configuration.
  - C:/Users/ezequ/source/repos/RepositorioBase/src/Web/Infrastructure/ProblemDetailsExceptionHandler.cs:19-22 copies the keys unchanged. C:/Users/ezequ/source/repos/RepositorioBase/src/Web/Infrastructure/ApiProblemDetailsMapper.cs:43-47 serializes with JsonSerializerDefaults.Web, which camel-cases properties but not dictionary keys, and Errors is a dictionary (ApiProblemDetails.cs:14).
  - C:/Users/ezequ/source/repos/RepositorioBase/src/Web/ClientApp/src/features/platform/api/platformClient.js:32,35,47,52,58,112 — the client sends token/password/confirmationToken/code/recoveryCode/email in camelCase.
  - C:/Users/ezequ/source/repos/RepositorioBase/src/Infrastructure/IdentityAccess/IdentityCredentialService.cs:16,42 and C:/Users/ezequ/source/repos/RepositorioBase/src/Application/IdentityAccess/Credentials/ChangePassword/ChangePasswordHandler.cs:28-29 — the only other field-keyed path uses camelCase 'newPassword' and messages the app wrote itself.
  - C:/Users/ezequ/source/repos/RepositorioBase/src/Web/ClientApp/src/features/identity/ProblemMessage.jsx:62-72 — the client prints the raw keys as 'field: messages'.
  - Same form, two shapes: RegisterPlatformInvitee.cs:25 (empty password -> `validation_failed` {Password}) vs RegisterPlatformInviteeHandler.cs:41-44 (weak password -> `invalid_invitation`, no field). PlatformAdministrators.cs:26 (empty email -> `validation_failed` {Email}) vs PlatformAdministrators.cs:53-61 (malformed email -> `invalid_platform_operation`, no field).
  - A search of tests/ found no assertion on the shape of the errors dictionary. PasswordLifecycleTests.cs:345-348 checks only the code.
- **Recommendation:** Configure camelCase keys once, via ValidatorOptions.Global.PropertyNameResolver or a camelCase key policy for Errors, so every key matches the JSON member name. Write a message per rule with WithMessage instead of relying on library defaults. Make handler-level input rules (password policy, email canonicalization) emit the same `validation_failed` + field shape. Add one contract test asserting that errors keys equal the request's JSON member names.
- **Verifier:** Verified. The PascalCase path: - ValidationException.cs:16-18 groups the errors by PropertyName. - A search of src found no ValidatorOptions, PropertyNameResolver, OverridePropertyName or WithName on any validator. The only WithName hits are endpoint names in EndpointRouteBuilderExtensions. - ProblemDetailsExceptionHandler.cs:19-22 copies the keys unchanged. - ApiProblemDetailsMapper.cs:43-47 serializes with JsonSerializerDefaults.Web, which sets no DictionaryKeyPolicy. Errors is a dictionary (ApiProblemDetails.cs:14), so the keys stay 'Token', 'Password' and so on. The camelCase paths: - IdentityCredentialService.cs:16 and :42 use 'newPassword'. - platformClient.js:32, 35, 47, 52, 58 and 112 send camelCase bodies. - problemDetails.test.js:31-34 also assumes camelCase keys. The mixed codes on one form are also confirmed: RegisterPlatformInvitee.cs:25 vs RegisterPlatformInviteeHandler.cs:41-44, and PlatformAdministrators.cs:26 vs :53-61. Tests check only whether errors is present (ProblemDetailsContractTests.cs:308) or only the code (PasswordLifecycleTests.cs:347-348). Caveats: - ProblemMessage.jsx:62-72 does not bind errors to inputs today; it renders a flat 'field: messages' list. The key mismatch is therefore latent. The visible effect is FluentValidation's default text, e.g. "Token: 'Token' must not be empty." - This is reachable: a Platform invitee who reloads the register page loses the token (useFragmentToken.js:15-21) and gets exactly that message. - On the auditor's open question: src/Web configures no culture or request localization (search found none). So the language of those default messages depends on the host's UI culture.

<a id="backend-validation-coverage-6"></a>

### backend-validation-coverage-6 — Field-level input errors on signed-in forms get operation codes whose UI message blames the wrong thing

- **Layer:** backend-validation-coverage
- **User impact:** An administrator who leaves a role name empty is told they lack permissions. One who mistypes an invitee's email is told the invitation is not usable. A person who clears their display name sees 'That request could not be completed.' None of these callers is anonymous, so merging the answers protects nothing and costs the correct explanation.
- **Evidence:**
  - C:/Users/ezequ/source/repos/RepositorioBase/src/Application/IdentityAccess/People/Profile/PersonalProfileHandlers.cs:53-56 — a blank or over-length fullName/displayName returns `profile_field_not_editable` ('The member ... cannot be edited', IdentityAccessErrors.cs:85-86). SPEC.md:247 and IA-REQ-050 (SPEC.md:113) reserve that code for fields outside the editable set. Lines :57-58: a missing version returns 409 `personal_profile_concurrency_conflict`.
  - C:/Users/ezequ/source/repos/RepositorioBase/src/Application/IdentityAccess/Credentials/PasswordRecovery/PasswordRecoveryHandlers.cs:108-109 — a null or >256-character new password on reset returns `invalid_credential_token`, meaning 'the link is not valid'.
  - C:/Users/ezequ/source/repos/RepositorioBase/src/Infrastructure/IdentityAccess/RoleAdministrationStore.cs:156-157,177-178 with C:/Users/ezequ/source/repos/RepositorioBase/src/Domain/IdentityAccess/Authorization/Role.cs:83-89 — an empty or >128-character role name becomes RoleWriteStatus.Invalid, then `invalid_role_operation` (RoleHandlers.cs:100-106, :342-347). ProblemMessage.jsx:32 explains that code as 'You can only grant permissions you hold yourself.'
  - C:/Users/ezequ/source/repos/RepositorioBase/src/Application/IdentityAccess/Invitations/InviteMember/InviteMemberHandler.cs:56-67 — a malformed invitee email or an empty role list returns `invalid_invitation`, shown as 'That invitation is not usable.' (ProblemMessage.jsx:19).
  - C:/Users/ezequ/source/repos/RepositorioBase/src/Application/IdentityAccess/People/CreatePersonalContext/CreatePersonalContextHandler.cs:34-35 — a malformed DNI or name from an already signed-in caller returns `invalid_registration`, a code SPEC.md:245 does not list for POST /api/identity/personal.
  - C:/Users/ezequ/source/repos/RepositorioBase/src/Web/ClientApp/src/features/identity/ProblemMessage.jsx:9-57 has no entry for `profile_field_not_editable` or `invalid_credential_token`, so both fall back to 'That request could not be completed.' (line 65).
- **Recommendation:** Split each case in two. (a) Syntax and shape problems -> 400 `validation_failed` with camelCase field keys: name, email, roleIds, fullName, displayName, documentNumber, newPassword, version. (b) The existing operation codes only for refusals about authority or state, where merging the answers is deliberate: grant ceiling, cross-tenant access, token state. Keep `profile_field_not_editable` for its SPEC meaning, a field outside the editable set.
- **Verifier:** The core claims are verified. Profile: - PersonalProfileHandlers.cs:53-56 returns profile_field_not_editable ('The member ... cannot be edited', IdentityAccessErrors.cs:85-86) for a blank or over-length name. - SPEC:247 and IA-REQ-050 (SPEC:113) reserve that code for members outside the accepted set. - A missing version gets 409 (lines 57-58). - A search of ClientApp found neither profile_field_not_editable nor invalid_credential_token, so both fall back to ProblemMessage.jsx:65. - PersonalPages.jsx:443-460 has required but no maxLength, so a 61+ character display name or a whitespace-only name reaches this path. Roles: - Role.cs:83 and :89 throw for an empty or over-128 name. - RoleAdministrationStore.cs:156-157 and :177-178 turn that into Invalid, then RoleHandlers.cs:100-106 into invalid_role_operation. - ProblemMessage.jsx:32 shows 'You can only grant permissions you hold yourself.' - RolesPage.jsx:381-389 has required but no maxLength. Invitations: - An empty role list is reachable, because InviteMemberPage.jsx:140-149 sends whatever is checked, unguarded. InviteMemberHandler.cs:61-67 answers invalid_invitation ('That invitation is not usable.'). Weaker sub-claims: - A malformed invitee email is mostly blocked by type=email (InviteMemberPage.jsx:216-225). Invitation.Canonicalize (Invitation.cs:256-270) only adds whitespace, uppercase and '@' checks. - The reset path's null or over-256 password (PasswordRecoveryHandlers.cs:108-109) is real in code but not realistically reachable from the UI. - CreatePersonalContext's invalid_registration is declared by the endpoint (PersonalEndpoints.cs:44) but omitted from SPEC:245. Its UI text is not misleading, so that item is a SPEC-table omission, not wrong blame.

<a id="backend-validation-coverage-7"></a>

### backend-validation-coverage-7 — A mistyped Platform MFA code is reported as an unusable invitation (verify) or an invalid session (step-up)

- **Layer:** backend-validation-coverage
- **User impact:** An operator who mistypes six digits during step-up is told their session is invalid and to sign in again. Signing out and back in does not help and wastes time. An invitee finishing enrollment is told the invitation is unusable and may abandon onboarding.
- **Evidence:**
  - C:/Users/ezequ/source/repos/RepositorioBase/src/Application/IdentityAccess/Platform/Mfa/PlatformMfaHandlers.cs:100 — a wrong TOTP code on /mfa/verify returns `invalid_invitation` (400).
  - C:/Users/ezequ/source/repos/RepositorioBase/src/Application/IdentityAccess/Platform/Mfa/PlatformMfaHandlers.cs:222-225 — a wrong TOTP code on /mfa/step-up returns `invalid_session` (401).
  - C:/Users/ezequ/source/repos/RepositorioBase/tests/Application.FunctionalTests/IdentityAccess/Platform/PlatformMfaAttemptLimitTests.cs:32 and :130 pin both codes as the contract.
  - C:/Users/ezequ/source/repos/RepositorioBase/src/Web/ClientApp/src/features/identity/ProblemMessage.jsx:13 and :19 — shown as 'Your session is no longer valid. Sign in again.' and 'That invitation is not usable.'
  - By the time the code is compared, the caller is already signed in and admitted (PlatformMfaGate.cs:35-58) and has spent an attempt from the limit (PlatformMfaHandlers.cs:94-96, :208-210). IdentityAccessErrors.cs:60-63 already accepts that 'wrong code' must be distinguishable from 'stop asking'.
- **Recommendation:** Add one code for a wrong second-factor code, either a dedicated 400 (for example `invalid_mfa_code`) or `validation_failed` with errors { code: [...] }. Keep `invalid_session` for session problems and `invalid_invitation` for token problems. Update the two tests and the UI message.
- **Verifier:** Verified. - A wrong TOTP code on /mfa/verify returns InvalidInvitation (PlatformMfaHandlers.cs:100). On step-up it returns InvalidSession, a 401 (lines 222-225). - PlatformMfaAttemptLimitTests.cs:32, :115 and :130 pin both codes. - The frontend pins the misleading text too: PlatformInvitationPages.test.jsx:215-223 expects 'invitation is not usable' after a wrong verify code. - For step-up, PlatformPanel.jsx:147-151 says in a comment that 'a refused code is answered where the code was typed'. Yet it renders ProblemMessage, which maps invalid_session to 'Your session is no longer valid. Sign in again.' (ProblemMessage.jsx:13). - The caller has already been admitted and has spent an attempt (lines 89-96 and 201-210). A 401 for a wrong code inside a valid session contradicts IA-REQ-030's meaning of 401. - SPEC:268-269 does not prescribe which code a wrong code gets, so this is not specified behaviour.

<a id="backend-validation-coverage-missed-1"></a>

### backend-validation-coverage-missed-1 — Organization-invitation sign-up answers a missing token with the neutral 202 ('Check your email'), while its Platform twin rejects it as input

- **Layer:** backend-validation-coverage
- **User impact:** Two cases lead here: a member reloads the invitation page, which is a natural reaction after the misleading 'That invitation is not usable.' from finding 2, or they open a truncated link. They then type a valid password and are told to check their email. Nothing is sent and nothing says the link's token is missing, so the onboarding dead-ends behind a success message. A missing token is decidable from the request alone, so neutrality protects nothing here.
- **Evidence:**
  - C:/Users/ezequ/source/repos/RepositorioBase/src/Application/IdentityAccess/Invitations/InvitationDelivery.cs:74-77: a null or blank token returns null without a lookup. This also answers the auditor's open question: a null token does not become a 500.
  - C:/Users/ezequ/source/repos/RepositorioBase/src/Application/IdentityAccess/Invitations/RegisterInvitedUser/RegisterInvitedUserHandler.cs:60-67: a null invitation leads to Result.Success(), which is a neutral 202. So an empty token is treated exactly like a dead one.
  - C:/Users/ezequ/source/repos/RepositorioBase/src/Web/Endpoints/Identity/InvitationEndpoints.cs:132-134 and :143-147: the endpoint's own comment says 'The only failure it can answer with is one decided from the request alone'. A missing token is exactly such a failure, yet it gets 202.
  - C:/Users/ezequ/source/repos/RepositorioBase/src/Application/IdentityAccess/Platform/Invitations/RegisterPlatformInvitee.cs:15-26: the Platform twin rejects an empty token as 'shape only' input, answering 400 validation_failed.
  - C:/Users/ezequ/source/repos/RepositorioBase/src/Web/ClientApp/src/features/identity/useFragmentToken.js:15-21: the token is read once from the URL fragment, and replaceState erases the fragment on mount. Reloading the page leaves the token null.
  - C:/Users/ezequ/source/repos/RepositorioBase/src/Web/ClientApp/src/features/identity/invitations/InvitationPages.jsx:45-53 and :63: the page never checks for a missing token. It submits `token ?? ''` and, on 202, shows 'Check your email. If that invitation is still open, we have sent you what you need to continue.'
- **Recommendation:** Reject a null, blank or over-length token before any lookup, as RegisterPlatformInviteeCommandValidator already does. Answer 400 validation_failed with { token: [...] }, and declare validation_failed on POST /api/invitations/register. On both invitation pages, detect a missing fragment token on mount and say the link is incomplete and should be reopened from the email, instead of rendering a form that cannot succeed.
- **Verifier:** found by verifier

<a id="error-code-catalogue-4"></a>

### error-code-catalogue-4 — Codes the runtime emits that OpenAPI and the SPEC do not declare, and declarations nothing emits

- **Layer:** error-code-catalogue
- **User impact:** OpenAPI is supposed to be the source of truth (SPEC.md:238), but a client or test built from it does not know that confirm-email can answer 409 personal_registration_conflict, 429 or 503. The UI therefore has no text for them: the 409 shows the generic fallback, while 429/503 get the shared rate-limit/unavailable text but nothing written for this flow. The Retry-After header on confirm-email is also undocumented.
- **Evidence:**
  - POST /api/identity/confirm-email declares only antiforgery_validation_failed, invalid_confirmation, registration_conflict and internal_server_error (Identity.cs:35-38 and the v1.json dump). SPEC.md:248 lists the same set. The handler additionally returns: - personal_registration_conflict (ConfirmEmailHandler.cs:288, 294); - rate_limit_exceeded (ConfirmEmailHandler.cs:218-222, 301-302); - service_unavailable (:303).
  - That same route spends a shared budget (PersonalAttemptBudgets.DocumentClaim, ConfirmEmailHandler.cs:218-221) but declares neither 429 nor 503. SPEC.md:182 (IA-REQ-057) requires every route that spends a budget to declare both.
  - POST /credentials/password/recovery and POST /account/reactivation-requests fall back to invalid_request on an unreadable body (PasswordEndpoints.cs:26; AccountLifecycleEndpoints.cs:46). Their WithApiProblemDetails lists omit it (PasswordEndpoints.cs:25; AccountLifecycleEndpoints.cs:41-45), and v1.json shows only antiforgery_validation_failed under 400.
  - POST /api/identity/personal/register declares rate_limit_exceeded and service_unavailable (PersonalEndpoints.cs:20-24, 30-36). Its handler takes no ISharedAttemptBudget (RegisterPersonalHandler.cs:23-33), and the route has no RequireLoginAttemptBudgets, so those codes cannot occur.
  - The same route does emit 409 personal_registration_conflict on a replay that is still in flight (RegisterPersonalHandler.cs:118-120), but SPEC.md:244 does not list it.
- **Recommendation:** - Declare PersonalRegistrationConflict, RateLimitExceeded and ServiceUnavailable on /confirm-email, and update the SPEC row. - Make WithBodyBindingFailureCode add its code to the declared contracts automatically, so the binding code can never be undeclared. - Drop the 429/503 declarations on /personal/register, or have the route actually spend a budget. - Add a functional test that drives each route's refusal paths and asserts every returned code is among the route's declared x-problem-codes.
- **Verifier:** Confirmed with a read-only dump of v1.json. POST /api/identity/confirm-email declares only these (Identity.cs:35-38; SPEC.md:248 lists the same): - 400 antiforgery_validation_failed and invalid_confirmation; - 409 registration_conflict; - 500. There is no 429, no 503 and no Retry-After. Yet the handler spends PersonalAttemptBudgets.DocumentClaim (ConfirmEmailHandler.cs:218-222) and returns: - rate_limit_exceeded and service_unavailable (:297-304), which breaks the IA-REQ-057 rule that every route naming a budget carries both (SPEC.md:182); - personal_registration_conflict (:288,294). The body-binding code invalid_request is missing from both public routes' 400 list, which shows only antiforgery_validation_failed (PasswordEndpoints.cs:25-26; AccountLifecycleEndpoints.cs:41-46). /personal/register declares 429 and 503 (PersonalEndpoints.cs:20-36), but: - its handler takes no ISharedAttemptBudget (RegisterPersonalHandler.cs:23-33); - the route has no RequireLoginAttemptBudgets; - the only rate limiter in Web is RequireLoginAttemptBudgets (LoginRateLimiting.cs:40). The in-flight replay 409 at RegisterPersonalHandler.cs:118-120 is declared in OpenAPI but missing from SPEC.md:244. One more of the same kind: /api/platform/mfa/step-up declares 409 invitation_conflict through the shared Gate list (PlatformMfaEndpoints.cs:38, 55-62, 70), but StepUpPlatformMfaCommandHandler (PlatformMfaHandlers.cs:198-231) never returns it.

<a id="error-code-catalogue-5"></a>

### error-code-catalogue-5 — registration_conflict says "That organization cannot be registered right now" but is also the answer for expired links of every confirmation kind

- **Layer:** error-code-catalogue
- **User impact:** An invited member or personal registrant with an expired link is told about an organization they never tried to register. An organization registrant whose confirmation won the address but lost the CUIT is given no hint that they now have a working account and can sign in.
- **Evidence:**
  - ConfirmEmailHandler.cs:53 (envelope terminated) and :54-66 (envelope expired) return RegistrationConflict before the message type is read at :69-100. So an invited member's or a personal signup's expired link also gets registration_conflict.
  - ConfirmEmailPage is shared by the organization and invited-member flows (ConfirmEmailPage.jsx:20-22). It shows ProblemMessage.jsx:21, "That organization cannot be registered right now.", at :46.
  - For an organization, the post-proof "CUIT taken" case creates and activates the account before refusing (ConfirmEmailHandler.cs:162-170; SPEC.md:109). The "address already had an identity" case also refuses (:151-154). In neither case can retrying "right now" or later succeed.
- **Recommendation:** Evaluate the message type before the expiry and terminated checks. For non-organization confirmations, answer invalid_confirmation (or a neutral expired-link code). Reword registration_conflict so it is true in all its cases without telling them apart, for example: "This link can no longer create the organization. If you have an account at this address, sign in; otherwise register again."
- **Verifier:** Confirmed. ConfirmEmailHandler.cs:53 (terminated envelope) and :54-66 (expired envelope) return RegistrationConflict before the message type is read at :69-100. Invited-member and personal links therefore get the organization code. The page is shared (ConfirmEmailPage.jsx:20-22, 46). In the CUIT-taken case the account is created and activated before the refusal (:162-170), and the page does not tell the person they can sign in. The auditor's open question is resolved, and it makes the finding stronger. ConfirmPlatformInviteeHandler.cs also returns registration_conflict: - :53-56 for a terminated envelope; - :58-63 for an expired one; - :92-95 for an invitation that is no longer pending. ConfirmPlatformInviteePage renders it at PlatformInvitationPages.jsx:164, so a Platform administrator invitee is told "That organization cannot be registered right now." The code itself matches the SPEC for the organization flow (SPEC.md:248 and row 526). The defect is the evaluation order for non-organization confirmations, plus the wording.

<a id="error-code-catalogue-7"></a>

### error-code-catalogue-7 — No test ties the React code map to the OpenAPI catalogue, and UI fixtures use codes and statuses the server never sends

- **Layer:** error-code-catalogue
- **User impact:** This is how seven live codes, including the password-reset one, shipped without text. A new server code will fall back to the generic message silently.
- **Evidence:**
  - SPEC.md:195 (IA-REQ-038) requires that "contract tests reject drift across runtime, OpenAPI, and React".
  - A search of src/Web/ClientApp/src for openapi, v1.json and x-problem finds nothing. OpenApiContractTests.cs:245-251 compares OpenAPI against hand-typed lists and never touches the client.
  - PersonalPages.test.jsx:184 mocks 'personal_context_conflict'. The server sends 'personal_registration_conflict' (IdentityAccessErrors.cs:75). The test only asserts that an alert appears (:193), so it passes on the generic fallback and hides the unmapped code from error-code-catalogue-1.
  - Other fixtures the server never produces: - 'invalid_cursor' (IdentityAccessReviewRevalidation.test.jsx:200); - 'context_unreadable' as a server 503 (ContextCreationRefresh.test.jsx:20); - platform_last_owner as 400 (PlatformIdentitiesPage.test.jsx:198), when the server sends 409 (ApiProblemMetadata.cs:82); - identity_reactivation_unavailable as 400 (PlatformIdentitiesPage.test.jsx:220), when the server sends 403 (ApiProblemMetadata.cs:101).
- **Recommendation:** Export MESSAGES and add a Vitest contract test that reads src/Web/wwwroot/openapi/v1.json. It should assert: 1. every x-problem-codes value has a MESSAGES entry; 2. every MESSAGES key is declared somewhere in OpenAPI; 3. the MSW problem(status, code) helper only accepts (status, code) pairs that OpenAPI declares. Then fix the fixtures listed above.
- **Verifier:** Confirmed. SPEC.md:195 requires contract tests that reject drift across runtime, OpenAPI and React. - A grep of src/Web/ClientApp/src for openapi, v1.json and x-problem finds nothing. No test in tests/ references ProblemMessage or MESSAGES either. - OpenApiContractTests.cs:245-251 compares OpenAPI against hand-typed arrays only. The fixtures are wrong as described: - PersonalPages.test.jsx:184 mocks personal_context_conflict and only asserts that an alert is visible (:193). - invalid_cursor (IdentityAccessReviewRevalidation.test.jsx:200) and context_unreadable (ContextCreationRefresh.test.jsx:20) appear in no C# source. - platform_last_owner is mocked as 400 (PlatformIdentitiesPage.test.jsx:198), but the contract is 409 (ApiProblemMetadata.cs:82). - identity_reactivation_unavailable is mocked as 400 (PlatformIdentitiesPage.test.jsx:220), but the contract is 403 (ApiProblemMetadata.cs:101).

<a id="error-code-catalogue-missed-1"></a>

### error-code-catalogue-missed-1 — Replaying a conflicted personal confirmation link answers 204 success after the first click answered 409

- **Layer:** error-code-catalogue
- **User impact:** First click: the user sees the generic fallback, because personal_registration_conflict is not mapped (error-code-catalogue-1). Second click on the same link: "Your address is confirmed. Sign in to continue." In the address-already-has-an-identity case (ConfirmEmailHandler.cs:236-239) and the unreadable-document case (:225-230), nothing was created and the submitted password was never applied. The sign-in the page asks for will fail with the password they chose.
- **Evidence:**
  - ConfirmEmailHandler.cs:47-51: a Consumed envelope returns ResultFor(recorded) only when SettledIntentAsync finds an intent, and Result.Success() otherwise.
  - ConfirmEmailHandler.cs:360-367: FindIntentAsync returns null for every message type except RegisterOrganizationCommandHandler.IntentConfirmationMessageType. A spent personal envelope therefore always answers Result.Success().
  - ConfirmEmailHandler.cs:282-288: SettlePersonalConflictAsync consumes the secret and returns 409 personal_registration_conflict. PersonalResultFor (:291-295) is reached only through :216, where the secret is not yet consumed. After a conflict that never happens.
  - ConfirmEmailHandler.cs:44-46: the handler's own comment warns that answering success on a spent envelope would tell a person their registration succeeded every time they clicked. It guards only organization intents.
  - PersonalJourneyTests.cs:71-72 asserts that a replay "answers what it settled as", but only for the Created case. No test replays a conflicted personal confirmation.
  - ConfirmEmailPage.jsx:51-53 renders any success as "Your address is confirmed. Sign in to continue."
- **Recommendation:** Look up the personal intent for a consumed envelope as well, for example by generalizing FindIntentAsync to PendingPersonalIntents. Return PersonalResultFor(outcome) so that every later spend answers the recorded outcome. Add a functional test that replays a conflicted personal confirmation and expects 409 personal_registration_conflict.
- **Verifier:** found by verifier

<a id="error-code-catalogue-missed-2"></a>

### error-code-catalogue-missed-2 — CUIT check digit is never validated, so a mistyped CUIT gets no error at all

- **Layer:** error-code-catalogue
- **User impact:** The product owner named this case explicitly. A registrant who transposes one CUIT digit is never told, and the organization is created under an invalid CUIT that becomes an exclusive claim. If the typo happens to match another organization's CUIT, the person gets registration_conflict at confirmation instead of a field error at submit time. The check depends on the input alone, so validating it is not enumeration-sensitive.
- **Evidence:**
  - NormalizedCuit.cs:9-38: From() rejects only empty input, characters other than digits, hyphens and whitespace, and a digit count other than 11. There is no check-digit (mod-11) verification.
  - A grep of src for check-digit, verificador or mod-11 logic finds nothing outside node_modules.
  - RegisterOrganizationPage.jsx:55-63: the CUIT field has no client-side check.
  - RegisterOrganizationHandler.cs:48 and :61-76: a CUIT with a wrong check digit passes TryNormalize, and an anonymous request gets the neutral 202.
  - ConfirmEmailHandler.cs:165-179: the organization is then created and claims that CUIT.
- **Recommendation:** Validate the CUIT check digit on input, in the registration handlers or a shared input check that runs before any lookup. Answer invalid_registration with errors.cuit, as recommended in error-code-catalogue-3, and add the same check to the client form.
- **Verifier:** found by verifier

<a id="frontend-presentation-3"></a>

### frontend-presentation-3 — The neutral registration outcome leaves an existing account holder (the product owner's case) with no next step

- **Layer:** frontend-presentation
- **User impact:** The owner saw a success message and nothing on screen said what to do next. The email's path (sign in, then register from your account) is not offered in the UI, and once signed in they must retype their own email and a password the server ignores. The request was specified to be neutral; the product owner read it as no validation at all.
- **Evidence:**
  - src/Web/ClientApp/src/features/identity/register/RegisterOrganizationPage.jsx:37-41 (the only outcome text; the whole file, lines 1-95, has no sign-in link or any other link)
  - src/Infrastructure/Outbox/RegistrationIntentDeliveryHandlers.cs:57 (the existing owner's email says: 'Your account already exists; sign in at {link} and register from there.')
  - src/Web/ClientApp/src/components/NavMenu.jsx:111-116 (a signed-in user has no navigation entry to /organizations/register); src/Web/ClientApp/src/features/identity/tenants/TenantSelector.jsx:50-57 (the empty state has no link)
  - RegisterOrganizationPage.jsx:66-87 (a signed-in user must still type their email and a required password); RegisterOrganizationHandler.cs:50-51, 56 (for a session, the email must match and the password is not even validated); src/Web/ClientApp/src/components/Layout.jsx:19 (rendered as a public entrance with no shell)
- **Recommendation:** Keep the response neutral, but add guidance that applies to everyone: 'If you already have an account, we emailed you instead — sign in to add the organization to it', with a Sign in link carrying returnUrl=/organizations/register, plus a 'didn't get an email?' hint. For a signed-in user, show a variant without email and password fields and add a navigation entry. None of this reveals whether the account exists.
- **Verifier:** Confirmed. RegisterOrganizationPage.jsx:1-95 imports no Link and renders only the neutral Alert (:37-41). Compare PersonalPages.jsx:129-131 and PasswordPages.jsx:80-83, which do offer a way on. RegistrationIntentDeliveryHandlers.cs:57 tells the existing owner to 'sign in ... and register from there'. That path has no UI: NavMenu.jsx:111-116 has no /organizations/register entry, and TenantSelector.jsx:50-57 is an empty state whose own comment admits it is a dead end. Layout.jsx:19 renders the route as a shell-less public card. For a signed-in caller, RegisterOrganizationHandler.cs:50-51 requires the typed email to match the session's. Line 181 rejects a blank password, while line 56 skips the policy check when a session exists, so the user must type a password that is then ignored. Static guidance shown to everyone would reveal nothing, so this is a presentation gap, not required neutrality.

<a id="frontend-presentation-4"></a>

### frontend-presentation-4 — Server field errors never reach the field: they appear as a flat 'rawKey: message' list in the top Alert

- **Layer:** frontend-presentation
- **User impact:** A reset or change-password refusal shows 'newPassword: Passwords must have at least one digit ('0'-'9').' in a banner instead of under 'New password'. The step-up screen can show 'Code: …'. Keys don't match the visible labels, fields are never marked invalid (no aria-invalid), and screen-reader users cannot move from a field to its error.
- **Evidence:**
  - src/Web/ClientApp/src/features/identity/ProblemMessage.jsx:62-72 (renders `{field}: {messages}` for every entry of problem.errors)
  - A grep for helperText|error={ across *.jsx finds only the static rule hints at PlatformRetentionPage.jsx:400,412; no TextField binds problem.errors
  - src/Infrastructure/IdentityAccess/IdentityCredentialService.cs:42 (key 'newPassword' with ASP.NET Identity descriptions); src/Application/IdentityAccess/Credentials/ChangePassword/ChangePasswordHandler.cs:28-29
  - src/Application/DependencyInjection.cs:11 (FluentValidation with no property-name resolver); ConfirmPlatformInvitee.cs:26, RegisterPlatformInvitee.cs:24, PlatformMfaCommands.cs:43,90,104 (keys ConfirmationToken, Token, Code, RecoveryCode with the library's default messages)
- **Recommendation:** Build one standard: a helper that maps server keys case-insensitively to field ids and sets error, helperText and aria-invalid/aria-describedby on each TextField. Keep the summary Alert only for unmapped keys, and link each item to its field. On the server, switch FluentValidation to camelCase property names and user-facing messages.
- **Verifier:** Confirmed. ProblemMessage.jsx:69-73 renders `{field}: {messages.join(' ')}` inside the one top Alert. A grep for helperText, error={ or aria-invalid finds only the static hints at PlatformRetentionPage.jsx:400 and :412. IdentityCredentialService.cs:42 keys the errors 'newPassword' and fills them with ASP.NET Identity's Description text, and ChangePasswordHandler.cs:28-29 and :44 plus PasswordRecoveryHandlers.cs:149 pass them through PasswordPolicyFailed. The FluentValidation validators (RegisterPlatformInvitee.cs:24, ConfirmPlatformInvitee.cs:26, PlatformMfaCommands.cs:30/43/90/104) use no WithName or WithMessage and there is no property-name resolver (DependencyInjection.cs:11). ValidationException.cs:16-18 groups by the PascalCase PropertyName, so keys such as 'Token' and 'ConfirmationToken' arrive with the library's default English messages. ResetPasswordPage and ChangePasswordPage both show these through ProblemMessage.

<a id="frontend-presentation-5"></a>

### frontend-presentation-5 — Several error codes the API sends to these screens are missing from the catalogue and show generic text

- **Layer:** frontend-presentation
- **User impact:** Someone following an expired or used reset link sees 'That request could not be completed.', with no hint to request a new link. An unconfirmed user is not told to confirm their email. A profile conflict does not say to refresh.
- **Evidence:**
  - src/Web/ClientApp/src/features/identity/ProblemMessage.jsx:9-57 (MESSAGES has no invalid_credential_token, email_confirmation_required, personal_registration_conflict, personal_profile_concurrency_conflict, profile_field_not_editable, session_not_found or invalid_request); :65 fallback 'That request could not be completed.'
  - src/Application/IdentityAccess/Credentials/PasswordRecovery/PasswordRecoveryHandlers.cs:109,112,124,146 (invalid_credential_token -> ResetPasswordPage)
  - src/Application/IdentityAccess/Credentials/Reauthenticate/ReauthenticateHandler.cs:29; src/Application/IdentityAccess/ExternalLogins/ExternalLoginHandlers.cs:113 (email_confirmation_required -> every proof-gated screen and Google sign-in)
  - src/Application/IdentityAccess/People/CreatePersonalContext/CreatePersonalContextHandler.cs:51-52; src/Application/IdentityAccess/Organizations/ConfirmEmail/ConfirmEmailHandler.cs:288,294 (personal_registration_conflict -> AddPersonalContext, ConfirmEmailPage)
  - src/Application/IdentityAccess/People/Profile/PersonalProfileHandlers.cs:54-79; src/Application/IdentityAccess/Sessions/ManageSessions/SessionManagementHandlers.cs:66,83,87; src/Web/Infrastructure/ProblemDetailsExceptionHandler.cs:44
  - A grep of *.test.* for ProblemMessage|catalogue|MESSAGES finds no contract test tying the catalogue to server codes
- **Recommendation:** Add wording with a next step for each code (for example, reset -> a link to /credentials/forgot; email_confirmation_required -> how to confirm). Add a contract test comparing the MESSAGES keys with the codes declared in ApiProblemMetadata/OpenAPI, so a new code cannot ship without wording.
- **Verifier:** Confirmed. MESSAGES (ProblemMessage.jsx:9-57) has no entry for invalid_credential_token, email_confirmation_required, personal_registration_conflict, personal_profile_concurrency_conflict, profile_field_not_editable, session_not_found or invalid_request; each falls back to line 65. The server emits every one of them: PasswordRecoveryHandlers.cs:109/112/124/146, ReauthenticateHandler.cs:29, ExternalLoginHandlers.cs:113, CreatePersonalContextHandler.cs:51-52, ConfirmEmailHandler.cs:288/294, PersonalProfileHandlers.cs:54-79, SessionManagementHandlers.cs:66/83/87 and ProblemDetailsExceptionHandler.cs:44. personal_registration_conflict also reaches PersonalRegisterPage on a replay (RegisterPersonalHandler.cs:120). ResetPasswordPage (PasswordPages.jsx:97-151) has no link to request a new reset, so a dead link is a dead end. The catalogue comment at ProblemMessage.jsx:49-50 says it matches the codes the API can answer with, and it does not. email_confirmation_required is harder to reach than the auditor implies, because a session already implies an active identity. The other codes (expired reset link, ended device, concurrent profile edit) are ordinary events.

<a id="frontend-presentation-6"></a>

### frontend-presentation-6 — A failed first load leaves a permanent loading skeleton next to the error, with no retry

- **Layer:** frontend-presentation
- **User impact:** A 403, a 500 or a network failure on first load shows a red Alert beside a 'Loading…' region that never finishes. The page contradicts itself, and a screen reader announces both. There is no Try again button, only a page reload.
- **Evidence:**
  - src/Web/ClientApp/src/features/identity/roles/RolesPage.jsx:91-105 (the catch leaves roles null) and :337-341 (roles === null renders role="status" 'Loading…' plus skeletons)
  - src/Web/ClientApp/src/features/identity/members/MembersPage.jsx:98-116 and :280-283
  - src/Web/ClientApp/src/features/identity/sessions/SessionsPage.jsx:58-67 and :177-180
  - src/Web/ClientApp/src/features/identity/credentials/ExternalAccountsPage.jsx:59-74 and :150-157
  - Screens that get this right: PersonalPages.jsx:393 and PlatformIdentitiesPage.jsx:352-373
- **Recommendation:** Use one read-state hook for the identity screens (usePlatformRead already provides the status values). Show skeletons only while loading, and offer Try again for retryable failures.
- **Verifier:** Confirmed on all four screens. RolesPage.jsx:102-104's catch never sets roles, so :337-341 keeps role="status" 'Loading…' and skeletons up; `catalog` also stays undefined, so the editor's skeleton at :396-399 spins forever too. The same pattern appears in MembersPage.jsx:113-115 with :280-283, SessionsPage.jsx:64-66 with :177-180, and ExternalAccountsPage.jsx:71-73 with :150-157. None offers a retry. The contrast holds: PersonalPages.jsx:393 hides the skeleton after a failure, and PlatformIdentitiesPage.jsx:352-372 separates loading, refused and errored.

<a id="frontend-presentation-7"></a>

### frontend-presentation-7 — Unexpected failures look different from screen to screen, and a real 500 on Platform reads offers no retry

- **Layer:** frontend-presentation
- **User impact:** The same network drop reads 'Something went wrong. Try again.' on one screen and 'That request could not be completed.' on the next. On Platform reads, a real 500 or 503 says 'Try again' but offers no button. Support has no reference ID to match a user's report to the logs.
- **Evidence:**
  - src/Web/ClientApp/src/features/identity/useSubmit.js:21-22 (becomes internal_server_error, shown as 'Something went wrong. Try again.')
  - Hand-written catches use { code: 'unexpected' }, which is uncatalogued: SessionsPage.jsx:65,89; MembersPage.jsx:114,137,186,206; RolesPage.jsx:103,128,183,202; ExternalAccountsPage.jsx:72,90,272; InviteMemberPage.jsx:133,161; PasswordPages.jsx:179; AccountLifecyclePages.jsx:63; PersonalPages.jsx:337; LoginPage.jsx:85. IdentityProvider.jsx:49 uses 'context_unreadable', also uncatalogued
  - src/Web/ClientApp/src/features/platform/shared/usePlatformRead.js:28-32 (any problem document counts as 'refused'); src/Web/Infrastructure/ApiProblemDetailsMapper.cs:50-68 (the server writes a 500 as problem+json) -> PlatformIdentitiesPage.jsx:364-371 and PlatformRetentionPage.jsx:353-369 show no Try again; PlatformPanel.jsx:197-198 never offers one
  - src/Web/ClientApp/src/features/identity/api/problemDetails.js:42 (traceId is parsed) vs ProblemMessage.jsx:59-76 (never shown)
- **Recommendation:** Write one function that turns any failure into a problem, and use it in useSubmit and in every hand-written catch; retire 'unexpected' and 'context_unreadable' or give them wording. Decide whether a retry is offered from the status and code (5xx, 503, 429), not from whether the response carried a problem document. For 5xx, show the traceId as a support reference.
- **Verifier:** Confirmed. useSubmit.js:21-22 turns non-problem failures into internal_server_error. The hand-written catches (SessionsPage.jsx:65/89, RolesPage.jsx:103/128/183/202, MembersPage.jsx:114/137/186/206, ExternalAccountsPage.jsx:72/90/272, InviteMemberPage.jsx:133/161, PasswordPages.jsx:179, AccountLifecyclePages.jsx:63, PersonalPages.jsx:337, LoginPage.jsx:85) use the uncatalogued 'unexpected'; IdentityProvider.jsx:49 uses 'context_unreadable'. usePlatformRead.js:32 marks any problem document as 'refused'. ApiProblemDetailsMapper.cs:50-68 writes a 500 as problem+json, so it counts as refused, and PlatformIdentitiesPage.jsx:364-371 and PlatformRetentionPage.jsx:353-369 then show no Try again. PlatformPanel.jsx:197-198 never offers one. problemDetails.js:42 parses traceId, but ProblemMessage never renders it. The wording difference alone would be cosmetic; medium stands because the retry rule keys on the wrong discriminator and support has no reference ID.

<a id="frontend-presentation-8"></a>

### frontend-presentation-8 — Errors appear far from the control that caused them, and focus never moves to them

- **Layer:** frontend-presentation
- **User impact:** A sighted user who acts at the bottom of a long list or table sees nothing change. The error is off-screen at the top of the page. It is announced (role=alert) but never brought into view.
- **Evidence:**
  - A grep for .focus(|autoFocus|scrollIntoView across src finds nothing
  - src/Web/ClientApp/src/features/identity/roles/RolesPage.jsx:309 (Alert at the top of the page) vs :370-430 (the Create/Save role form sits below the table)
  - src/Web/ClientApp/src/features/identity/members/MembersPage.jsx:254 (Alert at the top) vs :330-424 (row actions, ownership transfer and the inline role editor)
  - src/Web/ClientApp/src/features/identity/invitations/InviteMemberPage.jsx:207 vs :339-375 (Resend/Withdraw in the rows)
  - src/Web/ClientApp/src/features/platform/PlatformPanel.jsx:179 vs :229-241 and :256-282 (row actions and their confirmations)
- **Recommendation:** Set a standard: show the problem in the region of the action (the row, editor or confirmation), or focus the Alert (tabIndex=-1) and scroll it into view when it appears. Keep role=alert.
- **Verifier:** Confirmed. A grep for .focus(, autoFocus or scrollIntoView under ClientApp/src finds nothing. The single problem Alert sits far from the controls it reports on: RolesPage.jsx:309 against the editor at :370-430; MembersPage.jsx:254 against row actions and the inline editor at :330-424; InviteMemberPage.jsx:207 (above the form) against the Resend/Withdraw buttons in the table at :339-375; PlatformPanel.jsx:179 against the row actions and confirmations at :229-241 and :256-282. MUI Alert defaults to role='alert' (node_modules/@mui/material/Alert/Alert.js:167), so the error is announced but not brought into view.

<a id="frontend-presentation-9"></a>

### frontend-presentation-9 — Some failures produce no UI at all, and there is no error boundary

- **Layer:** frontend-presentation
- **User impact:** Switching organization from the app bar can fail (409 session_concurrency_conflict, 401, network) with no sign. The user carries on in the old organization believing they switched. Any render-time exception blanks the whole app with no message.
- **Evidence:**
  - src/Web/ClientApp/src/components/NavMenu.jsx:165-168 (ContextSwitcher closes the menu, then awaits identity.selectTenant with no catch and no busy state)
  - src/Web/ClientApp/src/components/NavMenu.jsx:88-92 with IdentityProvider.jsx:75-82 (a sign-out failure is re-thrown and navigate('/login') is skipped)
  - src/Web/ClientApp/src/features/platform/invitations/PlatformInvitationPages.jsx:344-353 (after acknowledging, the page shows success, then runs identity.reload/selectTenant with no catch)
  - A grep for ErrorBoundary|componentDidCatch|errorElement finds nothing; src/Web/ClientApp/src/App.jsx:9-24
- **Recommendation:** Route ContextSwitcher through useSubmit and show the problem (Snackbar or Alert) and a busy state. Catch and show the failures after acknowledgement. Add an app-level ErrorBoundary, and one per route, showing 'Something went wrong — reload' with a support reference.
- **Verifier:** Confirmed with two corrections. NavMenu.jsx:165-168: choose() awaits identity.selectTenant with no catch and no busy state, and MenuItem's onClick drops the promise, so the rejection goes unhandled. The userImpact overstates slightly: the trigger label (NavMenu.jsx:185) is drawn from the unchanged context and still shows the old organization. Sign-out (NavMenu.jsx:88-92 with IdentityProvider.jsx:75-82) rethrows as described. PlatformInvitationPages.jsx:344-353 shows 'done' and then calls selectTenant with no catch. Contrary to the auditor, identity.reload() cannot throw (loadContext catches and returns null); only selectTenant can. A grep for ErrorBoundary, componentDidCatch or errorElement finds nothing, and App.jsx:9-24 has no boundary, so a render-time exception blanks the whole app.

<a id="cross-cutting-runtime-2"></a>

### cross-cutting-runtime-2 — On .NET 10, unexpected 500s go unlogged outside MediatR, while expected 400/401/403 refusals are logged at Error

- **Layer:** cross-cutting-runtime
- **User impact:** When a user reports 'Something went wrong', the traceId behind it may match no log line at all (a fault outside MediatR), or only an exception type name with no stack (inside MediatR). Meanwhile every ordinary validation 400 and permission 403 writes an Error line, so alerts on Error are flooded and real faults are harder to spot. The request-duration metric also loses error.type for these faults.
- **Evidence:**
  - Directory.Build.props:6: <TargetFramework>net10.0</TargetFramework>. The installed runtime is 10.0.11 (dotnet --list-runtimes).
  - src/Web/Infrastructure/ProblemDetailsExceptionHandler.cs:8 injects only IProblemDetailsService, no ILogger. :30-34 sends unknown exceptions to WriteUnexpectedAsync and returns true; :36-37 returns true for everything else.
  - src/Web/Program.cs:47 calls app.UseExceptionHandler(options => { }) with no SuppressDiagnosticsCallback; src/Web/DependencyInjection.cs:24 registers AddExceptionHandler<ProblemDetailsExceptionHandler>().
  - C:/Program Files/dotnet/packs/Microsoft.AspNetCore.App.Ref/10.0.11/ref/net10.0/Microsoft.AspNetCore.Diagnostics.xml:153-155 says: when SuppressDiagnosticsCallback is null, 'the default behavior is to suppress diagnostics if the exception was handled by an IExceptionHandler'. :164-173 lists what is suppressed: the UnhandledException log, the HandledException EventSource event, and the error.type tag on http.server.request.duration.
  - src/Application/Common/Behaviours/UnhandledExceptionBehaviour.cs:18-23 logs only RequestName, ex.GetType().Name and CorrelationId. It does not pass the exception, so there is no stack trace, and it runs only for MediatR requests.
  - src/Application/DependencyInjection.cs:16-18 registers UnhandledExceptionBehaviour ahead of AuthorizationBehaviour and ValidationBehaviour, which makes it outermost. So ValidationBehaviour.cs:29 (throw new ValidationException) and AuthorizationBehaviour.cs:57, 65, 77 and 83 (UnauthorizedAccessException, ForbiddenAccessException) are each logged at Error as 'Unhandled exception' before becoming 400/401/403.
  - Code outside a MediatR handler, for example SessionCookieEvents.cs:34-43 (database reads during cookie validation, which runs in UseAuthentication after the exception handler), reaches ProblemDetailsExceptionHandler and produces no application log line.
  - tests/Application.FunctionalTests/IdentityAccess/Api/ProblemDetailsContractTests.cs:237-250 checks only the sanitized 500 body. No test checks that the fault is recorded anywhere.
- **Recommendation:** Log the unexpected branch in ProblemDetailsExceptionHandler at Error, with the traceId, the exception type and the stack trace. The redaction policy is to be decided (see open questions). Alternatively, set ExceptionHandlerOptions.SuppressDiagnosticsCallback to return true only for the expected types (ValidationException, NotFoundException, ForbiddenAccessException, UnauthorizedAccessException, BadHttpRequestException, JsonException), so every other exception is recorded. Stop UnhandledExceptionBehaviour from logging those expected types at Error: rethrow them silently, or log them at Information. Add a functional test that uses a capturing logger around TestApp.ForceUnexpectedFailure().
- **Verifier:** The core mechanism is confirmed: - The .NET 10 ref XML (Microsoft.AspNetCore.Diagnostics.xml:150-173) says a null SuppressDiagnosticsCallback suppresses the middleware's UnhandledException log, the EventSource event and error.type whenever an IExceptionHandler handled the exception. - ProblemDetailsExceptionHandler.cs:8 has no logger and always returns true (:33, :37). - Program.cs:47 sets no callback. - UnhandledExceptionBehaviour.cs:21-22 logs only the type name and CorrelationId, and never passes `ex`, so there is no stack trace anywhere. - Application DependencyInjection.cs:16-18 makes it the outermost behaviour, so AuthorizationBehaviour.cs:57/65/77/83 refusals are logged at Error. - No test asserts that a fault is recorded. TestLogCaptureProvider (WebApiFactory.cs:140-186) is used only for absence (hygiene) checks, and ProblemDetailsContractTests.cs:237-254 checks only the body. Downgraded to medium because two claims are overstated: (a) Only 8 FluentValidation validators exist, in 4 Platform files. Most 400s are Result-based (e.g. IdentityAccessErrors.cs:143) and never throw, so 'every ordinary validation 400 writes an Error line' is wrong. The Error-level noise is mainly 401/403 from AuthorizationBehaviour plus those few validators. (b) Endpoints overwhelmingly go through MediatR: SafeTelemetryTests.cs:38/55/71 witness CreateSessionCommand, ListOwnSessionsQuery and CreatePersonalContextCommand in the logs. Those faults get a type-plus-correlation line. The fully unlogged case is limited to non-MediatR code such as SessionCookieEvents.cs:34-43. The suppression covers only the exception-handler middleware's own diagnostics (XML:164-173). I did not verify what other framework loggers emit at runtime.

<a id="cross-cutting-runtime-3"></a>

### cross-cutting-runtime-3 — There is no global handling of session expiry: after a 401 invalid_session the signed-in shell stays and every screen fails one by one

- **Layer:** cross-cutting-runtime
- **User impact:** After 30 minutes idle, the user keeps the full navigation, and every action fails with 'Your session is no longer valid. Sign in again.' Nothing takes them to sign in or keeps their place, so they must reload or find Log out, which then also fails (finding 4).
- **Evidence:**
  - apiTransport.js:49-54: every non-ok problem becomes an ApiProblem, whatever the status. Nothing lets the transport tell anyone about a lost session.
  - IdentityProvider.jsx:29-56 and 58-67: the context is loaded only on mount, sign-in, sign-out or an explicit reload(). Nothing reacts to invalid_session coming back from other calls.
  - ProtectedRoute.jsx:13-19 decides on identity.isAuthenticated (context !== null), which stays true after the server has rejected the session.
  - SessionCookieEvents.cs:180-187: the server deletes the cookie when it rejects the session, so the server has already signed the user out while NavMenu.jsx:231 and 246-298 keep drawing the signed-in navigation.
  - ProblemMessage.jsx:13: 'Your session is no longer valid. Sign in again.' appears inline, with no link and no redirect.
  - SPEC.md:142-143 (IA-REQ-023/024): 30-minute idle lifetime; an expired or revoked session answers 401. SPEC.md:307: the client 'always handles 401'.
  - The only reload() calls are specific to one flow each: usePlatformStepUp.js:25, PlatformPanel.jsx:158, InvitationPages.jsx:98, ExternalAccountsPage.jsx:262, PersonalPages.jsx:365. None of them runs because of a 401.
- **Recommendation:** Give createApiTransport an onSessionLost callback. When a problem arrives with status 401 and code invalid_session or authentication_required, IdentityProvider clears the context and records the reason, so ProtectedRoute redirects to /login?returnUrl=... and LoginPage says the session expired. Do not trigger on every 401: recent_proof_required (SPEC.md:253), recent_mfa_required (SPEC.md:270) and credential_superseded (SPEC.md:249) are also 401s and do not mean the session is gone.
- **Verifier:** Confirmed: - apiTransport.js:49-54 turns every problem into an ApiProblem, with no hook for a lost session. - IdentityProvider.jsx:29-67 loads the context only on mount or an explicit reload. - ProtectedRoute.jsx:13-17 keys off isAuthenticated (context !== null). - SessionCookieEvents.cs:180-187 deletes the cookie on rejection. - ApiAuthorizationMiddlewareResultHandler.cs:16-21 answers invalid_session only when InvalidSessionKey was set. - ProblemMessage.jsx:13 is inline text with no link. - SPEC.md:143 and :307 require the 401 and say the client 'always handles 401'. - The reload() call sites match the auditor's list exactly (grep): NavMenu is not among them, and none is triggered by a 401. One nuance makes it slightly worse. After the first rejection the cookie is gone, so later calls answer authentication_required, and IdentityProvider.jsx:52 deliberately drops that code. The 'session no longer valid' explanation therefore appears at most once, inline, on whichever screen happened to make the call. The recommendation's exclusions are correct: credential_superseded (SPEC.md:249), recent_proof_required (:253) and recent_mfa_required (:270) are 401s that do not mean the session is lost.

<a id="cross-cutting-runtime-4"></a>

### cross-cutting-runtime-4 — Three event handlers await API calls with no catch, so failures are silent unhandled rejections, and a failed sign-out looks like a successful one

- **Layer:** cross-cutting-runtime
- **User impact:** If the sign-out request fails (a 409 or a network drop), the bar switches to the visitor view as if the user signed out, but the server session and its cookie can still be live. On a shared computer, the next person can reload into the account. If a tenant switch is refused, the menu closes and nothing happens, with no message.
- **Evidence:**
  - NavMenu.jsx:88-92 (the same code is at HEAD): handleSignOut does `await identity.signOut(); navigate('/login');` with no try/catch.
  - IdentityProvider.jsx:75-82: signOut runs `try { await identityClient.signOut(); } finally { setContext(null); ... }`. The context is cleared even when the DELETE failed, then the error is rethrown into handleSignOut, so navigate('/login') never runs and the rejection is unhandled.
  - SPEC.md:250: DELETE /api/identity/sessions/current may answer 409 session_concurrency_conflict ('a lost update that never settles'). A network failure also rejects.
  - NavMenu.jsx:165-168 and 190 (the same code is at HEAD, 166-168): ContextSwitcher.choose awaits identity.selectTenant(tenantId) in a MenuItem onClick with no catch and no problem state. SPEC.md:259 says PUT /api/identity/context/tenant may answer 409 session_concurrency_conflict. TenantSelector.jsx:32 and 48 wraps the same call in useSubmit plus ProblemMessage, so the page and the menu behave differently.
  - PlatformInvitationPages.jsx:343-353: inside the onClick, after acknowledgement, `if (platformTenant) await identity.selectTenant(platformTenant.id);` has no catch.
  - There is no global 'unhandledrejection' listener (see the grep in finding 1). App.test.jsx:93-99 tests only a successful log-out.
- **Recommendation:** Send these calls through useSubmit, or wrap them in try/catch, and show a ProblemMessage. In signOut, clear the context only after a confirmed 204 or a 401 invalid_session. On any other failure, keep the context and show the problem with a retry. Make ContextSwitcher reuse TenantSelector's useSubmit path. Add a window unhandledrejection listener as a backstop, and tests for sign-out and tenant-switch failures.
- **Verifier:** Confirmed. NavMenu.jsx:88-92 awaits signOut with no catch. IdentityProvider.jsx:75-82 clears the context in `finally` and then rethrows, so navigate('/login') never runs and the rejection goes unhandled. Because the context is null, ProtectedRoute redirects to /login anyway. The person lands on the sign-in page believing they are out. A network drop or the 409 session_concurrency_conflict (SPEC.md:250) can leave the server session live, and a reload restores it. Other evidence: - ContextSwitcher (NavMenu.jsx:165-168, :190) has no catch and no problem state, while TenantSelector.jsx:32 and :48 wraps the same call in useSubmit and ProblemMessage. - In PlatformInvitationPages.jsx:352, selectTenant is uncaught (reload at :350 never throws, because loadContext catches), and stage is already 'done'. - Both sign-out tests mock only a 204: App.test.jsx:81-100 and IdentityProvider.test.jsx:110-126. - There is no unhandledrejection listener (grep). Medium is appropriate: the case needs a failure, but its outcome is a false sense of having signed out.

<a id="cross-cutting-runtime-6"></a>

### cross-cutting-runtime-6 — Failures that are not problem documents (offline, network loss, a failed token bootstrap) collapse into two generic messages, and the bootstrap path throws away the server's problem

- **Layer:** cross-cutting-runtime
- **User impact:** A user who has lost their connection is told the server failed, with different wording from screen to screen. While admission is closed, the first form submission says 'Something went wrong' with no wait time, even though the server sent a code and a Retry-After.
- **Evidence:**
  - apiTransport.js:28-29: bootstrapAntiforgery does `if (!response.ok) throw new Error('Unable to establish the request token.')` and never reads the problem+json. A 503 from RecoveryAdmissionMiddleware.cs:58-67, with code recovery_admission_closed and Retry-After: 60, becomes a plain Error.
  - apiTransport.js:37: a mutation with no token runs that bootstrap first, and IdentityProvider.jsx:62 swallows a failed bootstrap at mount, so the first submission fails with the plain Error. apiTransport.js:53: if the refresh after antiforgery_validation_failed throws, the plain Error replaces the original ApiProblem.
  - apiTransport.js:39-47: a network failure from fetch (TypeError) is not caught or classified.
  - useSubmit.js:21-22 turns anything that is not an ApiProblem into internal_server_error, shown as 'Something went wrong. Try again.' (ProblemMessage.jsx:56). Nineteen hand-written catch blocks use { code: 'unexpected' }, which has no MESSAGES entry, so they fall back to 'That request could not be completed.' (ProblemMessage.jsx:65). Examples: AccountLifecyclePages.jsx:63, SessionsPage.jsx:65 and 89, MembersPage.jsx:114, 137, 186 and 206, RolesPage.jsx:103, 128, 183 and 202, LoginPage.jsx:85. IdentityProvider.jsx:49 uses a third code, context_unreadable.
  - No code checks navigator.onLine or listens for the online and offline events (grep). Only usePlatformRead.js:25-33 separates 'refused' from 'errored'.
- **Recommendation:** Make the transport the single place that classifies failures. Catch fetch TypeError and throw a typed client failure (for example network_unavailable). Parse problem+json in bootstrapAntiforgery the same way send does. Keep the original ApiProblem when the refresh fails. Replace the 19 catch blocks and useSubmit's fallback with one toProblem(error) helper. Give ProblemMessage specific wording for network_unavailable ('You appear to be offline...') and for an unreadable response. Optionally show an offline banner driven by the online and offline events.
- **Verifier:** The core claims are confirmed: - apiTransport.js:29 throws a plain Error and never reads the problem document. - :37 bootstraps lazily. :53 lets a failed refresh replace the original ApiProblem. :39-47 does not classify a fetch TypeError. - IdentityProvider.jsx:62 swallows a failed mount bootstrap. - useSubmit.js:21-22 maps anything that is not an ApiProblem to internal_server_error, which contradicts its own comment at :5-6 ('inventing a code the server never sent would put words in its mouth'). - I counted exactly 19 `error.problem ?? { code: 'unexpected' }` sites, and they fall back to ProblemMessage.jsx:65. - IdentityProvider.jsx:49 uses a third code, context_unreadable. - Nothing reads navigator.onLine or the online/offline events. One supporting example is weak. RecoveryAdmissionMiddleware (Program.cs:27) runs before UseFileServer (:33). In both Closed and Quarantined state (IRecoveryAdmission.cs:61 and :68; RecoveryAdmissionMiddleware.cs:51-52) it also refuses the SPA's own files, so the 'first submission while admission is closed' scenario is reachable only from a tab opened before the restart. The offline and bootstrap parts stand on their own.

<a id="error-path-tests-3"></a>

### error-path-tests-3 — The React side of IA-REQ-038's runtime/OpenAPI/React drift check does not exist: declared codes have no UI message, and SPA fixtures stub codes the API never emits, with assertions that pass on the generic fallback

- **Layer:** error-path-tests
- **User impact:** Users hitting real refusals, such as a personal-context conflict, an expired reset link (invalid_credential_token) or an unconfirmed email, see a vague generic message. Tests written to prove specific refusal messages quietly prove only the fallback, so the gap stays invisible.
- **Evidence:**
  - docs/features/identity-access/SPEC.md:195 - 'contract tests reject drift across runtime, OpenAPI, and React'
  - src/Web/ClientApp/src/features/identity/ProblemMessage.jsx:9-57 has no entry for codes the API declares: personal_registration_conflict (ApiProblemMetadata.cs:34; PersonalEndpoints.cs:34,48), profile_field_not_editable (:40), personal_profile_concurrency_conflict (:38), session_not_found (:42), email_confirmation_required (:53), invalid_credential_token (:55), invalid_request (:10), route_body_id_mismatch (:14). These fall back to 'That request could not be completed.' (ProblemMessage.jsx:65), and no test asserts that fallback text
  - features/identity/people/PersonalPages.test.jsx:181-193 - titled 'says what was refused when a document already belongs to somebody else', but it stubs 409 'personal_context_conflict' (no occurrence in src; the API emits personal_registration_conflict) and asserts only toBeVisible()
  - features/identity/IdentityAccessReviewRevalidation.test.jsx:200 stubs 'invalid_cursor' (no occurrence in src). features/identity/context/ContextCreationRefresh.test.jsx:20,52 stubs a server 503 'context_unreadable', a code the client itself synthesizes (IdentityProvider.jsx:47-49), and asserts only that an alert exists
  - features/platform/identities/PlatformIdentitiesPage.test.jsx:219-220 stubs identity_reactivation_unavailable as 400, but the API maps it to 403 (IdentityAccessErrors.cs:286-287; ApiProblemMetadata.cs:101)
  - Backend x-problem-codes assertions cover only a handful of route groups (OpenApiContractTests.cs:37-243); runtime-vs-document agreement is checked only for the 3 invitation routes (ProblemDetailsContractTests.cs:164-204)
- **Recommendation:** Export the MESSAGES keys and add a contract test that compares them with the served OpenAPI x-problem-codes union (e.g. a backend test writes the code list to a checked-in JSON that a vitest test reads, or the reverse). Fail on any declared code without a message and on any fixture code that is not declared. Correct the fixture codes and statuses, make those tests assert the message text, and add one test that pins the fallback wording.
- **Verifier:** Holds. SPEC:195 requires contract tests that reject drift across runtime, OpenAPI and React, and no test reads ProblemMessage or MESSAGES. ProblemMessage.jsx:9-57 has no entry for personal_registration_conflict, profile_field_not_editable, personal_profile_concurrency_conflict, session_not_found, email_confirmation_required, invalid_credential_token, invalid_request or route_body_id_mismatch. All of these fall back to line 65. PersonalPages.test.jsx:181-193 stubs 409 'personal_context_conflict', which occurs nowhere in src, and asserts only toBeVisible. The backend emits personal_registration_conflict (IdentityAccessErrors.cs:74-75), proven only at Application level (PersonalJourneyTests.cs:114). PlatformIdentitiesPage.test.jsx:219-220 stubs 400 where the API declares 403 (ApiProblemMetadata.cs:101). An extra example the auditor missed: ExternalAccountsPage.test.jsx:210 asserts /could not be completed/i, which matches both the invalid_external_login text (ProblemMessage.jsx:28) and the fallback (:65), so it passes even if that mapping is deleted. Two cited items are weaker than presented. The invalid_cursor stub (IdentityAccessReviewRevalidation.test.jsx:200) is a defensive MSW guard that is never meant to be hit. The context_unreadable 503 (ContextCreationRefresh.test.jsx:20,52) sits in a test about not replaying a mutation. OpenAPI code assertions cover only registration/confirm, the Platform MFA 429 and directory 401, invitations, context and sessions (OpenApiContractTests.cs:37-243).

<a id="error-path-tests-4"></a>

### error-path-tests-4 — PUT /api/identity/profile validation contradicts the SPEC, and no test covers either code

- **Layer:** error-path-tests
- **User impact:** A person who leaves their name empty is told the field 'cannot be edited', which is false and unhelpful. No test holds the documented field-level contract, so this drift survives.
- **Evidence:**
  - docs/features/identity-access/SPEC.md:673 - requires '400 validation_failed with field-indexed errors' for bad fullName/displayName/version, and profile_field_not_editable only for unmapped members (UnmappedMemberHandling = Disallow)
  - src/Application/IdentityAccess/People/Profile/PersonalProfileHandlers.cs:53-58 - empty or over-length fullName/displayName return ProfileFieldNotEditable('fullName'/'displayName'), whose detail reads 'cannot be edited through this request' (IdentityAccessErrors.cs:85-86); a blank version returns 409 personal_profile_concurrency_conflict
  - src/Web/Endpoints/Identity/PersonalEndpoints.cs:63-74 - the PUT /profile contract does not declare ValidationFailed
  - tests/Application.FunctionalTests/IdentityAccess/People/PersonalJourneyTests.cs:185,194 cover only success and a stale version; grep finds no 'profile_field_not_editable' anywhere in tests/
- **Recommendation:** Make the handler (or a validator) answer validation_failed with errors.fullName / errors.displayName / errors.version as SPEC:673 states, declare ValidationFailed on the route, keep profile_field_not_editable for unknown members only, and add HTTP tests for both codes (or amend the SPEC if the current behaviour is intended).
- **Verifier:** Holds exactly. SPEC:673 requires '400 validation_failed with field-indexed errors, including a version this server never issued', and profile_field_not_editable only for unmapped members. The handler instead returns ProfileFieldNotEditable('fullName'/'displayName') for empty or oversized names, and 409 personal_profile_concurrency_conflict for a blank version (PersonalProfileHandlers.cs:53-58). That detail reads 'cannot be edited through this request' (IdentityAccessErrors.cs:85-86). The PUT /profile contract does not declare ValidationFailed (PersonalEndpoints.cs:63-75). Its body-binding code is profile_field_not_editable, which does match the SPEC for unmapped members. The only tests are PersonalJourneyTests.cs:185 and 194 (success and a stale version). 'profile_field_not_editable' appears nowhere in tests/ or in the SPA tests. The SPA has no message for either code either, so the user sees the generic fallback.

## Low (23)

<a id="backend-model-and-mapping-6"></a>

### backend-model-and-mapping-6 — Framework bad-request errors all become 400, often with a business code, so a malformed payload looks like a business refusal

- **Layer:** backend-model-and-mapping
- **User impact:** Neither the client nor an operator can tell 'your payload was unreadable, the wrong media type or too large' from a business refusal, and the UI cannot say which field had the wrong type. The impact is small today, but it bakes a lossy rule into the standard.
- **Evidence:**
  - src/Web/Infrastructure/ProblemDetailsExceptionHandler.cs:26,40-45: every BadHttpRequestException or JsonException becomes a Validation error (400). The exception's own StatusCode is ignored, and the route's binding code is used for body, query, route, content-type and size failures alike
  - src/Web/DependencyInjection.cs:33-34 sets ThrowOnBadRequest = true, which sends all of these down that path
  - src/Web/Infrastructure/Identity/LoginRateLimitKeyMiddleware.cs:63-69 caps the login body at 16 KB, so an oversized body arrives as the server's 413 BadHttpRequestException and goes out as 400 invalid_request
  - src/Web/Endpoints/Identity.cs:34 with docs/features/identity-access/SPEC.md:243: on organization registration, a malformed JSON body and 'the submitted email does not match your session' both answer 400 invalid_registration
  - Intentional collapses are commented where they matter: src/Web/Endpoints/Identity/AccountLifecycleEndpoints.cs:57-59 and src/Web/Endpoints/Identity/DocumentDisputeEndpoints.cs:32-33
- **Recommendation:** Keep the route-specific binding code only where a comment documents an intentional collapse (reactivation, document dispute). Elsewhere, answer malformed payloads with invalid_request. Keep 413 and 415 as their own statuses with their own codes. Where the payload is not sensitive, put the JsonException path (for example $.cuit) into errors.
- **Verifier:** The core claim holds. ProblemDetailsExceptionHandler.cs:26 and :40-45 map every BadHttpRequestException and JsonException to category Validation (400) with the route's binding code, and ignore the exception's own StatusCode. Two caveats: - I could not verify the 413 example through LoginRateLimitKeyMiddleware.cs:63-69. Minimal-API body reading may treat Kestrel's 413 as an IOException instead of rethrowing it. Content-type failures under ThrowOnBadRequest are the cleaner example. - Part of the conflation is specified. SPEC.md:244 prescribes invalid_registration for malformed input on personal register, and SPEC.md:243 assigns invalid_registration to the session-email mismatch. Side observation: no endpoint in src/Web reads JSON by hand (a grep for ReadFromJsonAsync or DeserializeAsync finds none). The bare JsonException arm can therefore only catch JSON errors raised by server code, and it would misreport them as 400. I found no concrete request-path trigger, so I did not raise it separately.

<a id="backend-model-and-mapping-7"></a>

### backend-model-and-mapping-7 — Some refusals bypass the shared writer: the restore-admission 503 has no traceId and an uncatalogued code, and early middleware has no problem+json safety net

- **Layer:** backend-model-and-mapping
- **User impact:** While a restored deployment is quarantined, a support request cannot be traced by traceId and the UI shows a generic message. A fault in the early middleware returns the server's default 500 with no code.
- **Evidence:**
  - src/Web/Infrastructure/Identity/RecoveryAdmissionMiddleware.cs:58-67 writes its body by hand: code recovery_admission_closed, a non-standard retryAfterSeconds member, and no traceId or instance. SPEC.md:195 (IA-REQ-038) requires an opaque traceId on every non-success
  - recovery_admission_closed appears neither in src/Web/Infrastructure/ApiProblemMetadata.cs nor in src/Web/ClientApp/src (grep: no match), so ProblemMessage.jsx:65 falls back to its generic text
  - src/Web/Program.cs:27,31,33 run RecoveryAdmissionMiddleware, UseIdentitySecurityHeaders and UseFileServer before UseExceptionHandler (Program.cs:47), so an exception thrown there never reaches ProblemDetailsExceptionHandler
- **Recommendation:** Add traceId (from Activity.Current, which needs no scoped services) and instance to the hand-written body, and drop retryAfterSeconds in favour of the Retry-After header it already sends. Add recovery_admission_closed to ApiProblemMetadata and to the client's message map. Move UseExceptionHandler to the top of the pipeline, since it reads no database, or wrap the admission guard in a minimal try/catch that writes the fixed 500 body.
- **Verifier:** Verified. - RecoveryAdmissionMiddleware.cs:58-67 writes its body by hand: no traceId, no instance, and a non-standard retryAfterSeconds member. - Its stated reason (lines 62-63: the mapper 'resolves services from the request scope') is inaccurate. ApiProblemDetailsMapper is a singleton (DependencyInjection.cs:25) and reads the trace id from Activity.Current (ApiProblemDetailsMapper.cs:85-86), so adding a traceId has no real obstacle. - recovery_admission_closed appears only in the middleware, the docs and the tests. It is absent from ApiProblemMetadata.cs and from ClientApp. - The client reader accepts the body, since it needs only a code and reads the Retry-After header (problemDetails.js:34-47). The UI shows the generic 'That request could not be completed.' plus the retry delay. - Program.cs:27,31,33 run before UseExceptionHandler at :47. Confirmed.

<a id="backend-model-and-mapping-8"></a>

### backend-model-and-mapping-8 — The problem writer allocates a new JsonSerializerOptions for every error response

- **Layer:** backend-model-and-mapping
- **User impact:** Each error response rebuilds System.Text.Json serialization metadata (analyzer rule CA1869). That costs most when errors are frequent, such as the login route answering 429 during a password-spraying burst, and the writer also ignores whatever JSON options the app configures.
- **Evidence:**
  - src/Web/Infrastructure/ApiProblemDetailsMapper.cs:43-47 and 64-68 construct new JsonSerializerOptions(JsonSerializerDefaults.Web) on every WriteAsync and WriteUnexpectedAsync call
- **Recommendation:** Use one static readonly JsonSerializerOptions, or a source-generated JsonTypeInfo for ApiProblemDetails, in both methods.
- **Verifier:** The allocation is confirmed at ApiProblemDetailsMapper.cs:46 and :67. The impact is overstated. Since .NET 7, System.Text.Json shares its metadata cache between JsonSerializerOptions instances that are structurally equal, so each call costs an allocation and a cache lookup, not a metadata rebuild. CA1869 is evidently not a build-breaking warning here, even with TreatWarningsAsErrors. This is a code-quality nit, not an error-handling gap. Using fixed options for the wire shape is arguably deliberate.

<a id="backend-validation-coverage-8"></a>

### backend-validation-coverage-8 — Every FluentValidation failure is logged at Error level as an 'Unhandled exception'

- **Layer:** backend-validation-coverage
- **User impact:** Expected 400s caused by user input show up in logs and alerting as server errors. As validation moves into validators (findings 1, 4 and 6), this Error-level noise grows with every typo and buries real faults.
- **Evidence:**
  - C:/Users/ezequ/source/repos/RepositorioBase/src/Application/DependencyInjection.cs:16-18 — UnhandledExceptionBehaviour is registered outside ValidationBehaviour, so it wraps it.
  - C:/Users/ezequ/source/repos/RepositorioBase/src/Application/Common/Behaviours/ValidationBehaviour.cs:28-29 — validation failures are thrown as ValidationException.
  - C:/Users/ezequ/source/repos/RepositorioBase/src/Application/Common/Behaviours/UnhandledExceptionBehaviour.cs:18-23 — catches every exception and logs LogError('... Unhandled exception ...') before rethrowing, with no exclusion for ValidationException.
- **Recommendation:** In UnhandledExceptionBehaviour, rethrow ValidationException and the other exceptions the boundary already maps without Error-level logging, or log them at Information/Debug with only the request name.
- **Verifier:** Verified, and broader than stated. - MediatR runs behaviours in registration order, first one outermost. UnhandledExceptionBehaviour is registered first (Application/DependencyInjection.cs:16-18), so it wraps both AuthorizationBehaviour and ValidationBehaviour. - UnhandledExceptionBehaviour.cs:18-23 logs every exception with LogError('... Unhandled exception ...') and rethrows. It has no exclusions. - So beyond the ValidationException from ValidationBehaviour.cs:28-29, every UnauthorizedAccessException and ForbiddenAccessException thrown by AuthorizationBehaviour.cs:57, 65, 77 and 83 is also logged at Error. That means every pipeline 401 or 403 appears in the logs as a server error. - Only request metadata and the exception type name are logged, so nothing sensitive leaks. The impact is noise only, and low is right.

<a id="error-code-catalogue-6"></a>

### error-code-catalogue-6 — invalid_role_operation and invalid_membership_operation wording states one write-time reason, but the codes also answer reads and other refusals

- **Layer:** error-code-catalogue
- **User impact:** An administrator who switched organization in another tab and then opens Roles or Members sees a statement about granting permissions, although they changed nothing. Other refused role writes are misattributed to the grant ceiling.
- **Evidence:**
  - The read handlers return these codes when the route tenant is not the session's active tenant. RoleScope.Resolve checks this at RoleHandlers.cs:229, and the reads return InvalidRoleOperation at RoleHandlers.cs:23-24, 46-47 and 60-61. The member and invitation list reads return InvalidMembershipOperation at MembershipHandlers.cs:24-25 and 38-39. v1.json declares 400 invalid_role_operation and invalid_membership_operation on these GET routes.
  - RolesPage shows its load failure through ProblemMessage (RolesPage.jsx:91-104, 309), with the text "That role change is not allowed. You can only grant permissions you hold yourself." (ProblemMessage.jsx:32). MembersPage.jsx:114 and :254 show "That membership change is not allowed right now." (ProblemMessage.jsx:33).
  - Writes return the same role code for an inactive organization (RoleHandlers.cs:92-93), a blank version (:128-129) and any store status that is not Applied (:105), not only for the grant ceiling. IdentityAccessErrors.cs:170-175 documents that the code deliberately merges several reasons.
- **Recommendation:** Give reads a scope-mismatch answer that fits them. not_found is allowed by IA-REQ-030, or use invalid_request and reload the context on the client. Reword the two MESSAGES to cover every reason merged into the code, for example "That role change was not accepted.", without naming any one reason.
- **Verifier:** Confirmed. On the server: - The read handlers return InvalidRoleOperation when the scope does not match (RoleHandlers.cs:23-24, 46-47, 60-61, via RoleScope.Resolve at :229). - The member and invitation list reads return InvalidMembershipOperation (MembershipHandlers.cs:24-25, 38-39). - Writes reuse the role code for an inactive organization (:92-93), a blank version (:128-129), any store status that is not Applied (:105, :342-347) and a ceiling violation (:327). On the client: - RolesPage and MembersPage take tenantId from the cached client context (RolesPage.jsx:79; MembersPage.jsx:89). RoleScope compares it with the server-side active tenant. Switching organization in another tab therefore produces the mismatch. - Both pages render it through ProblemMessage (RolesPage.jsx:103, 309; MembersPage.jsx:114, 254) with wording about granting permissions or membership changes.

<a id="error-code-catalogue-8"></a>

### error-code-catalogue-8 — Client-invented codes are inconsistent, so the same non-problem failure shows two different messages

- **Layer:** error-code-catalogue
- **User impact:** A network outage, a proxy error page or contract drift (thrown at apiTransport.js:50 and problemDetails.js:23-36) reads as "Something went wrong" on some screens and "That request could not be completed" on others. Neither says the connection may be the problem.
- **Evidence:**
  - useSubmit's comment says a failure without a problem document is not turned into one: "inventing a code the server never sent would put words in its mouth" (useSubmit.js:4-7). It then sets { code: 'internal_server_error', status: 0 } at :22, as does usePlatformRead.js:28. That shows "Something went wrong. Try again." (ProblemMessage.jsx:56).
  - About ten screens with their own try/catch set { code: 'unexpected' } instead, which shows the generic fallback. Examples: SessionsPage.jsx:65,89; RolesPage.jsx:103,128; PasswordPages.jsx:179; AccountLifecyclePages.jsx:63; MembersPage.jsx:114; InviteMemberPage.jsx:133; ExternalAccountsPage.jsx:72; PersonalPages.jsx:337; LoginPage.jsx:85.
  - IdentityProvider.jsx:47-49 invents 'context_unreadable', which shows the fallback on LoginPage.jsx:100 and InvitationPages.jsx:139.
- **Recommendation:** Pick one client-side code for transport or contract failures, for example 'network_unavailable', and give it a MESSAGES entry such as "We could not reach the service. Check your connection and try again.". Route every screen through useSubmit or one shared helper, and correct the useSubmit comment.
- **Verifier:** Confirmed. - The useSubmit comment (useSubmit.js:4-7) contradicts its own line 22, which invents internal_server_error with status 0. usePlatformRead.js:28 does the same. - 'unexpected' is set at 19 call sites. Examples: SessionsPage.jsx:65,89; RolesPage.jsx:103,128,183,202; MembersPage.jsx:114,137,186,206; PasswordPages.jsx:179; AccountLifecyclePages.jsx:63; InviteMemberPage.jsx:133,161; ExternalAccountsPage.jsx:72,90,272; PersonalPages.jsx:337; LoginPage.jsx:85. - context_unreadable comes from IdentityProvider.jsx:47-49 and is shown at InvitationPages.jsx:139. Note that LoginPage.jsx has uncommitted changes in the main checkout. The ProblemMessage the auditor cites is at :100 in the working tree and :81 at HEAD 497e087.

<a id="error-code-catalogue-9"></a>

### error-code-catalogue-9 — Dead server-side catalogue entries and a misplaced comment

- **Layer:** error-code-catalogue
- **User impact:** No direct user impact. These are stale entries that make the catalogue harder to audit.
- **Evidence:**
  - identity_user_creation_failed and identity_user_deletion_failed (IdentityAccessErrors.cs:7-15) are used only by IdentityService.cs:46 and :83. There are no callers of CreateUserAsync or DeleteUserAsync anywhere in src. Both are Validation category, so they would surface as undeclared 400s if something ever wired them in.
  - route_body_id_mismatch (ApiProblemMetadata.cs:14) is not attached to any endpoint.
  - ProblemMessage.jsx:49-50 says a code is "Declared only on the document-dispute resolve route", but it sits above personal_profile_not_found, which GET/PUT /profile and the dispute route also declare (v1.json).
- **Recommendation:** Remove the two unused IdentityAccessErrors factories (and the unused IdentityService methods if they are template leftovers) and the RouteBodyIdMismatch contract. Move or correct the misplaced comment.
- **Verifier:** Confirmed. - The only src producers of identity_user_creation_failed and identity_user_deletion_failed are IdentityService.cs:46 and :83. Nothing in src calls CreateUserAsync or DeleteUserAsync. - RouteBodyIdMismatch appears only at ApiProblemMetadata.cs:14. - The comment at ProblemMessage.jsx:49-50 is wrong: GET and PUT /profile declare personal_profile_not_found (PersonalEndpoints.cs:60, 72), and PersonalPages.jsx:354 uses it. One caveat on the recommendation: tests use both factories and the interface methods (tests/Application.FunctionalTests/Infrastructure/TestApp.cs:416; tests/Application.UnitTests/Architecture/IdentityGuidContractTests.cs:41-42, 63-64). Removing them means changing those tests too.

<a id="frontend-presentation-10"></a>

### frontend-presentation-10 — Screens handle a missing link token inconsistently; some let the user submit and then show the validator's raw text

- **Layer:** frontend-presentation
- **User impact:** A truncated Platform confirmation link shows 'ConfirmationToken: 'Confirmation Token' must not be empty.' instead of 'Open the link from your email'. An organization-invitation registration without a token gets a success message.
- **Evidence:**
  - Handled well: ConfirmEmailPage.jsx:40-44,64; PasswordPages.jsx:111-115,142; AccountLifecyclePages.jsx:240-244,257
  - Not handled: src/Web/ClientApp/src/features/identity/invitations/InvitationPages.jsx:63,75 (register) and :149-160 (accept); src/Web/ClientApp/src/features/platform/invitations/PlatformInvitationPages.jsx:109,121 (register), :176-185 (confirm), :261-276 (Begin enrollment)
  - The server refuses empty tokens: RegisterPlatformInvitee.cs:24; ConfirmPlatformInvitee.cs:26; PlatformMfaCommands.cs:30 (the answer is shown through the flat list in ProblemMessage.jsx:69-72)
- **Recommendation:** Use one missing-token pattern everywhere: explain that the link is incomplete and disable the action, as ConfirmEmailPage already does.
- **Verifier:** Confirmed. InvitationPages.jsx:63 submits `token ?? ''` with no check. RegisterInvitedUserHandler.cs:60-67 returns Success when the token resolves to nothing, so a tokenless visitor reads 'Check your email ... we have sent you what you need' although nothing is sent. Because the client knows locally that the token is missing, explaining that reveals nothing. PlatformInvitationPages.jsx:109 (register), :176-185 (confirm, disabled={isBusy} only) and :261-276 (Begin enrollment) send '' as well. The NotEmpty validators (RegisterPlatformInvitee.cs:24, ConfirmPlatformInvitee.cs:26, PlatformMfaCommands.cs:30) then produce validation_failed, which ProblemMessage.jsx:69-72 prints as 'ConfirmationToken: ...'. The AcceptInvitationPage part is weak: an empty token there gets invalid_invitation, 'That invitation is not usable.', which is acceptable wording.

<a id="frontend-presentation-11"></a>

### frontend-presentation-11 — The profile form throws away the user's edits when a save is refused

- **Layer:** frontend-presentation
- **User impact:** After a concurrency conflict or a validation refusal, the names the user typed silently revert to the stored values under a generic error.
- **Evidence:**
  - src/Web/ClientApp/src/features/identity/people/PersonalPages.jsx:439 (setEdits(null) runs before submit, whatever the outcome)
  - PersonalPages.jsx:328-329 (on failure, `result` is unchanged, so the form falls back to the loaded values)
  - src/Application/IdentityAccess/People/Profile/PersonalProfileHandlers.cs:54-79 (the refusals possible here: profile_field_not_editable, personal_profile_concurrency_conflict — both uncatalogued)
- **Recommendation:** Clear the edits only after a successful save, as RolesPage.jsx:123-126 does with its draft.
- **Verifier:** Confirmed. PersonalPages.jsx:439 calls setEdits(null) before submit whatever the outcome. On failure `result` does not change (:328), so `form` (:329) falls back to the loaded profile and the typed names revert under a generic error. RolesPage.jsx:123-126 clears its draft only after act() succeeds.

<a id="frontend-presentation-12"></a>

### frontend-presentation-12 — Retry-After for 429/503 is shown as static text and the submit re-enables immediately

- **Layer:** frontend-presentation
- **User impact:** The user can keep pressing a button that will be refused, and the wait never counts down or clears.
- **Evidence:**
  - src/Web/ClientApp/src/features/identity/ProblemMessage.jsx:66-68 (static 'Try again in N seconds.')
  - src/Web/ClientApp/src/features/identity/useSubmit.js:24-26 (isBusy is cleared at once); e.g. src/Web/ClientApp/src/features/platform/shared/PlatformStepUpForm.jsx:38 (disabled={isBusy} only)
  - Where 429/503 come from: src/Web/Infrastructure/Identity/LoginRateLimiting.cs:98-102 (sign-in); IdentityAccessErrors.cs:64-65, 88-89, 95-96
- **Recommendation:** Disable the submit and count down for retryAfterSeconds; remove the message when the wait is over.
- **Verifier:** Confirmed. ProblemMessage.jsx:66-68 prints a static 'Try again in N seconds.' useSubmit.js:24-26 clears isBusy immediately, and PlatformStepUpForm.jsx:38 disables only on isBusy. The 429 and 503 answers carry Retry-After (LoginRateLimiting.cs:98-102; IdentityAccessErrors.cs:64-65, 88-89, 95-96, 128-129).

<a id="frontend-presentation-13"></a>

### frontend-presentation-13 — Inviting a Platform administrator shows no outcome at all

- **Layer:** frontend-presentation
- **User impact:** The operator cannot tell whether the invitation went out and may send it again.
- **Evidence:**
  - src/Web/ClientApp/src/features/platform/PlatformPanel.jsx:356-377 (the invite form); :124-130 (run ignores the result; inviteEmail is never cleared and no status is shown)
  - docs/features/identity-access/SPEC.md:275 (neutral 202)
- **Recommendation:** Show a neutral role=status confirmation ('If that address can be invited, an invitation is on its way') and clear the field, as InviteMemberPage.jsx:208-212 does.
- **Verifier:** Confirmed. PlatformPanel.jsx:362 calls run(() => platform.inviteAdministrator(inviteEmail)). run (:124-130) ignores the result apart from clearing pendingAction and refreshing the directories; a bodyless 202 returns null, so the `outcome !== undefined` branch runs. No status message is shown and inviteEmail is never cleared. SPEC.md:275 specifies a neutral 202, so a neutral confirmation shown to everyone is safe. InviteMemberPage.jsx:208-212 already shows the pattern.

<a id="frontend-presentation-missed-1"></a>

### frontend-presentation-missed-1 — No catch-all route: an unknown or mistyped URL renders a blank page with no 'not found' message

- **Layer:** frontend-presentation
- **User impact:** A mistyped address or a truncated link path shows an empty main area: the sidebar shell for a signed-in user, the visitor bar for a visitor. Nothing says the page does not exist or offers a way back, and the page reads as broken.
- **Evidence:**
  - src/Web/ClientApp/src/AppRoutes.jsx:35-78 (no `path: '*'` entry; every route is explicit)
  - src/Web/ClientApp/src/App.jsx:15-20 (Routes maps only AppRoutes; there is no fallback element)
  - src/Web/ClientApp/src/components/Layout.jsx:42, 61-72 (a path outside publicEntryPaths gets the shell, so an unmatched path shows NavMenu over an empty <main>)
- **Recommendation:** Add a `{ path: '*', element: <NotFoundPage /> }` route with a heading ('That page does not exist') and a link to /identity or /login, depending on the session. Add it to the AppRoutes test matrix.
- **Verifier:** found by verifier

<a id="frontend-presentation-missed-2"></a>

### frontend-presentation-missed-2 — The provider-return failure state tells the user to try again but offers no control to do it

- **Layer:** frontend-presentation
- **User impact:** After a failed provider sign-in, link or proof, the user sees a generic error ('That request could not be completed.' for a network failure) and an instruction they cannot act on from the page: no way back to /login or /identity/external.
- **Evidence:**
  - src/Web/ClientApp/src/features/identity/credentials/ExternalAccountsPage.jsx:269-273 (a failed completion sets problem to error.problem ?? { code: 'unexpected' }, which is uncatalogued)
  - src/Web/ClientApp/src/features/identity/credentials/ExternalAccountsPage.jsx:289-296 (renders 'Nothing was changed. You can try again.' with no button or link; the code comment admits the control 'is reported not written')
  - src/Web/ClientApp/src/AppRoutes.jsx:52 (/external/return is public, so a visitor whose Google sign-in failed lands here)
- **Recommendation:** Offer the next step with the refusal: 'Back to sign in' (to /login) for a sign-in round trip, 'Back to sign-in providers' (to /identity/external) for a link or proof. Include this state in the standard that every error state names and offers its way forward.
- **Verifier:** found by verifier

<a id="cross-cutting-runtime-5"></a>

### cross-cutting-runtime-5 — The traceId is read from every problem but never shown, so users cannot give support a reference

- **Layer:** cross-cutting-runtime
- **User impact:** For 'Something went wrong. Try again.' (a 500), the user has nothing support can look up. The join key the backend deliberately produces is thrown away at the last step.
- **Evidence:**
  - problemDetails.js:42 reads the traceId (`traceId: typeof body.traceId === 'string' ? body.traceId : undefined`).
  - ProblemMessage.jsx:59-76 renders the message for the code, the Retry-After and the field errors, but never problem.traceId.
  - ApiProblemDetailsMapper.cs:85-86 and SafeRequestLogContext.cs:9 both use Activity.Current.TraceId, so on the server the traceId directly matches the CorrelationId in UnhandledExceptionBehaviour.cs:21-22.
  - useSubmit.js:22 invents { code: 'internal_server_error', status: 0 } for failures that are not problems, so no traceId is kept.
- **Recommendation:** In ProblemMessage, show a small copyable 'Reference: <traceId>' line for status >= 500, and optionally for every problem except validation. The traceId is opaque and contains no personal data, so this fits IA-REQ-029 and IA-REQ-038.
- **Verifier:** The facts hold. problemDetails.js:42 parses traceId, ProblemMessage.jsx:59-76 never renders it, and useSubmit.js:22 invents a problem with no traceId. Downgraded to low: - IA-REQ-038 (SPEC.md:195) requires traceId in the response body, not in the UI, so this is a supportability improvement, not a contract gap. - Its value is also limited by finding 2: for a MediatR fault, the traceId joins only a type-name log line with no stack.

<a id="cross-cutting-runtime-7"></a>

### cross-cutting-runtime-7 — Health endpoints exist only in Development; elsewhere /health falls through to the SPA page whatever the state of the database or admission

- **Layer:** cross-cutting-runtime
- **User impact:** In production, an orchestrator or load balancer cannot learn from a probe that the database is down or admission is closed. A probe pointed at /health gets the SPA page, so traffic keeps going to a broken instance, and the outage shows up only as user-facing errors.
- **Evidence:**
  - src/ServiceDefaults/Extensions.cs:104-121: MapHealthChecks('/health') and ('/alive') run only inside `if (app.Environment.IsDevelopment())`. :95-100 adds only the 'self' liveness check.
  - A DbContext health check is registered: src/Infrastructure/DependencyInjection.cs:46 calls EnrichNpgsqlDbContext, and the package doc (~/.nuget/packages/aspire.npgsql.entityframeworkcore.postgresql/13.5.3/lib/net10.0/Aspire.Npgsql.EntityFrameworkCore.PostgreSQL.xml:33-35) says it 'Configures retries, health check, logging and telemetry'.
  - RecoveryAdmissionMiddleware.cs:13-16, 24 and 51 lets /health and /alive through as probes because 'an orchestrator has to be able to tell a process that is running and refusing from one that is not running'.
  - Outside Development those paths have no endpoint, so they reach src/Web/Program.cs:63 MapFallbackToFile('index.html') and get the SPA page, not a health status.
  - src/OutboxWorker/Program.cs:5 uses a generic host (Host.CreateApplicationBuilder) with no HTTP endpoint, so the delivery loop has no liveness probe at all.
- **Recommendation:** Map /health (readiness: the DbContext check, tagged 'ready') and /alive (liveness: 'self') in every environment. Return status only, with no body, as the admission middleware intends. Optionally restrict them with RequireHost or a management port. Make sure the SPA fallback cannot answer those paths. For OutboxWorker, add a heartbeat check that fails when the last successful pass is older than N intervals, exposed through a minimal endpoint or a file-based probe.
- **Verifier:** Confirmed: - ServiceDefaults/Extensions.cs:104-121 maps /health and /alive only in Development. - The Aspire package doc (xml:35) says EnrichNpgsqlDbContext adds a health check, and it is called at Infrastructure/DependencyInjection.cs:46. - RecoveryAdmissionMiddleware.cs:13-16, :24 and :51 lets these paths through as bodyless probes. Outside Development they reach Program.cs:63 MapFallbackToFile. - A grep of AppHost finds no WithHttpHealthCheck or probe configuration. Downgraded to low: - It is the template default, with an explicit security caveat (Extensions.cs:106-107). - Nothing in the repo configures a probe that would be misled. - It is operational readiness, not user-facing error handling. - With one shared database, taking instances out of rotation would not restore service. The most concrete part is that the admission middleware documents a probe contract that holds only in Development.

<a id="cross-cutting-runtime-8"></a>

### cross-cutting-runtime-8 — A database outage reaches callers as a generic 500 with no Retry-After, just like a bug; the admission-closed 503 has no traceId

- **Layer:** cross-cutting-runtime
- **User impact:** During a database blip, users read 'Something went wrong' instead of 'temporarily unavailable, try again shortly'. Clients and proxies get no Retry-After. Operators cannot tell an outage from a defect by status code.
- **Evidence:**
  - ProblemDetailsExceptionHandler.cs:17-28 has no case for NpgsqlException, TimeoutException or EF RetryLimitExceededException. They fall into `_ => null` and then WriteUnexpectedAsync (ApiProblemDetailsMapper.cs:50-69): 500 internal_server_error with no Retry-After.
  - ApplicationErrorCategory.Unavailable (503, ApiProblemDetailsMapper.cs:81) is produced only at LoginRateLimiting.cs:100 and IdentityAccessErrors.cs:96 (grep). PostgreSqlAttemptBudget.cs:73-78 already shows the classification to reuse.
  - SessionCookieEvents.cs:34-43 reads the database on every authenticated request before the endpoint runs, so during a database outage every authenticated call returns this 500.
  - ProblemMessage.jsx:55-56: service_unavailable reads 'That is temporarily unavailable. Try again shortly.'; internal_server_error reads 'Something went wrong. Try again.'
  - RecoveryAdmissionMiddleware.cs:64-67 writes the 503 body by hand as {type,title,status,detail,code:'recovery_admission_closed',retryAfterSeconds:60}. It has no traceId, which IA-REQ-038 requires on every non-success (SPEC.md:195). ProblemMessage.jsx:9-57 has no entry for recovery_admission_closed. The Retry-After header is set correctly at :59.
- **Recommendation:** In ProblemDetailsExceptionHandler, map transient infrastructure exceptions (NpgsqlException with IsTransient, TimeoutException, RetryLimitExceededException, and a DbUpdateException wrapping one of them) to ApplicationError('service_unavailable', Unavailable, retryAfterSeconds: N), and log them at Warning with the traceId. Leave everything else as a 500. Declare 503 in OpenAPI through ApiProblemMetadata.ServiceUnavailable for routes that touch the database. In RecoveryAdmissionMiddleware, add a traceId (Activity.Current?.TraceId or HttpContext.TraceIdentifier; neither needs the database), and add a MESSAGES entry for recovery_admission_closed.
- **Verifier:** The first half is specified behaviour, not a gap: - IA-REQ-038 (SPEC.md:195) says 'unexpected infrastructure or programmer failures remain exceptions ... a generic safe 500 exposes no internal diagnostics'. - IA-REQ-057 (SPEC.md:182) grants 503 service_unavailable only to the attempt-budget store. - Mapping Npgsql or timeout faults to 503 is therefore a proposal to change the spec. What stands: - RecoveryAdmissionMiddleware.cs:64-67 writes a problem body with no traceId, which IA-REQ-038 requires on every non-success. Activity.Current is available there without the database. - ProblemMessage.jsx:9-57 has no recovery_admission_closed entry. The Retry-After header is read (problemDetails.js:38), so the user sees 'That request could not be completed. Try again in 60 seconds.' - Because the SPA cannot load at all while admission is not open (see the note on finding 6), that client text is rarely reached. Low.

<a id="cross-cutting-runtime-9"></a>

### cross-cutting-runtime-9 — The outbox worker's failure log drops the exception and any correlation, unlike its sibling loops, and retries a failing pass every 5 s with no backoff

- **Layer:** cross-cutting-runtime
- **User impact:** When mail stops, for example the 'you already have an account' notice behind the neutral registration answer, operators see 'outbox_pass_failed' with no type. They cannot tell a database outage from a bug, and cannot go from the registration request's traceId to its outbox message.
- **Evidence:**
  - src/OutboxWorker/Worker.cs:36-42: `catch (Exception) { logger.LogError("An outbox dispatch pass failed (outbox_pass_failed)."); }` does not capture the exception, not even its type.
  - The sibling loops do log the type: LocalOutboxDeliveryService.cs:50-53, LifecycleMaintenanceService.cs:51-54 and PlatformBootstrapHostedService.cs:34-38.
  - Worker.cs:20 and 44-47: after a failed pass, delivered is 0, so the loop waits a fixed 5 s. A pass that keeps failing writes one context-free Error line every 5 s.
  - src/Domain/IdentityAccess/Outbox/OutboxMessage.cs:9-31 has no correlation or trace member, and OutboxDispatcher.cs:147-152 and 189 settle with only a FailureCode and a metric, with no log line, so a delivery outcome cannot be traced back to the request that queued it.
- **Recommendation:** Log exception.GetType().Name, as the sibling loops do, and wrap each pass in an Activity. Back off exponentially across consecutive pass failures. Store the queuing request's trace id on OutboxMessage (opaque, not personal data), and write it in a Warning when a message is Abandoned.
- **Verifier:** Confirmed: - Worker.cs:36-41 is `catch (Exception)` with no variable and logs a constant string. - The siblings log the type: LocalOutboxDeliveryService.cs:53, LifecycleMaintenanceService.cs:54 and PlatformBootstrapHostedService.cs:38. - A failed pass leaves delivered=0, so the loop waits a fixed IdleInterval of 5 s (Worker.cs:20 and :44-47). - OutboxMessage.cs:9-31 has no correlation member. - SettleLocalAsync (OutboxDispatcher.cs:187-189) records only a metric, with no log line. Clarification: 'no backoff' applies only to failures of a whole pass. Per-message retries do back off (OutboxMessage.cs:38-41, OutboxDispatcher.cs:150). Low is right.

<a id="cross-cutting-runtime-10"></a>

### cross-cutting-runtime-10 — An unknown /api/* path answers 200 with the SPA's index.html instead of a 404 problem document

- **Layer:** cross-cutting-runtime
- **User impact:** If the client and server versions drift after a deploy (a route renamed or removed), the user sees a generic error while the server reports 200 OK. The failure is invisible in error-rate metrics and logs.
- **Evidence:**
  - src/Web/Program.cs:63: app.MapFallbackToFile('index.html') has no /api exclusion. A grep for MapFallback in src/Web finds only this line. IdentitySecurityHeaders.cs:61-65 only adds headers for /api.
  - SPEC.md:195 (IA-REQ-038): every non-success is RFC 9457 problem+json with a stable code.
  - On the client, problemDetails.js:56-67 (readSuccess) fails at JSON.parse with 'The success response was not valid JSON.', and useSubmit.js:22 then shows the generic internal_server_error message.
  - A grep of tests/ finds no test for an unmatched API route.
- **Recommendation:** Before MapFallbackToFile, map a fallback for /api/{**path} that writes 404 not_found through IProblemDetailsService, or restrict the SPA fallback pattern so it excludes /api. Add a functional test.
- **Verifier:** Confirmed. Program.cs:63 is the only fallback in src/Web (grep for MapFallback|UseStatusCodePages), and it has no /api exclusion. For a GET, the client's readSuccess fails at JSON.parse (problemDetails.js:62-66) and useSubmit.js:22 shows the generic 500 text, while the server records a 200. I verified only the GET path, which is the one the client's reads use. Low.

<a id="cross-cutting-runtime-missed-1"></a>

### cross-cutting-runtime-missed-1 — A sign-in that succeeded is reported as failed when the token refresh after it fails, and a retry creates a second session

- **Layer:** cross-cutting-runtime
- **User impact:** If the antiforgery request right after a successful sign-in fails (a network blip, or a 5xx while the new cookie is validated), the person is told 'Something went wrong' although they are signed in. They press Sign in again. That either creates a second UserSession, which counts against the 5-session cap of IA-REQ-049, or fails with antiforgery_validation_failed because the token is stale.
- **Evidence:**
  - src/Web/ClientApp/src/features/identity/context/IdentityProvider.jsx:69-73: signIn runs `await identityClient.signIn(...)`, then `await identityClient.bootstrapAntiforgery();` with no catch, then `return loadContext();`. The same bootstrap is guarded with `.catch(() => undefined)` at :62 (mount), :80 (signOut) and :95 (deactivateAccount).
  - src/Web/ClientApp/src/features/identity/api/apiTransport.js:27-29: bootstrapAntiforgery throws a plain Error on any non-ok answer, and a network failure from fetch rejects as well.
  - src/Web/ClientApp/src/features/identity/useSubmit.js:20-22: a failure that is not an ApiProblem becomes { code: 'internal_server_error' }, shown as 'Something went wrong. Try again.' (ProblemMessage.jsx:56).
  - src/Web/ClientApp/src/features/identity/login/LoginPage.jsx:54, 58, 74 and 100: the login form submits through useSubmit(identity.signIn). It navigates away only when isAuthenticated is true, and loadContext never ran, so the page stays on Sign in and shows the error.
  - docs/features/identity-access/SPEC.md:249: the POST has already answered 204 and set the session cookie. SPEC.md:313: the server rotates the pair on sign-in and rejects old pairs, so the in-memory token is now stale.
  - apiTransport.js:21-22 itself warns that 'a state change nobody asked for twice is how one sign-in becomes two sessions'.
- **Recommendation:** Handle the post-sign-in bootstrap like the other three call sites: catch it, and still call loadContext() so the UI reflects the session that exists. Also have bootstrapAntiforgery clear requestToken before it fetches, so a failed refresh leaves null and the next mutation bootstraps lazily (apiTransport.js:37) instead of sending a stale token. Add an IdentityProvider test in which the post-sign-in GET /api/identity/antiforgery fails.
- **Verifier:** found by verifier

<a id="cross-cutting-runtime-missed-2"></a>

### cross-cutting-runtime-missed-2 — The client sets no timeout and cannot cancel requests, so a hung request leaves a form busy with no feedback

- **Layer:** cross-cutting-runtime
- **User impact:** During a database or network stall, the button stays disabled or the progress bar keeps running for as long as the browser or proxy waits. Nothing tells the person the request is slow, and there is no way to cancel and retry.
- **Evidence:**
  - src/Web/ClientApp/src/features/identity/api/apiTransport.js:28 and 39-47: both fetch calls pass no `signal`.
  - A grep of src/Web/ClientApp/src (tests excluded) for AbortController|signal:|AbortSignal|setTimeout( found nothing.
  - src/Web/ClientApp/src/features/identity/useSubmit.js:15-25: isBusy is cleared only when the promise settles, and screens disable their submit buttons while it is set (e.g. TenantSelector.jsx:63 and :82).
  - src/Infrastructure/DependencyInjection.cs:46 calls EnrichNpgsqlDbContext. Its package doc (Aspire.Npgsql.EntityFrameworkCore.PostgreSQL.xml:35) says it 'Configures retries', so a database fault can hold a request open while retries run. I did not verify the retry duration, which the auditor also left open.
- **Recommendation:** Give the transport a default per-request AbortSignal.timeout(N). Classify an abort as a typed client failure (for example request_timeout) with its own ProblemMessage wording. Let screens pass a signal so requests are cancelled on unmount. This fits the single classification point proposed in finding 6.
- **Verifier:** found by verifier

<a id="error-path-tests-5"></a>

### error-path-tests-5 — UI transport failures and mid-flow session loss on mutations are unproven

- **Layer:** error-path-tests
- **User impact:** Offline, proxy or gateway failures and a session that expires while a form is open are among the most common real-world error paths. Nothing proves the user gets an understandable message, keeps what they typed, or is sent to sign in; for example, a busy button that never re-enables would go unnoticed.
- **Evidence:**
  - src/Web/ClientApp/src/features/identity/useSubmit.js:20-23 maps any non-ApiProblem failure to {code:'internal_server_error', status:0}. No *.test.* file uses HttpResponse.error() or a rejected fetch (grep for HttpResponse.error / networkError / TypeError / 'status: 0' finds nothing)
  - src/Web/ClientApp/src/features/identity/api/apiTransport.js:29 (antiforgery bootstrap failure) and :50 (non-problem error on a mutation) have no tests. Non-problem failures are covered only for reads: PlatformIdentitiesPage.test.jsx:442-458 and PlatformRetentionPage.test.jsx:248-264
  - No SPA test has a mutation answer 401 invalid_session or authentication_required. The 401 stubs are recent_mfa_required/recent_proof_required step-up gates, plus one context reload returning invalid_session (PlatformRetentionPage.test.jsx:133-150)
  - tests/Web.AcceptanceTests/Features/IdentityAccess.feature:60-64 covers a revoked session only on the next protected page navigation, not a form submitted after the session died
- **Recommendation:** Add useSubmit-level page tests (e.g. InviteMemberPage, ChangePasswordPage) for HttpResponse.error(), a text/html 502, and an antiforgery bootstrap 500, each asserting the alert text and that the submit button is re-enabled. Add a test where a mutation returns problem(401,'invalid_session') and assert the message plus the intended context refresh or sign-in redirect.
- **Verifier:** The facts hold. No SPA test uses HttpResponse.error or any network-failure stub, and there is no apiTransport test file (features/identity/api has only problemDetails.test.js). The antiforgery stubs at ExternalAccountsPage.test.jsx:171,221 and AccountLifecyclePages.test.jsx:20 all succeed. Non-problem failures are covered only for reads (PlatformIdentitiesPage.test.jsx:442-458; PlatformRetentionPage.test.jsx:248-264). The only invalid_session stub is a context reload (PlatformRetentionPage.test.jsx:133-150), and IdentityAccess.feature:60-64 covers only the next page navigation. Downgraded to low because the current code handles these paths acceptably. useSubmit.js:20-26 turns any non-problem failure into internal_server_error ('Something went wrong. Try again.', ProblemMessage.jsx:56) and re-enables the button in finally. invalid_session is mapped at ProblemMessage.jsx:13. The gap is regression risk, not present user harm. Whether a mid-flow invalid_session should also refresh context or redirect is a behaviour question for another dimension; useSubmit does neither today.

<a id="error-path-tests-7"></a>

### error-path-tests-7 — The shared problem assertion skips the RFC 9457 status/type/title members, and cross-tenant refusals accept any of three statuses

- **Layer:** error-path-tests
- **User impact:** A body whose status disagrees with the HTTP status, or a cross-tenant refusal that flips between 400, 403 and 404 (changing what it reveals under IA-REQ-030), would pass unnoticed.
- **Evidence:**
  - tests/Application.FunctionalTests/IdentityAccess/Api/ProblemDetailsContractTests.cs:297-310 checks code, traceId and envelope absence, but not the body 'status' matching the HTTP status that SPEC.md:195 requires ('matching status'), nor type/title
  - tests/Application.FunctionalTests/IdentityAccess/Invitations/InvitationHttpContractTests.cs:127,242 use ShouldBeOneOf(BadRequest, Forbidden, NotFound) for issuing, resending or cancelling in a tenant the session is not operating in
- **Recommendation:** Extend AssertProblemAsync (and IdentityHttpHarness.ReadProblemAsync) to assert status == (int)response.StatusCode, type == 'about:blank' and a non-empty title, and pin the cross-tenant refusal to the single status the SPEC chooses.
- **Verifier:** Holds. AssertProblemAsync checks code, traceId and envelope absence, but not status, type or title (ProblemDetailsContractTests.cs:297-310). IdentityHttpHarness.ReadProblemAsync checks only the media type (IdentityHttpHarness.cs:74-80). Low is right: the mapper writes the body status and the HTTP status from the same value (ApiProblemDetailsMapper.cs:17-21,37), so drift is unlikely. ShouldBeOneOf(400,403,404) is confirmed at InvitationHttpContractTests.cs:127 and 242. On what the SPEC pins: IA-REQ-030 is permissive ('may return 404', SPEC:179), and the issue row (SPEC:260) states no cross-tenant answer. The resend and cancel rows (SPEC:261-262) do name '400 invalid_invitation for another tenant's offer', which supports pinning at least the case at line 242.

<a id="error-path-tests-missed-1"></a>

### error-path-tests-missed-1 — email_confirmation_required is declared and emitted but no test asserts it anywhere, and the UI has no message for it

- **Layer:** error-path-tests
- **User impact:** Someone whose account is not active is refused a re-authentication or a provider link with a vague generic message instead of 'confirm your email first'. A regression in this refusal's status, code or reachability would go unnoticed, although IA-REQ-005 makes email confirmation a gate for sensitive operations.
- **Evidence:**
  - src/Application/IdentityAccess/Credentials/Reauthenticate/ReauthenticateHandler.cs:29 and src/Application/IdentityAccess/ExternalLogins/ExternalLoginHandlers.cs:113 return IdentityAccessErrors.EmailConfirmationRequired() when the identity is not active
  - src/Application/IdentityAccess/Common/IdentityAccessErrors.cs:131-132 (Authorization category, so 403); src/Web/Infrastructure/ApiProblemMetadata.cs:53; declared on src/Web/Endpoints/Identity/SessionEndpoints.cs:51 and ExternalLoginEndpoints.cs:41
  - grep for 'email_confirmation_required' across tests/ (functional, integration, acceptance) and all SPA *.test.* files finds nothing, not even an Application-level Result assertion
  - src/Web/ClientApp/src/features/identity/ProblemMessage.jsx:9-57 has no entry for it, so line 65's generic 'That request could not be completed.' is shown
- **Recommendation:** Add an HTTP test on POST /api/identity/credentials/reauthenticate (and the external link route) for a non-active identity asserting 403 problem+json code email_confirmation_required. Add a ProblemMessage entry that tells the user to confirm their email, with a SPA test asserting that wording. Fold the code into the React catalogue drift check proposed in error-path-tests-3.
- **Verifier:** found by verifier

## Layer summaries

What each auditor found to exist, and what is worth keeping as the standard.

### backend-model-and-mapping

Scope: the backend error model and how it reaches HTTP, audited read-only on branch main (net10.0, Directory.Build.props:6). No files were changed and no build or test was run, because building writes to artifacts/ inside the repo.

HOW IT WORKS
- Expected failures are Result/Result<T> carrying one ApplicationError. That error holds a stable code, an ApplicationErrorCategory, an optional safe Detail, field errors (Validation category only) and Retry-After (RateLimited/Unavailable only).
- Endpoints map failures with ApiProblemDetailsMapper.ToHttpResult. Exceptions go through ProblemDetailsExceptionHandler, which is wired by AddExceptionHandler (DependencyInjection.cs:24) and UseExceptionHandler (Program.cs:47).
- Both paths serialize one ApiProblemDetails shape: type about:blank, title, status, detail, instance, code, traceId, and errors. The trace id is the W3C trace id of Activity.Current, falling back to TraceIdentifier.

CATEGORY TO STATUS (ApiProblemDetailsMapper.cs:73-83). Every category is mapped, and an unknown value throws.
| Category | Status | Extra |
|---|---|---|
| Validation | 400 | field errors allowed |
| Authentication | 401 | |
| Authorization | 403 | |
| NotFound | 404 | |
| Conflict | 409 | |
| RateLimited | 429 | Retry-After header |
| Unavailable | 503 | Retry-After header |
This matches IA-REQ-030, and it keeps 429 and 503 apart as IA-REQ-057 requires.

WHERE EACH NON-SUCCESS COMES FROM
| Source | Writer | Code | traceId |
|---|---|---|---|
| Result failure in an endpoint | mapper | the error's own | yes |
| FluentValidation ValidationException | exception handler | validation_failed + errors | yes |
| Unauthorized/Forbidden thrown by AuthorizationBehaviour | exception handler | authentication_required / permission_denied | yes |
| Authorization middleware challenge/forbid (audited) | ApiAuthorizationMiddlewareResultHandler | authentication_required or invalid_session / permission_denied | yes |
| Antiforgery or origin failure | Identity.ValidateAntiforgery | antiforgery_validation_failed (400) | yes |
| Model binding / JSON | exception handler, via ThrowOnBadRequest=true | endpoint's binding code or invalid_request, always 400 | yes |
| Login budgets | LoginAttemptBudgetMiddleware | rate_limit_exceeded (429) / service_unavailable (503) | yes |
| Unhandled exception | WriteUnexpectedAsync | internal_server_error, fixed body, nothing leaks | yes |
| Restore-admission refusal | hand-written body | recovery_admission_closed (503) | NO |
| Unknown /api route | SPA fallback | none (index.html 200, or bare 404) | NO |
| Exception in middleware ahead of UseExceptionHandler | none | server-default 500 | NO |

LOGGING
| What | Level | What is recorded |
|---|---|---|
| Expected Result failures | not logged | security denials are audited instead |
| Request start | Information | request type name + trace id |
| Unhandled exception inside MediatR | Error | exception type name + trace id only; no stack, no message |
| Unhandled exception outside MediatR | nothing | .NET 10 default suppresses diagnostics once an IExceptionHandler handled it |
| Background loops | Error | a fixed string, or the exception type name only |
The same trace id appears in the problem body, the pipeline logs and the denial audit. PII hygiene is good and is enforced by SensitiveRequestLoggingTests.

OPENAPI
- WithApiProblemDetails declares each route's statuses and codes by hand.
- ApiExceptionOperationTransformer turns them into x-problem-codes and a Retry-After header.
- Exact-set tests cover about a dozen routes. Nothing checks every endpoint.

MINOR NOTES (not reported as findings)
- The NotFoundException branch (ProblemDetailsExceptionHandler.cs:23) has no throw sites anywhere in src.
- The UnauthorizedAccessException path always answers authentication_required. The middleware path, by contrast, distinguishes invalid_session.

ON THE PRODUCT OWNER'S TRIGGER
Anonymous organization registration answers a bodyless 202 whatever the address (Identity.cs:57; RegisterOrganizationHandler.cs:73-77). That is specified neutrality (IA-REQ-003), and it is implemented consistently; even the password-policy check runs before any address lookup. The gap on that form is malformed input, which never gets field-level feedback (finding 2).

**Keep:**

- The error model enforces its own rules: every error needs a code, field errors are only allowed on validation failures, and Retry-After must be positive and is only allowed on rate-limited or unavailable failures. A Result holds exactly one of success or error. — src/Application/Common/Models/ApplicationError.cs:18-36; src/Application/Common/Models/Result.cs:7-10
- The internal Result is never serialized and there is no universal success/data/error envelope. Tests assert that envelope is absent. — src/Web/Infrastructure/ResultHttpExtensions.cs:8-23; tests/Application.FunctionalTests/IdentityAccess/Api/ProblemDetailsContractTests.cs:305-307; tests/Application.FunctionalTests/IdentityAccess/Sessions/SessionTests.cs:1458-1461
- The category-to-status map is exhaustive and fails loudly on an unknown category. It follows IA-REQ-030 (401 absent/invalid identity, 403 no permission, 404 for hidden resources) and keeps 503 separate from 429 (IA-REQ-057). — src/Web/Infrastructure/ApiProblemDetailsMapper.cs:73-83; src/Application/Common/Models/ApplicationErrorCategory.cs:16-21
- One problem writer (IProblemDetailsService) serves all the main paths: Result failures, exceptions, authorization challenge/forbid, antiforgery failures and the login budgets. Retry-After is set from the error model in one place. — src/Web/Infrastructure/ProblemDetailsExceptionHandler.cs:36; src/Web/Infrastructure/ApiAuthorizationMiddlewareResultHandler.cs:43; src/Web/Infrastructure/Identity/LoginRateLimiting.cs:99-102; src/Web/Infrastructure/ApiProblemDetailsMapper.cs:39-42
- An unhandled exception gets a fixed, safe 500 with code internal_server_error and a traceId, and never the exception message, type or stack. A functional test forces a real fault and checks the body leaks nothing. — src/Web/Infrastructure/ApiProblemDetailsMapper.cs:50-69; tests/Application.FunctionalTests/IdentityAccess/Api/ProblemDetailsContractTests.cs:210-255
- Error responses, pipeline logs and the security-denial audit all carry the same W3C trace id, so they can be joined. — src/Web/Infrastructure/ApiProblemDetailsMapper.cs:85-86; src/Application/Common/Behaviours/SafeRequestLogContext.cs:9; src/Web/Infrastructure/ApiAuthorizationMiddlewareResultHandler.cs:36-42
- 401 and 403 are shaped deliberately. The cookie handler answers 401/403 instead of redirecting, the challenge's Location header is removed, invalid_session is told apart from authentication_required, and every denial is audited before the problem is written. — src/Infrastructure/Identity/SessionCookieEvents.cs:97-107; src/Web/Infrastructure/ApiAuthorizationMiddlewareResultHandler.cs:16-28,33-44,50-60
- Antiforgery and exact-origin failures answer one stable problem, 400 antiforgery_validation_failed, before any business effect. A test covers seven variants (missing origin, cross-origin, wrong scheme, wrong port, missing cookie, missing token, wrong token). — src/Web/Endpoints/Identity.cs:68-118; tests/Application.FunctionalTests/IdentityAccess/Api/ProblemDetailsContractTests.cs:62-90
- Malformed bodies do not produce framework-default error bodies: ThrowOnBadRequest routes them to the shared writer with a code, and a route can choose its own binding code. — src/Web/DependencyInjection.cs:33-34; src/Web/Infrastructure/ProblemDetailsExceptionHandler.cs:26,40-45; src/Web/Infrastructure/ApiProblemMetadata.cs:129-134
- Each route declares its problem codes, and OpenAPI publishes them as x-problem-codes plus a Retry-After header. Several routes have exact-set tests that also reject over-declared statuses. — src/Web/Infrastructure/ApiProblemMetadata.cs:104-120; src/Web/Infrastructure/ApiExceptionOperationTransformer.cs:14-40; tests/Application.FunctionalTests/IdentityAccess/Api/OpenApiContractTests.cs:111-153,245-251
- Log hygiene meets IA-REQ-029: the pipeline logs only the request type name and trace id, a unit test proves passwords, tokens and provider text never reach the logs, and the OpenID Connect debug log floor is pinned so authorization codes cannot be logged. — src/Application/Common/Behaviours/LoggingBehaviour.cs:16; src/Application/Common/Behaviours/UnhandledExceptionBehaviour.cs:21-22; tests/Application.UnitTests/Common/Behaviours/SensitiveRequestLoggingTests.cs:18-63; src/Web/DependencyInjection.cs:68-71
- The error catalogue explains each intentional collapse of several failures into one code. That keeps enumeration-safe answers deliberate rather than accidental. — src/Application/IdentityAccess/Common/IdentityAccessErrors.cs:22-28,46-51,210-217,253-259

### backend-validation-coverage

SCOPE. I found 68 MediatR request types in C:/Users/ezequ/source/repos/RepositorioBase/src/Application by enumerating their record declarations. I also traced the Web request DTOs that feed them.

HOW VALIDATION WORKS TODAY — five mechanisms:
(a) FluentValidation. Only 8 validators exist, all under Platform: RegisterPlatformInvitee, ConfirmPlatformInvitee, the five MFA commands, and InvitePlatformAdministrator. All of them check shape only (NotEmpty and MaximumLength). ValidationBehaviour runs them after AuthorizationBehaviour. A failure throws ValidationException, which ProblemDetailsExceptionHandler turns into 400 `validation_failed` with `errors` keyed by the C# property name.
(b) Handler guards. Every non-Platform command validates inside its handler (TryNormalize-style checks for null, length and the presence of '@'). These return a Result carrying an operation-level code and no field errors. This is the dominant mechanism.
(c) Domain value objects. NormalizedCuit checks digits, hyphens and whitespace, and requires exactly 11 digits. It has no check digit. NormalizedDocument covers the Argentine DNI: separators are tolerated, leading zeros are stripped, and 7 or 8 digits are required. Invitation.Canonicalize trims, lowercases and normalizes the address, caps it at 256, and requires '@' with no whitespace or uppercase. Role names must be non-empty and at most 128 characters. Their ArgumentExceptions are caught and turned into Result failures.
(d) ASP.NET Identity password validators. Registration uses ValidatePasswordAsync, which returns a bool only. Reset and change use IdentityCredentialService, which returns errors keyed `newPassword`.
(e) Malformed JSON. It maps to a code each endpoint declares (WithBodyBindingFailureCode), never with field errors.
Only two paths ever produce field-keyed errors: FluentValidation (PascalCase keys) and password reset/change (camelCase `newPassword`).

COVERAGE TABLE (request -> mechanism -> fields and rules -> failure shape)

PUBLIC
- RegisterOrganizationCommand -> handler guard + NormalizedCuit + ValidatePasswordAsync -> email (not null, <=256, contains '@'); password (not blank, <=256, PasswordOptions); legalName (not blank, <=256); cuit (<=32, digits/-/space, 11 digits, NO check digit) -> 400 `invalid_registration` with no errors; malformed JSON gets the same.
- RegisterPersonalCommand -> handler guard + NormalizedDocument + ValidatePasswordAsync -> email and password as above; fullName not blank <=200; displayName not blank <=60; documentNumber is an AR DNI of 7-8 digits -> 400 `invalid_registration` with no errors.
- RegisterInvitedUserCommand (register from invitation) -> handler guard -> password (not null, <=256, policy); the token's shape is not checked -> 400 `invalid_invitation` with no errors; everything else is a neutral 202.
- RegisterPlatformInviteeCommand -> FluentValidation, then handler -> Token and Password NotEmpty <=256, then the policy -> 400 `validation_failed` {Token|Password} OR 400 `invalid_invitation` with no errors.
- ConfirmPlatformInviteeCommand -> FluentValidation -> ConfirmationToken NotEmpty <=256 -> 400 `validation_failed` {ConfirmationToken}.
- ConfirmEmailCommand -> ConfirmationToken.IsCanonical -> 400 `invalid_confirmation`.
- CreateSessionCommand (sign-in) -> handler guard -> email <=256 with '@'; password not blank <=256 -> neutral bodyless 204 with no cookie; malformed JSON -> 400 `invalid_request`.
- RequestPasswordRecoveryCommand / RequestAccountReactivationCommand -> handler guard -> email <=256 with '@' -> neutral 202, even for malformed input.
- ResetPasswordCommand -> handler + Identity validators -> token (hash not empty); newPassword null or >256 -> `invalid_credential_token`; policy -> 400 `validation_failed` {newPassword}.
- ReactivateAccountCommand -> handler -> token; password <=256 -> 400 `invalid_reactivation` (collapsed by design).
- StartExternalLoginCommand -> handler -> provider must be in the offered set -> 400 `invalid_external_login`.
- CompleteExternalLoginCommand and RecoverPendingPlatformOwnerInvitationCommand -> no body input.

AUTHENTICATED SELF-SERVICE
- ChangePasswordCommand -> handler + Identity validators -> 400 `validation_failed` {newPassword}.
- ReauthenticateCommand -> handler -> action must be in ProofActions.All; password <=256 -> 400 `invalid_credential_proof` (collapsed by design).
- SelectTenantCommand -> tenantId not empty -> 400 `invalid_request` with no errors.
- RevokeOwnSessionCommand -> SessionReference.TryFrom -> 404 `session_not_found`.
- StartExternalLink / StartExternalProof / UnlinkExternalLogin -> provider and action -> `invalid_external_login` / `not_found`.
- UpdatePersonalProfileCommand -> handler -> fullName <=200 and displayName <=60, both not blank -> 400 `profile_field_not_editable` with no errors; missing version -> 409 `personal_profile_concurrency_conflict`.
- CreatePersonalContextCommand -> handler + NormalizedDocument -> 400 `invalid_registration`.
- OpenDocumentDisputeCommand -> accepted reason code, case-sensitive country/type enums, DNI value object -> 400 `invalid_document_dispute`.
- No input at all: GetIdentityContext, GetOwnCredentials, ListOwnSessions, RevokeCurrentSession, RevokeOtherSessions, DeactivateAccount, ListExternalLogins, CompleteExternalLink/Proof, GetPersonalProfile.

TENANT ADMINISTRATION
- InviteMemberCommand -> handler + Invitation.Canonicalize -> email; roleIds non-empty with no empty GUID -> 400 `invalid_invitation` with no errors.
- AcceptInvitation / ResendInvitation / CancelInvitation -> handler -> `invalid_invitation` / `invitation_conflict`.
- CreateRole / UpdateRole / RetireRole -> handler + Role domain -> name non-empty <=128; permission codes trimmed and deduplicated; version required -> 400 `invalid_role_operation` with no errors.
- UpdateMemberRoles / ChangeMemberStatus / TransferOwnership -> version and role set -> 400 `invalid_membership_operation`.
- ListRoles, GetRole, GetPermissionCatalog, ListMembers, ListTenantInvitations -> tenant scope only; limit clamped; an unreadable cursor restarts the list.

PLATFORM
- BeginPlatformMfaEnrollment / AcknowledgePlatformRecoveryCodes -> FluentValidation Token -> `validation_failed` {Token}, then `invalid_invitation`.
- VerifyPlatformMfaEnrollment -> FluentValidation Token, Code <=16 -> `validation_failed` {Token|Code}; a wrong code -> 400 `invalid_invitation`.
- StepUpPlatformMfa -> FluentValidation Code <=16 -> `validation_failed` {Code}; a wrong code -> 401 `invalid_session`.
- RecoverPlatformMfa -> FluentValidation RecoveryCode <=64 -> `validation_failed` {RecoveryCode}, then `invalid_credential_proof`.
- InvitePlatformAdministrator -> FluentValidation Email NotEmpty <=256 -> `validation_failed` {Email}; a malformed address -> `invalid_platform_operation`.
- RevokePlatformAdministrator, Suspend/ReactivateOrganizationTenant, Suspend/ReactivateIdentity -> Guid.Empty guards and closed-set enums parsed at the endpoint -> 400 `invalid_platform_operation`.
- ResolveDocumentDispute -> outcome must be corrected or rejected; evidence reference shape -> 400 `invalid_document_dispute`.
- PlaceRetentionHold -> reason code and reference shape -> 400 `invalid_platform_operation`.
- ReleaseRetentionHold -> no validation (idempotent).
- ListPlatform* directories -> limit defaults to 25, clamped 1..100; an unreadable cursor restarts the list.
- GetRetentionPolicy -> no input.

NET RESULT. No non-Platform command has a validator. The public B2B and B2C registration forms, register-from-invitation and every tenant-administration form return one operation-level code with no field errors. The single well-formed field-level path is password reset/change.

**Keep:**

- There is one error boundary. Every failure becomes RFC 9457 with a stable code, and field errors are structurally allowed only for validation failures (Retry-After likewise only for 429/503). Unexpected exceptions become a sanitized 500 `internal_server_error`. — C:/Users/ezequ/source/repos/RepositorioBase/src/Application/Common/Models/ApplicationError.cs:23-36; C:/Users/ezequ/source/repos/RepositorioBase/src/Web/Infrastructure/ApiProblemDetailsMapper.cs:14-32,50-69; C:/Users/ezequ/source/repos/RepositorioBase/src/Web/Infrastructure/ProblemDetailsExceptionHandler.cs:17-45
- Malformed JSON gets a 400 carrying a code declared by that endpoint, instead of a generic 400 or a 500. — C:/Users/ezequ/source/repos/RepositorioBase/src/Web/Infrastructure/ProblemDetailsExceptionHandler.cs:26,40-45; C:/Users/ezequ/source/repos/RepositorioBase/src/Web/Infrastructure/ApiProblemMetadata.cs:129-134; C:/Users/ezequ/source/repos/RepositorioBase/src/Web/Endpoints/Identity.cs:34
- Checks are ordered so they cannot leak which emails have accounts. Every password-policy gate reads no stored data and runs before any email lookup or token lookup. This is exactly what makes field-level errors safe to add on the public forms. — C:/Users/ezequ/source/repos/RepositorioBase/src/Application/IdentityAccess/Organizations/RegisterOrganization/RegisterOrganizationHandler.cs:53-59; C:/Users/ezequ/source/repos/RepositorioBase/src/Infrastructure/IdentityAccess/IdentityAccountService.cs:163-174; C:/Users/ezequ/source/repos/RepositorioBase/src/Application/IdentityAccess/Invitations/RegisterInvitedUser/RegisterInvitedUserHandler.cs:16-19,49-52
- Password reset/change is the model to generalize. It answers 400 `validation_failed` with a field key (`newPassword`) that matches the request body, and its messages describe the rule, never the value (IA-REQ-029). A functional test covers it. — C:/Users/ezequ/source/repos/RepositorioBase/src/Infrastructure/IdentityAccess/IdentityCredentialService.cs:37-45; C:/Users/ezequ/source/repos/RepositorioBase/src/Application/IdentityAccess/Credentials/ChangePassword/ChangePasswordHandler.cs:27-29; C:/Users/ezequ/source/repos/RepositorioBase/tests/Application.FunctionalTests/IdentityAccess/Credentials/PasswordLifecycleTests.cs:334-351
- Input is bounded before any work, and inputs that could crash the server are turned into declared 400s instead of 500s. This covers length caps, caught domain ArgumentExceptions, empty-GUID guards and closed-set enum parsing at the endpoint. — C:/Users/ezequ/source/repos/RepositorioBase/src/Application/IdentityAccess/Organizations/RegisterOrganization/RegisterOrganizationHandler.cs:179,185; C:/Users/ezequ/source/repos/RepositorioBase/src/Application/IdentityAccess/Invitations/InviteMember/InviteMemberHandler.cs:61-67,264-273; C:/Users/ezequ/source/repos/RepositorioBase/src/Application/IdentityAccess/People/Documents/DocumentDisputeHandlers.cs:48-52; C:/Users/ezequ/source/repos/RepositorioBase/src/Web/Endpoints/Identity/InvitationEndpoints.cs:98-100; C:/Users/ezequ/source/repos/RepositorioBase/src/Web/Endpoints/Platform/PlatformEndpoints.cs:197-203,234-241
- Paging inputs cannot fail. Limits are clamped, and a cursor the server did not issue restarts the list instead of producing a 500 or acting as a probe. — C:/Users/ezequ/source/repos/RepositorioBase/src/Infrastructure/IdentityAccess/RoleAdministrationStore.cs:23,299-318; C:/Users/ezequ/source/repos/RepositorioBase/src/Infrastructure/IdentityAccess/MembershipAdministrationStore.cs:21,281-299; C:/Users/ezequ/source/repos/RepositorioBase/src/Infrastructure/Platform/PlatformOperationalProjectionReader.cs:169,186-222
- The DNI value object is well specified. It tolerates typed separators, refuses any other character instead of silently dropping it, strips leading zeros before the 7-8 digit check, and masks the digits in ToString so they never reach logs. — C:/Users/ezequ/source/repos/RepositorioBase/src/Domain/IdentityAccess/People/NormalizedDocument.cs:27-59

### error-code-catalogue

Audited on main at 497e087, read-only. The OpenAPI document (src/Web/wwwroot/openapi/v1.json) was dumped with a read-only node script; nothing was written.

**How it works.** Every non-success response goes through one writer: `IProblemDetailsService` in ApiProblemDetailsMapper.cs, lines 34-69. Four producers feed it:
- the exception handler (ProblemDetailsExceptionHandler.cs:17-45);
- the authorization handler (ApiAuthorizationMiddlewareResultHandler.cs:16-27);
- the antiforgery check (Identity.cs:99-100);
- the login budgets (LoginRateLimiting.cs:95-102).

Business failures come from `IdentityAccessErrors`. Each endpoint declares its codes with `WithApiProblemDetails`, and those appear in OpenAPI as `x-problem-codes` (ApiExceptionOperationTransformer.cs:14-29). On the client, problemDetails.js reads the code and ProblemMessage.jsx picks the text from its `MESSAGES` map (lines 9-57). Anything not in the map falls back to "That request could not be completed." (line 65).

**Numbers.**
- 50 distinct codes can reach the client. That is the OpenAPI union, plus runtime-only uses of codes already in it.
- 43 are in `MESSAGES`; 7 are not.
- 0 dead `MESSAGES` keys: every key is a live server code.
- 3 codes are invented by the client.
- 3 server codes never reach the boundary.

**Coverage matrix** (P = ProblemMessage.jsx line; ✗ = user sees the generic fallback)

| Status | Code | Produced at | Shown |
|---|---|---|---|
| 400 | antiforgery_validation_failed | Identity.cs:99-100 | P10; apiTransport.js:53 refreshes the token |
| 400 | validation_failed | ProblemDetailsExceptionHandler.cs:19-22; IdentityAccessErrors.cs:142-143 | P11, plus a flat field list at P69-72 |
| 400 | invalid_request | ProblemDetailsExceptionHandler.cs:44; SelectTenantHandler.cs:28; ContextEndpoints.cs:37 | ✗ |
| 400 | invalid_registration | IdentityAccessErrors.cs:17 | P17 |
| 400 | invalid_confirmation | IdentityAccessErrors.cs:18 | P18 |
| 400 | invalid_invitation | IdentityAccessErrors.cs:28 | P19 |
| 400 | invalid_platform_operation | IdentityAccessErrors.cs:51 | P24; also PlatformIdentitiesPage.jsx:321 |
| 400 | invalid_credential_proof | IdentityAccessErrors.cs:122 | P27 |
| 400 | invalid_credential_token | IdentityAccessErrors.cs:139 | ✗ |
| 400 | invalid_external_login | IdentityAccessErrors.cs:151 | P28 |
| 400 | invalid_role_operation | IdentityAccessErrors.cs:181 | P32 |
| 400 | invalid_membership_operation | IdentityAccessErrors.cs:188 | P33 |
| 400 | invalid_reactivation | IdentityAccessErrors.cs:217 | P38 |
| 400 | invalid_document_dispute | IdentityAccessErrors.cs:259 | P45 |
| 400 | profile_field_not_editable | IdentityAccessErrors.cs:86 | ✗ |
| 401 | authentication_required | ProblemDetailsExceptionHandler.cs:24; ApiAuthorizationMiddlewareResultHandler.cs:20 | P12; dropped for the context read at IdentityProvider.jsx:52 |
| 401 | invalid_session | ApiAuthorizationMiddlewareResultHandler.cs:20; Identity.cs:80-82; IdentityAccessErrors.cs:19 | P13 |
| 401 | credential_superseded | IdentityAccessErrors.cs:115 | P14 |
| 401 | recent_proof_required | IdentityAccessErrors.cs:118 | P26 |
| 401 | recent_mfa_required | IdentityAccessErrors.cs:58 | P25; also the step-up gates at PlatformIdentitiesPage.jsx:157,226 and PlatformRetentionPage.jsx:209 |
| 403 | permission_denied | ProblemDetailsExceptionHandler.cs:25; ApiAuthorizationMiddlewareResultHandler.cs:26; SelectTenantHandler.cs:52 | P15 |
| 403 | email_confirmation_required | IdentityAccessErrors.cs:132 | ✗ |
| 403 | owner_required | IdentityAccessErrors.cs:205 | P37 |
| 403 | self_resolution_refused | IdentityAccessErrors.cs:269 | P47 |
| 403 | identity_reactivation_unavailable | IdentityAccessErrors.cs:287 | P43 |
| 404 | not_found | ProblemDetailsExceptionHandler.cs:23; IdentityAccessErrors.cs:164,178,185,265,279 | P16 |
| 404 | session_not_found | IdentityAccessErrors.cs:103 | ✗ |
| 404 | personal_profile_not_found | IdentityAccessErrors.cs:79 | P51; also PersonalPages.jsx:354 |
| 409 | registration_conflict | IdentityAccessErrors.cs:20 | P21 |
| 409 | invitation_conflict | IdentityAccessErrors.cs:34 | P20 |
| 409 | session_concurrency_conflict | IdentityAccessErrors.cs:67 | P22 |
| 409 | platform_tenant_concurrency_conflict | IdentityAccessErrors.cs:44 | P23 |
| 409 | personal_registration_conflict | IdentityAccessErrors.cs:75 | ✗ |
| 409 | personal_profile_concurrency_conflict | IdentityAccessErrors.cs:82 | ✗ |
| 409 | external_login_conflict | IdentityAccessErrors.cs:158 | P29 |
| 409 | provider_already_linked | IdentityAccessErrors.cs:161 | P30 |
| 409 | role_concurrency_conflict | IdentityAccessErrors.cs:191 | P34 |
| 409 | membership_concurrency_conflict | IdentityAccessErrors.cs:194 | P35 |
| 409 | last_administrator_required | IdentityAccessErrors.cs:201 | P36 |
| 409 | last_authenticator_required | IdentityAccessErrors.cs:208 | P31 |
| 409 | platform_last_owner | IdentityAccessErrors.cs:225 | P39 |
| 409 | identity_concurrency_conflict | IdentityAccessErrors.cs:229 | P42; also PlatformIdentitiesPage.jsx:160 |
| 409 | platform_mfa_concurrency_conflict | IdentityAccessErrors.cs:236 | P44 |
| 409 | retention_hold_subject_purged | IdentityAccessErrors.cs:248 | P53 |
| 409 | retention_hold_conflict | IdentityAccessErrors.cs:251 | P52 |
| 409 | document_dispute_conflict | IdentityAccessErrors.cs:262 | P46 |
| 409 | document_already_recorded | IdentityAccessErrors.cs:276 | P48 |
| 429 | rate_limit_exceeded | LoginRateLimiting.cs:101; IdentityAccessErrors.cs:65,89,129; PlatformAttemptBudgets.cs:60-64 | P54, plus "Try again in N seconds" at P66-68 |
| 503 | service_unavailable | LoginRateLimiting.cs:100; IdentityAccessErrors.cs:96 | P55 |
| 500 | internal_server_error | ApiProblemDetailsMapper.cs:50-69 | P56 |

**Codes that never reach the client**
- Sign-in collapses its internal failures to a neutral 204, by design: `invalid_session` and the lock's `rate_limit_exceeded` (CreateSessionHandler.cs:46,59,75) are turned into 204 at SessionEndpoints.cs:96-98.
- `identity_user_creation_failed` and `identity_user_deletion_failed` (IdentityAccessErrors.cs:7-15) are dead: they are used only by IdentityService.cs:46,83, which has no callers.
- `route_body_id_mismatch` (ApiProblemMetadata.cs:14) is declared but never attached to any endpoint.

**Codes the client invents**
- `unexpected`, set by about 10 screens (for example SessionsPage.jsx:65 and RolesPage.jsx:103). It shows the fallback.
- `context_unreadable` (IdentityProvider.jsx:49). It shows the fallback on LoginPage.jsx:100 and InvitationPages.jsx:139.
- `internal_server_error` with status 0 (useSubmit.js:22, usePlatformRead.js:28). It shows "Something went wrong. Try again."

**The product owner's trigger scenario.** Registering an organization with an address that already has an account gets the neutral 202. That is required by SPEC.md:107 and :243, and the page explains it (RegisterOrganizationPage.jsx:38-40). It is not reported as a gap.

What is a gap is malformed input on the same form, and the other wrong-code and wording problems in the findings.

**Keep:**

- One problem shape with enforced invariants: every expected failure must carry a stable code; field errors are allowed only for Validation; Retry-After only for RateLimited/Unavailable. The shape is not open-ended. — src/Application/Common/Models/ApplicationError.cs:18-36; src/Application/Common/Models/ApplicationErrorCategory.cs:7-22
- A single writer serves every source of non-success: business Results, exceptions, authorization challenge/forbid, antiforgery and login budgets. One category-to-status switch, a traceId on every response, Retry-After from the error, and a sanitized 500 with code internal_server_error and no detail. — src/Web/Infrastructure/ApiProblemDetailsMapper.cs:34-83; ProblemDetailsExceptionHandler.cs:17-37; ApiAuthorizationMiddlewareResultHandler.cs:16-44; Web/Infrastructure/Identity/LoginRateLimiting.cs:95-102; Web/Endpoints/Identity.cs:99-100
- Each endpoint declares its own problem codes, and they are published to OpenAPI as x-problem-codes, with Retry-After on budget routes. A body the framework cannot bind is answered with a code the endpoint chose, so a neutral route never leaks a framework message or a different code. — src/Web/Infrastructure/ApiProblemMetadata.cs:104-134; ApiExceptionOperationTransformer.cs:14-39; ProblemDetailsExceptionHandler.cs:40-45
- Enumeration-sensitive refusals are collapsed into one code each, and the reason is documented where the code is defined. Sign-in stays a neutral 204, with credential_superseded as the one argued exception. — src/Application/IdentityAccess/Common/IdentityAccessErrors.cs:22-28, 46-51, 134-139, 145-151, 210-217; src/Web/Endpoints/Identity/SessionEndpoints.cs:90-99
- 429 and 503 are kept distinct (IA-REQ-057) and applied consistently: an exhausted budget answers rate_limit_exceeded, an unreachable store answers service_unavailable, and both carry Retry-After. — src/Web/Infrastructure/Identity/LoginRateLimiting.cs:95-101; src/Application/IdentityAccess/Platform/PlatformAttemptBudgets.cs:50-66; ApiProblemMetadata.cs:25,32
- The client reads problems strictly. It refuses anything that is not problem+json, requires a code, drops detail on 5xx and parses Retry-After. The on-screen text is chosen by code, never taken from server detail. antiforgery_validation_failed refreshes the token without replaying the request. — src/Web/ClientApp/src/features/identity/api/problemDetails.js:22-49; features/identity/ProblemMessage.jsx:5-8, 65; features/identity/api/apiTransport.js:49-54
- All 43 MESSAGES entries map live server codes; there are no dead UI entries. The wording for collapsed codes stays neutral. — ProblemMessage.jsx:9-57 checked against the x-problem-codes union in src/Web/wwwroot/openapi/v1.json (50 codes); ProblemMessage.jsx:38 (invalid_reactivation), :40-42 (identity_concurrency_conflict)

### frontend-presentation

Scope: every screen and component under src/Web/ClientApp/src, read as the working tree on disk in C:/Users/ezequ/source/repos/RepositorioBase (branch main). That tree has uncommitted edits, including LoginPage.jsx, AppRoutes.jsx, NavMenu.jsx and Home.jsx. Everything below comes from reading the code; I did not run the SPA or the tests.

HOW ERRORS FLOW TODAY
1. Transport: apiTransport.send turns every non-2xx response into one of two things. A problem+json response becomes an ApiProblem. Anything else (a proxy's HTML page, bad JSON, a network TypeError) becomes a plain Error. There is no central reaction to any status code; the only special case is re-bootstrapping the antiforgery token on antiforgery_validation_failed.
2. Submitting: useSubmit tracks isBusy, problem and result. A non-problem failure becomes { code: 'internal_server_error' }. Roughly half the screens do not use useSubmit and catch errors themselves as { code: 'unexpected' }. That code is not in the message catalogue, so it shows the fallback text.
3. Display: ProblemMessage looks the code up in a code-to-message map and renders it in an MUI Alert (role="alert" by default). When Retry-After is present it adds a static "Try again in N seconds" line. Field errors appear only as a flat "rawKey: messages" list inside that Alert. No TextField anywhere binds server errors (error/helperText), and nothing moves focus.
4. Reads: usePlatformRead reports loading / refused / errored / loaded. It treats any problem document as "refused", including a 500.
5. Session: IdentityProvider clears the session context only when the context read fails, on sign-out, or on deactivation. ProtectedRoute redirects to /login?returnUrl only when a route is entered with no context.
6. There is no ErrorBoundary.

/organizations/register — the form the product owner used
- Invalid CUIT:
  - Letters or not 11 digits: the server answers 400 invalid_registration with no `errors`. The user sees one Alert at the top of the card, "Check the details and try again.", and no field is marked.
  - 11 digits with a wrong check digit: nothing checks it, in the client or in NormalizedCuit. The user gets the neutral success message.
- Malformed email: a missing "@" is caught by the browser's native bubble (type=email; the form has no noValidate). "a@b" passes both the browser and the server, which only checks for an "@", and gets the neutral success.
- Password the policy refuses: the same generic invalid_registration Alert. No rule is shown and there is no client-side hint.
- Network failure or proxy HTML: "Something went wrong. Try again." No reference ID is shown; the button re-enables and the form keeps its values.
- Email that already has an account (the product owner's case): neutral 202 and "If that address can register, we have sent it a confirmation link. Check the inbox." That neutrality is correct and required. But the page gives no next step, even though the email the owner receives says to sign in and register from there.

PER-SCREEN MATRIX
Columns: (a) client validation | (b) server field errors | (c) business-problem placement | (d) non-problem failure | (g) 401 mid-flow | (h) busy state.

Behaviour shared by every screen:
- (e) A 5xx shows "Something went wrong. Try again." with detail and traceId suppressed.
- (f) A 429 shows "Too many attempts. Wait a moment and try again." plus a static "Try again in N seconds"; the submit re-enables immediately.
- (i) Errors are announced through the MUI Alert role="alert"; focus is never moved.

Abbreviations:
- ISE: the failure becomes internal_server_error, shown as "Something went wrong. Try again."
- UNX: the failure becomes 'unexpected', which is not catalogued and shows "That request could not be completed."
- STALE: the Alert says "Sign in to continue." with no link; the stale context stays and /login bounces back into the app.
- PM: the ProblemMessage Alert.

PUBLIC ENTRANCE CARDS (the Alert sits at the top of a short card, so it is visible; (g) does not apply)
- /login: (a) HTML5 only | (b) none; a refusal shows the neutral "Those details did not sign you in" | (d) ISE for the submit, UNX for "Continue with Google" | (h) submit disabled while busy; the Google button is never disabled | 429/503 on sign-in shows the Retry-After line.
- /register (ChooseContextPage): no inputs and no requests.
- /organizations/register: (a) HTML5 only; no CUIT format or check digit, no password rules | (b) the server sends none, so a generic message | (d) ISE | (h) disabled, no progress indicator | the neutral success has no sign-in path.
- /personal/register: (a) HTML5 plus a numeric keypad for DNI, no DNI shape check | (b) generic invalid_registration | (d) ISE | (h) disabled.
- /confirm-email: missing token disables the button and explains why (good) | (d) ISE | personal_registration_conflict is uncatalogued, so the fallback text.
- /credentials/forgot: HTML5 | (d) ISE | (h) disabled.
- /credentials/reset: HTML5; missing token disables and explains | (b) flat "newPassword: <ASP.NET Identity text>" | an expired or used link (invalid_credential_token) gets the fallback text with no link to request a new one | (d) ISE.
- /account/reactivation-request: HTML5 | (d) ISE | (h) disabled.
- /account/reactivate: HTML5; missing token disables and shows a warning | (d) ISE.
- /invitations/register: HTML5; can be submitted without a token and gets the neutral success | (d) ISE.
- /platform/invitations/register: HTML5; can be submitted without a token and gets a flat "Token: 'Token' must not be empty." | (d) ISE.
- /platform/invitations/confirm: button enabled without a token; gets a flat "ConfirmationToken: 'Confirmation Token' must not be empty." | (d) ISE.
- /platform/bootstrap/recover: no inputs | (d) ISE | 429 shown with the static Retry-After line.

SHELL SCREENS ((g) is STALE for all of them)
- /identity (IdentityContextPage): makes no requests; shows whatever context is held, even a stale one.
- /identity/profile:
  - Load failure: UNX, no retry. Save failure: ISE.
  - profile_field_not_editable and personal_profile_concurrency_conflict are uncatalogued.
  - A refused save throws away what the user typed.
  - AddPersonalContext: personal_registration_conflict is uncatalogued; 429 shown statically.
  - DocumentDispute clears the typed number on submit (deliberate).
- /identity/account: HTML5 plus the confirmation checkbox gate | (d) UNX | email_confirmation_required is uncatalogued | (h) disabled.
- /identity/sessions: buttons wait for the password (canBegin) | (d) UNX | a failed first load leaves the "Loading" skeleton up forever with no retry | session_not_found is uncatalogued | (h) disabled.
- /identity/password: HTML5 | (b) flat "newPassword: …" | (d) UNX | (h) disabled.
- /identity/external: no validation; "Link" works with an empty password and gets invalid_credential_proof | (d) UNX | a failed first load leaves the skeleton up forever.
- /external/return: (d) UNX | ends at "You can try again." with no control to do so.
- /organizations/select: (d) ISE | (h) LinearProgress plus disabled buttons.
- App-bar ContextSwitcher: no error display and no busy state; the failure is an unhandled promise rejection.
- /members/invite: HTML5 email; a refused roles or invitations read is handled per part | (d) UNX | the Alert sits above the form, far from the per-row Resend/Withdraw buttons | (h) spinner on the pressed button.
- /roles: HTML5 name plus the password gate | (d) UNX | a failed first load shows "Loading…" forever | the Alert is at the top of the page while the editor's submit is at the bottom.
- /members: password gate | (d) UNX | a failed first load leaves the skeleton up forever | the Alert is at the top, row actions far below.
- /invitations/accept: sign-in gate; the token is not checked | (d) ISE | (h) status spinner.
- /platform:
  - HTML5 invite email; no format check on the step-up code.
  - (b) flat "Code: …" / "Email: …".
  - Row actions report to an Alert at the top of the page.
  - A failed directory read shows an Alert with no retry.
  - A successful administrator invitation shows nothing.
  - "More organizations" is never disabled.
- /platform/identities: no inputs | "Try again" appears only for non-problem failures, so a real 500 or 503 gets none | (h) LinearProgress.
- /platform/retention: the one custom format check (regex, helperText, "Nothing was sent"), though the message still appears in the top Alert rather than on the field | same retry gap as /platform/identities.
- /platform/mfa (and the enrollment embedded in the register page): no code format check | "Begin enrollment" is enabled without a token and gets a flat "Token: …" | failures of the reload or tenant selection after acknowledging are unhandled.
- /platform/mfa/recover: HTML5 | (b) flat "RecoveryCode: …" | both fields are cleared on every submit (deliberate).
- NavMenu "Log out": a failure is unhandled, but ProtectedRoute recovers because the context is cleared.
- ProtectedRoute: returnUrl works only when a route is entered after the context is already gone.

**Keep:**

- Strict RFC 9457 reader. It refuses non-problem bodies and problems without a code, drops `detail` on 5xx and reads Retry-After. The user never sees server-authored or diagnostic text. — src/Web/ClientApp/src/features/identity/api/problemDetails.js:22-49 (line 46 suppresses detail when status >= 500; line 47 parses Retry-After)
- The wording belongs to the client. A stable code picks the message from one catalogue, so provider and framework text cannot leak through `detail` (in line with IA-REQ-029/038). — src/Web/ClientApp/src/features/identity/ProblemMessage.jsx:9-57, 65
- useSubmit gives one lifecycle for busy, problem and result. It clears the old problem when a retry starts, and most submit buttons disable while busy. — src/Web/ClientApp/src/features/identity/useSubmit.js:8-30; e.g. RegisterOrganizationPage.jsx:89, PasswordPages.jsx:75,142
- Enumeration-safe neutral wording is consistent across the public flows, including a neutral sign-in refusal. This matches IA-REQ-003/019/048. — RegisterOrganizationPage.jsx:37-41; PersonalPages.jsx:57-59; PasswordPages.jsx:55-57; AccountLifecyclePages.jsx:178-180; InvitationPages.jsx:50-52; PlatformInvitationPages.jsx:103-106,380-382; LoginPage.jsx:72-75,101-103
- One transport holds the antiforgery token. It refreshes the token on antiforgery_validation_failed and does not replay the mutation. — src/Web/ClientApp/src/features/identity/api/apiTransport.js:24-55
- Errors are announced: MUI Alert defaults to role="alert". Confirmations deliberately use role="status" so a refusal and a success never compete. — src/Web/ClientApp/node_modules/@mui/material/Alert/Alert.js:167; PasswordPages.jsx:203-209
- A pattern worth copying for mailed-link screens: when the token is missing, the page explains why and disables the action. — ConfirmEmailPage.jsx:40-44,64; PasswordPages.jsx:111-115,142; AccountLifecyclePages.jsx:240-244,257
- A read-state model worth copying: usePlatformRead separates loading, refused, errored and loaded, and PlatformIdentitiesPage renders empty, refused and failed differently, with a Try again button. — src/Web/ClientApp/src/features/platform/shared/usePlatformRead.js:14-49; PlatformIdentitiesPage.jsx:352-379
- The only client-side format validation, and a model for CUIT and DNI checks. It mirrors the server's rule, shows the rule as helperText and says "Nothing was sent". — src/Web/ClientApp/src/features/platform/retention/PlatformRetentionPage.jsx:77-78,144-149,393-415
- Codes are explained in context: a code that means several things gets a specific explanation next to the control that resolves it. Partial read refusals are told apart from empty lists. — PlatformIdentitiesPage.jsx:317-326; InviteMemberPage.jsx:94-103,237-244,287-294; PersonalPages.jsx:393 (no skeleton once the load has failed)

### cross-cutting-runtime

Scope: runtime failure handling that cuts across the whole system, not tied to any one feature. I read the main checkout's working tree on branch main. It has uncommitted edits (LoginPage.jsx, NavMenu.jsx, AppRoutes.jsx, among others), so line numbers are working-tree numbers. The NavMenu sign-out and tenant-switch code I cite is the same at HEAD (lines 88-92 and 166-168). I changed nothing and ran no tests. The only commands I ran were read-only: git ls-files/show/diff, dotnet --list-runtimes, and greps over the installed .NET ref pack and the Aspire package docs.

HOW IT WORKS TODAY

Backend:
- One RFC 9457 writer, ApiProblemDetailsMapper, serves Result mapping, the global IExceptionHandler (ProblemDetailsExceptionHandler, registered at Program.cs:47, before UseAuthentication at :52) and the login budget middleware.
- An unknown exception becomes a 500 internal_server_error with no detail. Its traceId is Activity.TraceId, the same value MediatR logging records as CorrelationId.
- Background work:
  - OutboxWorker is a separate generic host and runs only when Email:Enabled.
  - LocalOutboxDeliveryService runs in-process for a local drop folder.
  - Both loop over OutboxDispatcher.
  - LifecycleMaintenanceService runs every 15 minutes.
  - All of them catch a failed pass and keep going.
- Startup:
  - The migration hosted service calls ApplicationDbContextInitialiser, which logs 'An error occurred while initialising the database.' with the exception and rethrows, so the host stops. That is how a real fault differs from the expected EF Error on the __EFMigrationsHistory probe, which CLAUDE.md:71-74 documents as expected.
  - EmailReadiness and IdentityDeploymentGuard refuse to start on contradictory configuration.
  - Platform bootstrap logs and continues.

Frontend:
- One transport (apiTransport.js), shared by the identity and platform clients, with a strict problem reader.
- useSubmit and 19 hand-written catch blocks feed ProblemMessage's code-to-message map.
- There is no error boundary, no global rejection handler, no offline handling and no global 401 handling.

COVERAGE MATRIX (concern | state | key evidence)
- Exception thrown while React renders | MISSING, the whole screen goes blank | main.jsx:13-19, App.jsx:9-25
- Unhandled promise rejections | PARTIAL: most screens catch, 3 handlers do not, no global listener | NavMenu.jsx:88-92 and 165-168, PlatformInvitationPages.jsx:343-353
- Global 401 / session expiry | MISSING: only an inline message; the signed-in shell stays | apiTransport.js:49-54, IdentityProvider.jsx:29-67, ProtectedRoute.jsx:13-19
- antiforgery_validation_failed | PRESENT and matches SPEC section 8 (fetch a fresh pair, do not replay) | apiTransport.js:52-54, SPEC.md:313, IdentityProvider.test.jsx:133-156
- Offline / network loss | MISSING: reported as a server fault, with two different texts | useSubmit.js:21-22, ProblemMessage.jsx:56 and :65
- traceId shown to the user | MISSING: parsed, never rendered | problemDetails.js:42, ProblemMessage.jsx:59-76
- Unexpected exception, HTTP response | PRESENT: safe 500 with traceId | ProblemDetailsExceptionHandler.cs:30-34, ApiProblemDetailsMapper.cs:50-69
- Unexpected exception, log | GAP: on .NET 10 nothing is logged outside MediatR; inside MediatR only the type; expected 400/401/403 are logged at Error | Program.cs:47, ref-pack XML:153-173, Application DependencyInjection.cs:16-18
- Outbox retry, backoff, dead-letter | PRESENT: lease plus compare-and-swap, backoff from 30 s doubling to a 30 min cap, 8 attempts, 24 h window, then Abandoned with FailureCode and a metric | OutboxDispatcher.cs:20-23, 147-152, 175-189
- Background failure logging and correlation | PARTIAL: sibling loops log the type; OutboxWorker logs no exception information at all; OutboxMessage stores no correlation | Worker.cs:36-42, OutboxMessage.cs:9-31
- Startup faults | PRESENT: fail-fast versus continue is chosen deliberately per service | ApplicationDbContextInitialiser.cs:24-33, IdentityDeploymentGuard.cs:44, PlatformBootstrapHostedService.cs:34-39
- Health checks | GAP: a DbContext check is registered, but /health and /alive are mapped only in Development; elsewhere they fall through to the SPA page | ServiceDefaults/Extensions.cs:104-121, Program.cs:63
- Database outage, as seen by the caller | GAP: 500 internal_server_error with no Retry-After; only the attempt-budget store answers 503 | ProblemDetailsExceptionHandler.cs:17-28 versus LoginRateLimiting.cs:99-102
- Mail outage, as seen by the caller | PRESENT by design: it never reaches the request (outbox), and the neutral 202/204 answers are unaffected | OutboxDispatcher.cs:136-143
- Closed admission (restored deployment) | PRESENT: 503 with Retry-After, but the body has no traceId | RecoveryAdmissionMiddleware.cs:58-67
- Unknown /api route | GAP: answers 200 with index.html | Program.cs:63

**Keep:**

- One RFC 9457 writer with a stable code and traceId, and a safe 500 that says nothing about the fault. It sits before authentication, so faults during authentication also get problem+json. — ApiProblemDetailsMapper.cs:14-32 (Create), :34-48 (WriteAsync sets Retry-After at :39-42), :50-69 (WriteUnexpectedAsync writes no detail); ProblemDetailsExceptionHandler.cs:17-28 maps known exception types to categories; Program.cs:47 UseExceptionHandler comes before Program.cs:52 UseAuthentication; ProblemDetailsContractTests.cs:237-250 checks that the 500 body carries no token and no password.
- The traceId in each problem is the same value the MediatR logs record as CorrelationId, so the join key already exists. Only the UI fails to show it. — ApiProblemDetailsMapper.cs:85-86 (Activity.Current.TraceId); SafeRequestLogContext.cs:8-9 (CorrelationId = Activity.Current.TraceId); UnhandledExceptionBehaviour.cs:21-22 logs it.
- The shared attempt-budget store tells an outage apart from a real exhausted budget: an outage answers 503 service_unavailable, an exhausted budget answers 429 rate_limit_exceeded, and both carry Retry-After through the shared writer. This is the pattern to generalise to other dependencies. — PostgreSqlAttemptBudget.cs:73-78 (NpgsqlException/InvalidOperationException/TimeoutException become Unavailable, failing closed); LoginRateLimiting.cs:95-103; SPEC.md:182 (IA-REQ-057).
- Outbox delivery is robust. Messages are claimed with a lease using FOR UPDATE SKIP LOCKED plus a generation compare-and-swap. Retries back off from 30 s, doubling each attempt up to 30 min, for at most 8 attempts inside a 24-hour window. Provider answers are classified as permanent or transient. Dead-lettering means Abandoned plus a FailureCode, with the secret envelope terminalized. The settlement metric uses only closed labels. Neither a closed admission nor a bad mail configuration can use up attempts. — OutboxDispatcher.cs:20-23, 27-36, 47-80, 82-84, 99-100, 147-152, 161-168, 175-180, 189; IdentityEmailAdapter.cs:83-102; IdentityAccessMetrics.cs:37-39 and 59-60.
- The background loops survive failed passes, honour shutdown, and keep personal data out of logs (IA-REQ-029). — Worker.cs:27-42; LocalOutboxDeliveryService.cs:41-54; LifecycleMaintenanceService.cs:32-55: each catches OperationCanceledException only while stopping, and otherwise logs and keeps looping.
- Each startup service deliberately chooses to fail fast or continue, and an idle worker says so instead of staying silent. — ApplicationDbContextInitialiser.cs:24-33 logs with the exception and rethrows; IdentityDeploymentGuard.cs:34-46 and 51-82 and EmailReadiness (Infrastructure DependencyInjection.cs:208-221) refuse to start on contradictory configuration; PlatformBootstrapHostedService.cs:13-18 and 34-39 log and continue; OutboxWorker/Program.cs:12-19 and 30-38 (IdleAnnouncement warning); CLAUDE.md:71-74 documents the expected EF __EFMigrationsHistory Error.
- Recovery from antiforgery_validation_failed follows SPEC section 8 and is tested: the client fetches a fresh token pair and does not replay the mutation, so a sign-in never runs twice. — apiTransport.js:21-22 (rationale) and :52-54; ProblemMessage.jsx:10; IdentityProvider.test.jsx:133-156 ('refreshes the pair on antiforgery_validation_failed and does not replay the mutation'); SPEC.md:313.
- The client reads problems strictly: it refuses failures without a code or without the problem+json type, hides 5xx detail, and polices success envelopes. usePlatformRead already separates 'refused' from 'errored', which is the right model for every screen. — problemDetails.js:22-49 and 56-101; usePlatformRead.js:25-33.
- A database fault during cookie validation propagates as an error instead of being treated as an invalid session, so a database blip does not delete everyone's cookie and sign them out. — SessionCookieEvents.cs:34-43 (database reads with no catch) versus :180-187 (RejectAsync, which deletes the cookie, runs only on explicit rejection conditions).

### error-path-tests

WHAT EXISTS. Error paths are tested in five places:
- Backend functional tests over HTTP (tests/Application.FunctionalTests), through two paths: the default Development host with test doubles, and a Production harness with no doubles (IdentityHttpHarness.CreateProductionHarness).
- Application-level Result assertions via TestApp.SendAsync.
- Unit tests for Result, ApplicationError and ValidationException.
- Integration tests for outbox and delivery failures.
- SPA vitest+MSW tests (setup.js:24 sets onUnhandledRequest:'error'), plus the RFC 9457 reader test problemDetails.test.js and a few Playwright acceptance scenarios.
Rough count of status-code assertions across the functional and integration tests (git grep of HttpStatusCode.X): 400=53, 401=62, 403=15, 404=8, 409=31, 429=6, 500=7, 503=8. 405, 415 and 413 have none.

PROVEN AT THE HTTP BOUNDARY (runtime problem+json with code):
- 400 antiforgery_validation_failed: 7 bad origin/token combinations with zero outbox effects (ProblemDetailsContractTests.cs:61-90).
- 400 invalid_confirmation (ProblemDetailsContractTests.cs:58).
- 400 invalid_registration: malformed input and binding failures (RegistrationHttpValidationTests.cs:15-108).
- 400 invalid_invitation, including empty GUIDs never becoming a 500 (InvitationHttpContractTests.cs:93-157).
- 400 invalid_request (SessionTests.cs:671, 912).
- 400 validation_failed, code only (PasswordLifecycleTests.cs:348).
- 401 authentication_required and 403 permission_denied from the endpoint-level authorization handler (ProblemDetailsContractTests.cs:175-191).
- 401 invalid_session (RegistrationHttpValidationTests.cs:111-128).
- 404 session_not_found (SessionManagementTests.cs:155).
- 429 rate_limit_exceeded with Retry-After (ProblemDetailsContractTests.cs:144-162 through the mapper; SharedAbuseControlTests.cs:82).
- 503 service_unavailable with Retry-After=30 (SharedAbuseControlTests.cs:85-101).
- Sanitized 500 internal_server_error on two routes (ProblemDetailsContractTests.cs:210-255; SessionTests.cs:69-98, production harness, rollback checked).
- OpenAPI x-problem-codes exact arrays, and statuses a route must NOT declare, for registration/confirm, invitations, context, sessions and the Platform MFA/directory routes (OpenApiContractTests.cs:37-243).
- Generic not_found is proven only as an Application Result (DocumentDisputeTests.cs:189; PlatformIdentityLifecycleTests.cs:223).

VALIDATION RULES PROVEN:
- Registration: null or malformed email, weak password, non-digit CUIT, oversized email/password/legal name, null password/legal name. Each returns invalid_registration with no durable effects and no secret echoed back (RegistrationHttpValidationTests.cs:79-108; RegistrationInputValidationTests.cs:17-56).
- Body binding: malformed JSON, missing body, wrong types and trailing-slash variants map to the endpoint's code, but only for register and confirm-email (RegistrationHttpValidationTests.cs:15-77).
- CUIT digits and length (OrganizationProfileTests.cs:20-23).

UI ERROR PRESENTATION PROVEN:
- About 25 code-to-message mappings asserted by rendered text (e.g. ExternalAccountsPage.test.jsx:84,98; PlatformPanel.test.jsx:181-204; PlatformIdentitiesPage.test.jsx:226-244).
- Retry-After seconds shown to the user (PlatformInvitationPages.test.jsx:243-254; materialUiMigration.contract.test.js:234-253).
- Antiforgery token refreshed without replaying the mutation (IdentityProvider.test.jsx:133-157).
- A non-problem failure on a READ shows "something went wrong" with a retry (PlatformIdentitiesPage.test.jsx:442-458; PlatformRetentionPage.test.jsx:248-264).
- Neutral acknowledgements (PasswordPages.test.jsx:20-35; acceptance IdentityAccessPages.cs:101-102).

NOT PROVEN:
- The field-indexed errors member: present, keyed, or rendered.
- Any FluentValidation validator's failure path.
- The CUIT check digit.
- Field-level registration feedback.
- The PUT /api/identity/profile validation contract.
- A React-side check of the code catalogue against the API.
- The generic fallback message for unmapped codes.
- Network failure and non-problem failures on mutations.
- A mutation answered with 401 invalid_session mid-flow.
- Application-level ForbiddenAccessException and NotFoundException becoming HTTP problem+json.
- The RFC 9457 status member.
- 405, 415, 413 and unknown /api routes.

**Keep:**

- A strict RFC 9457 client reader whose tests refuse drift instead of absorbing it — problemDetails.test.js:22-88 checks 7 statuses, refuses HTML/502, JSON without the problem media type, and a problem without a code; strips detail from a 500; parses Retry-After; refuses internal-Result, envelope and pagination shapes. The reader is at problemDetails.js:22-49.
- Body-binding failures map to the endpoint's own code, with no JSON parser text, no secret echoed back and zero durable effects, independent of environment — RegistrationHttpValidationTests.cs:15-77 (sentinel secrets, the text 'JSON' asserted absent, RegistrationSubmission and Outbox counts stay 0). DependencyInjection.cs:33-34 sets ThrowOnBadRequest = true, so the path does not depend on Development defaults.
- The sanitized 500 is proven on routes that really persist, including rollback and a clean retry — ProblemDetailsContractTests.cs:210-255 arms the fault on the issue route because acceptance returns before saving, and asserts no token, 'password' or 'Exception' in the body. SessionTests.cs:69-98 uses the production harness: 500 internal_server_error, no Set-Cookie, zero sessions and audits, and the retry succeeds once.
- Antiforgery refusal is exhaustively proven before any business effect — ProblemDetailsContractTests.cs:61-90 covers missing, cross-origin, http, wrong-port and missing-cookie origins plus null and wrong tokens, with the outbox count unchanged; host isolation at :93-113.
- OpenAPI tests pin exact code arrays and also the statuses a route must NOT advertise; runtime/document agreement is checked for the invitation routes — OpenApiContractTests.cs:111-153 and 215-243 (including Retry-After required on the login 429); ProblemDetailsContractTests.cs:164-204 is the runtime counterpart.
- Rate-limit and outage are separate, distinguishable answers, and both carry Retry-After — SharedAbuseControlTests.cs:85-101: 503 service_unavailable, Retry-After 30s, no credential checked. ProblemDetailsContractTests.cs:144-162: 429 has Retry-After and no success envelope.
- Neutrality is tested as neutrality, at the API and in the UI — InvitationHttpContractTests.cs:50-63: bodyless 202 for an unknown token. OpenApiContractTests.cs:132-140: the public register route must not declare 401/403/404/409/429. IdentityAccessPages.cs:101-102: acceptance asserts the neutral status text. PasswordPages.test.jsx:20-35.
- Registration refusals are backed by a positive control, so they cannot pass by refusing everything — RegistrationInputValidationTests.cs:40-56 checks that valid input still writes one submission, one intent and one outbox row. The refusals check 9 entity counts at :58-69.
- SPA tests cannot silently fall through to the network, and most UI refusals assert the rendered wording — setup.js:22-24 (onUnhandledRequest:'error'); identityServer.js:29-33 is a shared problem() builder; about 30 findByRole('alert').toHaveTextContent assertions across the feature tests.
- Background delivery failure paths are well covered — OutboxDeliveryTests.cs:121-341: capped exponential backoff, permanent after 8 attempts, provider exception never stored in a column, secret cleared on every exhausted failure, no ninth attempt after crashes.
