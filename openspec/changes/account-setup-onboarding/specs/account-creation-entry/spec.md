# Account Creation Entry Specification

## Purpose

Signing in (`/login`, the bar's "Log in") never asks the account type. Creating an account (`/register`, the bar's
"Register") asks Personal or Company first, then offers Google or the email form, and the type survives the Google
round trip. Every SPA form that asks for a CUIT or DNI asks for it first, preparing a later lookup. This is functional
work with no new error code; the anonymous email path keeps link confirmation. Email codes, AFIP/ARCA lookups
(blocked by IA-REQ-056), password recovery, in-account Google linking and visual redesign are out of scope.

## Requirements

### Requirement: Signing in never asks the account type

Sources: pre-proposal scope; `LoginPage.jsx`; flow design at commit `4a2cfdb5` (decision 1, non-normative).

- `/login` MUST offer "Continue with Google" and the email-and-password form, and MUST NOT ask Personal or Company.
- A Google sign-in started there MUST write no type carry and MUST clear any carry before the browser leaves.
- A person without a counting context then reaches the generic type choice through the guard (`account-setup`).

#### Scenario: Signing in offers both ways and no type

- GIVEN a visitor without a session
- WHEN `/login` renders
- THEN "Continue with Google", "Email", "Password" and "Sign in" are offered, and no type choice

#### Scenario: A Google sign-in clears a leftover type

- GIVEN a carry naming Company, left by an abandoned create-account round trip
- WHEN "Continue with Google" is pressed on `/login` and the start succeeds
- THEN the carry is gone before the browser leaves

### Requirement: Creating an account asks the type first

Sources: pre-proposal scope; `ChooseContextPage.jsx`, `NavMenu.jsx`, `ExternalLoginEndpoints.cs`.

- `/register` MUST ask Personal or Company before any registration field; Personal opens `/personal/register` and
  Company `/organizations/register`, as today.
- Each anonymous registration screen MUST offer "Continue with Google" (`identity:login.continueWithGoogle`) before its
  email form, starting the same `POST /api/identity/external/{provider}/login/start` as `/login`. A refused start MUST
  show its problem there, as `/login` does, and keep the visitor on the screen.
- The email form keeps its neutral bodyless `202` and its confirmation link. The signed-in branch of
  `/organizations/register` MUST NOT offer Google.

#### Scenario: The type comes before any form

- GIVEN a visitor without a session
- WHEN `/register` renders
- THEN it offers only the two choices and the sign-in link

#### Scenario: Each registration screen offers Google

- GIVEN a visitor without a session at `/personal/register` or `/organizations/register`
- WHEN the screen renders
- THEN one "Continue with Google" button precedes the email form, whose fields and "Register" button change only in
  order

#### Scenario: No Google control once signed in

- GIVEN a signed-in person with `setupRequired` `false` at `/organizations/register`
- WHEN the screen renders
- THEN no "Continue with Google" button is offered

### Requirement: The chosen type survives the Google round trip

Sources: pre-proposal carry finding; `useIdentityProof.js`, `ExternalAccountsPage.jsx` (`ExternalReturnPage`).

- The carry MUST be a `sessionStorage` record, like the pending-proof record, holding only the type (Personal or
  Company): no address, token, identity or tenant. It dies with the tab, and unavailable storage loses it.
- It MUST be written only by a create-account Google control, after the start succeeded and just before the browser
  leaves. A write replaces any earlier record; a refused start writes nothing.
- `/external/return` MUST use it at most once: only after the completion succeeded and the context reloaded, with
  outcome `signed_in` and `setupRequired` `true`. Using it clears it and opens `/identity/setup` on that type's data
  step.
- Every other case MUST clear it unused: a `refused` or unknown outcome, a failed completion, `linked` or `proved`,
  and `signed_in` with `setupRequired` `false`, which goes to `/identity` as today.
- A `signed_in` return with `setupRequired` `true` and no readable record MUST reach the generic type choice.
- The record MUST NEVER reach the server, authorize anything, or change what a screen says about an account; it only
  chooses which setup step opens first.

#### Scenario: The chosen type opens its data step

- GIVEN a visitor chose Company and pressed "Continue with Google" on `/organizations/register`
- WHEN the return is `signed_in`, the completion succeeds and the reloaded context reports `setupRequired` `true`
- THEN the person is at the Company data step on `/identity/setup`, and the record is gone

#### Scenario: The record is written only when the provider leg starts

- GIVEN a visitor at `/personal/register`
- WHEN "Continue with Google" gets `429 rate_limit_exceeded`, and a later start succeeds
- THEN the refusal writes no record and keeps the visitor on the form, and the success writes Personal before leaving

#### Scenario: A person who already has a context ignores the record

- GIVEN a record naming Personal
- WHEN a `signed_in` return reloads a context with `setupRequired` `false`
- THEN the record is cleared, the person is on `/identity`, and nothing refers to the chosen type

#### Scenario: A refused or failed return clears the record

- GIVEN a record naming Company
- WHEN the outcome is `refused`, or the completion answers `409 external_login_conflict`
- THEN the record is cleared and that outcome's existing handling is unchanged

#### Scenario: A lost record falls back to the type choice

- GIVEN no readable record
- WHEN a `signed_in` return reloads a context with `setupRequired` `true`
- THEN the person is on `/identity/setup` at the generic type choice

### Requirement: The CUIT or DNI is the first field

Sources: pre-proposal field-order finding; `fieldErrors.js`, `PersonalPages.jsx`, `RegisterOrganizationPage.jsx`.

- Every SPA form that asks for a CUIT or DNI MUST ask for it first: first in DOM and tab order, and first to receive
  focus when several fields are in error, whether from client checks or `400 validation_failed`.
- Only the order changes: labels, ids, names, `type`, `autoComplete`, `required`, input attributes and helper texts
  MUST NOT.

| Form | Where | Today | After |
| --- | --- | --- | --- |
| Organization registration, anonymous | `/organizations/register` | Legal name, CUIT, Email, Password | CUIT, Legal name, Email, Password |
| Organization registration, signed in | `/organizations/register`, Company step | Legal name, CUIT | CUIT, Legal name |
| Personal signup | `/personal/register` | Full name, Display name, DNI, Email, Password | DNI, Full name, Display name, Email, Password |
| Add a personal context | `/identity/profile`, Personal step | Full name, Display name, DNI | DNI, Full name, Display name |
| Document correction | `/identity/profile` | number, reason | unchanged |

- `features/identity/fieldErrors.js` keeps the exports the shared-problem-infrastructure specification pins; only the
  order inside `organizationRegistrationFields` and `personalRegistrationFields` changes. The Google button is not a
  form field.

#### Scenario: An empty organization form focuses the CUIT

- GIVEN a visitor without a session at `/organizations/register`
- WHEN they press "Register" with every field empty
- THEN tab order is CUIT, Legal name, Email, Password, focus moves to CUIT, and nothing is sent

#### Scenario: Server field errors focus the DNI

- GIVEN `/personal/register` or the add-personal-context form
- WHEN `400 validation_failed` names `documentNumber`, `fullName` and `displayName`
- THEN focus moves to DNI

#### Scenario: The signed-in organization form focuses the CUIT

- GIVEN a signed-in person with `setupRequired` `false` at `/organizations/register`
- WHEN `400 validation_failed` names `legalName` and `cuit`
- THEN focus moves to CUIT, which precedes Legal name

#### Scenario: Only the order changes

- GIVEN any form in the table
- WHEN it renders
- THEN each field keeps today's id and label, such as `register-cuit` "CUIT" and `add-personal-document` "DNI"

### Requirement: The Company choice names the CUIT first

Sources: pre-proposal `copy`.

`identity:register.choose.organization.detail` MUST name the CUIT before the legal name in every supported language,
on `/register` and on the setup type choice, as a declared copy change in `en` and `es`.
`identity:register.choose.personal.detail` MUST NOT change.

#### Scenario: The Company choice reads CUIT first in both languages

- GIVEN `/register`
- WHEN it renders in `en` and in `es`
- THEN the Company choice reads "For a company. You will be asked for its CUIT and legal name." and "Para una
  empresa. Se le solicitarán el CUIT y la razón social."
- AND the Personal choice still reads "For yourself. You will be asked for your name and your DNI." and "Para usted.
  Se le solicitarán su nombre y su DNI."

## Localization catalog

| Key | `en` | `es` | Change |
| --- | --- | --- | --- |
| `identity:register.choose.organization.detail` | For a company. You will be asked for its CUIT and legal name. | Para una empresa. Se le solicitarán el CUIT y la razón social. | changed from "For a company. You will be asked for its legal name and CUIT." / "Para una empresa. Se le solicitarán la razón social y el CUIT." |
| `identity:register.choose.personal.detail` | For yourself. You will be asked for your name and your DNI. | Para usted. Se le solicitarán su nombre y su DNI. | unchanged |
| `identity:login.continueWithGoogle` | Continue with Google | Continuar con Google | reused on both registration screens |

Reordering changes no label, and the carried type is invariant data that is never shown.

## Project rules (`rules.specs`)

| Rule | Status |
| --- | --- |
| Catalog entries in `en` and `es` | One changed value, in both languages. |
| New error code | None. |
| Neutral public flows | Unchanged: anonymous registrations still answer a neutral bodyless `202`, field order changes no answer, and the client-only carry never reaches the server. |