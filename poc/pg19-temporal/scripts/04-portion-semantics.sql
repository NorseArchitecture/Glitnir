-- ═══════════════════════════════════════════════════════════════════════════
-- Matrix #4 — FOR PORTION OF semantics: triggers, RETURNING, rowcounts, bounds
--
-- Known open items in play:
--   "Trigger behavior inconsistencies between leftovers"  → capture exact firing
--   "NULL vs. keyword for unbounded portion bounds"       → capture accepted syntax
-- Pins the Neon-doc claims: portion row fires UPDATE; leftover flanks fire
-- INSERT; RETURNING and rowcounts exclude leftovers.
-- ═══════════════════════════════════════════════════════════════════════════
\set ON_ERROR_STOP off
\timing on

\echo '── setup: business-effective table (Model A) + trigger observatory ──'
DROP TABLE IF EXISTS class_factors, trigger_log CASCADE;

CREATE TABLE class_factors (
	class_code text NOT NULL,
	valid_period daterange NOT NULL,
	loss_cost_factor numeric(10,4) NOT NULL,
	PRIMARY KEY (class_code, valid_period WITHOUT OVERLAPS)
);

CREATE TABLE trigger_log (
	seq bigint GENERATED ALWAYS AS IDENTITY,
	fired_at timestamptz NOT NULL DEFAULT clock_timestamp(),
	op text NOT NULL,
	when_fired text NOT NULL,
	old_range daterange,
	new_range daterange,
	old_factor numeric,
	new_factor numeric
);

CREATE FUNCTION log_factor_change() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
	INSERT INTO trigger_log (op, when_fired, old_range, new_range, old_factor, new_factor)
	VALUES (TG_OP, TG_WHEN,
	        CASE WHEN TG_OP IN ('UPDATE','DELETE') THEN OLD.valid_period END,
	        CASE WHEN TG_OP IN ('UPDATE','INSERT') THEN NEW.valid_period END,
	        CASE WHEN TG_OP IN ('UPDATE','DELETE') THEN OLD.loss_cost_factor END,
	        CASE WHEN TG_OP IN ('UPDATE','INSERT') THEN NEW.loss_cost_factor END);
	RETURN COALESCE(NEW, OLD);
END $$;

CREATE TRIGGER trg_log AFTER INSERT OR UPDATE OR DELETE ON class_factors
	FOR EACH ROW EXECUTE FUNCTION log_factor_change();

-- Full-year 2026 factor for WC class 8810.
INSERT INTO class_factors VALUES ('8810', daterange('2026-01-01', '2027-01-01'), 1.0000);
TRUNCATE trigger_log;   -- observe only the portion operations below

\echo ''
\echo '── TEST 4a: UPDATE FOR PORTION OF — Q3 correction (expect 3-way split) ──'
UPDATE class_factors
   FOR PORTION OF valid_period FROM '2026-07-01' TO '2026-10-01'
   SET loss_cost_factor = 1.4200
 WHERE class_code = '8810';

SELECT class_code, valid_period, loss_cost_factor
  FROM class_factors ORDER BY valid_period;

\echo '   trigger firing (Neon doc claims: 1 UPDATE + 2 INSERTs):'
SELECT seq, op, old_range, new_range, old_factor, new_factor
  FROM trigger_log ORDER BY seq;

\echo ''
\echo '── TEST 4b: RETURNING + rowcount exclude leftovers? ──'
TRUNCATE trigger_log;
DO $$
DECLARE n int;
BEGIN
	UPDATE class_factors
	   FOR PORTION OF valid_period FROM '2026-08-01' TO '2026-09-01'
	   SET loss_cost_factor = 1.5000
	 WHERE class_code = '8810';
	GET DIAGNOSTICS n = ROW_COUNT;
	RAISE NOTICE 'ROW_COUNT reported: % (doc claim: excludes leftover flanks)', n;
END $$;

UPDATE class_factors
   FOR PORTION OF valid_period FROM '2026-09-01' TO '2026-09-15'
   SET loss_cost_factor = 1.5500
 WHERE class_code = '8810'
RETURNING class_code, valid_period, loss_cost_factor;
\echo '   (RETURNING rows above should show only the directly-modified portion)'

\echo ''
\echo '── TEST 4c: DELETE FOR PORTION OF — carve a hole (expect gap, flanks preserved) ──'
TRUNCATE trigger_log;
DELETE FROM class_factors
   FOR PORTION OF valid_period FROM '2026-03-01' TO '2026-04-01'
 WHERE class_code = '8810';

SELECT class_code, valid_period, loss_cost_factor
  FROM class_factors ORDER BY valid_period;
SELECT seq, op, old_range, new_range FROM trigger_log ORDER BY seq;

\echo ''
\echo '── TEST 4d: unbounded bound syntax (open item: NULL vs. keyword) ──'
\echo '   FROM <date> TO NULL:'
UPDATE class_factors
   FOR PORTION OF valid_period FROM '2026-11-01' TO NULL
   SET loss_cost_factor = 1.6000
 WHERE class_code = '8810';

\echo '   FROM NULL TO <date>:'
UPDATE class_factors
   FOR PORTION OF valid_period FROM NULL TO '2026-02-01'
   SET loss_cost_factor = 0.9500
 WHERE class_code = '8810';

SELECT class_code, valid_period, loss_cost_factor
  FROM class_factors ORDER BY valid_period;

\echo ''
\echo '── TEST 4e: system-time idiom — single-statement versioned update ──'
\echo '   (the Model B trigger-killer IF single-table were viable: FROM now() TO NULL)'
DROP TABLE IF EXISTS sys_demo CASCADE;
CREATE TABLE sys_demo (
	id uuid NOT NULL,
	val text NOT NULL,
	system_period tstzrange NOT NULL DEFAULT tstzrange(now(), 'infinity'),
	PRIMARY KEY (id, system_period WITHOUT OVERLAPS)
);
INSERT INTO sys_demo (id, val) VALUES ('33333333-3333-3333-3333-333333333333', 'original');
SELECT pg_sleep(0.05);

UPDATE sys_demo
   FOR PORTION OF system_period FROM now() TO NULL
   SET val = 'corrected'
 WHERE id = '33333333-3333-3333-3333-333333333333';

\echo '   (expect: closed flank with ''original'', current row with ''corrected'')'
SELECT val, system_period FROM sys_demo ORDER BY lower(system_period);
