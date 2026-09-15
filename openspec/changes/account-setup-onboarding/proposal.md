# Proposal: Account Setup Onboarding

## Intent

- No-context sessions have no next step; create account lacks Google; `/personal/register` dead-ends sessions; CUIT/DNI is not first; session-registered Companies await redundant email confirmation.

## Classification and Authorities

- Functional: `*.test.jsx`, `tests/` page objects, `src/Web/ClientApp/src/AppRoutes.jsx` and `src/Web/ClientApp/src/features/identity/api/identityClient.js` change only to assert new behavior, declared per file; no new error code.
- Authorities: `openspec/specs/identity-access/spec.md`, `openspec/specs/localization/spec.md`, `openspec/decisions/ADR-004-Adopt-Multitenant-Identity-Access.md` (decision 18), `openspec/decisions/ADR-007-Adopt-Cross-Project-Localization.md`, `openspec/decisions/ADR-001-Use-EFCore-In-Application-Layer.md`, `openspec/changes/account-setup-onboarding/preproposal.md` (new, revision 3).

## Scope

### In Scope

- Context: server-derived additive `setupRequired` in `GET /api/identity/context`; strict SPA reader.
- Setup: one guard, closed exemptions, `safeReturnUrl` resume; `/identity/setup` type choice, then data step reusing existing forms; signed-in `/personal/register` redirects.
- Entry: type-first Create account with Google, the type surviving the round trip; CUIT/DNI first in every SPA form (tab order, focus on error); CUIT-first Company card copy.
- Activation: session-registered Companies are Active immediately.

### Out of Scope

- 6-digit email code (change 2); anonymous email keeps link confirmation.
- AFIP/ARCA padron autocomplete (change 3, blocked by IA-REQ-056; document-first order prepares it).
- Invitation listing on setup (known gap).
- Password recovery, in-account Google linking, visual redesign.

## Capabilities

### New Capabilities

- `identity-context-contract`: Context scope.
- `account-setup`: Setup scope, including Company completion.
- `account-creation-entry`: Entry scope.

### Modified Capabilities

- `identity-access`: deltas on IA-REQ-003/048, IA-REQ-004 and IA-REQ-005; signed-in `POST /api/identity/organizations/register` creates tenant, responsible membership, ownership, initial roles and audit Active in one transaction without confirmation email; anonymous branch unchanged.

## Approach

- Counting, exemption and carry rules follow the pre-proposal; reused forms keep ids, names, `data-testid`, roles, headings and en strings; the carry mirrors `src/Web/ClientApp/src/features/identity/useIdentityProof.js`; specs pin signed-in success (`202` today).

## Affected Areas

| Area | Impact | Description |
|---|---|---|
| `src/Application/IdentityAccess/Context/GetIdentityContext/GetIdentityContextHandler.cs`, `src/Web/Endpoints/Identity/Contracts/IdentityContextResponse.cs` | Modified | Signal |
| `src/Application/IdentityAccess/Organizations/RegisterOrganization/RegisterOrganizationHandler.cs`, `src/Web/Endpoints/Identity.cs` | Modified | Activation |
| `src/Web/ClientApp/src/features/identity/setup/` (new), `src/Web/ClientApp/src/AppRoutes.jsx`, `src/Web/ClientApp/src/components/api-authorization/ProtectedRoute.jsx`, `src/Web/ClientApp/src/features/identity/register/`, `src/Web/ClientApp/src/features/identity/people/`, `src/Web/ClientApp/src/features/identity/credentials/ExternalAccountsPage.jsx`, `src/Web/ClientApp/src/features/identity/fieldErrors.js`, `src/Web/ClientApp/src/i18n/locales/en/identity.json`, `src/Web/ClientApp/src/i18n/locales/es/identity.json` | New/Modified | Setup, guard, entry, copy |
| `src/Web/ClientApp/src/test/identityServer.js`, `src/Web/ClientApp/src/test/identityFiles.contract.test.js`, `tests/Application.FunctionalTests/IdentityAccess/`, `tests/Web.AcceptanceTests/Features/`, `tests/Web.AcceptanceTests/Pages/IdentityAccessPages.cs`, `tests/Web.AcceptanceTests/IdentityAccessFixtures.cs` | Modified | Pins, populations, journeys |

## Risks

| Risk | Likelihood | Mitigation |
|---|---|---|
| Guard misroutes invitees or exempt routes | Med | Test each population and exemption |
| Immediate graph differs from confirmed | Med | Functional test compares both |
| Memberless journeys; 400-line budget | High | Journey edits with guard; seven deliveries |

## Rollback Plan

- `git revert` deliveries on `main`, newest first; never force-push.
- Guard: reverting restores routing; `setupRequired` is additive.
- Activation: revert only after the guard, or Company setup loops while PendingConfirmation; activated Companies match confirmed ones; no repair.

## Dependencies

- Seven verified commits, at most 400 changed lines each: order and copy; activation; signal; form reuse; setup screen; guard, redirect, journeys; Google carry; the guard never precedes its screen.

## Success Criteria

- [ ] Tests prove every In Scope item, `setupRequired` per population and the unchanged anonymous `202`.
- [ ] A no-context journey completes setup and resumes.
- [ ] `cd src/Web/ClientApp && npx vitest run && npx eslint src/ && npx vite build`, the CLAUDE.md journey command and `npm run i18n:unused` pass; en/es complete; `src/Web/ClientApp/src/api/problemCodes.json` unchanged; no undeclared `tests/` changes.