-- Realistic dummy clients for the House of Pianos CRM.
-- No celebrities, no joke names. Safe to re-run: skips emails that already exist.

SET client_encoding = 'UTF8';
BEGIN;

WITH biz AS (
    SELECT id FROM businesses WHERE slug = 'house-of-pianos' LIMIT 1
),
admin AS (
    SELECT id FROM users WHERE lower(email) IN ('shavkat@example.com', 'admin@houseofpianos.ae')
    ORDER BY CASE WHEN lower(email) = 'shavkat@example.com' THEN 0 ELSE 1 END
    LIMIT 1
),
seed(name, email, phone, notes) AS (
    VALUES
    ('Noura Al Hashimi',     'noura.hashimi@example.com',     '+971 50 412 8831', 'Considering a Model B for a villa music room in Jumeirah.'),
    ('Khalid Al Suwaidi',    'khalid.suwaidi@example.com',    '+971 50 623 1044', 'Asked for a comparison between the Model O and Model A.'),
    ('Mariam Al Falasi',     'mariam.falasi@example.com',     '+971 55 218 9076', 'Wants a compact grand for an apartment in Downtown Dubai.'),
    ('Yusuf Rahman',         'yusuf.rahman@example.com',      '+971 52 774 3310', 'First Steinway enquiry. Prefers ebonised high polish.'),
    ('Helena Bergstrom',     'helena.bergstrom@example.com',  '+971 50 889 2265', 'Looking at a recital grand for a private hall in Emirates Hills.'),
    ('Omar Al Zarooni',      'omar.zarooni@example.com',      '+971 56 401 7728', 'Requested delivery and first tuning included in the quote.'),
    ('Lina Haddad',          'lina.haddad@example.com',       '+971 50 335 6192', 'Interested in Spirio for silent practice at home.'),
    ('James Whitmore',       'james.whitmore@example.com',    '+971 55 902 4481', 'Corporate gift for a new office reception in DIFC.'),
    ('Aisha Al Nuaimi',      'aisha.nuaimi@example.com',      '+971 52 118 6640', 'Teaching studio — needs a reliable upright with a full voice.'),
    ('Ravi Menon',           'ravi.menon@example.com',        '+971 50 776 2293', 'Asked about climate control and ongoing maintenance.'),
    ('Sophie Laurent',       'sophie.laurent@example.com',    '+971 54 330 8157', 'Prefers a lighter finish; reviewing ivory white and walnut.'),
    ('Hassan Al Marri',      'hassan.marri@example.com',      '+971 50 247 9914', 'Follow-up after a showroom sitting on the Model D.'),
    ('Elena Petrova',        'elena.petrova@example.com',     '+971 55 614 0738', 'Wants a baby grand that still projects in a large living room.'),
    ('Tariq Al Ketbi',       'tariq.ketbi@example.com',       '+971 52 508 3366', 'Comparing Steinway and Blüthner for a salon setting.'),
    ('Maya Chen',            'maya.chen@example.com',         '+971 50 981 4420', 'Family home in Palm Jumeirah. Timeline around December.'),
    ('Andreas Vogel',        'andreas.vogel@example.com',     '+971 56 223 7705', 'Collector — asked for current Model C availability.')
)
INSERT INTO clients (id, business_id, name, email, phone, notes, created_by, created_at, updated_at)
SELECT gen_random_uuid(), biz.id, seed.name, seed.email, seed.phone, seed.notes, admin.id, now(), now()
FROM seed, biz, admin
WHERE NOT EXISTS (
    SELECT 1 FROM clients c WHERE lower(c.email) = lower(seed.email)
);

COMMIT;

SELECT name, email, phone FROM clients ORDER BY name;
