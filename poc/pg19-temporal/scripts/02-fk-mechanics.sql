-- ═══════════════════════════════════════════════════════════════════════════
-- Matrix #2 — FK mechanics against a single-table temporal parent
--
-- Claim under test (the spec's killer objection to single-table system time):
--   1. A plain FK to id is impossible (no unique constraint on id alone).
--   2. Temporal PERIOD FKs require the REFERENCING side to carry a period
--      (viral temporality).
--   3. Model B (current + history) preserves plain FKs.
-- ═══════════════════════════════════════════════════════════════════════════
\set ON_ERROR_STOP off
\timing on

\echo '── setup: single-table temporal parent ──'
DROP TABLE IF EXISTS policies_plain, policies_temporal, customers_st2, customers_mb, customers_mb_history CASCADE;

CREATE TABLE customers_st2 (
	id uuid NOT NULL,
	full_name text NOT NULL,
	system_period tstzrange NOT NULL DEFAULT tstzrange(now(), 'infinity'),
	PRIMARY KEY (id, system_period WITHOUT OVERLAPS)
);

\echo ''
\echo '── TEST 2a: plain FK to single-table temporal parent (expect: ERROR, no unique on id) ──'
CREATE TABLE policies_plain (
	id uuid PRIMARY KEY,
	customer_id uuid NOT NULL REFERENCES customers_st2 (id)
);

\echo ''
\echo '── TEST 2b: temporal PERIOD FK — child must carry its own period (viral) ──'
CREATE TABLE policies_temporal (
	id uuid NOT NULL,
	customer_id uuid NOT NULL,
	valid_period tstzrange NOT NULL,
	PRIMARY KEY (id, valid_period WITHOUT OVERLAPS),
	FOREIGN KEY (customer_id, PERIOD valid_period)
		REFERENCES customers_st2 (id, PERIOD system_period)
);
\echo '   (if the above succeeded, note: the CHILD was forced to become temporal — the viral cost)'

\echo ''
\echo '── TEST 2c: temporal FK coverage semantics — child period must be covered by parent versions ──'
INSERT INTO customers_st2 (id, full_name, system_period) VALUES
	('22222222-2222-2222-2222-222222222222', 'Covered Co', tstzrange('2026-01-01', 'infinity'));

-- Child inside parent coverage (expect: OK)
INSERT INTO policies_temporal (id, customer_id, valid_period) VALUES
	('aaaaaaaa-0000-0000-0000-000000000001', '22222222-2222-2222-2222-222222222222',
	 tstzrange('2026-02-01', '2026-12-31'));

-- Child BEFORE parent existed (expect: FK violation)
INSERT INTO policies_temporal (id, customer_id, valid_period) VALUES
	('aaaaaaaa-0000-0000-0000-000000000002', '22222222-2222-2222-2222-222222222222',
	 tstzrange('2025-01-01', '2025-06-01'));

\echo ''
\echo '── TEST 2c2: THE COVERAGE QUESTION — child span across TWO CONTIGUOUS parent versions ──'
\echo '   (Neon PG18 docs claim single-row containment; SQL:2011 says aggregated coverage.'
\echo '    If this INSERT fails, temporal FKs are practically dead against system-time'
\echo '    parents — every parent edit splits versions and strands child spans.)'
INSERT INTO customers_st2 (id, full_name, system_period) VALUES
	('44444444-4444-4444-4444-444444444444', 'Split v1', tstzrange('2026-01-01', '2026-06-01')),
	('44444444-4444-4444-4444-444444444444', 'Split v2', tstzrange('2026-06-01', 'infinity'));

-- Child span straddles the v1/v2 boundary — contiguous coverage exists in aggregate.
INSERT INTO policies_temporal (id, customer_id, valid_period) VALUES
	('aaaaaaaa-0000-0000-0000-000000000003', '44444444-4444-4444-4444-444444444444',
	 tstzrange('2026-04-01', '2026-09-01'));
\echo '   (success = aggregated coverage; failure = single-row containment — record verdict)'

\echo ''
\echo '── TEST 2c3: temporal FK referential actions — is CASCADE declarable in PG19? ──'
\echo '   (PG18 shipped RESTRICT/NO ACTION; temporal CASCADE needs portion semantics = PG19 feature)'
DROP TABLE IF EXISTS policies_cascade CASCADE;
CREATE TABLE policies_cascade (
	id uuid NOT NULL,
	customer_id uuid NOT NULL,
	valid_period tstzrange NOT NULL,
	PRIMARY KEY (id, valid_period WITHOUT OVERLAPS),
	FOREIGN KEY (customer_id, PERIOD valid_period)
		REFERENCES customers_st2 (id, PERIOD system_period)
		ON DELETE CASCADE
);
\echo '   (capture: accepted / rejected — and if accepted, test a portion delete cascading)'

\echo ''
\echo '── TEST 2d: Model B — plain FK to lean current table works; history keeps temporal PK ──'
CREATE TABLE customers_mb (
	id uuid PRIMARY KEY,
	full_name text NOT NULL,
	system_period tstzrange NOT NULL DEFAULT tstzrange(now(), 'infinity')
);
CREATE TABLE customers_mb_history (
	id uuid NOT NULL,
	full_name text NOT NULL,
	system_period tstzrange NOT NULL,
	PRIMARY KEY (id, system_period WITHOUT OVERLAPS)
);
CREATE TABLE policies_plain (
	id uuid PRIMARY KEY,
	customer_id uuid NOT NULL REFERENCES customers_mb (id)
);
\echo '   (expect: all three created cleanly — Model B preserves plain FKs)'
\d policies_plain
