-- ═══════════════════════════════════════════════════════════════════════════
-- Q2: the filtered-array-subset projection — Mongo's $elemMatch / positional.
-- Two questions:
--   (a) Filter ROWS whose array has any element matching a predicate.
--   (b) Project only the MATCHING array elements (not the whole array, not the whole doc).
-- And: is the row filter (a) GIN-indexable?
-- ═══════════════════════════════════════════════════════════════════════════
\set ON_ERROR_STOP off

\echo === (a) row filter: any update with amount > 1000 — jsonpath exists (@?) ===
SELECT id, doc->>'id' AS policy
FROM policy_view
WHERE doc @? '$.updates[*] ? (@.amount > 1000)';

\echo === (b) subset projection: ONLY the matching array elements — jsonb_path_query_array ===
SELECT
	doc->>'id' AS policy,
	jsonb_path_query_array(doc, '$.updates[*] ? (@.amount > 1000)') AS big_updates
FROM policy_view
WHERE doc @? '$.updates[*] ? (@.amount > 1000)';

\echo === (b2) same subset, exploded to rows via JSON_TABLE (pg17+) ===
SELECT pv.doc->>'id' AS policy, u.seq, u.amount, u.reason
FROM policy_view pv,
	JSON_TABLE(pv.doc, '$.updates[*]'
		COLUMNS (
			seq    int     PATH '$.seq',
			amount numeric PATH '$.amount',
			reason text    PATH '$.reason'
		)
	) AS u
WHERE u.amount > 1000
ORDER BY policy, u.seq;

\echo === GIN check: does the @? row filter ride the jsonb_path_ops index? ===
EXPLAIN (ANALYZE, BUFFERS, COSTS OFF)
SELECT id FROM policy_view WHERE doc @? '$.updates[*] ? (@.amount > 1000)';
