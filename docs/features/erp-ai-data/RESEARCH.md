# Research: "Connect any ERP and get AI-generated data and screens"

**Status:** Research only — no decision, no SPEC, no committed scope.
**Date:** 2026-09-14
**Trigger:** A product idea in the shape of [Visionaris](https://www.visionaris.net/): plug into whatever ERP the
customer already runs, and have an AI layer return answers, indicators and ready-made screens on top of it.
**Question this document answers:** what such a product actually does internally, which parts are hard, and what a
version of it built on this repository would look like.

---

## 1. Executive summary

1. **Nothing in this category works by handing a language model a database connection.** Every credible product puts
   two deterministic layers between the model and the data: a **canonical model** (each ERP's tables mapped onto
   business entities) and a **semantic layer** (metrics, dimensions, joins, grain and access rules declared once).
   The model chooses *from* those declarations; it does not author SQL against raw ERP tables.
2. **"Connects to any ERP" is a marketing sentence for "we ship N pre-built adapters and sell the N+1 as a service."**
   The adapter — knowing that this table is a sales order and that column is net of tax in this ERP — is the actual
   product. It is built by people, once per ERP, and amortised across customers.
3. **"The AI builds the screens" is real but far less magical than it reads.** The model does not emit React. It emits
   a **JSON view spec** that references components from a closed catalog and metrics from the semantic layer; the
   frontend renders it. This is the industry's "declarative generative UI" pattern, and it is chosen precisely because
   it is safe, consistent and testable.
4. **Accuracy is bought with modelling, not with a better prompt.** Published 2026 numbers put frontier models at
   84–90% on bare schemas and at 98–100% once a semantic layer grounds them; Databricks documents 53% → 80% → 100% as
   metadata and metric definitions are added. The delta is the modelling work, not the model.
5. **This repository is unusually well positioned for it.** [ADR-006](../../decisions/ADR-006-Bot-Capability-Catalog-And-Modules.md)
   already decided the exact safety architecture this product needs — a closed capability catalog, a model that never
   authors a query or an identifier, per-module encrypted credentials, computed availability, deterministic rendering
   of anything a human approves. An ERP-AI product is that ADR with a new module kind and a semantic layer under it.
6. **The honest risk:** the demo is a weekend, the pilot is a quarter, and the moat is the adapter and metric library
   that takes two years. Plan for the library, not for the demo.

> **Evidence caveat.** `visionaris.net` and `softwaredoit.es` are blocked by this session's network egress proxy, so
> the Visionaris section below is reconstructed from search-engine summaries of their own pages plus third-party
> descriptions. Statements about their *internals* are inference from their public positioning, and are marked as
> such. Everything in sections 3–8 is general architecture, independently sourced.

---

## 2. What Visionaris appears to be

Reconstructed from their public pages (`/`, `/soluciones-pre-armadas/`, `/soluciones-embebidas/`, `/data-collect/`,
`/servicios/`, `/blog/`) and from an aggregator listing.

**Positioning.** A Business Intelligence / Enterprise Performance Management suite from Buenos Aires (Av. Roque Sáenz
Peña 616), sold as "all your company's data, whatever its source, connected, analysed and returned as dashboards and
control panels", with real-time access for every member of the organisation. Services listed include BI, command
dashboards, EPM, KPIs, Big Data, Machine Learning, advanced analytics and balanced scorecard.

**Modules named publicly.** Data Analytics, Data Action, Data Collect, Data Planning — i.e. read (dashboards), act,
capture data the ERP does not hold, and plan/budget on top of actuals.

**The two commercial packages that reveal the architecture:**

- **Soluciones pre-armadas** ("pre-built solutions"). Their words: it is *not necessary to create anything*, because
  a complete implementation already exists — the data models were built by their own experts over 10+ years. That is
  a plain description of a **library of per-ERP adapters plus per-domain dimensional models plus canned dashboards**.
- **Soluciones embebidas** ("embedded solutions"). Their words: pre-resolved queries in data flows already created
  with all the tables and queries for your system; data sets for the usual functional areas, fully extensible;
  analysis templates, indicators and dashboards; pre-designed dashboards integrated into *your* application, where
  *your* customers can also build their own. That is **embedded/white-label analytics sold to ISVs** — the same
  library, rendered inside someone else's product.

**Inference about internals** (not confirmed): this is a classic BI stack — an ETL/replication tier into a
dimensional model, an analytical engine, and a dashboard authoring/embedding tier — with ML and NL features layered
on top, rather than an LLM inventing screens at runtime. Two signals support that reading: the emphasis on
*pre-built* models as the value proposition (a truly generative system would not need them), and a blog piece
arguing you should implement Visionaris *now, without waiting for the new ERP* — the pitch of a read-only analytics
layer that sits beside whatever source system exists.

**What that means for you.** If you build this, you are not competing with a model. You are competing with a
ten-year head start on adapters and metric definitions — and the leverage an LLM gives you is that the adapter and
metric authoring can now be assisted, and that the last mile (question → screen) no longer needs a BI consultant.

---

## 3. The anatomy of the thing you actually want to build

Seven layers. Each one is deterministic except L4, and L4's output is validated against L3 before anything runs.

```
L6  Governance, audit, evals, cost control            (cross-cutting)
────────────────────────────────────────────────────────────────────
L5  View spec  → rendered by a closed component catalog        (screens)
L4  Agent      → emits a QUERY SPEC, never SQL                 (the "AI")
────────────────────────────────────────────────────────────────────
L3  Semantic layer: metrics, dimensions, joins, grain, policies
L2  Canonical model: SalesOrder, ArInvoice, StockMove, GlEntry
L1  Landing / raw: one schema per source, as extracted
L0  Connectivity: CDC · read replica · API · file drop · on-prem agent
```

### L0 — Connectivity: how you physically reach an ERP

Four families, and a real product ships all four because customers are not uniform:

| Mode | When it applies | Cost/risk |
|---|---|---|
| **Log-based CDC** on the ERP's own database | On-prem SQL Server/Oracle/Postgres ERPs; you can reach the DB | Lowest latency and lowest load on the source; needs DB privileges and network access |
| **Read replica / nightly snapshot** | Same, but the customer will not enable CDC | Simple, hours-stale, heavy queries on someone else's box |
| **Vendor API** (REST/OData/SOAP/RPC) | Cloud ERPs and modern suites | Governed and supported, but rate-limited, paginated, and often missing the fields analytics needs |
| **On-prem agent / file drop** | Locked-down networks, tiny ERPs, "we can export a CSV" | Always works; the most operational support burden |

Argentine market note: **Tango (Axoft)** stores data in Microsoft SQL Server and exposes only partial REST surfaces
(e.g. a stock endpoint under `tiendas.axoft.com/api/`) — so the realistic Tango adapter is *database-level*, not
API-level. Cloud-native local players (Xubio, Colppy) are the opposite: API-only. SAP Business One offers the
Service Layer / DI API; Odoo offers XML-RPC/JSON-RPC and direct Postgres. **Design for both shapes from day one** —
an adapter contract that assumes "there is an API" will fail on the single most common ERP in your target market.

Egress design matters as much as protocol: most on-prem customers will not open an inbound port. The standard answer
is an **outbound-only agent** installed in the customer's network that pulls from the ERP and pushes to you over
TLS, holding its own credentials.

### L1 — Landing: raw, per-source, append-only

Land extracted rows exactly as they came, with `_source`, `_extracted_at`, `_batch_id`, `_op` (insert/update/delete)
and a natural key. Never transform on the way in. This is what lets you fix an adapter bug by replaying instead of
re-extracting from a customer's production ERP at 3 a.m.

### L2 — Canonical model: the adapter, and the real product

A small, stable set of business entities the rest of the system speaks: `SalesOrder`, `SalesOrderLine`, `ArInvoice`,
`ApInvoice`, `Payment`, `Customer`, `Supplier`, `Product`, `StockMove`, `GlEntry`, `CostCenter`, `Employee`.
Each ERP adapter is a mapping from its schema onto these, and the mapping carries the domain knowledge nobody wants
to rediscover per customer:

- which document types count as a sale and which are internal;
- whether the amount column is gross, net, or net of a discount applied at the header;
- how the ERP models credit notes, and therefore how a return reduces revenue;
- what "cancelled" looks like — a status flag in one ERP, a compensating document in another;
- multi-currency: the rate table, the rate date convention, and which columns are already converted;
- the tenant/company/branch dimension inside a single ERP database.

**Two rules that decide whether this scales.** First, the adapter is *declarative wherever it can be* (a mapping
document, versioned, reviewable, testable against a fixture database) and code only where it must be. Second, every
adapter ships with **fixtures and assertion tests** — a known ERP snapshot in, known canonical rows out — because
ERP versions and per-customer customisations (SAP Z-tables, Tango custom fields) will silently break mappings, and
you need that to be a red test rather than a wrong number on a CFO's screen.

Expect per-customer deviation even within one ERP. The mature design is `adapter = base mapping + customer overlay`,
where the overlay is data, not a fork.

### L3 — Semantic layer: where accuracy actually comes from

A declaration, per domain, of:

- **entities** and their **grain** (`ar_invoice` is one row per invoice; `ar_invoice_line` is one per line);
- **dimensions** (`customer.segment`, `product.category`, `date.month`) with synonyms in every language you sell in;
- **metrics** with an explicit formula, an aggregation, a currency/unit, and a definition in words
  (`net_revenue = sum(line.net_amount) excluding document types in (…), credit notes negative`);
- **joins** the compiler is allowed to make, so fan-out double counting is structurally impossible;
- **access policies** — row-level and column-level — attached to the model, not to the query.

This is the layer that turns "how much did we sell last month" from a guess into a compile. Published 2026 evidence
on the size of the effect:

- dbt Labs' benchmark: **90.0% → 98.2%** (Claude Sonnet 4.6) and **84.1% → 100%** (GPT-5.3-Codex) when the same
  questions are grounded in a semantic layer instead of a bare schema; semantic modelling beats raw text-to-SQL by
  8–16 points overall.
- Databricks Genie: **53% baseline → 80% with metadata enrichment → 100% with full metric definitions and example
  SQL.**
- Snowflake Cortex Analyst: 90%+ on real workloads, explicitly attributed to the semantic model rather than the LLM.

Read those numbers as a work order: **the roadmap item that raises accuracy is writing metric definitions, not
swapping models.**

The other reason this layer exists is security. When row-level and role-based rules are evaluated at
*query-compile* time, an agent literally cannot query data the user is not allowed to see — the rules become part of
how every query is generated, instead of a filter a prompt might forget.

### L4 — The agent: text → query spec, not text → SQL

The single most important design decision in the whole product:

> The model's output is a **JSON query spec** naming metrics, dimensions, filters, a time grain and a limit —
> all of which must exist in the semantic layer. A deterministic compiler turns that spec into SQL, injecting the
> tenant and the caller's row-level policy. An invalid spec is rejected before it reaches the database.

```jsonc
// what the model is allowed to emit
{
  "intent": "trend",
  "metrics": ["net_revenue", "gross_margin_pct"],
  "dimensions": ["date.month", "product.category"],
  "filters": [{ "field": "date.month", "op": "last_n", "value": 12 },
              { "field": "customer.segment", "op": "in", "value": ["retail"] }],
  "order_by": [{ "field": "date.month", "dir": "asc" }],
  "limit": 5000
}
```

Why not let it write SQL, given that models are good at SQL now? Because a wrong join is indistinguishable from a
right one on the screen, because tenant scoping becomes a prompt instruction rather than a boundary, and because a
spec can be *validated, cached, diffed, replayed and explained* while a SQL string cannot. ADR-006 in this repo
already rejected "generated SQL constrained by prompt instructions" in exactly these terms: *instructions are not a
security boundary, and the failure is silent cross-tenant disclosure.*

The agent loop in practice:

1. **Resolve context** — who is asking, which tenant, which semantic models they may see, what the conversation
   already established.
2. **Retrieve** the relevant slice of the semantic layer (not all of it) — metric and dimension descriptions,
   synonyms, a handful of curated example question→spec pairs. This retrieval quality is a top-three accuracy lever.
3. **Plan** — emit the query spec under a strict JSON schema (structured output / constrained decoding).
4. **Validate** — every name exists, grains are compatible, the joins are declared, the filters type-check, the
   policy allows it. Reject with a typed error, do not "fix" silently.
5. **Compile and execute** — SQL generated by code, parameterised, tenant-bound, cost-capped, timed out.
6. **Explain** — return the metric definitions used, the filters applied and the row count, rendered from the spec,
   not narrated by the model. The user must be able to audit the number.
7. **Present** — hand the result set to L5.

Ambiguity is a first-class outcome: "sales" may map to `gross_revenue` or `net_revenue`. Asking one clarifying
question beats a confident wrong number, and the answer becomes a synonym you record in the semantic layer.

### L5 — Generative UI: how the screens appear

The 2026 industry taxonomy has three patterns, distinguished by how much freedom the model gets:

| Pattern | The model returns | Verdict |
|---|---|---|
| **Static** | Only *which* pre-built component to show, and its data | Safest, least flexible; right for the first release |
| **Declarative** | A **UI spec** (cards, tables, charts, filters) that the frontend renders with its own components and styling | **The production default.** Google's A2UI and AG-UI standardise exactly this: an agent may only request components from a pre-approved catalog |
| **Open-ended** | A full UI surface / generated code, embedded | Flexible, unsafe, inconsistent; not for a multi-tenant product |

So the "AI made me a screen" experience is: **model → view spec → your renderer**. A page is a composition of
allow-listed blocks (`kpi_row`, `time_series`, `breakdown_bar`, `table`, `chip_status`, `filter_bar`, `empty_state`),
each bound to a query spec from L4.

```jsonc
{
  "title": "Ventas por categoría — últimos 12 meses",
  "blocks": [
    { "type": "kpi_row", "items": [{ "metric": "net_revenue", "compare": "yoy" },
                                   { "metric": "gross_margin_pct", "compare": "yoy" }] },
    { "type": "time_series", "query": "q1", "x": "date.month", "series": "product.category" },
    { "type": "table", "query": "q1", "columns": ["product.category", "net_revenue", "gross_margin_pct"] }
  ]
}
```

This is what Power BI Copilot does when it "suggests a report outline with pages and creates them", and what Qlik's
Insight Advisor does when it "generates visualisations from natural language" — a chart-type choice driven by the
shape of the result (cardinality, time grain, measure count) plus an LLM's naming and grouping, rendered by the
product's own visual system. Microsoft's own guidance is the tell: *a well-structured, clearly named model helps
Copilot produce more accurate suggestions.* Again — the model quality is downstream of the semantic model.

A generated screen must be **savable**: the moment a user pins it, it stops being generative output and becomes a
versioned artifact with a stable id, owner, permissions and a definition that no longer depends on an LLM to
re-render. That is also how the product accumulates the customer's own metric library.

### L6 — Governance, evals and cost

- **Evals are not optional.** A golden set of question → expected spec (and expected number) per domain, run in CI
  against every prompt, model, semantic-model and adapter change. Without it you cannot tell an upgrade from a
  regression, and this product's regressions are silent wrong numbers.
- **Audit** every question, spec, compiled SQL, row count, latency and cost, per tenant and per user.
- **Cost and latency caps** at compile time (row limits, scanned-bytes limits, timeouts), plus aggressive caching:
  identical spec + identical policy + unchanged data version = cached result.
- **Data freshness must be visible on the screen.** "As of 03:15" prevents the support ticket where a CFO compares
  your dashboard to the ERP at 11:00 and concludes the product lies.

---

## 4. Where this maps onto this repository

The base already carries most of the hard, non-obvious decisions.

| Product need | Already in this repo |
|---|---|
| Model must not author queries or identifiers | ADR-006 §1 — closed capability catalog, the model selects and supplies parameters |
| Declarative reports without a deployment; effects require code review | ADR-006 §2 — `Query` capabilities may be configuration, `Action` must be code, enforced by a DB constraint |
| No parallel business layer for the AI channel | ADR-006 §3 — capabilities project onto existing Application requests and inherit validation and permissions |
| Per-ERP credentials, per environment, encrypted, with health and expiry | ADR-006 §9 |
| An ERP whose credentials died must disappear from what the model can choose | ADR-006 §10 — availability is computed, not assumed |
| Nothing irreversible on a model's say-so | ADR-006 §4–6 — two-phase runs, deterministic summaries, confirmation policy per organization |
| Ambiguous external outcomes | ADR-006 §7–8 — mandatory reconciliation, never retry an unknown outcome |
| Tenant isolation, permissions, sessions, audit | ADR-004 — deny-by-default authorization, tenant resolved server-side from the persisted session |
| Reliable background effects | The transactional outbox and `OutboxWorker` — the natural home for ingestion runs |
| One failure contract end to end | The error-handling skill: `Result`/`ApplicationError`, RFC 9457, `problemCodes.json`, the strict client reader |
| Screens that look like one product | The frontend standard: standard MUI, theme-only visual policy, page header + one primary action, mandatory empty and loading states |
| Multi-language from day one | The localization standard — critical here, because *metric synonyms are localization data* |

**The shape of the addition, in this architecture:**

- **Domain** — `DataSource`, `ErpAdapter` (+ version), `IngestionRun`, `CanonicalEntity`, `SemanticModel`,
  `Metric`, `Dimension`, `SavedView`, `QuestionRun`. Note that `Metric` is a domain concept with an invariant code
  and localized display names, exactly like the invariant-code pattern the localization standard already mandates.
- **Application** — `AskQuestion` (plan → validate → compile → execute → present), `PinView`, `RunIngestion`,
  `RegisterDataSource`, `TestConnection`. Read paths are queries; anything touching an ERP with a write is an
  ADR-006 `Action` with a confirmation phase.
- **Infrastructure** — one project per connector family; the on-prem agent as a separate deployable; the analytical
  store starting as PostgreSQL (it will carry a mid-market ERP's fact tables comfortably for a long time) with the
  warehouse as a later swap behind the same compiler interface.
- **Web** — `POST /api/ai/ask` returning `{ answer, querySpec, viewSpec, data, explanation, freshness }`;
  `/api/semantic/*` for the model catalog; `/api/sources/*` for connection lifecycle. New problem codes are born
  complete — factory, endpoint contract, `problemCodes.json`, client message, test — e.g.
  `semantic_metric_unknown`, `query_spec_invalid`, `query_cost_exceeded`, `erp_connection_failed`,
  `ingestion_stale`, `question_ambiguous`.
- **ClientApp** — a `ViewSpecRenderer` whose catalog is standard MUI composition (page header, `Chip` for status,
  tables for collections, loading states that hold the layout, empty states everywhere). The renderer is the *only*
  place that turns a spec into markup, so a generated screen cannot violate the design standard.

---

## 5. What goes wrong (and what it costs)

| Risk | Why it bites | Mitigation |
|---|---|---|
| **"Any ERP"** | Each adapter is weeks of domain archaeology, and per-customer customisation multiplies it | Ship 1–2 adapters properly; sell the third as a paid onboarding; base mapping + customer overlay as data |
| **Schema drift** | An ERP upgrade renames a column; numbers change silently | Fixture-based adapter tests in CI; a daily contract check against each live source; alert, don't guess |
| **Metric drift** | Finance's "revenue" ≠ Sales' "revenue"; both are on the same screen | One definition per metric in the semantic layer, with the words visible in the UI next to the number |
| **Silent wrong answers** | The model picks a plausible-but-wrong metric or grain | Golden evals in CI; show the definitions and filters used; make ambiguity a clarifying question, not a guess |
| **Cross-tenant leakage** | The classic multi-tenant analytics failure | Tenant and RLS injected by the compiler, never by the model; ADR-004 authorization; a test suite that tries to break out |
| **Latency and cost** | A wide question over a fact table, per user, per keystroke | Row/byte caps, timeouts, pre-aggregations, spec-level caching, small model for planning + escalate only when needed |
| **Trust collapse** | One wrong number in front of a CFO ends the pilot | Freshness banner, drill-through to the ERP document, an "explain this number" affordance that is deterministic |
| **The pilot that never ends** | Every customer wants their own metric before signing | Make metric authoring a self-serve, reviewable artifact — not a consulting engagement |

---

## 6. Buy vs build, per layer

| Layer | Buy | Build | Recommendation for a first version |
|---|---|---|---|
| L0 connectivity | Fivetran, Airbyte, Debezium, Qlik Replicate | Own extractor + on-prem agent | **Debezium/own agent.** Per-connector SaaS pricing destroys mid-market unit economics, and your target ERPs are the ones they cover worst |
| L1/L2 storage + canonical | — | Own, in Postgres | **Build.** This is the product |
| L3 semantic layer | Cube, dbt Semantic Layer, AtScale | Own YAML/DB-backed model + compiler | **Start with your own, narrow** (metrics + dimensions + declared joins + policies). Adopt Cube only if you need its caching and its MCP surface before you can build them |
| L4 agent | Cortex Analyst, Genie, Spotter (all warehouse-bound) | Own planner over your own semantic layer | **Build.** The differentiator is that your semantic layer is pre-populated per ERP — that is precisely what the platform vendors cannot ship |
| L5 rendering | Embedded Power BI / Qlik / Metabase | Own MUI renderer | **Build.** Embedding a third-party BI product forfeits the design system, the localization contract and the price point |
| L6 evals | Braintrust, LangSmith, promptfoo | Own harness in CI | **Buy or thin-build**, but *have one from week two* |

---

## 7. A build order that survives contact with a customer

**Phase 0 — Choose the wedge (1 week, mostly non-technical).** One ERP, one industry, one department. In the
Argentine mid-market the two rational first picks are **Tango** (largest installed base, database-level access, hard
for cloud BI vendors to reach) or **Odoo** (clean API, growing, technically cheapest). Pick by which customer you
can actually get, not by which is elegant.

**Phase 1 — One vertical slice, end to end (4–6 weeks).** One data source, CDC or nightly snapshot into landing;
canonical `SalesOrder`/`ArInvoice`/`Customer`/`Product`; ~20 metrics with real definitions; three canned screens
built by hand from the same view-spec renderer the AI will later target. **No AI yet.** If those three screens are
not correct and fast, no model will save the product.

**Phase 2 — Ask (3–4 weeks).** The L4 planner over the L3 model: question → validated spec → compiled SQL →
existing renderer. Ship the explanation panel in the same release as the answer. Start the golden eval set with the
first 50 real questions from the pilot customer.

**Phase 3 — Screens (3–4 weeks).** Multi-block view specs, "suggest a dashboard for this role", pin-and-version.
Now the demo matches the pitch.

**Phase 4 — The second adapter (and the moat).** The second ERP is where you find out whether the canonical model
was designed or improvised. Budget for refactoring it, and treat adapter authoring tooling as a product surface.

**Phase 5 — Write-back (`Data Action`), planning/budget, and alerting.** These are what turn an analytics tool into
an operational one — and they are exactly where ADR-006's two-phase confirmation, deterministic summaries and
mandatory reconciliation stop being ceremony and start being the reason nothing catastrophic happens.

---

## 8. Open questions for the product owner

1. **Who is the buyer** — the end customer (SaaS, self-serve) or the ERP vendor / integrator (embedded, like
   Visionaris' *soluciones embebidas*)? This changes the product more than any technical choice: embedded means
   multi-brand theming, per-ISV metric libraries and a partner console.
2. **Read-only, or read and write?** Read-only sells slowly and safely. Write-back (issue the invoice, release the
   order) is where ADR-006's whole apparatus earns its keep — and where a bug is permanent.
3. **Where does the data live** — your cloud (fast, cheap to operate, a hard sell to some CFOs) or the customer's
   (slow, expensive, sometimes the only way to close)?
4. **Which ERP first**, and do you have a named design-partner customer for it today?
5. **How much per-customer metric authoring is self-serve** versus your team's job? The answer determines whether
   this is a product or a consultancy.
6. **What is the promise on accuracy?** "Assistive, always shows its definitions" is defensible today. "Trust the
   number blindly" is not — and the difference must be visible in the UI, not buried in a contract.

---

## Sources

- [Visionaris — home](https://www.visionaris.net/), [Soluciones Pre-Armadas](https://www.visionaris.net/soluciones-pre-armadas/),
  [Soluciones Embebidas](https://www.visionaris.net/soluciones-embebidas/), [Data Collect](https://www.visionaris.net/data-collect/),
  [Servicios](https://www.visionaris.net/servicios/),
  [Por qué implementar VISIONARIS hoy, sin esperar al nuevo ERP](https://www.visionaris.net/por-que-implementar-visionaris-hoy-sin-esperar-al-nuevo-erp/),
  [IA, Machine Learning y BI](https://www.visionaris.net/inteligencia-artificial-machine-learning-y-bi-transformando-datos-en-oportunidades/)
  *(reached through search-result summaries; the domain is blocked from this environment)*
- [Visionaris — software de Business Intelligence (softwaredoit.es)](https://www.softwaredoit.es/visionaris/visionaris.html),
  [Visionaris on LinkedIn](https://www.linkedin.com/company/visionarisinternational)
- [Semantic Layers for Reliable LLM-Powered Data Analytics: A Paired Benchmark (arXiv)](https://arxiv.org/pdf/2604.25149)
- [Atlan — Text-to-SQL for Enterprise: Metric Drift and Context Layer (2026)](https://atlan.com/know/ai-agent/data-for-ai/text-to-sql-for-enterprise/),
  [Atlan — Cortex Analyst vs Custom Text-to-SQL](https://atlan.com/know/snowflake/cortex-analyst-vs-text-to-sql/)
- [Cube — Semantic Layer for AI Agents (2026)](https://cube.dev/articles/semantic-layer-for-ai-agents-2026),
  [AtScale — What is MCP](https://www.atscale.com/glossary/model-context-protocol-mcp/)
- [Promethium — Text-to-SQL tools comparison 2026](https://promethium.ai/guides/text-to-sql-comparison-2026-enterprise-solutions/),
  [Genloop — Agentic data analysis tools 2026](https://genloop.ai/blogs/top-7-tools-for-agentic-data-analysis-in-2026)
- [CopilotKit — The Developer's Guide to Generative UI in 2026](https://www.copilotkit.ai/blog/the-developer-s-guide-to-generative-ui-in-2026),
  [awesome-generative-ui](https://github.com/narrowin/awesome-generative-ui),
  [LangChain — Generative UI docs](https://docs.langchain.com/oss/python/langchain/frontend/generative-ui)
- [Microsoft Learn — Copilot for Power BI overview](https://learn.microsoft.com/en-us/power-bi/create-reports/copilot-introduction),
  [Create and edit reports with Copilot](https://learn.microsoft.com/en-us/power-bi/create-reports/copilot-create-reports),
  [Optimize your semantic model for Copilot](https://learn.microsoft.com/en-us/power-bi/create-reports/copilot-evaluate-data)
- [Fivetran — SAP ERP data replication](https://www.fivetran.com/data-movement/sap-replication),
  [Confluent — What is Change Data Capture](https://www.confluent.io/learn/change-data-capture/)
- [Model Context Protocol — 2026-07-28 specification](https://blog.modelcontextprotocol.io/posts/2026-07-28/),
  [Securing MCP: Risks, Controls, and Governance (arXiv)](https://arxiv.org/html/2511.20920v1)
- [Axoft / TANGO](https://www.axoft.com/tango/software-de-gestion/), [TangoSoftware ApiTiendas](https://github.com/TangoSoftware/ApiTiendas)
- This repository: [ADR-004](../../decisions/ADR-004-Adopt-Multitenant-Identity-Access.md),
  [ADR-006](../../decisions/ADR-006-Bot-Capability-Catalog-And-Modules.md),
  [whatsapp-bot MODULES](../whatsapp-bot/MODULES.md)
