-- ═══════════════════════════════════════════════════════════════════════════
-- Matrix #1 — RLS-based history immutability on a single-table temporal entity
--
-- Known open item: "FOR PORTION OF incompatible with RLS" (Eisentraut).
-- This script CAPTURES current beta1 behavior, whatever it is. Either outcome
-- is informative:
--   - FOR PORTION OF errors under RLS  → no immutability mitigation exists;
--     Model B's forensic argument stands unopposed.
--   - Leftover inserts bypass WITH CHECK → immutability is partially achievable
--     but fake-history INSERT remains a hole (also captured below).
-- ═══════════════════════════════════════════════════════════════════════════
\set ON_ERROR_STOP off
\timing on

\echo '── setup: single-table system-versioned entity ──'
DROP TABLE IF EXISTS customers_st CASCADE;
DROP ROLE IF EXISTS app_rw;

CREATE TABLE customers_st (
	id uuid NOT NULL,
	full_name text NOT NULL,
	risk_tier int NOT NULL,
	system_period tstzrange NOT NULL DEFAULT tstzrange(now(), 'infinity'),
	PRIMARY KEY (id, system_period WITHOUT OVERLAPS)
);

CREATE ROLE app_rw LOGIN PASSWORD 'app';
GRANT SELECT, INSERT, UPDATE, DELETE ON customers_st TO app_rw;

ALTER TABLE customers_st ENABLE ROW LEVEL SECURITY;
-- The mitigation under test: app may only touch CURRENT rows.
CREATE POLICY sel_all ON customers_st FOR SELECT TO app_rw USING (true);
CREATE POLICY upd_current ON customers_st FOR UPDATE TO app_rw
	USING (upper(system_period) = 'infinity');
CREATE POLICY del_current ON customers_st FOR DELETE TO app_rw
	USING (upper(system_period) = 'infinity');
CREATE POLICY ins_current ON customers_st FOR INSERT TO app_rw
	WITH CHECK (upper(system_period) = 'infinity');

-- Seed: one customer with one closed version and one current version.
INSERT INTO customers_st (id, full_name, risk_tier, system_period) VALUES
	('11111111-1111-1111-1111-111111111111', 'Acme Old',     2, tstzrange('2026-01-01', '2026-03-01')),
	('11111111-1111-1111-1111-111111111111', 'Acme Current', 3, tstzrange('2026-03-01', 'infinity'));

\echo ''
\echo '── TEST 1a: app role rewrites a CLOSED portion (expect: 0 rows / denied) ──'
SET ROLE app_rw;
UPDATE customers_st SET risk_tier = 99
 WHERE id = '11111111-1111-1111-1111-111111111111'
   AND upper(system_period) <> 'infinity';

\echo ''
\echo '── TEST 1b: app role FOR PORTION OF rewrites HISTORY (expect: denied or 0 rows) ──'
UPDATE customers_st
   FOR PORTION OF system_period FROM '2026-01-15' TO '2026-02-01'
   SET risk_tier = 99
 WHERE id = '11111111-1111-1111-1111-111111111111';

\echo ''
\echo '── TEST 1c: THE CRUX — app role FOR PORTION OF on the CURRENT row ──'
\echo '   (open item says RLS-incompatible: capture error vs. success vs. policy clash on flanks)'
UPDATE customers_st
   FOR PORTION OF system_period FROM now() TO NULL
   SET risk_tier = 5
 WHERE id = '11111111-1111-1111-1111-111111111111'
   AND upper(system_period) = 'infinity';

\echo ''
\echo '── TEST 1d: app role INSERTs fake closed history directly (expect: WITH CHECK rejects) ──'
INSERT INTO customers_st (id, full_name, risk_tier, system_period)
VALUES ('11111111-1111-1111-1111-111111111111', 'Forged History', 1,
        tstzrange('2025-01-01', '2025-06-01'));

RESET ROLE;
\echo ''
\echo '── final state (superuser view) ──'
SELECT full_name, risk_tier, system_period
  FROM customers_st
 WHERE id = '11111111-1111-1111-1111-111111111111'
 ORDER BY lower(system_period);
