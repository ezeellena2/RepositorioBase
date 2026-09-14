# Engineering rules

Apply these rules only to the affected behavior and layers. Preserve approved SPECs, accepted ADRs, and verified conventions; resolve conflicts from that evidence before introducing a different contract.

## Protect layer responsibilities

| Layer | Required responsibility and boundary |
| --- | --- |
| Domain | Own business rules and invariants. Depend on neither Application, Infrastructure, nor Web. Introduce entities and value objects only for domain meaning, identity, behavior, or invariant protection. |
| Application | Own use cases, orchestration, authorization, and explicit contracts. Consume narrow, consumer-oriented infrastructure ports. Preserve `Result<T>` and `ApplicationError` conventions for expected business failures; keep unexpected infrastructure or programmer failures on the existing exception path. |
| Infrastructure | Implement persistence, PostgreSQL, Identity, and external-service adapters. Keep business decisions in Domain/Application. Ensure transactions and concurrency protections cover the complete affected data set. |
| Web | Keep endpoints thin and HTTP contracts consistent. Map internal results to the established HTTP contract rather than exposing internal result types. Organize React by journeys/features; correctly expose accessibility, loading, error, and permission states. |

Use DDD to express the established domain language, boundaries, and invariants. Do not invent aggregates or wrap every primitive. Preserve the repository's CQRS separation: commands change state and queries read without business side effects. Add separate models, stores, buses, or event sourcing only for a demonstrated need consistent with the existing architecture.

## Base-product capability gate

RepositorioBase is a reusable technical base, so implementation starts by deciding where each concern belongs. Cross-project mechanics should gain leverage and locality from one established module and interface; business rules that are meaningful only inside one domain should remain there.

Before implementing a feature, check each applicable item against current repository evidence:

- [ ] Classify the behavior as a domain-specific rule or a cross-project technical capability. Do not move domain meaning into `Common` merely because another feature may exist later.
- [ ] Search approved SPECs and ADRs, local standards, current shared modules, nearby implementations, and contract-test facilities before designing a feature-local mechanism.
- [ ] For a pageable collection screen, use the current canonical pagination request, response, control, and per-page read-state contract. Do not add pagination to a form, detail screen, or collection that does not need it.
- [ ] Route every human-readable and accessibility string through the established localization mechanism, including every supported language and explicit locale or recipient culture where required. Keep invariant protocol data untranslated.
- [ ] Keep HTTP successes as endpoint-specific DTOs or bodyless statuses. Do not introduce a universal `{ success, data, error }` envelope or expose internal `Result` types.
- [ ] Follow the error-handling standard for every failure path: use the shared mapping and transport, declare and test endpoint errors, and complete the required catalogue and localized-message coverage.
- [ ] Reuse the existing frontend transport, components, and loading, empty, refused, errored, stale-data, and mutation-state patterns. Follow the frontend standard; do not create a feature-local alternative, wrapper layer, or parallel design system.
- [ ] If the required shared capability is missing, place it at the narrowest existing seam that owns the shared semantics and prove it through that interface. If the implementation remains local, record why its semantics are feature-specific and why sharing would be speculative or incorrect.

A feature is not complete when it introduces a second way to solve an established cross-project concern without explicit evidence that the existing contract cannot serve it. This gate does not itself authorize a new abstraction or override KISS, YAGNI, Rule of Three, approved contracts, or the specialist standards.

## Choose a sufficient solution

Before writing code:

1. Confirm the requested outcome and approved acceptance criteria from the available evidence. Do not reinterpret or drop requested behavior to simplify the implementation.
2. Inspect existing code and conventions; reuse suitable code or established patterns before adding an implementation.
3. Evaluate the standard library, native platform capabilities, and installed dependencies against the actual requirements and repository boundaries.
4. Where those options leave a concrete unmet need, implement the smallest sufficient local solution. Justify a new dependency or abstraction by that need and its net benefit.

## Diagnose before abstracting

For a fix, trace the real flow and affected callers to identify the existing responsibility that owns the violated behavior. Fix a shared location only when its callers share the same invariant and semantics; do not change every caller by default or expand into unrelated behavior.

- Use domain names and cohesive functions/classes with clear responsibilities. Keep rules that change together together, make errors and effects explicit, and keep code easy to remove or replace.
- Apply SOLID through observable problems: independent reasons to change (SRP), demonstrated variation (OCP), preserved substitute contracts (LSP), unnecessary consumer dependencies (ISP), and actual policy/adapter boundaries (DIP). Do not require every class to satisfy a mechanical checklist.
- Point dependencies toward abstractions where a real boundary exists; keep interfaces small and consumer-oriented. Prefer composition when it reduces complexity, not by decree.
- Apply KISS to the current requirement and YAGNI to hypothetical extension points. Use Rule of Three as evidence that repeated behavior forms a stable abstraction; do not count text similarity alone or postpone necessary invariant protection until a third occurrence.
- Introduce a design pattern only when all conditions hold: it solves demonstrated complexity, reduces real coupling or duplication, offers more benefit than cost, fits existing conventions, and can be justified by the problem it solves. Start with the simplest solution; never require each class to have a pattern.
- Reject arbitrary line, parameter, or field limits; an interface per class; value objects without domain meaning; exceptions replacing expected `Result` failures; unnecessary layers, DTOs, repositories, factories, or services; and aesthetic refactors outside the task.

During the existing review, check proportionally for redundant code or dependencies and speculative flexibility within the task. Simplify only while preserving requested behavior, domain boundaries, explicit errors, security, accessibility, and transaction/concurrency guarantees, with appropriate tests. Line count alone never decides; retain interfaces justified by existing boundaries and reuse the established test frameworks and harnesses. This check adds no review phase, gate, report, or approval.

## Protect security and data

Enforce authorization at the application boundary with the relevant tenant/resource scope. Validate untrusted input at boundaries, protect secrets and private data, use safe persistence parameters, and preserve safe HTTP errors. Visible UI permissions never replace server enforcement.

Identify every row, aggregate, and competing operation contributing to an invariant. Scope transactions and concurrency protection to that full set, including rollback and conflict behavior; protecting only the initially loaded object is insufficient when the invariant spans more data. Validate the chosen mechanism against the actual database semantics and existing conventions.

## Choose proportional proof

- Test externally observable behavior and contracts, not internal implementation details. Match depth and scope to risk; use structural readback for passive instruction/document changes.
- When the workflow requires RED → GREEN → REFACTOR, establish a discriminating RED before correcting a defect: fail for the intended missing behavior, not compilation, setup, or unrelated environmental errors. Apply the smallest correction, observe GREEN, and refactor within scope while preserving it.
- Use real PostgreSQL integration tests for guarantees dependent on PostgreSQL semantics; mocks or substitute engines cannot establish those guarantees.
- For a race, use deterministic coordination at the competing operations and assert the invariant/outcomes. Do not rely on sleeps or probabilistic repetition.
- Reserve E2E tests for critical user journeys. Place each guarantee at the narrowest sufficient level; avoid duplicating it across every layer.
- Reuse existing test facilities. Do not create unnecessary test-only applications or infrastructure, or change an assertion solely to accommodate the current implementation.
- Preserve unavailable or partial verification honestly; never claim a passing check that did not run.

## Combine skills contextually

Resolve applicable specialist paths from the current registry and pass exact paths to delegated agents. Load the complete files rather than copying these rules into prompts.

| Work | Combination when relevant |
| --- | --- |
| Backend / architecture | `engineering-standards`; add `architecture-patterns` when needed. |
| PostgreSQL | Add `postgresql-expert`. |
| React | Add the current applicable React skill from the registry. |
| Code review | Add `code-review`, subject to the existing Gentle AI review authority and kill switch. |
| Completion | Add `verification-before-completion` for completion claims. |

These combinations do not load global skills for unrelated tasks or create additional review, SDD, approval, or delivery ceremonies.
