-- ═══════════════════════════════════════════════════════════════════════════
-- Matrix #3 — hot-table bloat: versions inline vs. lean Model B main table
--
-- 10k entities × 50 closed versions + 1 current.
--   Single-table:  510k rows in one table; current rows found via partial index.
--   Model B:       10k rows in main; 500k in history.
-- Synthetic history generation (direct inserts of closed spans) for speed; the
-- write-path cost of FOR PORTION OF vs. triggers is measured in script 04.
-- ═══════════════════════════════════════════════════════════════════════════
\set ON_ERROR_STOP off
\timing on

\echo '── setup + load (synthetic history) ──'
DROP TABLE IF EXISTS bench_st, bench_mb, bench_mb_history CASCADE;

CREATE TABLE bench_st (
	id uuid NOT NULL,
	payload text NOT NULL,
	version_no int NOT NULL,
	system_period tstzrange NOT NULL,
	PRIMARY KEY (id, system_period WITHOUT OVERLAPS)
);

CREATE TABLE bench_mb (
	id uuid PRIMARY KEY,
	payload text NOT NULL,
	version_no int NOT NULL,
	system_period tstzrange NOT NULL
);
CREATE TABLE bench_mb_history (
	id uuid NOT NULL,
	payload text NOT NULL,
	version_no int NOT NULL,
	system_period tstzrange NOT NULL,
	PRIMARY KEY (id, system_period WITHOUT OVERLAPS)
);

-- 10k deterministic ids; 50 closed weekly versions each + 1 current row.
WITH ids AS (
	SELECT n, md5(n::text)::uuid AS id FROM generate_series(1, 10000) n
)
INSERT INTO bench_st (id, payload, version_no, system_period)
SELECT id, 'v' || v, v,
       tstzrange('2025-01-01'::timestamptz + (v - 1) * interval '7 days',
                 '2025-01-01'::timestamptz + v * interval '7 days')
  FROM ids, generate_series(1, 50) v;

WITH ids AS (
	SELECT n, md5(n::text)::uuid AS id FROM generate_series(1, 10000) n
)
INSERT INTO bench_st (id, payload, version_no, system_period)
SELECT id, 'current', 51, tstzrange('2025-01-01'::timestamptz + 50 * interval '7 days', 'infinity')
  FROM ids;

INSERT INTO bench_mb_history SELECT * FROM bench_st WHERE upper(system_period) <> 'infinity';
INSERT INTO bench_mb         SELECT * FROM bench_st WHERE upper(system_period) = 'infinity';

-- The single-table mitigation under test: partial index over current rows only.
CREATE INDEX ix_bench_st_current ON bench_st (id) WHERE upper(system_period) = 'infinity';

VACUUM ANALYZE bench_st;
VACUUM ANALYZE bench_mb;
VACUUM ANALYZE bench_mb_history;

\echo ''
\echo '── physical sizes ──'
SELECT 'bench_st (single)' AS model, pg_size_pretty(pg_total_relation_size('bench_st')) AS total
UNION ALL
SELECT 'bench_mb (main)', pg_size_pretty(pg_total_relation_size('bench_mb'))
UNION ALL
SELECT 'bench_mb_history', pg_size_pretty(pg_total_relation_size('bench_mb_history'));

\echo ''
\echo '── TEST 3a: point lookup of ONE current row ──'
\echo '   single-table via partial index:'
EXPLAIN (ANALYZE, BUFFERS)
SELECT * FROM bench_st
 WHERE id = md5('5000')::uuid AND upper(system_period) = 'infinity';

\echo '   Model B lean main table:'
EXPLAIN (ANALYZE, BUFFERS)
SELECT * FROM bench_mb WHERE id = md5('5000')::uuid;

\echo ''
\echo '── TEST 3b: scan-shaped read — count current rows by predicate ──'
\echo '   single-table:'
EXPLAIN (ANALYZE, BUFFERS)
SELECT count(*) FROM bench_st
 WHERE upper(system_period) = 'infinity' AND version_no = 51;

\echo '   Model B:'
EXPLAIN (ANALYZE, BUFFERS)
SELECT count(*) FROM bench_mb WHERE version_no = 51;

\echo ''
\echo '── TEST 3c: as-of point query (where single-table should SHINE — no UNION) ──'
\echo '   single-table:'
EXPLAIN (ANALYZE, BUFFERS)
SELECT * FROM bench_st
 WHERE id = md5('5000')::uuid AND system_period @> '2025-06-15'::timestamptz;

\echo '   Model B (current UNION ALL history — the timeline-view shape):'
EXPLAIN (ANALYZE, BUFFERS)
SELECT * FROM (
	SELECT * FROM bench_mb
	UNION ALL
	SELECT * FROM bench_mb_history
) t
 WHERE id = md5('5000')::uuid AND system_period @> '2025-06-15'::timestamptz;
