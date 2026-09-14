# Grounding the AI on an existing ERP database

**Status:** Research only. No decision, no SPEC, no scope.
**Date:** 2026-09-14
**Context:** Two production ERP systems (Macrosistemas, Nexobyte) whose databases are reachable, each carrying
hundreds of customers on what is assumed to be a shared schema per system.
**Question:** how do you "train" an AI so it returns the reports and answers those systems do not return today?

---

## 0. The reframe: you do not train it, you ground it

"Training" points at fine-tuning, and fine-tuning is the wrong lever here — probably permanently. It is expensive, it
goes stale the day the schema changes, it cannot be audited, and it does not fix the actual failure mode. The actual
failure mode is not that the model writes bad SQL; it is that **the model does not know what your tables mean**.

What you build instead is five artifacts. Together they *are* the training data, and every one of them is a
reviewable, versioned, testable file in your repository:

| # | Artifact | What it is | Who authors it |
|---|---|---|---|
| 1 | **Schema profile** | Every table, column, key, row count, null rate, usage frequency, plus the text of every view, stored procedure and report query | Deterministic code. No LLM. |
| 2 | **Canonical mapping** | This ERP's tables → business entities (`SalesOrder`, `ArInvoice`, `StockMove`, `GlEntry`) | LLM drafts, human ratifies |
| 3 | **Metric library** | Each metric's invariant code, formula, grain, filters, currency, words in each language, synonyms | LLM drafts from existing reports, human ratifies |
| 4 | **Example library** | Real question → validated query spec, ~100–300 pairs, retrieved at runtime | Mined from real usage |
| 5 | **Eval set** | Question → expected spec **and expected number**, run in CI | Ground truth taken from the ERP's own reports |

Everything else — model choice, prompt wording, framework — is downstream of these five. The published 2026 numbers
say the same thing in the other direction: the same model goes from 53% to 100% on Databricks' measurements purely by
adding metadata and metric definitions, and from 90.0% to 98.2% on dbt's when a semantic layer grounds it. Nobody got
there by fine-tuning.

---

## 1. Your actual advantage: the same schema, hundreds of times

This is the part that most people building an AI-analytics product do not have, and it is worth being explicit about
what it buys:

1. **The adapter is amortised across hundreds of customers.** The single most expensive artifact — the canonical
   mapping — is written once per system and pays off N times. A generic "connect any ERP" vendor cannot do this;
   they write it once per *customer*.
2. **You can see which parts of the schema are real.** Out of a few thousand tables, a small number are used by
   almost every customer, and a long tail is used by nobody. Profile all of them, then rank by how many customer
   databases actually populate each table. That ranking is your roadmap, and it is free.
3. **You already own the ground truth.** The systems' existing reports produce numbers customers accept as correct.
   That makes them the reference answer set for your evals — you can prove your AI agrees with the ERP before you
   ever ask it something the ERP cannot answer.
4. **Cross-customer benchmarking is a product only you can ship.** "Your DSO is 47 days; the median in your sector on
   this platform is 31" is impossible for any single customer to compute and impossible for a generic BI tool to
   offer. It requires aggregation across the tenant set, which requires consent (see §7) — but it is the feature
   that is genuinely defensible.
5. **A fix propagates instantly.** Correct a mapping or a metric and every customer gets it in the next deploy.

---

## 2. Finding what the ERPs *don't* return

This is the commercially important question, and it is answerable from data you already hold. Do not guess the gaps —
mine them. Six sources, roughly in order of signal quality:

**a. The export logs.** Every row a user exports to Excel is a report the system failed to finish. What they export,
how often, and — if you can get it — what they do to it afterwards, is the highest-signal gap list in the business.
If the systems do not log exports today, **instrumenting that is the single cheapest research investment available**
and it starts paying in a week.

**b. The per-customer custom reports.** Every report your support or dev team ever wrote for one customer is a gap
that customer paid to close. Collect the SQL. It is simultaneously a gap list, a metric library draft, and an
example library draft — the formulas are already written.

**c. Support tickets and WhatsApp/email requests.** Filter for "necesito un informe de…", "se puede sacar…", "cómo
veo…". Classify them with an LLM into a ranked demand list. This is a batch job over text you already store.

**d. The database's own query log.** SQL Server Query Store, or `pg_stat_statements` on PostgreSQL, records the ad-hoc
queries people actually run against production. Whoever is running hand-written SQL against the ERP is telling you
precisely which question the UI does not answer.

**e. The shadow spreadsheets.** Ask five friendly customers for the Excel file they maintain alongside the system.
Every tab in it is a missing screen. This takes an afternoon and outperforms a month of speculation.

**f. The complement of the report catalog.** List every report the ERP ships. The interesting gaps cluster in
predictable places: anything **cross-module** (margin by salesperson needs sales + costs + payroll), anything
**time-comparative** (this month vs. same month last year vs. rolling twelve), anything **cohort- or
retention-shaped**, anything **predictive** (which customer is about to stop buying, which stock item will break),
and anything **cross-customer**. Transactional ERPs are built to record, not to compare — the gap is structural, not
accidental.

Turn the output into one ranked list: *question · how many customers asked · how hard to compute · which canonical
entities it needs*. That list drives which metrics you author first, and it is the same list that becomes your eval
set. Build for the top 20 with hand-written screens; the AI exists for the long tail below them.

---

## 3. The pipeline, step by step

### Step 1 — Profile both databases (code, no LLM)

Run the read-only profilers in [`profiler/`](profiler/) against a **restored backup or a read replica, never
production**. They extract structure and behaviour, and — deliberately — **no customer rows**:

- tables, columns, types, nullability, primary and foreign keys, indexes;
- row counts per table, so you can see what is real;
- **the text of every view, stored procedure and function** — this is the richest single input you will get, because
  the ERP's own report logic already encodes the business rules you would otherwise have to reverse-engineer;
- read/write frequency per table, so you can rank by what is actually used.

Run it across a sample of ~20 customer databases per system, not one. The differences between them tell you which
columns are truly optional and which "customisations" exist in the wild.

A second pass adds **null rates and distinct-value counts per column**, and **masked** sample values for low-cardinality
columns only (status codes, document types, units) — those are the vocabulary the mapping needs. Never sample a name,
a document number, an address or an amount.

### Step 2 — Draft the canonical mapping (LLM proposes, human ratifies)

Feed the profile plus the view/procedure text to the model and ask for candidates, not conclusions:

> Given this schema profile and these view definitions, propose which tables constitute the sales-invoice subject
> area. For each, state the grain, the natural key, the columns that carry amounts and whether they appear to be
> gross or net, the document-type discriminator, how cancellations and credit notes are represented, and the
> evidence in the view definitions supporting each claim. Mark anything you are inferring rather than reading.

Then a human who knows the ERP ratifies it. This is the step that cannot be skipped and cannot be automated: the
model can read four thousand tables in an afternoon and will be roughly right, but "roughly right" about whether a
column is net of tax is a wrong number on a CFO's screen six months later.

Practical notes: this is a bulk, latency-insensitive job — the Batch API runs it at 50% of standard cost. Use
`claude-opus-5` for the mapping passes; the reasoning quality matters more than the token price on a job you run
once per subject area.

Output is a versioned mapping file per system, with a `confidence` and an `evidence` field per mapping, and a review
status. Anything unratified is invisible to the runtime.

### Step 3 — Build the metric library from the reports that already exist

Do **not** invent metrics. Extract them. Every existing report, stored procedure and custom SQL query is a metric
definition someone already agreed to. For each one, record:

```yaml
- code: net_revenue                    # invariant, never translated
  grain: ar_invoice_line
  formula: sum(line.net_amount)
  filters: [document_type in (FAC_A, FAC_B, FAC_C), status != cancelled]
  credit_notes: subtract               # the rule that makes it match the ERP
  currency: source_currency -> ars @ invoice_date
  display:
    es: "Ventas netas"
    en: "Net revenue"
  synonyms_es: [ventas, facturación, vendido, ingresos por ventas]
  definition_es: "Suma del neto gravado de facturas emitidas, menos notas de crédito, sin IVA."
  source_report: rpt_ventas_por_periodo   # provenance: which existing report this came from
  verified_against: rpt_ventas_por_periodo # and which one the eval compares to
```

Two fields carry disproportionate weight. **`synonyms_es`** must contain the words your customers actually use, not
the words the schema uses — *remito, cuenta corriente, IVA, percepción, retención, CAE, nota de crédito, saldo,
mora, cheque en cartera*. And **`verified_against`** is what makes the metric provable: if your `net_revenue` does
not equal the ERP's own sales report to the peso, the metric is wrong and the test says so.

Localization matters more here than anywhere else in the product: the metric **code** is invariant and never
translated; the display name, the definition and the synonyms are catalog entries, complete in every supported
language before release. That is the same invariant-code discipline the repository's localization standard already
requires everywhere else.

### Step 4 — Retrieval, not a giant prompt

You will end up with hundreds of metrics and dimensions. Do not put them all in the prompt.

- Embed each metric's display name, definition and synonyms.
- At question time, retrieve the top ~15 candidate metrics and ~15 dimensions plus 3–5 example question→spec pairs.
- Put *those* in the prompt, after the stable prefix.

Retrieval quality is one of the top three accuracy levers in the whole system, and it is ordinary engineering: better
synonyms, better descriptions, hybrid keyword+vector search, and a hard rule that anything not retrieved cannot be
used. When the retriever misses, the answer is to fix the synonyms — which is precisely why the feedback loop in §5
matters.

### Step 5 — The feedback loop is the real "training"

This is where hundreds of customers stops being a data-privacy problem and starts being a compounding advantage. Log,
per question: the text, the retrieved candidates, the emitted spec, the validation result, the compiled SQL, the row
count, the latency, the cost, whether the user re-asked, whether they pinned the result, and any explicit thumbs
down.

Then, weekly, triage every failure into exactly one of five buckets — and note that each bucket has a *different*
fix, which is the whole point of separating them:

| Failure | Fix | Where it lands |
|---|---|---|
| The user's word wasn't recognised | Add a synonym | Metric library |
| The metric doesn't exist | Author it | Metric library |
| The metric exists but the number is wrong | Fix the formula or the mapping | Metric library / canonical mapping |
| The right metric existed but wasn't retrieved | Improve the description or the retriever | Retrieval |
| The planner picked wrong with everything available | Add an example pair; if it repeats, fix the prompt | Example library |

Every triaged failure becomes a new eval case. That loop, run weekly, is what "training" means in this architecture,
and it is the reason the product gets better with customers instead of getting harder to maintain.

### Step 6 — Evals before you have users, not after

Start with 50 questions whose answers the ERP's own reports already produce. Assert the **number**, not just the
shape of the query. Grow to 200. Run in CI on every change to a prompt, a mapping, a metric or a model. Split into
train / validation / test so you are not tuning on your own scoreboard.

Without this you cannot tell an improvement from a regression, and in this product a regression is a wrong number
delivered confidently — the failure mode that ends pilots.

---

## 4. What the runtime actually looks like

```
question
  → retrieve candidate metrics/dimensions/examples   (embeddings over metadata)
  → planner emits a QUERY SPEC                       (structured output, schema-constrained)
  → validate spec against the semantic layer         (deterministic; reject, never repair silently)
  → compile to SQL, inject tenant + row-level policy (deterministic)
  → execute with row/time/cost caps
  → planner emits a VIEW SPEC from a closed catalog  (structured output)
  → render with the existing MUI components
  → show the metric definitions and filters used     (rendered from the spec, not narrated)
```

Notes that matter for cost and reliability, given a large and stable semantic-layer prompt:

- **Structured outputs, always.** Constrain the planner's output with `output_config.format` (or `strict: true` on a
  tool) so the spec is schema-valid by construction. A free-text spec that you then parse is a bug generator.
- **Prompt caching is worth real money here**, because your prompt is mostly stable. The cache is a *prefix* match
  and the render order is `tools` → `system` → `messages`: put the frozen instructions, the spec schema and the
  component catalog first, and the retrieved candidates and the user's question *after* the last cache breakpoint.
  Anything volatile in the prefix — a timestamp, an unsorted JSON blob, a per-request id — silently invalidates
  everything after it. Verify by watching `usage.cache_read_input_tokens`; if it stays zero across repeated
  requests, something in your prefix is moving.
- **Model choice.** Default to `claude-opus-5` ($5 / $25 per MTok, 1M context) for planning — this is the step where
  a wrong choice becomes a wrong number. `claude-sonnet-5` ($2 / $10) and `claude-haiku-4-5` ($1 / $5) exist for
  routing, classification and bulk work; moving the planner to one of them is a decision to make **after** the eval
  set can measure what it costs you in accuracy, not before. Bulk offline jobs (schema mining, ticket
  classification) go through the Batch API at 50%.
- The project is .NET, so this is the official Anthropic C# SDK on the server side. The model is never called from
  the browser, and the SPA never sees an API key.

---

## 5. Where fine-tuning would actually earn its place

Essentially nowhere, at least for the first two years. The honest test: fine-tuning is worth considering only once
you have thousands of *validated* question→spec pairs, your eval score has plateaued with retrieval and examples
already tuned, and latency or per-call cost is the binding constraint rather than accuracy. Even then, the artifact
you would fine-tune on is the example library from §3 — which means **building the example library is the
prerequisite either way**. Build it. Decide about fine-tuning in two years, from data.

---

## 6. What breaks, specifically in this setup

| Risk | Why it bites here | Mitigation |
|---|---|---|
| **Per-customer schema drift** | "Same schema, hundreds of customers" is never fully true — custom fields, disabled modules, versions behind | Profile ~20 databases, not one; base mapping + per-customer overlay as data; a nightly contract check per customer that alerts on drift |
| **Version skew across the fleet** | Customer A is three releases behind customer B | Mapping is versioned against ERP version ranges; the runtime resolves which mapping applies |
| **The number disagrees with the ERP** | The customer trusts the ERP, not you — correctly | Every metric declares `verified_against`; the eval asserts equality; the UI shows the definition and drills through to the source document |
| **Reading production** | Analytical queries on a live ERP will get you blamed for every slowdown | Replica or restored backup, always; ingest incrementally; never let a generated query touch the OLTP database |
| **Cross-tenant leakage** | Hundreds of tenants, one platform: the classic failure | Tenant and row-level policy injected by the compiler, never by the model; a test suite that actively tries to break out |
| **PII reaching the model** | Metadata is safe; rows are not | See §7 — the architecture keeps rows out of the prompt by default, which is also the cheapest design |
| **Building for the average customer** | The mean of hundreds of customers is a customer that does not exist | Pick 3 design partners, ship for them, generalise after |

---

## 7. The part that is not technical

You have access to two databases holding hundreds of *other companies'* commercial data. Three things follow, and
they are design constraints rather than paperwork:

1. **Check the legal basis before the data leaves its current use.** Operating the ERP is one purpose; building and
   improving an analytics product on that data is another, and cross-customer benchmarking is a third. Argentina's
   Ley 25.326 plus whatever your service agreements say govern this. Get it right up front — it is far cheaper than
   unwinding a product built on data you were not permitted to use that way.
2. **Design so that customer rows never reach the model.** Everything in §1–§3 runs on *metadata*: schemas, view
   definitions, aggregate counts, masked low-cardinality vocabularies. The model needs to know that a column is a
   document type with values `FAC_A`/`FAC_B`/`NC_A` — it never needs to see a customer's name or an invoice amount.
   At runtime, results go from the database to the renderer; the model sees the spec, not the rows. Where the model
   must summarise a result set, mask identifiers first and cap what is passed. This is a genuinely cheaper
   architecture as well as a defensible one.
3. **Make benchmarking opt-in, aggregated and k-anonymous.** Never a peer group small enough to identify a
   competitor; a documented minimum cohort size; and a switch the customer controls. Done this way it is your best
   feature. Done casually it is the reason a customer leaves and tells the market why.

---

## 8. The first 30 days, concretely

| Days | Do | Output |
|---|---|---|
| 1–3 | Run the profilers over ~20 customer databases per system, read-only, off a replica | Two schema profiles + every view/procedure definition |
| 4–7 | Mine the gaps: export logs, custom reports, support tickets, query logs, five customer spreadsheets | One ranked list of questions the systems do not answer |
| 8–14 | Draft the canonical mapping for **one** subject area (sales + accounts receivable) with the model; ratify it with someone who knows the ERP | `mapping.v1.yaml` per system, ratified |
| 15–21 | Author ~20 metrics extracted from existing reports, each with `verified_against` | Metric library v1 + a passing equality test per metric against the ERP's own report |
| 22–30 | Ship **three hand-built screens** over that metric library — no AI in the path — and put them in front of one real customer | Proof the numbers are right and fast, plus the first 50 eval cases |

Only after that does the planner go in. If the three hand-built screens are wrong or slow, no model fixes it; if
they are right, the planner is a comparatively small addition on top of a foundation that already works.

---

## Related

- [RESEARCH.md](RESEARCH.md) — the architecture of the category, layer by layer.
- [profiler/](profiler/) — read-only schema profilers for SQL Server and PostgreSQL.
- [ADR-006](../../decisions/ADR-006-Bot-Capability-Catalog-And-Modules.md) — the closed-catalog decision this
  runtime inherits: the model selects and parameterises, it never authors a query.
