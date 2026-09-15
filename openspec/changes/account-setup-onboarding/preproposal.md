# Pre-proposal state: account-setup-onboarding

```yaml
schema: gentle-ai.sdd-preproposal/v1
revision: 3
change: account-setup-onboarding
project: repositoriobase
artifact_store: hybrid
recorded_at: 2026-09-15
revision_note: >-
  Revision 3 re-anchors the change after the repository restructuring (commit 9953050a), which retired
  docs/features/identity-access/SPEC.md and lost the untracked change package. It records the decisions confirmed
  after revision 2 (suspended_counts, company_activate_immediately, copy_cuit_first), pins how PendingConfirmation
  counts, and carries the verified findings and the suggested delivery plan. Revision 2 is history only.
session_preflight:
  pace: auto
  artifacts: hybrid
  delivery_strategy: ask-on-risk
  chain_strategy: stacked-to-main
  chain_strategy_basis: CLAUDE.md delivery policy (direct commits to main; no pull requests or feature branches).
  confirmed_by: 'user on 2026-09-15 (Pace: Automatic; Artifacts: Both; PR strategy: Ask me)'
  delivery_note: >-
    Every delivery is a direct commit to main after green verification (CLAUDE.md); no pull requests or branches;
    at most 400 changed lines per delivery unless a maintainer-approved size:exception is recorded.
authorities:
  baseline_specs:
    - openspec/specs/identity-access/spec.md
    - openspec/specs/localization/spec.md
  accepted_adrs:
    - openspec/decisions/ADR-001-Use-EFCore-In-Application-Layer.md
    - openspec/decisions/ADR-004-Adopt-Multitenant-Identity-Access.md
    - openspec/decisions/ADR-007-Adopt-Cross-Project-Localization.md
  project_config: openspec/config.yaml
  standards:
    - .agents/skills/engineering-standards/SKILL.md
    - .agents/skills/frontend-design-standards/SKILL.md
    - .agents/skills/localization-standards/SKILL.md
    - .agents/skills/error-handling-standards/SKILL.md
  non_normative_references:
    - 'git show 4a2cfdb5:docs/superpowers/specs/2026-09-14-entrar-crear-cuenta-design.md (flow design validated by the user; git history only)'
    - 'docs/mockups/README.md, section "Entrar y crear cuenta" (navigable prototype approved by the user)'
  retired: >-
    docs/features/identity-access/SPEC.md and every other docs/features path are not authority. This change
    specifies the GET /api/identity/context response contract as the new identity-context-contract capability;
    openspec/specs/localization/spec.md keeps owning the account-preference scenarios that already mention the
    identity context (lines 59 and 192).
exploration:
  outcome: done
  references:
    openspec: null
    engram: sdd/account-setup-onboarding/explore
  openspec_note: >-
    exploration.md was untracked and was lost in the restructuring. It is not restored: it cites retired paths and
    its recommendations are superseded by the confirmed decisions below. The Engram copy remains evidence only.
  known_defects:
    - It cites retired docs/features/identity-access/SPEC.md paths; the baseline is openspec/specs/identity-access/spec.md.
    - >-
      It states that no backend change is needed for a no-context signal and recommends a narrow SPA-only gate with
      redirects into existing screens; superseded by gate_broad_server_signal and data_step_dedicated.
    - >-
      It states that the signed-in branch of POST /api/identity/organizations/register creates the tenant directly.
      False. That branch creates a PendingConfirmation tenant and responsible membership and queues
      identity.confirmation.requested
      (src/Application/IdentityAccess/Organizations/RegisterOrganization/RegisterOrganizationHandler.cs:99-114);
      activation happens on confirmation
      (src/Application/IdentityAccess/Organizations/ConfirmEmail/ConfirmEmailHandler.cs:110-119).
    - >-
      It cites the add-personal focus-order test as PersonalPages.jsx:342-369; the test is
      src/Web/ClientApp/src/features/identity/people/PersonalPages.test.jsx:342-374.
research:
  requested: false
  classes: []
  admission: not_requested
  outcome: not_selected
  references:
    openspec: null
    engram: null
product_decisions:
  - id: research
    status: confirmed
    answer: research_skip
    confirmed_by: user
  - id: gate
    status: confirmed
    answer: gate_broad_server_signal
    confirmed_by: >-
      user delegation in revision 2 ("lo que mejor recomiendes vos, lo que sea mas robusto y que la mayoria de los
      sistemas apliquen"); reconfirmed by the user in the 2026-09-15 resume instructions
    decision: >-
      GET /api/identity/context gains the additive, server-derived boolean member setupRequired. It is true when an
      authenticated identity holds no counting context (see suspended_counts) and is not a Platform invitee. A
      Platform invitee is never setup-required, neither while bound to a pending PlatformAdminInvitation
      (IsPendingAt) nor while holding a PlatformMfaEnrollment in Pending. The SPA enforces the signal with one guard
      on authenticated routes that redirects to /identity/setup?returnUrl=<requested path> and resumes through the
      existing safeReturnUrl validation after setup completes.
    exempt_routes_closed_list:
      - /identity/setup
      - /platform/mfa
      - /platform/mfa/recover
      - /invitations/accept
      - /external/return
      - /identity/account
      - /identity/sessions
      - /identity/password
      - /identity/external
    not_exempt:
      - /identity/profile
    evidence:
      - src/Application/IdentityAccess/Platform/Invitations/ConfirmPlatformInviteeHandler.cs:84-95 (BoundIdentityId and IsPendingAt)
      - src/Application/IdentityAccess/Platform/Mfa/PlatformMfaHandlers.cs:48-59 (PlatformMfaEnrollmentStatus.Pending)
      - tests/Application.FunctionalTests/IdentityAccess/Sessions/SessionTests.cs:490 (the context already carries the server-derived personalData member)
      - src/Web/ClientApp/src/components/api-authorization/ProtectedRoute.jsx (current authenticated route wrapper)
      - src/Web/ClientApp/src/features/identity/login/LoginPage.jsx (safeReturnUrl)
    rejected_options: [gate_narrow_honor_return_url, gate_narrow_override_return_url, gate_broad_without_server_signal]
  - id: suspended_counts
    status: confirmed
    answer: suspended_counts
    confirmed_by: user
    decision: >-
      A Suspended membership or a Suspended tenant counts as an existing context (setupRequired false). A Revoked
      membership or a Closed tenant does not count. A PendingConfirmation membership or a PendingConfirmation tenant
      does not count.
    pending_confirmation_basis: >-
      The user's 2026-09-15 resume instructions asked the spec to pin PendingConfirmation with the recommendation
      "does not count". Verified: every tenant is created PendingConfirmation
      (src/Domain/IdentityAccess/Tenants/Tenant.cs:124-139), every membership starts PendingConfirmation
      (src/Domain/IdentityAccess/Memberships/TenantMembership.cs:30-44), and Reinstate returns a revoked membership
      to PendingConfirmation until its invitation is accepted again (TenantMembership.cs:105-115). An Active identity
      can hold such rows only through a signed-in organization registration made before company_activate_immediately
      ships, through a reinstated membership awaiting acceptance at the exempt /invitations/accept, or through a
      Platform membership covered by the Platform invitee rule
      (src/Application/IdentityAccess/Platform/Mfa/PlatformMfaHandlers.cs:157). None of them gives the person a
      usable context.
  - id: invitations
    status: confirmed
    answer: invitations_defer
    confirmed_by: user
    known_gap: >-
      The setup screen does not list pending invitations in this change; a person with an invitation keeps using
      /invitations/accept?token=... as today.
  - id: data_step
    status: confirmed
    answer: data_step_dedicated
    confirmed_by: user
    decision: >-
      After choosing Personal or Company, the person completes the data inside /identity/setup, reusing the existing
      personal-context form (POST /api/identity/personal) and the authenticated organization registration form
      (POST /api/identity/organizations/register) without changing their ids, names, data-testid values, roles,
      headings or en strings. A preselected type (from the Google create-account carry or from the
      /personal/register redirect) opens directly on its data step, and the person can go back and change the type.
      An authenticated visit to /personal/register goes to /identity/setup with Personal preselected when
      setupRequired is true and to /identity/profile otherwise; it never calls registerPersonal.
  - id: company_activation
    status: confirmed
    answer: company_activate_immediately
    confirmed_by: user
    decision: >-
      With a validated session (an Active identity, IA-REQ-048 and IA-REQ-054), the signed-in branch of
      POST /api/identity/organizations/register creates the tenant, the responsible membership, ownership, the
      initial roles and the audit record Active in one transaction, with no confirmation email. The anonymous
      branch is unchanged.
    current_behavior: >-
      The signed-in branch creates PendingConfirmation rows and queues identity.confirmation.requested
      (src/Application/IdentityAccess/Organizations/RegisterOrganization/RegisterOrganizationHandler.cs:99-114);
      ConfirmEmailHandler activates the identity, tenant and membership and transfers ownership on confirmation
      (src/Application/IdentityAccess/Organizations/ConfirmEmail/ConfirmEmailHandler.cs:110-119).
    baseline_effect: Delta on openspec/specs/identity-access/spec.md requirements IA-REQ-003/048, IA-REQ-004 and IA-REQ-005.
    knock_on_effects:
      - Application functional tests for the signed-in registration branch.
      - The SPA message shown by the signed-in branch of /organizations/register.
      - The acceptance journey that registers another organization.
      - >-
        The spec fixes the signed-in branch's success status and body; the anonymous branch keeps its neutral bodyless
        202 (IA-REQ-003/048).
      - >-
        Rows left PendingConfirmation by signed-in registrations issued before this ships complete through the
        existing confirmation path; the design states how.
  - id: copy
    status: confirmed
    answer: copy_cuit_first
    confirmed_by: user
    decision: >-
      The Company choice card text register.choose.organization.detail names the CUIT before the legal name in en
      and es. It is a declared copy change made in every supported language.
    current_values: 'en "For a company. You will be asked for its legal name and CUIT."; es "Para una empresa. Se le solicitarán la razón social y el CUIT." (src/Web/ClientApp/src/i18n/locales/{en,es}/identity.json:21)'
    scope_note: register.choose.personal.detail is unchanged by this decision.
proposal_ready: true
proposal_handoff:
  capabilities:
    new:
      - identity-context-contract
      - account-setup
      - account-creation-entry
    modified:
      - identity-access (deltas on IA-REQ-003/048, IA-REQ-004 and IA-REQ-005 for company_activate_immediately)
  work_classification: >-
    Functional change. It deliberately edits the *.test.jsx files, page objects under tests/, AppRoutes.jsx and
    features/*/api/ files that assert the new behavior, each declared file by file. It introduces no error code.
  scope_in:
    - Server-derived setupRequired in GET /api/identity/context and the strict SPA contract that reads it.
    - One SPA guard with the closed exemption list and resume through safeReturnUrl.
    - /identity/setup with the type choice and a dedicated data step that reuses the existing forms.
    - Create account asks the account type first and offers Continue with Google; the chosen type survives the Google round trip.
    - CUIT or DNI is the first field in every SPA form that asks for one (tab order and focus on error).
    - An authenticated caller never reaches the anonymous /personal/register form.
    - Immediate activation of a Company registered with a session.
    - CUIT-first copy on the Company choice card.
  scope_out:
    - Email verification with a 6-digit code (change 2); the anonymous email path keeps link confirmation.
    - AFIP/ARCA padron autocomplete (change 3, blocked by IA-REQ-056); CUIT or DNI goes first now so it can be added later.
    - Listing pending invitations on the setup screen (known gap).
    - Password recovery, linking Google from inside the account, and general visual redesign.
  verified_findings:
    - >-
      The SPA reads the identity context strictly. identityContextMembers
      (src/Web/ClientApp/src/features/identity/api/identityClient.js:25) must add setupRequired, and signedInContext
      (src/Web/ClientApp/src/test/identityServer.js:7), the MSW identity-context fixture, must default it to false.
    - >-
      src/Web/ClientApp/src/web-api-client.ts and src/Web/wwwroot/openapi/v1.json are gitignored (.gitignore:42-43)
      and regenerated by the build.
    - >-
      Declared edits that are easy to miss. src/Web/ClientApp/src/test/identityFiles.contract.test.js:117-127 pins
      exactly twelve toProblem(error) catches, and
      tests/Application.FunctionalTests/IdentityAccess/Sessions/SessionTests.cs:490 pins the exact context member set.
    - >-
      The guard affects many journeys. IdentitySignInPage.SignInAsync
      (tests/Web.AcceptanceTests/Pages/IdentityAccessPages.cs:15-23) asserts landing on /identity, and
      IdentityAccessFixtures.ConfirmedIdentityAsync (tests/Web.AcceptanceTests/IdentityAccessFixtures.cs:110-127)
      seeds an Active identity with no membership. The "belongs to nothing" scenario becomes the setup journey,
      invitation journeys expect the setup screen after sign-in, and account lifecycle, device and
      forgotten-password journeys need a seeded membership.
    - >-
      Field order is functional work (tab order and focus on error). It touches organizationRegistrationFields and
      personalRegistrationFields (src/Web/ClientApp/src/features/identity/fieldErrors.js:50-51), the add-personal
      field list and focus order in src/Web/ClientApp/src/features/identity/people/PersonalPages.jsx (lines 79-88),
      src/Web/ClientApp/src/features/identity/register/RegisterOrganizationPage.test.jsx and
      src/Web/ClientApp/src/features/identity/people/PersonalPages.test.jsx:342-374.
    - >-
      The account-type carry is a non-authoritative sessionStorage record following
      src/Web/ClientApp/src/features/identity/useIdentityProof.js. It is written only when the external login
      actually starts, consumed once on signed_in with setupRequired true, and cleared in every other case; when it
      is lost, setup falls back to the generic type choice.
    - >-
      Google cannot be driven in Playwright. Coverage comes from
      tests/Application.FunctionalTests/IdentityAccess/ExternalLogins/GoogleOidcTests.cs and SPA tests with MSW; the
      setup journey signs in with a password.
    - >-
      GetIdentityContextHandler
      (src/Application/IdentityAccess/Context/GetIdentityContext/GetIdentityContextHandler.cs) must gain the
      Platform invitee checks. Revision 2's design proposed one shared rule, IdentitySetupRequirement.IsRequiredAsync,
      also used by SelectTenantHandler (src/Application/IdentityAccess/Context/SelectTenant/SelectTenantHandler.cs);
      the design revalidates it.
  constraints:
    - No new error code; every failure reuses the existing contract.
    - >-
      Every new or changed human-readable string ships in en and es; the spec catalog includes identity:setup.title,
      identity:setup.subtitle, identity:setup.changeType and the CUIT-first register.choose.organization.detail.
    - Strict TDD (openspec/config.yaml strict_tdd true).
    - >-
      The design leaves no open decision, includes the threat matrix from
      .agents/skills/sdd-design/references/threat-matrix.md, and passes a fresh-context contract validation.
    - Conventional commits with no Co-Authored-By or AI attribution (AGENTS.md).
    - No new truth under docs/features and no plans under docs/superpowers.
    - Changes made by the user's parallel chats are never staged or reverted.
  suggested_deliveries:
    ordering_rule: The guard never ships before the screen it redirects to.
    slices:
      - 1. Field order and CUIT-first copy.
      - 2. Immediate Company activation.
      - 3. setupRequired signal and SPA contract.
      - 4. Refactor to reuse the forms.
      - 5. The /identity/setup screen.
      - 6. Guard, /personal/register redirect and journey edits.
      - 7. Google carry on create account.
    budget: >-
      At most 400 changed lines per verified commit to main; sdd-tasks forecasts the workload and the orchestrator
      asks for the cut before apply (ask-on-risk).
```