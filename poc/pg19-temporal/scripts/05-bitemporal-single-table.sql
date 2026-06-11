-- ═══════════════════════════════════════════════════════════════════════════
-- Matrix #5 — can a dual-flavor entity (business-effective + system time)
--             express in ONE table?
--
-- The spec's dual-flavor entity (ClassFactor) uses Model A for the business
-- period + Model B history for system time. This script probes whether a
-- single bitemporal table is even expressible: WITHOUT OVERLAPS accepts one
-- range per key, so the second dimension needs an EXCLUDE constraint — and
-- FOR PORTION OF on one dimension does something to the other dimension's
-- column on the flanks (copied verbatim = stale system periods?). Capture it.
-- ═══════════════════════════════════════════════════════════════════════════
\set ON_ERROR_STOP off
\timing on

\echo '── TEST 5a: two WITHOUT OVERLAPS ranges in one PK (expect: ERROR — one allowed) ──'
DROP TABLE IF EXISTS bitemporal CASCADE;
CREATE TABLE bitemporal (
	class_code text NOT NULL,
	valid_period daterange NOT NULL,
	system_period tstzrange NOT NULL,
	factor numeric(10,4) NOT NULL,
	PRIMARY KEY (class_code, valid_period WITHOUT OVERLAPS, system_period WITHOUT OVERLAPS)
);

\echo ''
\echo '── TEST 5b: one temporal PK + gist EXCLUDE for the second dimension ──'
CREATE TABLE bitemporal (
	class_code text NOT NULL,
	valid_period daterange NOT NULL,
	system_period tstzrange NOT NULL DEFAULT tstzrange(now(), 'infinity'),
	factor numeric(10,4) NOT NULL,
	-- "for any class+effective-span, only one CURRENT system version may overlap"
	EXCLUDE USING gist (
		class_code WITH =,
		valid_period WITH &&,
		system_period WITH &&
	)
);
\echo '   (if created: note there is NO temporal PK at all now — FOR PORTION OF requirements?)'

INSERT INTO bitemporal (class_code, valid_period, factor)
VALUES ('8810', daterange('2026-01-01', '2027-01-01'), 1.0000);

\echo ''
\echo '── TEST 5c: FOR PORTION OF the BUSINESS period — what happens to system_period on flanks? ──'
SELECT pg_sleep(0.05);
UPDATE bitemporal
   FOR PORTION OF valid_period FROM '2026-07-01' TO '2026-10-01'
   SET factor = 1.4200
 WHERE class_code = '8810';

\echo '   (if flanks copied system_period verbatim, the new flank rows carry the ORIGINAL'
\echo '    system lower bound — i.e., system time is silently wrong on engine-made rows)'
SELECT class_code, valid_period, system_period, factor
  FROM bitemporal ORDER BY valid_period;

\echo ''
\echo '── TEST 5d: now version the Q3 row in SYSTEM time too (the bitemporal update) ──'
UPDATE bitemporal
   FOR PORTION OF system_period FROM now() TO NULL
   SET factor = 1.4500
 WHERE class_code = '8810'
   AND valid_period = daterange('2026-07-01', '2026-10-01');

SELECT class_code, valid_period, system_period, factor
  FROM bitemporal ORDER BY valid_period, lower(system_period);
\echo ''
\echo '   VERDICT INPUT: count the conceptual hoops above vs. the spec''s two-table'
\echo '   composition (Model A business table + Model B system history). If this'
\echo '   section reads as a puzzle, the spec''s split stands for dual-flavor entities.'
