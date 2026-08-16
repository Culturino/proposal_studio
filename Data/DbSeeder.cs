using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProposalStudio.Models;

namespace ProposalStudio.Data
{
    /// <summary>
    /// One-time Phase 1 seed. After the database has been initialized, never re-inserts
    /// products / addons / users / templates — so admin deletes stay deleted across restarts.
    /// </summary>
    public static class DbSeeder
    {
        private const string SeedMarkerKey = "phase1_seeded";

        public static async Task SeedAsync(AppDbContext db)
        {
            await EnsureSupportTablesAsync(db);

            var now = DateTimeOffset.UtcNow;

            // Safe on every boot: schema columns + narrative copy (does not re-insert deleted rows)
            await EnsureBlurbsAsync(db, now);

            // Already initialized → do not resurrect anything an admin removed
            if (await IsInitializedAsync(db))
            {
                await EnsureSeedMarkerAsync(db, now);
                return;
            }

            var business = await db.Businesses.FirstOrDefaultAsync(b => b.Slug == "house-of-pianos");
            if (business == null)
            {
                business = new Business
                {
                    Id = Guid.NewGuid(),
                    Name = "House of Pianos",
                    Slug = "house-of-pianos",
                    ReferencePrefix = "HOP",
                    Blurb = CatalogCopy.HouseOfPianosBlurb,
                    Active = true,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                db.Businesses.Add(business);
                await db.SaveChangesAsync();
            }

            // Brands
            var brandNames = new[] { "Steinway & Sons", "Blüthner", "Boston", "Essex", "Kurzweil" };
            var brands = new Dictionary<string, Brand>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in brandNames)
            {
                var brand = await db.Brands.FirstOrDefaultAsync(b => b.BusinessId == business.Id && b.Name == name);
                if (brand == null)
                {
                    brand = new Brand
                    {
                        Id = Guid.NewGuid(),
                        BusinessId = business.Id,
                        Name = name,
                        Blurb = CatalogCopy.BrandBlurbs.GetValueOrDefault(name),
                        CreatedAt = now,
                        UpdatedAt = now
                    };
                    db.Brands.Add(brand);
                }
                brands[name] = brand;
            }
            await db.SaveChangesAsync();

            // Categories
            var categoryDefs = new (string Name, string Slug, int Order)[]
            {
                ("Concert Grand", "concert-grand", 1),
                ("Grand", "grand", 2),
                ("Baby Grand", "baby-grand", 3),
                ("Upright", "upright", 4),
                ("Digital", "digital", 5)
            };
            var categories = new Dictionary<string, ProductCategory>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, slug, order) in categoryDefs)
            {
                var cat = await db.ProductCategories.FirstOrDefaultAsync(c => c.BusinessId == business.Id && c.Slug == slug);
                if (cat == null)
                {
                    cat = new ProductCategory
                    {
                        Id = Guid.NewGuid(),
                        BusinessId = business.Id,
                        Name = name,
                        Slug = slug,
                        Active = true,
                        SortOrder = order,
                        CreatedAt = now,
                        UpdatedAt = now
                    };
                    db.ProductCategories.Add(cat);
                }
                categories[slug] = cat;
            }
            await db.SaveChangesAsync();

            // Steinway 2026 AED retail — blurbs from official Steinway model pages
            var steinway = brands["Steinway & Sons"];
            var products = new[]
            {
                new SeedProduct("Model D-274", "concert-grand", 1055670m, "The concert standard",
                    CatalogCopy.ProductBlurbs["Model D-274"],
                    new[] { "Exclusive use of solid wood", "Continuous bent rim", "Diaphragmatic soundboard", "Hexagrip pinblock", "Laminated bridge", "Duplex scale" },
                    """{"length":"274 cm","width":"158 cm","weight":"approx. 480 kg"}"""),
                new SeedProduct("Model C-227", "grand", 778470m, "The recital grand",
                    CatalogCopy.ProductBlurbs["Model C-227"],
                    new[] { "Exclusive use of solid wood", "Continuous bent rim", "Diaphragmatic soundboard", "Hexagrip pinblock" },
                    """{"length":"227 cm","width":"155 cm","weight":"approx. 425 kg"}"""),
                new SeedProduct("Model B-211", "grand", 690690m, "“The perfect piano”",
                    CatalogCopy.ProductBlurbs["Model B-211"],
                    new[] { "Exclusive use of solid wood", "Continuous bent rim", "Diaphragmatic soundboard", "Hexagrip pinblock", "Laminated bridge", "Duplex scale" },
                    """{"length":"211 cm (6′10½″)","width":"148 cm (58¼″)","weight":"approx. 345 kg","setting":"Salon · Studio · Recital"}"""),
                new SeedProduct("Model A-188", "grand", 593670m, "The salon grand",
                    CatalogCopy.ProductBlurbs["Model A-188"],
                    new[] { "Exclusive use of solid wood", "Diaphragmatic soundboard", "Hexagrip pinblock" },
                    """{"length":"188 cm","width":"146 cm","weight":"approx. 320 kg"}"""),
                new SeedProduct("Model O-180", "grand", 575190m, "The living-room grand",
                    CatalogCopy.ProductBlurbs["Model O-180"],
                    new[] { "Exclusive use of solid wood", "Diaphragmatic soundboard", "Hexagrip pinblock" },
                    """{"length":"180 cm","width":"146 cm","weight":"approx. 280 kg"}"""),
                new SeedProduct("Model M-170", "grand", 521829m, "The studio grand",
                    CatalogCopy.ProductBlurbs["Model M-170"],
                    new[] { "Exclusive use of solid wood", "Diaphragmatic soundboard", "Hexagrip pinblock" },
                    """{"length":"170 cm","width":"146 cm","weight":"approx. 275 kg"}"""),
                new SeedProduct("Model S-155", "baby-grand", 503349m, "The baby grand",
                    CatalogCopy.ProductBlurbs["Model S-155"],
                    new[] { "Exclusive use of solid wood", "Diaphragmatic soundboard", "Hexagrip pinblock" },
                    """{"length":"155 cm","width":"146 cm","weight":"approx. 255 kg"}"""),
                new SeedProduct("Model K-132", "upright", 263109m, "The upright grand",
                    CatalogCopy.ProductBlurbs["Model K-132"],
                    new[] { "Exclusive use of solid wood", "Diaphragmatic soundboard", "Hexagrip pinblock" },
                    """{"length":"132 cm (height)","width":"152 cm","weight":"approx. 295 kg"}"""),
            };

            var finishes = new[] { "Ebonised High Polish", "Ivory White", "Snow White", "Crown Jewel veneers" };
            var includes = new[]
            {
                "Delivery & white-glove installation, UAE",
                "First professional tuning, on site",
                "Adjustable artist bench",
                "12-month manufacturer warranty",
                "Piano Care Guide (English & Arabic)",
                "Lifetime advisory & service support"
            };
            var excludes = new[]
            {
                "Ongoing tuning beyond the first visit",
                "Climate-control equipment",
                "Custom finishes & Crown Jewel veneers",
                "Inter-emirate & international freight"
            };

            foreach (var sp in products)
            {
                var product = new Product
                {
                    Id = Guid.NewGuid(),
                    BrandId = steinway.Id,
                    CategoryId = categories[sp.CategorySlug].Id,
                    Model = sp.Model,
                    Dimensions = JsonDocument.Parse(sp.DimensionsJson),
                    Features = sp.Features,
                    Blurb = sp.Blurb,
                    Tagline = sp.Tagline,
                    Finishes = finishes,
                    DefaultIncludes = includes,
                    DefaultExcludes = excludes,
                    Status = "active",
                    CreatedAt = now,
                    UpdatedAt = now
                };
                db.Products.Add(product);
                await db.SaveChangesAsync();

                db.Prices.Add(new Price
                {
                    Id = Guid.NewGuid(),
                    ProductId = product.Id,
                    Currency = "AED",
                    Finish = "Ebonised High Polish",
                    Amount = sp.Amount,
                    ValidFrom = new DateOnly(2026, 1, 1),
                    Source = "2026 retail list",
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }

            var bluthner = brands["Blüthner"];
            var bluModel = new Product
            {
                Id = Guid.NewGuid(),
                BrandId = bluthner.Id,
                CategoryId = categories["grand"].Id,
                Model = "Model 4",
                Dimensions = JsonDocument.Parse("""{"length":"210 cm","width":"151 cm","weight":"approx. 350 kg"}"""),
                Features = new[] { "Aliquot stringing", "Solid Saxon spruce soundboard", "Hand-notched bridges" },
                Blurb = CatalogCopy.ProductBlurbs["Model 4"],
                Tagline = "Aliquot stringing",
                Finishes = new[] { "Ebony Polish", "Walnut", "Mahogany", "White Polish" },
                DefaultIncludes = includes,
                DefaultExcludes = excludes,
                Status = "active",
                CreatedAt = now,
                UpdatedAt = now
            };
            db.Products.Add(bluModel);
            await db.SaveChangesAsync();

            db.Prices.Add(new Price
            {
                Id = Guid.NewGuid(),
                ProductId = bluModel.Id,
                Currency = "AED",
                Finish = "Ebony Polish",
                Amount = null,
                ValidFrom = new DateOnly(2026, 1, 1),
                Source = "pending confirmation",
                CreatedAt = now,
                UpdatedAt = now
            });

            db.Templates.Add(new Template
            {
                Id = Guid.NewGuid(),
                BusinessId = business.Id,
                Key = "piano_luxury",
                Name = "Piano — Luxury",
                Version = 1,
                PageSchema = JsonDocument.Parse("""{"pages":["cover","instrument","gallery","specs","investment","closing"]}"""),
                StylingLocked = true,
                Active = true,
                CreatedAt = now,
                UpdatedAt = now
            });

            var addonDefs = new (string Name, decimal Amount)[]
            {
                ("Annual Maintenance (2 tunings/year)", 2400m),
                ("Concert artist bench, leather", 4500m),
                ("Fitted dust cover", 1200m),
                ("Dampp-Chaser climate system", 5200m)
            };
            foreach (var (name, amount) in addonDefs)
            {
                db.Addons.Add(new Addon
                {
                    Id = Guid.NewGuid(),
                    BusinessId = business.Id,
                    Name = name,
                    Amount = amount,
                    Currency = "AED",
                    Active = true,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }

            db.GovernanceSettings.Add(new GovernanceSettings
            {
                Id = Guid.NewGuid(),
                BusinessId = business.Id,
                DiscountFloorPercent = 8m,
                HighValueThreshold = 1_000_000m,
                VatDefaultMode = "line",
                AllowPublicPrices = false,
                CreatedAt = now,
                UpdatedAt = now
            });

            var demoUsers = new (string Name, string Email, string Role)[]
            {
                ("Shavkat Mamadjonov", "admin@houseofpianos.ae", "admin"),
                ("Layla Haddad", "layla@houseofpianos.ae", "manager"),
                ("Omar Khan", "omar@houseofpianos.ae", "advisor"),
                ("Sara Idris", "sara@houseofpianos.ae", "advisor")
            };
            var hash = BCrypt.Net.BCrypt.HashPassword("Password123!");
            foreach (var (name, email, role) in demoUsers)
            {
                db.Users.Add(new User
                {
                    Id = Guid.NewGuid(),
                    BusinessId = business.Id,
                    Name = name,
                    Email = email,
                    PasswordHash = hash,
                    Role = role,
                    Active = true,
                    TwoFactorEnabled = false,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }

            await db.SaveChangesAsync();
            await EnsureSeedMarkerAsync(db, now);
        }

        /// <summary>
        /// True once the DB has been seeded or already contains operational data.
        /// </summary>
        private static async Task<bool> IsInitializedAsync(AppDbContext db)
        {
            if (await HasSeedMarkerAsync(db))
                return true;

            // Existing installs without a marker — treat as initialized so we never re-add deleted rows
            return await db.Users.AnyAsync()
                || await db.Products.AnyAsync()
                || await db.Addons.AnyAsync()
                || await db.Templates.AnyAsync();
        }

        private static async Task<bool> HasSeedMarkerAsync(AppDbContext db)
        {
            await using var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM seed_meta WHERE key = @k LIMIT 1";
            var p = cmd.CreateParameter();
            p.ParameterName = "@k";
            p.Value = SeedMarkerKey;
            cmd.Parameters.Add(p);
            var result = await cmd.ExecuteScalarAsync();
            return result != null && result != DBNull.Value;
        }

        private static async Task EnsureSeedMarkerAsync(AppDbContext db, DateTimeOffset now)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO seed_meta (key, value, updated_at)
                VALUES ({SeedMarkerKey}, {"done"}, {now})
                ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value, updated_at = EXCLUDED.updated_at
                """);
        }

        private static async Task EnsureSupportTablesAsync(AppDbContext db)
        {
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS governance_settings (
                    id uuid PRIMARY KEY,
                    business_id uuid NOT NULL,
                    discount_floor_percent numeric NOT NULL DEFAULT 8,
                    high_value_threshold numeric NOT NULL DEFAULT 1000000,
                    vat_default_mode text NOT NULL DEFAULT 'line',
                    allow_public_prices boolean NOT NULL DEFAULT false,
                    created_at timestamptz NOT NULL,
                    updated_at timestamptz NOT NULL
                );
                """);

            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS share_links (
                    id uuid PRIMARY KEY,
                    proposal_id uuid NOT NULL,
                    token text NOT NULL UNIQUE,
                    created_at timestamptz NOT NULL,
                    expires_at timestamptz NULL,
                    revoked boolean NOT NULL DEFAULT false
                );
                """);

            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS audit_logs (
                    id uuid PRIMARY KEY,
                    actor_id uuid NULL,
                    action text NOT NULL,
                    entity text NOT NULL,
                    entity_id uuid NULL,
                    before jsonb NULL,
                    after jsonb NULL,
                    occurred_at timestamptz NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_audit_logs_occurred_at ON audit_logs (occurred_at DESC);
                CREATE INDEX IF NOT EXISTS ix_audit_logs_entity_occurred ON audit_logs (entity, occurred_at DESC);
                """);

            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS approval_requests (
                    id uuid PRIMARY KEY,
                    proposal_id uuid NOT NULL,
                    kind text NOT NULL,
                    requested_by uuid NOT NULL,
                    approver_id uuid NULL,
                    status text NOT NULL,
                    offered_price numeric NULL,
                    floor_price numeric NULL,
                    catalog_price numeric NULL,
                    message text NULL,
                    created_at timestamptz NOT NULL,
                    decided_at timestamptz NULL
                );
                CREATE INDEX IF NOT EXISTS ix_approval_requests_proposal_status
                    ON approval_requests (proposal_id, status);
                """);

            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS notifications (
                    id uuid PRIMARY KEY,
                    user_id uuid NOT NULL,
                    title text NOT NULL,
                    body text NOT NULL,
                    kind text NULL,
                    related_id uuid NULL,
                    read boolean NOT NULL DEFAULT false,
                    created_at timestamptz NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_notifications_user_read
                    ON notifications (user_id, read, created_at DESC);
                """);

            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS seed_meta (
                    key text PRIMARY KEY,
                    value text NOT NULL,
                    updated_at timestamptz NOT NULL
                );
                """);

            await db.Database.ExecuteSqlRawAsync("""
                ALTER TABLE businesses ADD COLUMN IF NOT EXISTS blurb text;
                ALTER TABLE brands ADD COLUMN IF NOT EXISTS blurb text;
                """);
        }

        /// <summary>
        /// Fills business/brand placeholder blurbs and refreshes known product blurbs.
        /// Does not insert new rows.
        /// </summary>
        private static async Task EnsureBlurbsAsync(AppDbContext db, DateTimeOffset now)
        {
            var business = await db.Businesses.FirstOrDefaultAsync(b => b.Slug == "house-of-pianos");
            if (business != null && business.Blurb != CatalogCopy.HouseOfPianosBlurb)
            {
                business.Blurb = CatalogCopy.HouseOfPianosBlurb;
                business.UpdatedAt = now;
            }

            var brands = await db.Brands.ToListAsync();
            foreach (var brand in brands)
            {
                if (CatalogCopy.BrandBlurbs.TryGetValue(brand.Name, out var brandBlurb) &&
                    brand.Blurb != brandBlurb)
                {
                    brand.Blurb = brandBlurb;
                    brand.UpdatedAt = now;
                }
            }

            var products = await db.Products.ToListAsync();
            foreach (var product in products)
            {
                if (CatalogCopy.ProductBlurbs.TryGetValue(product.Model, out var productBlurb) &&
                    product.Blurb != productBlurb)
                {
                    product.Blurb = productBlurb;
                    product.UpdatedAt = now;
                }
            }

            await db.SaveChangesAsync();
        }

        private record SeedProduct(
            string Model,
            string CategorySlug,
            decimal Amount,
            string Tagline,
            string Blurb,
            string[] Features,
            string DimensionsJson);
    }
}
