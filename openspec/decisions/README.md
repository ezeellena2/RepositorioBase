# Architectural Decision Records

This directory contains Architecture Decision Records (ADRs) for the Clean Architecture Solution Template.

An ADR captures a significant cross-project architectural decision: its context, decision, rationale, and
consequences. Significant decisions are identified and accepted during the design of an active OpenSpec change.
Retaining or promoting them here during archive is a repository-owned, manual SDD obligation enforced by local
instructions and tests today; Gentle AI and OpenSpec do not perform that promotion automatically. Feature-local
decisions remain in the archived design.

An accepted ADR is immutable. If the architecture changes, add a new ADR and mark the old record as superseded rather
than rewriting history. Use [ADR-000-template.md](ADR-000-template.md) when adding a record. Current behavior belongs
in [`openspec/specs/`](../specs/), active work in [`openspec/changes/`](../changes/), and implementation evidence in
`src/` and `tests/`.

| ADR | Title | Date | Status |
|---|---|---|---|
| [ADR-001](ADR-001-Use-EFCore-In-Application-Layer.md) | Use EF Core in the Application Layer | 2024-02-29 | Accepted |
| [ADR-002](ADR-002-Aspire-For-Orchestration-And-Testing.md) | Aspire for Orchestration and Testing | 2026-03-12 | Accepted |
| [ADR-003](ADR-003-MediatR-Contracts-In-Domain.md) | MediatR.Contracts Reference in Domain | 2026-03-16 | Accepted |
| [ADR-004](ADR-004-Adopt-Multitenant-Identity-Access.md) | Adopt Multitenant Identity Access with React and PostgreSQL | 2026-08-31 | Proposed |
| [ADR-005](ADR-005-Adopt-WhatsApp-Delivery-Channel.md) | Adopt WhatsApp as a Second Delivery Channel | 2026-08-31 | Proposed |
| [ADR-006](ADR-006-Bot-Capability-Catalog-And-Modules.md) | Bot Capability Catalog and Pluggable Modules | 2026-08-31 | Proposed |
| [ADR-007](ADR-007-Adopt-Cross-Project-Localization.md) | Adopt a Cross-Project Localization Standard | 2026-09-10 | Accepted |

