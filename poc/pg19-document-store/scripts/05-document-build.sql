-- ═══════════════════════════════════════════════════════════════════════════
-- Q5: "build the view in memory or in database?" — prove the in-DB path so the choice
-- is informed. Relational source-of-truth (a policy + its updates) is assembled into the
-- jsonb document Postgres-side via jsonb_build_object / jsonb_agg. The app-side alternative
-- (worker materializes the doc in C# and upserts it — the Replace analog) is the comparison;
-- this script shows the DB can do it, so the decision is cost/clarity, not capability.
-- ═══════════════════════════════════════════════════════════════════════════
\set ON_ERROR_STOP off

DROP TABLE IF EXISTS policy_update;
DROP TABLE IF EXISTS policy;

CREATE TABLE policy (
	id             uuid PRIMARY KEY,
	customer_id    text NOT NULL,
	effective_date date NOT NULL,
	premium_amount numeric NOT NULL,
	premium_currency text NOT NULL,
	product_code   text NOT NULL
);
CREATE TABLE policy_update (
	policy_id uuid NOT NULL REFERENCES policy(id),
	seq       int  NOT NULL,
	amount    numeric NOT NULL,
	at        timestamptz NOT NULL,
	reason    text NOT NULL,
	PRIMARY KEY (policy_id, seq)
);

INSERT INTO policy VALUES
	('00000000-0000-0000-0000-000000000010', 'c-1', '2026-01-01', 4200.00, 'USD', 'WC');
INSERT INTO policy_update VALUES
	('00000000-0000-0000-0000-000000000010', 1, 100.00,  '2026-01-02T10:00:00Z', 'endorsement'),
	('00000000-0000-0000-0000-000000000010', 2, 2500.00, '2026-02-15T10:00:00Z', 'audit');

\echo === in-DB build: assemble the document from the relational source of truth ===
SELECT jsonb_build_object(
	'id',            p.id,
	'customerId',    p.customer_id,
	'effectiveDate', p.effective_date,
	'productCode',   p.product_code,
	'premium',       jsonb_build_object('amount', p.premium_amount, 'currency', p.premium_currency),
	'updates', (
		SELECT coalesce(jsonb_agg(
			jsonb_build_object('seq', u.seq, 'amount', u.amount, 'at', u.at, 'reason', u.reason)
			ORDER BY u.seq), '[]'::jsonb)
		FROM policy_update u WHERE u.policy_id = p.id
	)
) AS built_document
FROM policy p
WHERE p.id = '00000000-0000-0000-0000-000000000010';

\echo === and the build can populate the document table directly (the in-DB Replace) ===
INSERT INTO policy_view (id, dedup_key, status, doc)
SELECT p.id, gen_random_uuid(), 'Active',
	jsonb_build_object(
		'id', p.id, 'customerId', p.customer_id, 'productCode', p.product_code,
		'premium', jsonb_build_object('amount', p.premium_amount, 'currency', p.premium_currency),
		'updates', (SELECT coalesce(jsonb_agg(jsonb_build_object('seq', u.seq, 'amount', u.amount) ORDER BY u.seq), '[]'::jsonb)
			FROM policy_update u WHERE u.policy_id = p.id))
FROM policy p
WHERE p.id = '00000000-0000-0000-0000-000000000010'
ON CONFLICT (id) DO UPDATE SET doc = EXCLUDED.doc, status = EXCLUDED.status;

SELECT id, status, jsonb_pretty(doc) FROM policy_view WHERE id = '00000000-0000-0000-0000-000000000010';
