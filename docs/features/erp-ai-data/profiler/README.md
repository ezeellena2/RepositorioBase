# Schema profilers

Read-only profilers that produce the schema profile described in
[GROUNDING.md §3 step 1](../GROUNDING.md) — the first artifact of the five that ground an AI on an existing ERP.

| File | Target |
|---|---|
| [sqlserver.sql](sqlserver.sql) | Microsoft SQL Server 2016+ |
| [postgresql.sql](postgresql.sql) | PostgreSQL 12+ |

## Rules

1. **Run against a restored backup or a read replica.** Never a live production ERP: section 5 is cheap, but the
   opt-in probes in section 8 are not, and no analytics work should ever be blamed for an ERP slowdown.
2. **Sections 1–7 read no customer data.** They query system catalogs only: structure, counts, activity counters and
   the text of views, procedures and functions. Section 8 is opt-in and is the only part that touches table
   contents — point it exclusively at low-cardinality columns you have confirmed carry no personal or commercial
   data (status codes, document types, units, currencies).
3. **Run it across ~20 customer databases per system, not one.** The diffs between them are the per-customer drift
   the canonical mapping has to absorb.
4. **Section 6 is the most valuable output.** The ERP's own view and procedure text already encodes the business
   rules — which document types count as a sale, how credit notes are represented, which joins exist without a
   declared foreign key. It is the primary input to the mapping draft, ahead of the schema itself.

## Verification status

`postgresql.sql` was executed end to end against PostgreSQL 16 on a seeded schema (tables, a view, a generated
column, a function, foreign keys, low-cardinality status and document-type columns). Sections 1–6 and 8 return the
expected rows; section 7 raises `relation "pg_stat_statements" does not exist` unless that extension is installed,
which is the documented behaviour.

`sqlserver.sql` has **not** been executed — no SQL Server instance was available. Run it section by section the
first time and expect to adjust for the instance's collation and for pre-2016 versions (Query Store in section 7 is
2016+).
