# Delta for Identity and Access

Applies pre-proposal `company_activate_immediately`: a validated session is one of the two proofs ADR-004 decision 18
accepts, so a registration made with one no longer waits for email confirmation. Each block replaces its whole
baseline requirement; the anonymous branch does not change.

## MODIFIED Requirements

### Requirement: IA-REQ-003/048 — Exclusive registration state requires proof of control

An exclusive durable reservation, including a normalized CUIT, organization, tenant, membership, role, personal
document, or global email identity, MUST be created only for a validated persisted session or a single-use token
delivered to and spent by the identity's email address. Unproven initiation MUST leave no state that changes another
caller's answer.

Anonymous organization registration MUST return a neutral `202`, persist only a bounded pending intent and delivery
effect, and reveal neither email nor CUIT existence. Authenticated registration MUST derive the address from the
validated session and reject a conflicting CUIT with `409 registration_conflict`. CUIT input MUST normalize to eleven
digits and satisfy the AFIP modulo-11 verifier.

Because the session already proves control, authenticated registration MUST create the tenant, organization profile,
responsible membership, ownership, initial roles and audit record `Active` in one transaction, MUST NOT send a
confirmation email or write a confirmation outbox message or secret, and MUST answer a bodyless `204 No Content`. A
request with an invalid session cookie MUST get `401 invalid_session` and MUST NOT fall back to the anonymous branch.
Authenticated answers depend only on the caller's session and the submitted organization data.

Why `204`: successes are endpoint-specific DTOs or bodyless statuses, never an envelope (IA-REQ-038). Here a bodyless
`202` answers work that waits for something mailed (registrations, recovery, invitations), while a finished
authenticated creation with nothing to return answers `204`, as `POST /api/identity/personal` does. Company completion
in setup needs no body: it reloads the identity context to see the organization and `setupRequired`.

(Previously: authenticated registration created `PendingConfirmation` rows, queued a confirmation email, and answered
the anonymous branch's bodyless `202`.)

#### Scenario: An anonymous caller submits a known address or CUIT

- **WHEN** the caller starts organization registration without a validated session
- **THEN** the API returns the same neutral bodyless `202` for known and unknown state
- **AND** creates no identity, tenant, organization, membership, role, or CUIT claim before email proof is spent.

#### Scenario: Proof arrives after the CUIT was claimed

- **WHEN** a person spends a valid email token after another organization claimed the CUIT
- **THEN** the proved identity may still be activated
- **AND** organization creation is refused with `409 registration_conflict` without creating a partial graph.

#### Scenario: A signed-in caller registers an organization

- **GIVEN** a validated session of an `Active` identity
- **WHEN** it registers an organization with an unclaimed CUIT
- **THEN** the API answers a bodyless `204`, and the tenant and responsible membership are `Active`
- **AND** the membership owns the organization and holds its initial roles, and one registration audit record exists
- **AND** no confirmation outbox message, secret or email exists for it.

#### Scenario: A signed-in caller submits a claimed CUIT

- **GIVEN** a validated session and a CUIT another organization holds
- **WHEN** it registers an organization with that CUIT
- **THEN** the API answers `409 registration_conflict`, and that submission leaves no tenant, organization profile,
  membership, role, role assignment, ownership, audit record, outbox message or secret; only its completed idempotency
  record (outcome `RegistrationConflict`) is kept, so an equivalent replay answers the same `409`.

#### Scenario: An invalid session never registers anonymously

- **GIVEN** a request carrying an expired or revoked session cookie
- **WHEN** it submits an organization registration
- **THEN** the API answers `401 invalid_session` and writes no intent, message, tenant or audit record.

#### Scenario: The anonymous branch still confirms by link

- **GIVEN** no session and an address with no account
- **WHEN** the caller registers an organization
- **THEN** the API answers a bodyless `202`, leaving only an intent, its confirmation message and a tenantless audit
  record
- **AND** the organization is created only when that token is spent.

### Requirement: IA-REQ-004 — Registration is atomic and idempotent

Organization creation MUST write identity, tenant, profile, responsible membership, initial roles, audit, and outbox
effects in one consistency boundary or write none. Equivalent anonymous submissions MUST reuse the completed neutral
response without duplicating intent, message, or audit state. Confirmation tokens MUST be single-use and later spends
MUST return the recorded terminal outcome.

An authenticated registration's boundary MUST hold its tenant, profile, responsible membership, ownership, initial
roles and audit record (code `organization.registration.requested`, outcome `registered`), with no identity or outbox
effect. An equivalent authenticated submission (same identity, legal name and CUIT) MUST NOT create a second graph or
audit record, and replaying a registration completed this way MUST answer its recorded outcome. The resulting
organization MUST match a token-confirmed one in tenant type and status, membership status, ownership and initial
roles.

(Previously: authenticated registration also wrote a confirmation outbox effect, and only anonymous replays were
specified.)

#### Scenario: The same initiation is replayed

- **WHEN** an equivalent anonymous registration request is submitted more than once
- **THEN** every response remains bodyless `202`
- **AND** only one intent, delivery effect, and audit record exist.

#### Scenario: A signed-in registration cannot commit

- **GIVEN** a validated session
- **WHEN** the registration transaction fails before commit
- **THEN** no tenant, profile, membership, ownership, role assignment or audit record from it remains.

#### Scenario: A signed-in registration is replayed

- **GIVEN** an authenticated registration that completed with `204`
- **WHEN** the same identity submits the same legal name and CUIT again
- **THEN** the API answers `204` again, and exactly one tenant, membership and registration audit record exist.

#### Scenario: Immediate and confirmed organizations match

- **GIVEN** one organization registered with a session and one registered anonymously and confirmed by link
- **WHEN** their graphs are compared
- **THEN** both tenants are `Active` organizations whose `Active` responsible memberships own them with the same
  initial roles.

### Requirement: IA-REQ-005 — Confirmation purposes remain distinct

Email MUST be confirmed before invitations are accepted, roles are administered, members are invited, or sensitive
operations execute. Organization, personal, invited-member, and Platform confirmation envelopes MUST be classified by
their exact message type and MUST NOT cross purposes. Invalid or terminal personal/member/Platform confirmation
answers `400 invalid_confirmation`; terminal organization registration answers `409 registration_conflict`.

An authenticated organization registration MUST NOT issue a confirmation envelope. An envelope issued by an
authenticated registration before immediate activation (`identity.confirmation.requested`) MUST still be delivered if
queued and stay spendable at `POST /api/identity/confirm-email` until it expires: spending it activates its
`PendingConfirmation` tenant and responsible membership and names the owner, as before, and an expired or terminal one
answers `409 registration_conflict`. Nothing else activates or repairs those rows, and until confirmed they do not
count as a context (`identity-context-contract`).

(Previously: authenticated registrations issued this envelope, and only spending it activated their rows.)

#### Scenario: A token is presented to the wrong confirmation flow

- **WHEN** a valid token for one confirmation purpose reaches another purpose
- **THEN** it is refused without confirming the identity or activating a membership.

#### Scenario: A signed-in registration issues no confirmation

- **GIVEN** a validated session
- **WHEN** it registers an organization
- **THEN** no organization confirmation envelope, secret or email exists for that registration.

#### Scenario: A pending signed-in registration from before the change is confirmed

- **GIVEN** `PendingConfirmation` rows from an authenticated registration made before immediate activation, with a
  live envelope
- **WHEN** its token is spent at `POST /api/identity/confirm-email`
- **THEN** the API answers a bodyless `204`, the tenant and membership are `Active`, and the membership owns the
  organization.

#### Scenario: An expired envelope from before the change

- **GIVEN** the same rows with an expired envelope
- **WHEN** its token is spent
- **THEN** the API answers `409 registration_conflict` and the rows stay `PendingConfirmation`.

## Project rules (`rules.specs`)

| Rule | Status |
| --- | --- |
| Catalog entries in `en` and `es` | None; the SPA words are `account-setup`'s `identity:register.organization.registered`, and audit data stays invariant. |
| New error code | None. |
| Neutral public flows | Unchanged: the anonymous branch keeps its neutral bodyless `202`; `204` and `409` exist only for a validated session and reveal nothing about other identities. |