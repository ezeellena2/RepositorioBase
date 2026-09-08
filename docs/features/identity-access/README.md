# Identity Access

Proposed identity/access planning package prepared for SDD delivery of a reusable multitenant SaaS foundation with ASP.NET Core, PostgreSQL, React, and a minimal MFA-bound Platform operations slice.

The package adopts the reference standard's business semantics, not its workflow.

## Artifacts

- [SPEC.md](SPEC.md): behavior, invariants, contracts, and acceptance criteria.
- [TASKS.md](TASKS.md): slices, dependencies, review forecast, and status.
- [TRACEABILITY.md](TRACEABILITY.md): requirement → task → test → evidence.
- [RUNNING-LOCALLY.md](RUNNING-LOCALLY.md): the synthetic run, end to end.
- [OPERATIONS.md](OPERATIONS.md): what an operator configures, what they can watch, and what they still cannot do.
- [RECOVERY-AND-RETENTION.md](RECOVERY-AND-RETENTION.md): lost second factors, stored personal data, restore admission.
- [EMAIL-SETUP.md](EMAIL-SETUP.md): mail transport configuration.
- [ADR-004](../../decisions/ADR-004-Adopt-Multitenant-Identity-Access.md): architecture decision.
- [Implementation plan](../../superpowers/plans/2026-08-31-identity-access-foundation.md): executable TDD steps.

## Status

The planning package remains under human review for the pending Identity Access slices. Tasks IA-002 and IA-003 are implemented with a PostgreSQL-only template, a safe baseline migration, and no default credentials; this does not authorize the remaining runtime behavior.
