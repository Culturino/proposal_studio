-- Remove joke / dummy rows left over from local testing.
-- Safe to re-run. Keeps House of Pianos catalog, Shavkat admin accounts,
-- and the one real-looking client (abdul majaz).

SET client_encoding = 'UTF8';
BEGIN;

UPDATE products
SET tagline = E'\u201CThe perfect piano\u201D', updated_at = now()
WHERE model = 'Model B-211';

UPDATE products
SET tagline = 'The living-room grand', updated_at = now()
WHERE model = 'Model O-180';

DELETE FROM proposal_items
WHERE product_id IN (SELECT id FROM products WHERE lower(btrim(model)) = 'mike hawk');

DELETE FROM prices
WHERE product_id IN (SELECT id FROM products WHERE lower(btrim(model)) = 'mike hawk');

DELETE FROM products
WHERE lower(btrim(model)) = 'mike hawk';

CREATE TEMP TABLE dummy_clients AS
SELECT id
FROM clients
WHERE coalesce(email, '') ILIKE '%@example.com'
   OR coalesce(email, '') ILIKE '%barack.obama%'
   OR name ~* '(ronaldo|messi|gaga|rihanna|shakira|taylor swift|tom hanks|will smith|emma watson|keanu reeves|dicaprio|jennifer lopez|serena williams|micheal jordan|michael jordan|lady gaga)'
   OR name ILIKE 'jimmy_john%'
   OR lower(btrim(name)) IN ('micheal', 'hussein')
   OR coalesce(notes, '') = 'string';

DELETE FROM share_links
WHERE proposal_id IN (SELECT id FROM proposals WHERE client_id IN (SELECT id FROM dummy_clients));

DELETE FROM proposal_events
WHERE proposal_id IN (SELECT id FROM proposals WHERE client_id IN (SELECT id FROM dummy_clients));

DELETE FROM approval_requests
WHERE proposal_id IN (SELECT id FROM proposals WHERE client_id IN (SELECT id FROM dummy_clients));

DELETE FROM notifications
WHERE related_id IN (SELECT id FROM proposals WHERE client_id IN (SELECT id FROM dummy_clients));

DELETE FROM proposals
WHERE client_id IN (SELECT id FROM dummy_clients);

DELETE FROM clients
WHERE id IN (SELECT id FROM dummy_clients);

-- Remaining proposals (abdul majaz) should sit on the real admin, not demo staff.
UPDATE proposals
SET advisor_id = keep.id, updated_at = now()
FROM (SELECT id FROM users WHERE lower(email) = 'shavkat@example.com' LIMIT 1) AS keep
WHERE advisor_id IS DISTINCT FROM keep.id
  AND advisor_id IN (
      SELECT id FROM users
      WHERE lower(email) NOT IN ('shavkat@example.com', 'admin@houseofpianos.ae')
  );

UPDATE clients
SET created_by = keep.id
FROM (SELECT id FROM users WHERE lower(email) = 'shavkat@example.com' LIMIT 1) AS keep
WHERE created_by IN (
    SELECT id FROM users
    WHERE lower(email) NOT IN ('shavkat@example.com', 'admin@houseofpianos.ae')
);

DELETE FROM password_resets
WHERE user_id IN (
    SELECT id FROM users
    WHERE lower(email) NOT IN ('shavkat@example.com', 'admin@houseofpianos.ae')
);

DELETE FROM user_invites
WHERE created_by IN (
    SELECT id FROM users
    WHERE lower(email) NOT IN ('shavkat@example.com', 'admin@houseofpianos.ae')
);

DELETE FROM notifications
WHERE user_id IN (
    SELECT id FROM users
    WHERE lower(email) NOT IN ('shavkat@example.com', 'admin@houseofpianos.ae')
);

DELETE FROM users
WHERE lower(email) NOT IN ('shavkat@example.com', 'admin@houseofpianos.ae');

-- Frozen PDFs still carry the joke taglines until the snapshot is rebuilt.
UPDATE proposals
SET snapshot = NULL, updated_at = now()
WHERE snapshot IS NOT NULL;

COMMIT;

SELECT 'users' AS kind, count(*)::text AS n FROM users
UNION ALL SELECT 'clients', count(*)::text FROM clients
UNION ALL SELECT 'proposals', count(*)::text FROM proposals
UNION ALL SELECT 'products', count(*)::text FROM products
UNION ALL SELECT 'b211', tagline FROM products WHERE model = 'Model B-211'
UNION ALL SELECT 'o180', tagline FROM products WHERE model = 'Model O-180';
