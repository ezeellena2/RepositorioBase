# ERP AI Data

Research package for a product idea: connect a customer's existing ERP, and let an AI layer answer questions and
assemble screens over that data. The reference point is [Visionaris](https://www.visionaris.net/).

This folder is **research only**. It authorizes nothing, claims no runtime behavior, and proposes no scope. It exists
to establish how such products work internally before anyone writes a SPEC.

## Artifacts

- [RESEARCH.md](RESEARCH.md): what the category is, layer by layer; what Visionaris appears to do; how the
  architecture maps onto this repository; risks, buy-vs-build, and a build order.
- [GROUNDING.md](GROUNDING.md): how you actually "train" the AI on an existing ERP database — the five artifacts
  that replace fine-tuning, how to mine what the ERP does not report today, and a 30-day plan.
- [profiler/](profiler/): read-only schema profilers for SQL Server and PostgreSQL, the first step of that plan.

## The short version

An LLM never touches the ERP schema. Two deterministic layers sit in between — a **canonical model** (each ERP's
tables mapped onto business entities by a per-ERP adapter) and a **semantic layer** (metrics, dimensions, joins,
grain and access policies declared once). The model emits a validated **query spec** and a **view spec** built from
allow-listed components; deterministic code compiles the spec to SQL and renders the screen. Accuracy comes from the
modelling, not from the model: published 2026 benchmarks move from 53–90% on bare schemas to 98–100% once a semantic
layer grounds the same questions.

## Relationship to existing decisions

[ADR-006](../../decisions/ADR-006-Bot-Capability-Catalog-And-Modules.md) already decided the safety architecture this
product needs — a closed catalog the model selects from, a model that never authors a query or an identifier,
per-module encrypted credentials with computed availability, and two-phase confirmation with deterministic summaries
for anything irreversible. [ADR-004](../../decisions/ADR-004-Adopt-Multitenant-Identity-Access.md) supplies the
tenant isolation and deny-by-default authorization that make multi-tenant analytics safe. An ERP-AI product is those
decisions plus a new module kind and a semantic layer, not a new architecture.

## Status

Research. Not reviewed, not accepted. Next step, if the idea proceeds, is a wedge decision (which ERP, which buyer,
read-only or write-back) — see the open questions at the end of RESEARCH.md.
