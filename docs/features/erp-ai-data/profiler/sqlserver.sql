/*
  Read-only schema profiler — Microsoft SQL Server (2016+)

  Purpose: produce the "schema profile" input described in GROUNDING.md §3 step 1.
  Run against a RESTORED BACKUP or a READ REPLICA, never a live production ERP.

  Safety properties, on purpose:
    - every statement is a SELECT against system catalog views;
    - nothing here reads a customer row. Structure, counts and code, only.
      Section 8 is the exception and is opt-in: it samples low-cardinality
      vocabularies (status codes, document types) and must be pointed only at
      columns you have confirmed carry no personal or commercial data.

  Usage: run section by section, exporting each result set to CSV/JSON, e.g.

    sqlcmd -S <server> -d <db> -E -i sqlserver.sql -s"," -W -o profile.csv

  Repeat across ~20 customer databases per system and diff the outputs: the
  differences are the per-customer drift you will have to model.
*/

SET NOCOUNT ON;

/* ------------------------------------------------------------------ */
/* 1. Tables and row counts — what is real, ranked                     */
/* ------------------------------------------------------------------ */
SELECT
    s.name                                        AS schema_name,
    t.name                                        AS table_name,
    SUM(CASE WHEN p.index_id IN (0, 1) THEN p.row_count ELSE 0 END) AS row_count,
    CAST(SUM(p.reserved_page_count) * 8.0 / 1024 AS decimal(18,2))  AS reserved_mb,
    t.create_date,
    t.modify_date
FROM sys.tables t
JOIN sys.schemas s              ON s.schema_id = t.schema_id
JOIN sys.dm_db_partition_stats p ON p.object_id = t.object_id
GROUP BY s.name, t.name, t.create_date, t.modify_date
ORDER BY row_count DESC;

/* ------------------------------------------------------------------ */
/* 2. Columns — types, nullability, defaults, computed columns         */
/* ------------------------------------------------------------------ */
SELECT
    s.name          AS schema_name,
    t.name          AS table_name,
    c.column_id,
    c.name          AS column_name,
    ty.name         AS data_type,
    c.max_length,
    c.precision,
    c.scale,
    c.is_nullable,
    c.is_identity,
    c.is_computed,
    cc.definition   AS computed_definition,
    dc.definition   AS default_definition
FROM sys.columns c
JOIN sys.tables t                    ON t.object_id = c.object_id
JOIN sys.schemas s                   ON s.schema_id = t.schema_id
JOIN sys.types ty                    ON ty.user_type_id = c.user_type_id
LEFT JOIN sys.computed_columns cc    ON cc.object_id = c.object_id AND cc.column_id = c.column_id
LEFT JOIN sys.default_constraints dc ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
ORDER BY s.name, t.name, c.column_id;

/* ------------------------------------------------------------------ */
/* 3. Primary keys and unique constraints — the natural keys           */
/* ------------------------------------------------------------------ */
SELECT
    s.name  AS schema_name,
    t.name  AS table_name,
    i.name  AS index_name,
    i.is_primary_key,
    i.is_unique_constraint,
    STUFF((
        SELECT ', ' + c2.name
        FROM sys.index_columns ic2
        JOIN sys.columns c2 ON c2.object_id = ic2.object_id AND c2.column_id = ic2.column_id
        WHERE ic2.object_id = i.object_id
          AND ic2.index_id  = i.index_id
          AND ic2.is_included_column = 0
        ORDER BY ic2.key_ordinal
        FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 2, '') AS key_columns
FROM sys.indexes i
JOIN sys.tables t  ON t.object_id = i.object_id
JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE i.is_primary_key = 1 OR i.is_unique_constraint = 1 OR i.is_unique = 1
ORDER BY s.name, t.name, i.is_primary_key DESC, i.name;

/* ------------------------------------------------------------------ */
/* 4. Foreign keys — the declared joins                                */
/*    Undeclared joins are common in older ERPs; treat this as a       */
/*    starting point and recover the rest from section 6.              */
/* ------------------------------------------------------------------ */
SELECT
    fk.name AS fk_name,
    sp.name AS parent_schema,
    tp.name AS parent_table,
    cp.name AS parent_column,
    sr.name AS referenced_schema,
    tr.name AS referenced_table,
    cr.name AS referenced_column,
    fkc.constraint_column_id AS ordinal
FROM sys.foreign_keys fk
JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
JOIN sys.tables  tp ON tp.object_id  = fkc.parent_object_id
JOIN sys.schemas sp ON sp.schema_id  = tp.schema_id
JOIN sys.columns cp ON cp.object_id  = fkc.parent_object_id     AND cp.column_id = fkc.parent_column_id
JOIN sys.tables  tr ON tr.object_id  = fkc.referenced_object_id
JOIN sys.schemas sr ON sr.schema_id  = tr.schema_id
JOIN sys.columns cr ON cr.object_id  = fkc.referenced_object_id AND cr.column_id = fkc.referenced_column_id
ORDER BY sp.name, tp.name, fk.name, fkc.constraint_column_id;

/* ------------------------------------------------------------------ */
/* 5. Read/write activity per table — what the application uses        */
/*    Counters reset when the instance restarts; note the uptime.      */
/* ------------------------------------------------------------------ */
SELECT sqlserver_start_time AS counters_since FROM sys.dm_os_sys_info;

SELECT
    s.name AS schema_name,
    t.name AS table_name,
    SUM(us.user_seeks + us.user_scans + us.user_lookups) AS reads,
    SUM(us.user_updates)                                 AS writes,
    MAX(us.last_user_seek)                               AS last_seek,
    MAX(us.last_user_scan)                               AS last_scan,
    MAX(us.last_user_update)                             AS last_update
FROM sys.dm_db_index_usage_stats us
JOIN sys.tables t  ON t.object_id = us.object_id
JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE us.database_id = DB_ID()
GROUP BY s.name, t.name
ORDER BY reads DESC;

/* ------------------------------------------------------------------ */
/* 6. THE GOLDMINE: every view, procedure and function definition      */
/*    This is the ERP's own business logic in text form — the report   */
/*    formulas, the document-type filters, the joins nobody declared.  */
/*    Feed it to the model alongside sections 1-4.                     */
/* ------------------------------------------------------------------ */
SELECT
    s.name       AS schema_name,
    o.name       AS object_name,
    o.type_desc  AS object_type,
    o.create_date,
    o.modify_date,
    LEN(m.definition) AS definition_length,
    m.definition
FROM sys.sql_modules m
JOIN sys.objects o ON o.object_id = m.object_id
JOIN sys.schemas s ON s.schema_id = o.schema_id
ORDER BY o.type_desc, s.name, o.name;

/* ------------------------------------------------------------------ */
/* 7. Ad-hoc queries people actually run (Query Store, if enabled)     */
/*    Whoever hand-writes SQL against the ERP is telling you exactly   */
/*    which question the UI does not answer.                           */
/*    Enable with: ALTER DATABASE [db] SET QUERY_STORE = ON;           */
/* ------------------------------------------------------------------ */
IF EXISTS (SELECT 1 FROM sys.database_query_store_options WHERE actual_state = 2)
BEGIN
    -- Aggregate first, join the query text afterwards: SQL Server cannot
    -- apply MIN/MAX to nvarchar(max), so the text must stay out of GROUP BY.
    ;WITH stats AS (
        SELECT
            q.query_text_id,
            SUM(rs.count_executions) AS executions,
            AVG(rs.avg_duration)     AS avg_duration_us
        FROM sys.query_store_query q
        JOIN sys.query_store_plan p           ON p.query_id = q.query_id
        JOIN sys.query_store_runtime_stats rs ON rs.plan_id = p.plan_id
        GROUP BY q.query_text_id
    )
    SELECT TOP (300)
        st.executions,
        CAST(st.avg_duration_us / 1000.0 AS decimal(18,2)) AS avg_duration_ms,
        qt.query_sql_text
    FROM stats st
    JOIN sys.query_store_query_text qt ON qt.query_text_id = st.query_text_id
    ORDER BY st.executions DESC;
END
ELSE
    SELECT 'Query Store is not enabled on this database.' AS note;

/* ------------------------------------------------------------------ */
/* 8. OPT-IN second pass: null rates, distinct counts, and the         */
/*    low-cardinality vocabularies the mapping needs.                  */
/*                                                                     */
/*    This block GENERATES the probe statements rather than running    */
/*    them, so you can review, filter and schedule them. Run the       */
/*    generated SQL only against columns you have confirmed carry no   */
/*    personal or commercial data — status codes, document types,      */
/*    units, currencies. Never against names, documents or amounts.    */
/* ------------------------------------------------------------------ */
SELECT
    'SELECT ''' + s.name + ''' AS schema_name, ''' + t.name + ''' AS table_name, ''' + c.name + ''' AS column_name, '
    + 'COUNT(*) AS rows_total, '
    + 'COUNT(' + QUOTENAME(c.name) + ') AS rows_not_null, '
    + 'COUNT(DISTINCT ' + QUOTENAME(c.name) + ') AS distinct_values '
    + 'FROM ' + QUOTENAME(s.name) + '.' + QUOTENAME(t.name) + ';' AS probe_sql
FROM sys.columns c
JOIN sys.tables t  ON t.object_id = c.object_id
JOIN sys.schemas s ON s.schema_id = t.schema_id
JOIN sys.types ty  ON ty.user_type_id = c.user_type_id
WHERE c.is_computed = 0
  AND c.is_identity = 0
  AND ty.name IN ('char', 'nchar', 'varchar', 'nvarchar', 'tinyint', 'smallint', 'int', 'bit')
  AND (ty.name IN ('tinyint', 'smallint', 'int', 'bit') OR c.max_length <= 40)
  -- never a key column: those are identifiers, not vocabularies
  AND NOT EXISTS (
      SELECT 1
      FROM sys.index_columns ic
      JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
      WHERE ic.object_id = c.object_id AND ic.column_id = c.column_id AND i.is_primary_key = 1
  )
  AND NOT EXISTS (
      SELECT 1
      FROM sys.foreign_key_columns fkc
      WHERE fkc.parent_object_id = c.object_id AND fkc.parent_column_id = c.column_id
  )
ORDER BY s.name, t.name, c.column_id;
