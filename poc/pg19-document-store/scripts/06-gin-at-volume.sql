-- ═══════════════════════════════════════════════════════════════════════════
-- Q1/Q2 follow-up: the 3-row table in 00-setup always seq-scans (planner cost on a
-- tiny relation), so 01/02 could not DEMONSTRATE GIN usage. Seed volume, ANALYZE, and
-- re-EXPLAIN both predicates. Then force enable_seqscan=off to prove the jsonb_path_ops
-- index is at least CAPABLE of serving @> and @? — separating "planner declined it" from
-- "index can't serve it." The performance premise of the whole document-store move rides here.
-- ═══════════════════════════════════════════════════════════════════════════
\set ON_ERROR_STOP off

INSERT INTO policy_view (id, dedup_key, status, doc)
SELECT gen_random_uuid(), gen_random_uuid(), 'Active',
	jsonb_build_object(
		'id', 'bulk-' || g,
		'customerId', 'c-' || (g % 1000),
		'productCode', (ARRAY['WC','GL','BOP','UMB'])[1 + (g % 4)],
		'premium', jsonb_build_object('amount', (g % 50000)::numeric, 'currency', 'USD'),
		'updates', jsonb_build_array(
			jsonb_build_object('seq', 1, 'amount', (g % 3000)::numeric,  'reason', 'endorsement'),
			jsonb_build_object('seq', 2, 'amount', (g % 9000)::numeric,  'reason', 'audit')))
FROM generate_series(1, 20000) AS g;

ANALYZE policy_view;
SELECT count(*) AS total_rows FROM policy_view;

\echo === @> containment at volume (planner choice) ===
EXPLAIN (ANALYZE, BUFFERS, COSTS OFF)
SELECT id FROM policy_view WHERE doc @> '{"customerId": "c-1"}';

\echo === @? array-subset filter at volume (planner choice) ===
EXPLAIN (ANALYZE, BUFFERS, COSTS OFF)
SELECT id FROM policy_view WHERE doc @? '$.updates[*] ? (@.amount > 8500)';

\echo === capability proof: same two predicates with enable_seqscan = off ===
SET enable_seqscan = off;
EXPLAIN (ANALYZE, BUFFERS, COSTS OFF)
SELECT id FROM policy_view WHERE doc @> '{"customerId": "c-1"}';
EXPLAIN (ANALYZE, BUFFERS, COSTS OFF)
SELECT id FROM policy_view WHERE doc @? '$.updates[*] ? (@.amount > 8500)';
RESET enable_seqscan;
