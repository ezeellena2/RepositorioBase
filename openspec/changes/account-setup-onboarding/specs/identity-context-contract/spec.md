# Identity Context Contract Specification

## Purpose

Every identity-context response carries the server-derived, additive boolean `setupRequired`, and the SPA reads it
strictly, so no screen infers setup from `availableTenants` and no population that must never see setup is sent
there. This is functional work with no new error code or human-readable text. Routing on the signal belongs to
`account-setup`; `preferredLanguage` stays with the localization specification (L10N-REQ-004, L10N-REQ-016).

## Requirements

### Requirement: Setup-required signal

Sources: pre-proposal `gate`, `suspended_counts`; `GetIdentityContextHandler.cs`, `SelectTenantHandler.cs`,
`IdentityContextResponse.cs`, `PlatformAdminInvitation.cs`.

- Every identity-context response MUST carry the top-level boolean `setupRequired`: `GET /api/identity/context`,
  `PUT /api/identity/context/tenant` and `PUT /api/identity/context/language`.
- The server MUST derive it for every response from the validated session and the caller's own rows. No query,
  header, body, claim or client storage can set it.
- It MUST be `true` exactly when the identity holds no counting membership and is not a Platform invitee; otherwise
  it MUST be `false`.
- A membership counts only when neither it nor its tenant excludes it, whatever the tenant type:

| Membership status | Tenant status | Counts |
| --- | --- | --- |
| `Active` or `Suspended` | `Active` or `Suspended` | yes |
| `PendingConfirmation` or `Revoked` | any | no |
| any | `PendingConfirmation` or `Closed` | no |

- A Platform invitee is the bound identity of a `PlatformAdminInvitation` pending when the response is built
  (`IsPendingAt`), or an identity holding a `PlatformMfaEnrollment` in status `Pending`. It is never setup-required.
- The member is additive: `availableTenants` still lists only `Active` memberships on `Active` tenants, and no other
  member changes. The top-level members are exactly `activeTenant`, `availableTenants`, `permissions`,
  `personalData`, `preferredLanguage`, `session`, `setupRequired` and `user`.
- Anonymous and invalid-session callers keep `401 authentication_required` and `401 invalid_session`.

#### Scenario: An active membership counts

- GIVEN an identity whose only membership is `Active` on an `Active` Organization, Personal or Platform tenant
- WHEN it reads `GET /api/identity/context`
- THEN `setupRequired` is `false`

#### Scenario: A suspended membership or tenant still counts

- GIVEN an identity whose only membership is `Suspended` on an `Active` tenant, `Active` on a `Suspended` tenant, or
  `Suspended` on a `Suspended` tenant
- WHEN it reads its context
- THEN `setupRequired` is `false` and `availableTenants` is empty

#### Scenario: No membership at all

- GIVEN an `Active` identity with no membership, such as one created by a first Google sign-in or one whose confirmed
  registration lost its CUIT or document to a competing claim
- WHEN it reads its context
- THEN `setupRequired` is `true`

#### Scenario: A revoked membership or a closed tenant does not count

- GIVEN an identity whose only membership is `Revoked` on an `Active` tenant, or `Active` on a `Closed` tenant
- WHEN it reads its context
- THEN `setupRequired` is `true`

#### Scenario: A pending membership or tenant does not count

- GIVEN an identity whose only membership is a reinstated `PendingConfirmation` membership on an `Active` tenant, or
  whose only tenant and membership are still `PendingConfirmation` from a signed-in registration made before
  immediate activation
- WHEN it reads its context
- THEN `setupRequired` is `true`

#### Scenario: One counting membership is enough

- GIVEN an identity with a `Revoked` membership, a membership on a `Closed` tenant, and a `Suspended` membership on an
  `Active` tenant
- WHEN it reads its context
- THEN `setupRequired` is `false`

#### Scenario: A Platform invitee is never setup-required

- GIVEN an identity with no counting membership that is bound to a still-pending Platform invitation, or that holds a
  Platform MFA enrollment in status `Pending`
- WHEN it reads its context
- THEN `setupRequired` is `false`

#### Scenario: An ended Platform invitation no longer exempts

- GIVEN an identity with no counting membership, no `Pending` enrollment, and a bound Platform invitation that expired
  or was cancelled
- WHEN it reads its context
- THEN `setupRequired` is `true`

#### Scenario: The value is derived on every response

- GIVEN a read answered `false`, and the identity's only membership is then revoked
- WHEN it reads its context again with `setupRequired=false` in the query string and in a request header
- THEN `setupRequired` is `true`

#### Scenario: Tenant and language answers carry the same value

- GIVEN an identity with no counting membership, and another identity with an `Active` tenant
- WHEN the first changes its language and the second selects that tenant
- THEN the language answer carries `setupRequired` `true` and the tenant answer carries `false`

#### Scenario: The member set is exact

- GIVEN any signed-in identity
- WHEN it reads `GET /api/identity/context`
- THEN the top-level members are exactly the eight listed above, and every other member keeps its current value

### Requirement: Strict SPA reader

Sources: `identityClient.js` (`identityContextMembers`), `problemDetails.js` (`readSuccess`), `IdentityProvider.jsx`.

- The SPA MUST require `setupRequired` in the three identity-context responses, alongside `user`,
  `preferredLanguage`, `availableTenants`, `permissions`, `session` and `personalData`. `activeTenant` stays optional.
- A missing required member already fails the read as client code `unreadable_response` with status `0`. The same
  MUST happen when `setupRequired` is missing or is not a JSON boolean, such as a string, a number or `null`.
- A failed context read MUST keep no identity context: as for any unreadable context, a route that requires a session
  sends the person to sign in, where the alert shows the `errors:unreadable_response` words. A failed tenant selection
  or language change MUST show `unreadable_response` where that action shows problems and MUST NOT apply the answer.
- The SPA MUST take the signal only from this member, never from `availableTenants`, `activeTenant`, `permissions` or
  client storage.

#### Scenario: A boolean value is read

- GIVEN `GET /api/identity/context` answers `setupRequired` `true`
- WHEN the identity provider loads it
- THEN the person is signed in and the context reports `setupRequired` `true`

#### Scenario: A missing or non-boolean value is contract drift

- GIVEN `GET /api/identity/context` answers `200` without `setupRequired`, or with it as `"false"`, `0` or `null`
- WHEN the identity provider loads it
- THEN the read fails with `unreadable_response` and status `0`, and no context is kept
- AND opening `/identity` leads to sign in, where the alert reads "We could not read the answer. Reload the page and
  try again."

#### Scenario: Tenant and language answers are read the same way

- GIVEN a tenant selection or language change answered without a boolean `setupRequired`
- WHEN that action completes
- THEN it shows the `unreadable_response` problem where it shows problems today, and the context is not replaced

### Requirement: Account language handling is unchanged

Sources: L10N-REQ-004, L10N-REQ-016.

The member MUST NOT change how `preferredLanguage` is applied or returned: the SPA applies a supported account
preference and writes the culture cookie whatever `setupRequired` says, and the localization specification keeps
owning that behavior.

#### Scenario: A setup-required person's preference still applies

- GIVEN a context with `preferredLanguage` `es` and `setupRequired` `true`
- WHEN the identity provider loads it
- THEN the SPA selects `es` and writes the culture cookie for `es`

## Project rules (`rules.specs`)

| Rule | Status |
| --- | --- |
| Catalog entries in `en` and `es` | None added or changed; `setupRequired` is invariant data, and drift reuses `errors:unreadable_response`. |
| New error code | None; `problemCodes.json` is unchanged. |
| Neutral public flows | N/A: the routes require a validated session and describe only the caller. |