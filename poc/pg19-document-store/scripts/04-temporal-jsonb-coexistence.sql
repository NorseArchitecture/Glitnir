-- ═══════════════════════════════════════════════════════════════════════════
-- Q4: do the two storage models coexist on one primary (and stream to the replica)?
--   - System-time temporal source-of-truth (Model B shape from pg19-temporal: a main
--     table with a tstzrange system_period + a GiST exclusion so only one current row
--     per logical id exists at a time).
--   - The jsonb document/view table from 00-setup.
-- This is not a re-test of pg19-temporal's verdicts — it confirms the temporal apparatus
-- and the jsonb document table share a database without interference.
-- ═══════════════════════════════════════════════════════════════════════════
\set ON_ERROR_STOP off

DROP TABLE IF EXISTS policy_sot;
CREATE TABLE policy_sot (
	id            uuid NOT NULL,
	customer_id   text NOT NULL,
	premium_amount numeric NOT NULL,
	product_code  text NOT NULL,
	system_period tstzrange NOT NULL DEFAULT tstzrange(now(), NULL, '[)'),
	-- only one current (unbounded-upper) row per id may overlap in time
	EXCLUDE USING gist (id WITH =, system_period WITH &&)
);

\echo === insert a current row, then version it (close old period, open new) ===
INSERT INTO policy_sot (id, customer_id, premium_amount, product_code)
VALUES ('00000000-0000-0000-0000-000000000001', 'c-1', 4200.00, 'WC');

-- version the row the SQL:2011 way available pre-FOR-PORTION: close the current, insert the next
UPDATE policy_sot
SET system_period = tstzrange(lower(system_period), now(), '[)')
WHERE id = '00000000-0000-0000-0000-000000000001' AND upper_inf(system_period);

INSERT INTO policy_sot (id, customer_id, premium_amount, product_code)
VALUES ('00000000-0000-0000-0000-000000000001', 'c-1', 4500.00, 'WC');

\echo === current row (unbounded upper) ===
SELECT id, premium_amount, system_period FROM policy_sot WHERE upper_inf(system_period);

\echo === full timeline for the id ===
SELECT id, premium_amount, system_period FROM policy_sot
WHERE id = '00000000-0000-0000-0000-000000000001'
ORDER BY lower(system_period);

\echo === coexistence: jsonb document table is untouched and still queryable ===
SELECT count(*) AS policy_view_rows FROM policy_view;

\echo === both object kinds present in the same database ===
SELECT relname, relkind FROM pg_class
WHERE relname IN ('policy_view', 'policy_sot', 'ix_policy_view_doc')
ORDER BY relname;
