# Account Setup Specification

## Purpose

A signed-in identity whose context reports `setupRequired` `true` (`identity-context-contract`) finishes setting up
its account at `/identity/setup`: it chooses Personal ("A personal account") or Company ("An organization"), then
completes that type's existing form. One SPA guard sends every other route that requires a session there, and the
person resumes afterwards. This is functional work: the `*.test.jsx`, page-object, `AppRoutes.jsx` and
`features/*/api/` edits the proposal declares are allowed (CLAUDE.md), visual contracts still bind the reused forms,
and no error code is added. Invitation listing, email codes, AFIP/ARCA lookups and visual redesign are out of scope.

## Requirements

### Requirement: One setup guard

Sources: pre-proposal `gate`; `ProtectedRoute.jsx`, `AppRoutes.jsx`, `LoginPage.jsx`.

- The SPA MUST apply one guard to every route that requires a session (every route that sends an anonymous visitor to
  `/login?returnUrl=…`), and MUST NOT redirect a route in the closed exemption list.
- When the loaded context reports `setupRequired` `true` on a guarded route, the guard MUST replace the history entry
  with `/identity/setup?returnUrl=<percent-encoded path and query>`, built from the router location like the sign-in
  redirect.
- It MUST decide from `setupRequired` alone and MUST NOT act while the context loads. Visitors without a session keep
  today's sign-in redirect.
- Routes reachable without a session, such as `/login`, `/register`, `/organizations/register` and `/confirm-email`,
  are outside the guard, so a mailed link's fragment token is never lost.

#### Scenario: A guarded route sends the person to setup

- GIVEN a signed-in context with `setupRequired` `true`
- WHEN the person opens `/members?q=1`
- THEN the location is replaced by `/identity/setup?returnUrl=%2Fmembers%3Fq%3D1`

#### Scenario: Every non-exempt route that requires a session is guarded

- GIVEN a signed-in context with `setupRequired` `true`
- WHEN the person opens `/identity`, `/identity/profile`, `/organizations/select`, `/members/invite`, `/roles`,
  `/members`, `/platform`, `/platform/identities` or `/platform/retention`
- THEN each redirects to `/identity/setup` with its own path as `returnUrl`, and none of those screens renders

#### Scenario: No redirect without the signal

- GIVEN a signed-in context with `setupRequired` `false` and an empty `availableTenants`, because the only membership
  is suspended
- WHEN the person opens any guarded route
- THEN no redirect happens

#### Scenario: Nothing is decided while the context loads

- GIVEN a context read that has not answered
- WHEN a guarded route is opened
- THEN nothing renders and no redirect happens until the read settles

#### Scenario: A visitor without a session signs in first

- GIVEN no session
- WHEN the visitor opens `/identity/setup` or a guarded route
- THEN they are sent to `/login?returnUrl=<that path and query>`, as today

#### Scenario: A mailed link is not redirected

- GIVEN a signed-in context with `setupRequired` `true`
- WHEN the person opens `/confirm-email#token=…`
- THEN the confirmation screen renders with its token

### Requirement: Closed exemption list

Sources: pre-proposal `gate` (`exempt_routes_closed_list`, `not_exempt`).

The guard MUST exempt exactly these nine routes; changing the list is a change of its own. `/identity/profile` MUST NOT
be exempt, because its add-personal-context form would be a second setup path.

| Route | Why |
| --- | --- |
| `/identity/setup` | the guard's destination |
| `/platform/mfa` | Platform invitee enrollment, whose token is in the URL fragment |
| `/platform/mfa/recover` | Platform authenticator recovery |
| `/invitations/accept` | acceptance creates the membership that ends setup |
| `/external/return` | finishes a provider round trip and picks its own destination |
| `/identity/account`, `/identity/sessions`, `/identity/password`, `/identity/external` | self-service that belongs to the identity, not to a tenant |

#### Scenario: Exempt routes are never redirected

- GIVEN a signed-in context with `setupRequired` `true`
- WHEN the person opens any of the nine exempt routes
- THEN the guard does not redirect it

### Requirement: Setup screen

Sources: pre-proposal `data_step`; `ChooseContextPage.jsx`, `Layout.jsx`, `ui-composition-rules.md`.

- `/identity/setup` MUST require a session and render in the application shell, not as a public-entry route.
- When the loaded context reports `setupRequired` `false`, it MUST render no step and replace the location with the
  resume destination.
- Otherwise it MUST have one `h1` (`identity:setup.title`) and two steps in order: the type choice, with
  `identity:setup.subtitle` and the Personal and Company options named and described by the existing
  `identity:register.choose.personal.*` and `identity:register.choose.organization.*` values; then the chosen type's
  data step.
- Choosing an option MUST open its data step inside `/identity/setup`, without navigating to `/personal/register` or
  `/organizations/register` and without any request.
- The data step MUST name its type with the option's title and offer `identity:setup.changeType`, which returns to the
  type choice and sends nothing.
- A preselected type, such as one carried by the create-account Google round trip or set by the signed-in
  `/personal/register` redirect, MUST open on its data step with the change-type action. It is not authoritative.

#### Scenario: The type choice opens first

- GIVEN `setupRequired` `true` and no preselected type
- WHEN `/identity/setup` renders
- THEN its only `h1` reads "Finish setting up your account", followed by "Choose the kind of account to set up before
  you continue."
- AND the options read "A personal account" ("For yourself. You will be asked for your name and your DNI.") and "An
  organization" ("For a company. You will be asked for its CUIT and legal name.")

#### Scenario: Choosing and changing the type sends nothing

- GIVEN the type choice
- WHEN the person chooses "An organization" and then presses "Change account type"
- THEN the path stayed `/identity/setup`, the Company data step showed under "An organization", the type choice shows
  again, and no request was sent

#### Scenario: A preselected type opens its data step

- GIVEN `/identity/setup` opens with Personal preselected
- WHEN it renders
- THEN the Personal data step shows directly, with "Change account type"

#### Scenario: Setup that is not required is skipped

- GIVEN `setupRequired` `false`
- WHEN `/identity/setup?returnUrl=%2Fmembers` opens
- THEN the location is replaced by `/members` and no step renders

### Requirement: Data steps reuse the existing forms

Sources: pre-proposal `data_step`; `PersonalPages.jsx`, `RegisterOrganizationPage.jsx`, `PersonalEndpoints.cs`,
`Identity.cs`.

- The Personal step MUST be the existing add-personal-context form: DNI (`add-personal-document`), Full name
  (`add-personal-full-name`) and Display name (`add-personal-display-name`), submitted by "Add my personal account" to
  `POST /api/identity/personal` with `documentNumber`, `fullName` and `displayName` only.
- The Company step MUST be the existing signed-in organization form: CUIT (`register-cuit`) and Legal name
  (`register-legal-name`), submitted by "Register" to `POST /api/identity/organizations/register` with today's body:
  `cuit`, `legalName`, and an empty `email` and `password`.
- Reuse MUST NOT change any id, name, `data-testid`, role, label, `type`, `autoComplete`, `required`, disabled
  expression, helper text or `en` string, on setup or on `/identity/profile` and `/organizations/register`, which keep
  their headings. Only the CUIT- or DNI-first order changes (`account-creation-entry`).

#### Scenario: The Personal step sends the existing personal claim

- GIVEN the Personal data step
- WHEN the person enters DNI `30111222`, "Jane Doe" and "Jane", and presses "Add my personal account"
- THEN exactly one `POST /api/identity/personal` carries `documentNumber`, `fullName` and `displayName`, and no
  `email` or `password`

#### Scenario: The Company step sends the signed-in organization body

- GIVEN the Company data step
- WHEN the person enters CUIT `30-12345678-1` and "Northwind SA", and presses "Register"
- THEN exactly one `POST /api/identity/organizations/register` carries that `cuit` and `legalName`, with `email` and
  `password` empty

#### Scenario: The screens that already use these forms keep their contracts

- GIVEN `/identity/profile` for an identity with an organization and no personal profile, and `/organizations/register`
  for a signed-in identity with `setupRequired` `false`
- WHEN they render
- THEN their headings, field ids, labels and submit names are today's, in CUIT- or DNI-first order

### Requirement: Completion and resume

Sources: pre-proposal `gate`; error-handling rule 25; `PersonalPages.jsx` (`AddPersonalContext`).

- After a data step succeeds, it MUST announce `identity:people.context.success` (Personal) or
  `identity:register.organization.registered` (Company) as `role="status"`, and the SPA MUST reload the context once.
- If the reloaded context reports `setupRequired` `false`, the SPA MUST replace the location with
  `safeReturnUrl(returnUrl)` when setup carries a `returnUrl`, and with `/identity` otherwise. An off-origin or
  unparseable value resolves to `/`, which sends a signed-in person to `/identity`.
- If it still reports `true`, the person MUST stay on `/identity/setup` at the type choice with the same `returnUrl`.
- A failed reload MUST NOT report the completion as failed or resend it; the context is cleared and the person is
  sent to sign in, where the problem shows.

#### Scenario: Personal completion resumes the captured destination

- GIVEN `/identity/setup?returnUrl=%2Fmembers` on the Personal data step
- WHEN `POST /api/identity/personal` answers `204` and the reloaded context reports `setupRequired` `false`
- THEN "Your personal account was added. Refreshing your access…" is announced and the location becomes `/members`

#### Scenario: Company completion resumes to the access page by default

- GIVEN `/identity/setup` without `returnUrl`, on the Company data step
- WHEN the registration answers `204` and the reloaded context reports `setupRequired` `false`
- THEN "The organization is registered and ready to use." is announced and the location becomes `/identity`

#### Scenario: An off-origin return URL is never followed

- GIVEN a setup `returnUrl` of `//evil.test` or `/\evil.test`
- WHEN a completion succeeds and setup is no longer required
- THEN the SPA navigates to `/`, never off-origin, and the person ends on `/identity`

#### Scenario: A failed reload is not a failed completion

- GIVEN a data step whose request succeeded
- WHEN the reload fails with a network error
- THEN nothing says the completion failed, nothing is resent, and sign-in shows the `errors:network_unavailable` words

#### Scenario: Setup still required after a success

- GIVEN a data step whose request succeeded
- WHEN the reloaded context still reports `setupRequired` `true`
- THEN the person is at the type choice with the same `returnUrl`, and nothing was resent

#### Scenario: A person who belongs to nothing sets up and resumes

- GIVEN a confirmed `Active` identity with no membership
- WHEN they sign in with their password
- THEN they land on `/identity/setup?returnUrl=%2Fidentity`
- AND after the Personal data step they reach "Your access" at `/identity`

### Requirement: Company completion activates immediately

Sources: pre-proposal `company_activation`; `identity-access` delta.

- The Company step and the signed-in branch of `/organizations/register` MUST treat the bodyless `204` of the
  signed-in registration as finished: the organization is `Active`, and neither may mention a confirmation link or
  show the anonymous delivery help.
- The signed-in branch MUST announce `identity:register.organization.registered` as `role="status"` and reload the
  context once, so the new organization is offered without confirming. A failed reload is not a failed registration.
- The anonymous branch MUST keep `identity:register.organization.acknowledgement`, `.existingAccountNext` and
  `.deliveryHelp` unchanged.

#### Scenario: A signed-in registration says the organization is ready

- GIVEN a signed-in person with `setupRequired` `false` at `/organizations/register`
- WHEN they submit a CUIT and legal name and the API answers `204`
- THEN the status reads "The organization is registered and ready to use.", nothing mentions a confirmation link, and
  the context is reloaded once

#### Scenario: The new organization is offered without confirming

- GIVEN an identity with one active membership registered another organization while signed in
- WHEN they open the workspace chooser
- THEN both organizations are offered without any confirmation email

#### Scenario: The anonymous acknowledgement is unchanged

- GIVEN a visitor without a session at `/organizations/register`
- WHEN the registration answers `202`
- THEN the status still reads "If that address can register, we have sent it a confirmation link. Check the inbox."

### Requirement: A signed-in person never reaches the personal signup

Sources: pre-proposal `data_step`; `PersonalPages.jsx`; `RegisterPersonalHandler.cs` refuses a session.

- `/personal/register` MUST render its form only after the context settles without a session.
- For a signed-in person it MUST NOT render the form or call `registerPersonal`, and MUST replace the location with
  `/identity/setup` on the Personal data step when `setupRequired` is `true`, or with `/identity/profile` when `false`.

#### Scenario: A setup-required person goes to the Personal data step

- GIVEN `setupRequired` `true`
- WHEN the person opens `/personal/register`
- THEN they are at the Personal data step on `/identity/setup`, and no personal registration request is sent

#### Scenario: A person with a context goes to their profile

- GIVEN `setupRequired` `false`
- WHEN the person opens `/personal/register`
- THEN the location becomes `/identity/profile` and the form never rendered

#### Scenario: Nothing renders before the context settles

- GIVEN the context read has not answered
- WHEN `/personal/register` renders
- THEN no field or "Register" button renders until the read answers `401 authentication_required`

### Requirement: Refusals reuse the existing contract

Sources: codes declared in `PersonalEndpoints.cs` and `Identity.cs`; error-handling rules 18, 20 and 21.

- Every refusal MUST render through the existing problem contract; no code, message or `problemCodes.json` entry is
  added.
- `400 validation_failed` errors MUST show on their fields and focus the first one in CUIT- or DNI-first order;
  unclaimed errors stay in the alert.
- `409 registration_conflict` (Company) and `409 personal_registration_conflict` (Personal) MUST show their existing
  words, keep the typed values, and stay on the data step.
- Personal `429 rate_limit_exceeded` and `503 service_unavailable` keep the `Retry-After` handling. A
  `401 invalid_session` or `401 authentication_required` ends the session centrally and sends the person to sign in
  with a `returnUrl` back to setup.
- The Company step can answer only `antiforgery_validation_failed`, `validation_failed`, `invalid_registration`,
  `invalid_session`, `registration_conflict` and `internal_server_error`; the Personal step only
  `antiforgery_validation_failed`, `validation_failed`, `authentication_required`, `invalid_session`,
  `permission_denied`, `personal_registration_conflict`, `rate_limit_exceeded`, `service_unavailable` and
  `internal_server_error`.

#### Scenario: A CUIT that is already registered

- GIVEN the Company data step with a CUIT and legal name typed
- WHEN the API answers `409 registration_conflict`
- THEN the alert shows the `errors:registration_conflict` words and both values stay on the Company data step

#### Scenario: A personal claim that conflicts

- GIVEN the Personal data step with its fields filled
- WHEN the API answers `409 personal_registration_conflict`
- THEN the alert shows the `errors:personal_registration_conflict` words and the typed values stay

#### Scenario: Field errors focus the document first

- GIVEN the Personal data step
- WHEN `400 validation_failed` names `documentNumber`, `fullName` and `displayName`
- THEN each field is marked invalid with its error, and focus moves to DNI

#### Scenario: A lost session during completion

- GIVEN a data step at `/identity/setup?returnUrl=%2Fmembers`
- WHEN the API answers `401 invalid_session`
- THEN the context is cleared and sign-in gets a `returnUrl` set to that setup address

### Requirement: Known gap — setup lists no invitation

Sources: pre-proposal `invitations`.

`/identity/setup` MUST NOT list or accept invitations in this change. A person with setup pending accepts through the
mailed link at `/invitations/accept`, which is exempt; acceptance creates the membership and ends setup.

#### Scenario: An invitee with setup pending accepts through the link

- GIVEN `setupRequired` `true` and a live invitation for the person's address
- WHEN they open the invitation link and press "Accept"
- THEN they are not redirected, the membership is created, and "Continue" reaches `/identity` without setup

## Localization catalog

| Key | `en` | `es` | Change |
| --- | --- | --- | --- |
| `identity:setup.title` | Finish setting up your account | Termine de configurar su cuenta | new |
| `identity:setup.subtitle` | Choose the kind of account to set up before you continue. | Elija el tipo de cuenta que desea configurar antes de continuar. | new |
| `identity:setup.changeType` | Change account type | Cambiar el tipo de cuenta | new |
| `identity:register.organization.registered` | The organization is registered and ready to use. | La organización quedó registrada y está lista para usar. | new |

Everything else reuses existing keys unchanged: `identity:register.choose.*` (its organization detail change is owned
by `account-creation-entry`), `identity:people.*`, `identity:register.organization.*`, `common:navigation.register`
and `errors:*`; the `DNI` label stays an invariant literal. No other accessible name is needed: the `h1` names the
page, each option's title and detail name it, and `identity:setup.changeType` names the action. The `es` values use
neutral professional Spanish addressed as "usted" and are flagged for native review.

## Project rules (`rules.specs`)

| Rule | Status |
| --- | --- |
| Catalog entries in `en` and `es` | Four new keys above; everything else reused unchanged. |
| New error code | None; `problemCodes.json` is unchanged. |
| Neutral public flows | Setup and both data steps require a validated session; the anonymous `/organizations/register` and `/personal/register` keep their neutral bodyless `202`; no field error is added to a neutral route. |