/*
  Read-only schema profiler — PostgreSQL (12+)

  Purpose: produce the "schema profile" input described in GROUNDING.md §3 step 1.
  Run against a RESTORED BACKUP or a READ REPLICA, never a live production ERP.

  Safety properties, on purpose:
    - every statement is a SELECT against the system catalogs;
    - nothing here reads a customer row. Structure, counts and code, only.
      Section 8 is the exception and is opt-in: it generates probes for
      low-cardinality vocabularies (status codes, document types) and must be
      pointed only at columns you have confirmed carry no personal or
      commercial data.

  Usage: run section by section, exporting each result to CSV, e.g.

    psql -d <db> -f postgresql.sql --csv -o profile.csv

  Repeat across ~20 customer databases per system and diff the outputs: the
  differences are the per-customer drift you will have to model.
*/

-- ------------------------------------------------------------------
-- 1. Tables and row estimates — what is real, ranked
--    reltuples is the planner's estimate; run ANALYZE first for accuracy.
-- ------------------------------------------------------------------
SELECT
    n.nspname                                   AS schema_name,
    c.relname                                   AS table_name,
    c.reltuples::bigint                         AS row_estimate,
    pg_total_relation_size(c.oid) / 1024 / 1024 AS total_mb,
    obj_description(c.oid, 'pg_class')          AS table_comment
FROM pg_class c
JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE c.relkind = 'r'
  AND n.nspname NOT IN ('pg_catalog', 'information_schema')
ORDER BY c.reltuples DESC;

-- ------------------------------------------------------------------
-- 2. Columns — types, nullability, defaults, generated columns
-- ------------------------------------------------------------------
SELECT
    c.table_schema      AS schema_name,
    c.table_name,
    c.ordinal_position,
    c.column_name,
    c.data_type,
    c.character_maximum_length,
    c.numeric_precision,
    c.numeric_scale,
    c.is_nullable,
    c.is_generated,
    c.column_default,
    col_description(
        to_regclass(quote_ident(c.table_schema) || '.' || quote_ident(c.table_name)),
        c.ordinal_position::int
    ) AS column_comment
FROM information_schema.columns c
WHERE c.table_schema NOT IN ('pg_catalog', 'information_schema')
ORDER BY c.table_schema, c.table_name, c.ordinal_position;

-- ------------------------------------------------------------------
-- 3. Primary keys and unique constraints — the natural keys
-- ------------------------------------------------------------------
SELECT
    n.nspname AS schema_name,
    t.relname AS table_name,
    con.conname AS constraint_name,
    CASE con.contype WHEN 'p' THEN 'primary key' WHEN 'u' THEN 'unique' END AS constraint_type,
    pg_get_constraintdef(con.oid) AS definition
FROM pg_constraint con
JOIN pg_class t     ON t.oid = con.conrelid
JOIN pg_namespace n ON n.oid = t.relnamespace
WHERE con.contype IN ('p', 'u')
  AND n.nspname NOT IN ('pg_catalog', 'information_schema')
ORDER BY n.nspname, t.relname, con.contype;

-- ------------------------------------------------------------------
-- 4. Foreign keys — the declared joins
--    Undeclared joins are common in older ERPs; treat this as a
--    starting point and recover the rest from section 6.
-- ------------------------------------------------------------------
SELECT
    n.nspname   AS schema_name,
    t.relname   AS table_name,
    con.conname AS fk_name,
    pg_get_constraintdef(con.oid) AS definition,
    rn.nspname  AS referenced_schema,
    rt.relname  AS referenced_table
FROM pg_constraint con
JOIN pg_class t      ON t.oid  = con.conrelid
JOIN pg_namespace n  ON n.oid  = t.relnamespace
JOIN pg_class rt     ON rt.oid = con.confrelid
JOIN pg_namespace rn ON rn.oid = rt.relnamespace
WHERE con.contype = 'f'
  AND n.nspname NOT IN ('pg_catalog', 'information_schema')
ORDER BY n.nspname, t.relname, con.conname;

-- ------------------------------------------------------------------
-- 5. Read/write activity per table — what the application uses
--    Counters accumulate since the last pg_stat_reset(); check stats_reset.
-- ------------------------------------------------------------------
SELECT stats_reset AS counters_since FROM pg_stat_database WHERE datname = current_database();

SELECT
    schemaname AS schema_name,
    relname    AS table_name,
    seq_scan,
    idx_scan,
    COALESCE(seq_scan, 0) + COALESCE(idx_scan, 0) AS reads,
    n_tup_ins + n_tup_upd + n_tup_del             AS writes,
    n_live_tup,
    n_dead_tup,
    last_autoanalyze
FROM pg_stat_user_tables
ORDER BY reads DESC;

-- ------------------------------------------------------------------
-- 6. THE GOLDMINE: every view and function definition
--    This is the ERP's own business logic in text form — the report
--    formulas, the document-type filters, the joins nobody declared.
--    Feed it to the model alongside sections 1-4.
-- ------------------------------------------------------------------
SELECT
    schemaname AS schema_name,
    viewname   AS object_name,
    'view'     AS object_type,
    definition
FROM pg_views
WHERE schemaname NOT IN ('pg_catalog', 'information_schema')

UNION ALL

SELECT
    schemaname AS schema_name,
    matviewname AS object_name,
    'materialized view' AS object_type,
    definition
FROM pg_matviews
WHERE schemaname NOT IN ('pg_catalog', 'information_schema')

UNION ALL

SELECT
    n.nspname AS schema_name,
    p.proname AS object_name,
    CASE p.prokind WHEN 'f' THEN 'function' WHEN 'p' THEN 'procedure' ELSE 'routine' END AS object_type,
    pg_get_functiondef(p.oid) AS definition
FROM pg_proc p
JOIN pg_namespace n ON n.oid = p.pronamespace
WHERE n.nspname NOT IN ('pg_catalog', 'information_schema')
  AND p.prokind IN ('f', 'p')
ORDER BY object_type, schema_name, object_name;

-- ------------------------------------------------------------------
-- 7. Ad-hoc queries people actually run (pg_stat_statements, if installed)
--    Whoever hand-writes SQL against the ERP is telling you exactly
--    which question the UI does not answer.
--    Enable with: CREATE EXTENSION pg_stat_statements;
--    (plus shared_preload_libraries = 'pg_stat_statements')
--    PostgreSQL 12 names the column total_time, not total_exec_time.
-- ------------------------------------------------------------------
SELECT
    calls,
    ROUND(total_exec_time::numeric / NULLIF(calls, 0), 2) AS avg_ms,
    rows,
    query
FROM pg_stat_statements
WHERE dbid = (SELECT oid FROM pg_database WHERE datname = current_database())
ORDER BY calls DESC
LIMIT 300;

-- ------------------------------------------------------------------
-- 8. OPT-IN second pass: null rates, distinct counts, and the
--    low-cardinality vocabularies the mapping needs.
--
--    pg_stats already holds most of this WITHOUT touching a single row,
--    because ANALYZE has sampled it for the planner. Prefer it.
--    most_common_vals on a status or document-type column is exactly the
--    vocabulary the canonical mapping needs.
--
--    Review the output before using it: exclude any column carrying
--    personal or commercial data (names, documents, addresses, amounts).
-- ------------------------------------------------------------------
SELECT
    st.schemaname AS schema_name,
    st.tablename  AS table_name,
    st.attname    AS column_name,
    st.null_frac,
    st.n_distinct,
    st.most_common_vals,
    st.most_common_freqs
FROM pg_stats st
WHERE st.schemaname NOT IN ('pg_catalog', 'information_schema')
  AND st.n_distinct BETWEEN 0 AND 50     -- low-cardinality columns only
  AND NOT EXISTS (                        -- and never a key column: those are
      SELECT 1                            -- identifiers, not vocabularies
      FROM pg_constraint con
      JOIN pg_class rel     ON rel.oid = con.conrelid
      JOIN pg_namespace nsp ON nsp.oid = rel.relnamespace
      JOIN pg_attribute att ON att.attrelid = rel.oid AND att.attnum = ANY (con.conkey)
      WHERE con.contype IN ('p', 'f')
        AND nsp.nspname = st.schemaname
        AND rel.relname = st.tablename
        AND att.attname = st.attname
  )
ORDER BY st.schemaname, st.tablename, st.attname;
