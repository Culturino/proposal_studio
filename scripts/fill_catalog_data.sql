-- Restores catalog fields lost when the API mis-serialised jsonb columns and clients
-- echoed the wrapper back on save. Safe to re-run: every statement targets a model by name
-- and overwrites with the intended value.
SET client_encoding = 'UTF8';

BEGIN;

-- ── Dimensions ───────────────────────────────────────────────────────────────
-- Grands carry length/width/weight/setting; the upright carries height/width/depth/weight.
-- The proposal's specifications table renders the first four keys in insertion order.

UPDATE products SET dimensions = '{
  "length": "274 cm  (8′11¾″)",
  "width":  "158 cm  (62¼″)",
  "weight": "approx. 480 kg",
  "setting": "Concert Hall · Recording"
}'::jsonb WHERE model = 'Model D-274';

UPDATE products SET dimensions = '{
  "length": "227 cm  (7′5″)",
  "width":  "155 cm  (61″)",
  "weight": "approx. 425 kg",
  "setting": "Recital · Conservatory"
}'::jsonb WHERE model = 'Model C-227';

UPDATE products SET dimensions = '{
  "length": "211 cm  (6′10½″)",
  "width":  "148 cm  (58¼″)",
  "weight": "approx. 345 kg",
  "setting": "Salon · Studio · Recital"
}'::jsonb WHERE model = 'Model B-211';

UPDATE products SET dimensions = '{
  "length": "180 cm  (5′10¾″)",
  "width":  "146 cm  (57½″)",
  "weight": "approx. 280 kg",
  "setting": "Living Room · Studio"
}'::jsonb WHERE model = 'Model O-180';

UPDATE products SET dimensions = '{
  "length": "170 cm  (5′7″)",
  "width":  "146 cm  (57½″)",
  "weight": "approx. 275 kg",
  "setting": "Studio · Teaching"
}'::jsonb WHERE model = 'Model M-170';

UPDATE products SET dimensions = '{
  "length": "155 cm  (5′1″)",
  "width":  "146 cm  (57½″)",
  "weight": "approx. 255 kg",
  "setting": "Apartment · Salon"
}'::jsonb WHERE model = 'Model S-155';

UPDATE products SET dimensions = '{
  "height": "132 cm  (52″)",
  "width":  "152 cm  (59¾″)",
  "depth":  "68 cm  (26¾″)",
  "weight": "approx. 295 kg"
}'::jsonb WHERE model = 'Model K-132';

UPDATE products SET dimensions = '{
  "length": "210 cm  (6′10¾″)",
  "width":  "151 cm  (59½″)",
  "weight": "approx. 350 kg",
  "setting": "Salon · Recital"
}'::jsonb WHERE model = 'Model 4';

-- ── Craftsmanship features ───────────────────────────────────────────────────
-- The specifications page lists up to seven; the instrument page shows the first four.

UPDATE products SET features = ARRAY[
  'Exclusive use of solid wood',
  'Continuous bent rim',
  'Diaphragmatic soundboard',
  'Hexagrip pinblock',
  'Laminated bridge',
  'Tubular metallic action frame',
  'Duplex scale'
] WHERE model IN ('Model D-274', 'Model C-227', 'Model B-211',
                  'Model O-180', 'Model M-170', 'Model S-155');

UPDATE products SET features = ARRAY[
  'Exclusive use of solid wood',
  'Diaphragmatic soundboard',
  'Hexagrip pinblock',
  'Full-length music desk',
  'Slow-close fallboard',
  'Sostenuto pedal'
] WHERE model = 'Model K-132';

UPDATE products SET features = ARRAY[
  'Aliquot stringing',
  'Solid Saxon spruce soundboard',
  'Hand-notched bridges',
  'Continuous bent rim',
  'Tapered soundboard'
] WHERE model = 'Model 4';

-- ── Finishes, inclusions and exclusions ──────────────────────────────────────

UPDATE products SET finishes = ARRAY[
  'Ebonised High Polish', 'Ivory White', 'Snow White', 'Crown Jewel veneers'
] WHERE model LIKE 'Model %' AND model <> 'Model 4';

UPDATE products SET default_includes = ARRAY[
  'Delivery & white-glove installation, UAE',
  'First professional tuning, on site',
  'Adjustable artist bench',
  '12-month manufacturer warranty',
  'Piano Care Guide (English & Arabic)',
  'Lifetime advisory & service support'
];

UPDATE products SET default_excludes = ARRAY[
  'Ongoing tuning beyond the first visit',
  'Climate-control equipment',
  'Custom finishes & Crown Jewel veneers',
  'Inter-emirate & international freight'
];

-- ── Remaining blanks ─────────────────────────────────────────────────────────

UPDATE products SET availability_note = 'Subject to availability'
WHERE availability_note IS NULL OR availability_note = '';

-- Blüthner Model 4 was seeded without a price amount.
UPDATE prices SET amount = 419790.00
WHERE amount IS NULL
  AND product_id = (SELECT id FROM products WHERE model = 'Model 4');

UPDATE products SET updated_at = now();

COMMIT;
