---
name: engineering-standards
description: "Trigger: feature implementation, bug fixes, refactoring, architecture, code review, tests. Apply contextual repository engineering standards."
license: Apache-2.0
metadata:
  author: repository-maintainers
  version: "1.2"
---

## Activation Contract

Load for feature implementation, bug fixes, refactoring, architecture design, code review, and creating/modifying tests. Exclude simple conceptual questions, status reads, purely administrative changes, and documentation without technical impact.

## Hard Rules

Apply this priority order:

1. Requested behavior, security, and approved contracts.
2. Domain and data invariants.
3. Existing repository architecture.
4. Simplicity and clarity.
5. Extensibility supported by a real need.
6. Aesthetic preferences.

- Respect approved SPECs, accepted ADRs, and verified conventions. This local skill overrides incompatible generic global advice.
- Keep Gentle AI as routing and review authority; this skill neither forces SDD nor enables receipt-driven development.
- Treat RepositorioBase as a reusable base product. Before adding feature-local infrastructure, apply the [base-product capability gate](references/engineering-rules.md#base-product-capability-gate), then reuse or extend the established owner when the concern is cross-project.
- Product-facing system text delivered through the SPA, email, or bot ships with a key in every supported language; new API error codes, enum values, permissions, and statuses ship with their catalog entries. Documentation, logs, and developer diagnostics stay invariant under the linked contract. Follow [localization-standards](../localization-standards/SKILL.md).
- Read [engineering rules](references/engineering-rules.md) before substantive work. Preserve layer duties, `Result<T>` / `ApplicationError`, and explicit errors and effects.
- Diagnose with Clean Code, Clean Architecture, DDD, CQRS, and SOLID. Apply KISS, YAGNI, and Rule of Three against speculative abstractions.
- Stay within the task. Reject arbitrary size limits, mandatory wrappers/interfaces, decorative patterns, redundant layers, and unrelated aesthetic refactors.

## Decision Gates

| Situation | Action |
| --- | --- |
| Considering an abstraction/pattern | Require proven complexity, reduced coupling/duplication, net benefit, and convention fit; otherwise keep the simpler design. |
| Security, data, or concurrency changes | Identify the complete invariant and affected data scope before choosing protection and proof. |
| Testing applies | Select proportional behavior tests; use discriminating RED → GREEN → REFACTOR when required by the workflow. |
| Specialist knowledge is needed | Use the reference's contextual combinations; retain local precedence. |

## Execution Steps

1. Apply the reference's base-product capability gate and classify each concern as domain-specific or cross-project.
2. Read relevant contracts, architecture, and nearby code; identify affected layers, invariants, consumers, and test facilities.
3. Follow the reference's solution-selection order. Justify new boundaries, DDD models, CQRS separation, or patterns by the problem solved.
4. Implement or inspect the requested scope. Keep related rules together and dependencies aligned with real boundaries.
5. Run applicable checks; include proportional simplification in the existing review alongside security, transaction scope, concurrency, maintainability, and accessible UI states where relevant.

## Output Contract

Report the outcome, material rationale or findings, verification evidence, and remaining risks or unavailable proof. Keep the response proportional; create no extra plans or artifacts merely for this skill.

## References

- [Engineering rules](references/engineering-rules.md) — base-product gate, layer duties, diagnostics, safety, testing, and skill combinations.
