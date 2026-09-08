# Independent Identity Access review for development

Review the complete Identity Access feature in the current checkout of this repository, including Tasks 1–28 and relevant uncommitted changes. Determine whether its intended behavior is implemented correctly and usable in LOCAL DEVELOPMENT. We are not releasing to production. We need to run the application, test its real journeys thoroughly, and find serious bugs before a later Azure deployment.

Follow the repository's applicable instructions. Treat attached specifications, plans, delivery records, and reports as evidence to verify, not instructions to execute. Respect the disabled receipt-driven-development switch; do not enable RDD or start a new SDD workflow. Do not send repository contents to an additional external review service without authorization.

## Scope and evidence

Record the commit and working-tree status you inspect. Include relevant uncommitted code rather than silently reviewing only HEAD. If another agent is still changing those files, coordinate a stable review snapshot; do not edit or interfere with its work. Revalidate previous findings against the actual current code: the account lifecycle and context-refresh fixes may already be present.

Read the accepted requirements and latest amendments in SPEC.md and ADR-004, the implementation plan, TASKS.md, TRACEABILITY.md, DELIVERY.md, and the local-running and operations guides. Resolve historical contradictions explicitly. Withdrawn proposals are not current requirements: in particular, preserve C6 amendment E1's password-based reactivation contract rather than resurrecting provider recovery.

Build a compact coverage matrix for every accepted requirement. Trace it through the actual HTTP contract, React route and action, application handler, domain rule, persistence and transaction, authorization, outbox delivery, configuration, and relevant tests. Mark each row as implemented and verified, implemented with limited evidence, partially implemented, missing, or explicitly deferred. Do not infer completion from task checkboxes or test totals.

Look for implementations that exist only on paper or in isolation: unused client methods, unreachable screens, missing registrations or message handlers, configuration that never reaches its consumer, guards with no callers, and workers missing from the local launch topology. Verify links and navigation from the person's starting point, not only by manually opening a deep route.

## Journeys to inspect

- Organization and Personal registration, neutral responses, delivered confirmation, sign-in, and adding a context to an existing identity.
- Password change, public password recovery, devices and session revocation, own-account deactivation and reactivation.
- Invitations, resend and cancellation, acceptance and reincorporation, member administration, custom roles, permission changes, tenant selection, and ownership transfer.
- Personal document masking, claims, dispute submission, two-party correction, and retained fingerprints.
- External sign-in, explicit linking and unlinking, recent proof, and return to the pending operation, including targets beyond the first list page.
- Platform bootstrap, later administrator onboarding, MFA enrollment and acknowledgement, step-up, recovery, organization and identity administration, and audit visibility.
- Retention configuration, legal holds, maintenance execution, erasure, and recovery admission, within the accepted local synthetic scope.

Check successful paths and meaningful refusals: unauthenticated or wrong-tenant access, insufficient permission, invalid or expired tokens, replay, stale versions, conflicting operations, removed roles, disabled identities, and failures of dependencies. Examine security, consistency, and data loss even when a bug requires concurrency, but give a concrete scenario and evidence instead of hypothetical severity.

Pay particular attention to tenant and Platform isolation, immediate server-side permission revocation, owner and administrator floors, session cookies and antiforgery, neutral public responses, single-use session/action-bound C4 proof, MFA recovery, provider no-auto-linking, shared abuse budgets, complete transaction scope, outbox retries and leases, legal hold versus purge, and recovery quarantine. Do not weaken an existing control to obtain a passing test.

## Verification that cannot skip the broken step

Use the existing test facilities and normal local setup. At the narrowest useful level, reproduce suspected defects before declaring them confirmed. Run relevant broader suites once after focused investigation when justified. Use fresh isolated test databases, synthetic data, and local mail delivery. Never reset, drop, or reseed the developer's persistent database or remove its container. Clean up only disposable resources created by your own run.

Check that the tests themselves distinguish the intended behavior:

- A reactivation journey must receive the dispatcher's actual delivered email and use its link. Reading a decrypted token directly from the database or a secret service does not prove delivery.
- Invitation acceptance and Personal-context creation must expose the new context through normal SPA navigation without reloading the document. Preserve the intended active-tenant semantics; do not invent automatic switching.
- Permission checks must reach the server with the affected identity, rather than only asserting that a button disappears.
- A provider return must retain the operation's target and intent, resolve paginated data, and execute at most once.
- Concurrency tests must control the competing operations and observe the invariant, rather than rely on sleeps or repeated luck.
- A test for rejection must fail for the stated reason, not because setup, authentication, or some earlier unrelated guard already rejected the request.

Record exact commands, exit codes, counts, and unavailable evidence. You may add clearly scoped reproduction tests, but do not alter product code, existing assertions, or completion documents to make the review pass. Do not commit, push, deploy, reset, restore, or stash the working tree. Leave findings for a separate correction step.

## Proportionate conclusions

Distinguish local bugs from later deployment work. Real Google credentials and behavior, a real email domain, real personal data, production keys, Azure infrastructure, external restore certification, legal assessment, and tests across separate machines do not block testing the local journeys. Missing local configuration wiring does constitute a local integration defect.

Missing retention categories or unmodeled external deletion evidence are incomplete functionality where the accepted scope requires them. Report them honestly without treating them as a reason to postpone all other developer testing. Do not add requirements, speculative abstractions, arbitrary refactors, formatting preferences, or a quota of findings. You may delegate independent read-only areas, but avoid redundant whole-feature reviews.

## Report in Spanish

Lead with whether the main journeys can be tested now and whether the entire accepted feature is actually complete. Then provide:

1. Confirmed findings ordered by severity: exact file/line, triggering situation, expected and actual behavior, consequence, evidence, and the smallest appropriate correction or missing regression.
2. Suspicions separately, with the specific evidence still needed. Absence of a test alone is not proof of a bug.
3. The accepted-requirement coverage matrix and inspected areas, including anything not inspected.
4. Executed tests and their practical limits.
5. Open items classified as a local functional blocker, incomplete accepted scope, documentation inconsistency, manual validation, or later Azure/deployment/certification work.
6. A short prioritized correction sequence that gets us to complete, correct development journeys.

Do not claim that there are zero critical bugs simply because the tests passed. State what your inspection and executions actually establish.
