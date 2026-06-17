-- ═══════════════════════════════════════════════════════════════════════════
-- Q1 (server-side half): does jsonb express the IDocumentRepository<T> read shapes?
--   filter (containment + jsonpath), sort, skip/take, projection to a narrower shape.
-- The consume-side half — Npgsql deserializing these into POCOs with no EF — is the harness.
-- ═══════════════════════════════════════════════════════════════════════════
\set ON_ERROR_STOP off

\echo === filter: containment (@>) — "customerId = c-1" ===
SELECT id, status, doc->>'productCode' AS product
FROM policy_view
WHERE doc @> '{"customerId": "c-1"}';

\echo === filter: jsonpath predicate (@@) — premium.amount > 1000 ===
SELECT id, doc->'premium'->>'amount' AS premium
FROM policy_view
WHERE doc @@ '$.premium.amount > 1000';

\echo === sort + skip/take — order by effectiveDate, OFFSET 1 LIMIT 1 ===
SELECT doc->>'id' AS policy, doc->>'effectiveDate' AS eff
FROM policy_view
ORDER BY doc->>'effectiveDate'
OFFSET 1 LIMIT 1;

\echo === projection: jsonb_build_object — narrow summary shape ===
SELECT jsonb_build_object(
	'id',       doc->'id',
	'status',   status,
	'product',  doc->'productCode',
	'premium',  doc->'premium'
) AS summary
FROM policy_view
WHERE doc @> '{"customerId": "c-1"}';

\echo === projection: SQL/JSON JSON_VALUE + JSON_QUERY (pg17+) ===
SELECT
	JSON_VALUE(doc, '$.productCode')        AS product_code,
	JSON_VALUE(doc, '$.premium.amount' RETURNING numeric) AS premium_amount,
	JSON_QUERY(doc, '$.premium')            AS premium_obj
FROM policy_view
WHERE doc @> '{"customerId": "c-2"}';

\echo === EXPLAIN: containment filter should ride the GIN index ===
EXPLAIN (ANALYZE, BUFFERS, COSTS OFF)
SELECT id FROM policy_view WHERE doc @> '{"customerId": "c-1"}';
