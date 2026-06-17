-- ═══════════════════════════════════════════════════════════════════════════
-- Q3 (primary-side inspection): is the standby streaming, and what does the server
-- report for lag? The precise shim-INSERT → visible-on-replica timing is the harness's
-- job (insert on 5455, poll 5456 until visible, stopwatch); this script confirms the
-- replication link is live and exposes the server's own lag accounting.
-- ═══════════════════════════════════════════════════════════════════════════
\set ON_ERROR_STOP off

\echo === is a standby connected? (expect one row, state=streaming) ===
SELECT client_addr, state, sync_state,
	write_lag, flush_lag, replay_lag,
	sent_lsn, write_lsn, flush_lsn, replay_lsn
FROM pg_stat_replication;

\echo === current WAL position on the primary ===
SELECT pg_current_wal_lsn() AS primary_lsn;

\echo === write a marker the harness/replica can look for ===
INSERT INTO policy_view (id, dedup_key, status, doc)
VALUES (gen_random_uuid(), gen_random_uuid(), 'Active',
	jsonb_build_object('id', 'marker-' || extract(epoch FROM clock_timestamp())::bigint, 'marker', true));

SELECT pg_current_wal_lsn() AS primary_lsn_after_marker;

\echo === NOTE: check pg_last_wal_replay_lsn on the replica port 5456 to confirm catch-up ===
