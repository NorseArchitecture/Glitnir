-- ═══════════════════════════════════════════════════════════════════════════
-- Prerequisites + the jsonb document table the matrix queries against.
--
-- The document table models a policy "view" — the wire shape .Server returns, the
-- Postgres analog of a Mongo document. Two columns carry identity and idempotency
-- exactly as the decision record §4.4 rules:
--   id        — SequentialGuid PK in production (gen_random_uuid here); index-friendly.
--   dedup_key — separate UNIQUE column = UUIDv5(principal, body-hash); the dedup mechanism.
-- A GIN index (jsonb_path_ops) backs containment (@>) and jsonpath (@?, @@) predicates.
-- ═══════════════════════════════════════════════════════════════════════════
\set ON_ERROR_STOP off

CREATE EXTENSION IF NOT EXISTS btree_gist;   -- needed by 04 (temporal GiST exclusion)
SELECT version();

DROP TABLE IF EXISTS policy_view;
CREATE TABLE policy_view (
	id        uuid PRIMARY KEY,
	dedup_key uuid NOT NULL UNIQUE,
	status    text NOT NULL,
	doc       jsonb NOT NULL
);

CREATE INDEX ix_policy_view_doc ON policy_view USING gin (doc jsonb_path_ops);

INSERT INTO policy_view (id, dedup_key, status, doc) VALUES
(gen_random_uuid(), gen_random_uuid(), 'Active', '{
	"id": "p-1001", "customerId": "c-1", "status": "Active",
	"effectiveDate": "2026-01-01", "productCode": "WC",
	"premium": {"amount": 4200.00, "currency": "USD"},
	"updates": [
		{"seq": 1, "amount": 100.00, "at": "2026-01-02T10:00:00Z", "reason": "endorsement"},
		{"seq": 2, "amount": 2500.00, "at": "2026-02-15T10:00:00Z", "reason": "audit"}
	]
}'),
(gen_random_uuid(), gen_random_uuid(), 'Active', '{
	"id": "p-1002", "customerId": "c-1", "status": "Active",
	"effectiveDate": "2026-03-01", "productCode": "GL",
	"premium": {"amount": 900.00, "currency": "USD"},
	"updates": [
		{"seq": 1, "amount": 50.00, "at": "2026-03-05T10:00:00Z", "reason": "endorsement"}
	]
}'),
(gen_random_uuid(), gen_random_uuid(), 'Pending', '{
	"id": "p-1003", "customerId": "c-2", "status": "Pending",
	"effectiveDate": "2026-04-01", "productCode": "WC",
	"premium": {"amount": 12000.00, "currency": "USD"},
	"updates": [
		{"seq": 1, "amount": 8000.00, "at": "2026-04-02T10:00:00Z", "reason": "audit"},
		{"seq": 2, "amount": 1500.00, "at": "2026-04-20T10:00:00Z", "reason": "endorsement"}
	]
}');

SELECT count(*) AS seeded_rows FROM policy_view;
