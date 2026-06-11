-- ═══════════════════════════════════════════════════════════════════════════
-- Prerequisites for every matrix script.
--
-- btree_gist: WITHOUT OVERLAPS builds a GiST index mixing scalar columns with
-- a range column; the scalar opclasses come from this extension. Without it,
-- temporal PK creation fails. (Norns spec §7.6: migration helpers must emit
-- this before any temporal apparatus — finding from the Neon PG18 docs.)
-- ═══════════════════════════════════════════════════════════════════════════
\set ON_ERROR_STOP off

CREATE EXTENSION IF NOT EXISTS btree_gist;
SELECT version();
SELECT extname, extversion FROM pg_extension ORDER BY extname;
