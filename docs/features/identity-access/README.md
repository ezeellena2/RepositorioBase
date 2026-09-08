# Identity Access

Proposed identity/access planning package prepared for SDD delivery of a reusable multitenant SaaS foundation with ASP.NET Core, PostgreSQL, React, and a minimal MFA-bound Platform operations slice.

The package adopts the reference standard's business semantics, not its workflow.

## Artifacts

- [SPEC.md](SPEC.md): behavior, invariants, contracts, and acceptance criteria.
- [TASKS.md](TASKS.md): slices, dependencies, review forecast, and status.
- [TRACEABILITY.md](TRACEABILITY.md): requirement → task → test → evidence.
- [DELIVERY.md](DELIVERY.md): what was delivered, what it is evidence for, and what it is not.
- [RUNNING-LOCALLY.md](RUNNING-LOCALLY.md): the synthetic run, end to end.
- [OPERATIONS.md](OPERATIONS.md): what an operator configures, what they can watch, and what they still cannot do.
- [RECOVERY-AND-RETENTION.md](RECOVERY-AND-RETENTION.md): lost second factors, stored personal data, restore admission.
- [EMAIL-SETUP.md](EMAIL-SETUP.md): mail transport configuration.
- [ADR-004](../../decisions/ADR-004-Adopt-Multitenant-Identity-Access.md): architecture decision.
- [Implementation plan](../../superpowers/plans/2026-08-31-identity-access-foundation.md): executable TDD steps.

## Status

**Local B2B/B2C identity access is functionally complete with synthetic data** as of 2026-09-07 (plan Tasks 17–28). That sentence is the whole claim: real personal data, production, Google activation and live restore certification are separate gates and remain open, each with a named owner in [DELIVERY.md](DELIVERY.md#6-what-is-not-closed-and-who-owns-it).
