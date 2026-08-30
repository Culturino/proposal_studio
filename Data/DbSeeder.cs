using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProposalStudio.Models;

namespace ProposalStudio.Data
{
    /// <summary>
    /// Brings a database up to the content the application needs, in three tiers.
    ///
    /// Reference rows (business, brands, categories, template, governance) are re-created
    /// whenever they are missing, because nothing works without them. Catalog rows (products,
    /// prices, addons) are seeded once and then left alone, so a product an admin deletes stays
    /// deleted while a product added to a later release still lands. Users are either the demo
    /// accounts or a single bootstrap administrator, never both.
    ///
    /// Schema is not created here — that is the migrations' job. Run them first.
    /// </summary>
    public static class DbSeeder
    {
        public static async Task SeedAsync(AppDbContext db, SeedOptions options, ILogger logger)
        {
            var now = DateTimeOffset.UtcNow;
            var ledger = await LoadLedgerAsync(db);

            var business = await EnsureBusinessAsync(db, now);
            var brands = await EnsureBrandsAsync(db, business, now);
            var categories = await EnsureCategoriesAsync(db, business, now);
            await EnsureTemplateAsync(db, business, now);
            await EnsureGovernanceAsync(db, business, now);

            if (options.SeedCatalog)
                await SeedCatalogAsync(db, ledger, business, brands, categories, now);
            await SeedUsersAsync(db, ledger, business, options, logger, now);

            await RefreshBlurbsAsync(db, now);
            await SaveLedgerAsync(db, ledger, now);
        }

        // ---------------------------------------------------------------- reference tier

        private static async Task<Business> EnsureBusinessAsync(AppDbContext db, DateTimeOffset now)
        {
            var business = await db.Businesses.FirstOrDefaultAsync(b => b.Slug == "house-of-pianos");
            if (business != null)
                return business;

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
            return business;
        }

        private static async Task<Dictionary<string, Brand>> EnsureBrandsAsync(
            AppDbContext db, Business business, DateTimeOffset now)
        {
            var names = new[] { "Steinway & Sons", "Blüthner", "Boston", "Essex", "Kurzweil" };
            var existing = await db.Brands
                .Where(b => b.BusinessId == business.Id)
                .ToDictionaryAsync(b => b.Name, StringComparer.OrdinalIgnoreCase);

            foreach (var name in names)
            {
                if (existing.ContainsKey(name))
                    continue;

                var brand = new Brand
                {
                    Id = Guid.NewGuid(),
                    BusinessId = business.Id,
                    Name = name,
                    Blurb = CatalogCopy.BrandBlurbs.GetValueOrDefault(name),
                    CreatedAt = now,
                    UpdatedAt = now
                };
                db.Brands.Add(brand);
                existing[name] = brand;
            }

            await db.SaveChangesAsync();
            return existing;
        }

        private static async Task<Dictionary<string, ProductCategory>> EnsureCategoriesAsync(
            AppDbContext db, Business business, DateTimeOffset now)
        {
            var defs = new (string Name, string Slug, int Order)[]
            {
                ("Concert Grand", "concert-grand", 1),
                ("Grand", "grand", 2),
                ("Baby Grand", "baby-grand", 3),
                ("Upright", "upright", 4),
                ("Digital", "digital", 5)
            };

            var existing = await db.ProductCategories
                .Where(c => c.BusinessId == business.Id)
                .ToDictionaryAsync(c => c.Slug, StringComparer.OrdinalIgnoreCase);

            foreach (var (name, slug, order) in defs)
            {
                if (existing.ContainsKey(slug))
                    continue;

                var category = new ProductCategory
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
                db.ProductCategories.Add(category);
                existing[slug] = category;
            }

            await db.SaveChangesAsync();
            return existing;
        }

        private static async Task EnsureTemplateAsync(AppDbContext db, Business business, DateTimeOffset now)
        {
            var exists = await db.Templates.AnyAsync(
                t => t.BusinessId == business.Id && t.Key == "piano_luxury" && t.Version == 1);
            if (exists)
                return;

            db.Templates.Add(new Template
            {
                Id = Guid.NewGuid(),
                BusinessId = business.Id,
                Key = "piano_luxury",
                Name = "Piano — Luxury",
                Version = 1,
                PageSchema = JsonDocument.Parse(
                    """{"pages":["cover","instrument","gallery","specs","investment","closing"]}"""),
                StylingLocked = true,
                Active = true,
                CreatedAt = now,
                UpdatedAt = now
            });
            await db.SaveChangesAsync();
        }

        private static async Task EnsureGovernanceAsync(AppDbContext db, Business business, DateTimeOffset now)
        {
            if (await db.GovernanceSettings.AnyAsync(g => g.BusinessId == business.Id))
                return;

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
            await db.SaveChangesAsync();
        }

        // ------------------------------------------------------------------ catalog tier

        private static async Task SeedCatalogAsync(
            AppDbContext db,
            SeedLedger ledger,
            Business business,
            IReadOnlyDictionary<string, Brand> brands,
            IReadOnlyDictionary<string, ProductCategory> categories,
            DateTimeOffset now)
        {
            foreach (var seed in CatalogProducts)
            {
                if (!brands.TryGetValue(seed.Brand, out var brand) ||
                    !categories.TryGetValue(seed.CategorySlug, out var category))
                    continue;

                var key = $"product:{seed.Brand}|{seed.Model}";
                var present = await db.Products.AnyAsync(p => p.BrandId == brand.Id && p.Model == seed.Model);

                if (!ledger.ShouldSeed(key, present))
                    continue;

                var product = new Product
                {
                    Id = Guid.NewGuid(),
                    BrandId = brand.Id,
                    CategoryId = category.Id,
                    Model = seed.Model,
                    Dimensions = JsonDocument.Parse(seed.DimensionsJson),
                    Features = seed.Features,
                    Blurb = seed.Blurb,
                    Tagline = seed.Tagline,
                    Finishes = seed.Finishes,
                    DefaultIncludes = DefaultIncludes,
                    DefaultExcludes = DefaultExcludes,
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
                    Finish = seed.PriceFinish,
                    Amount = seed.Amount,
                    ValidFrom = new DateOnly(2026, 1, 1),
                    Source = seed.PriceSource,
                    CreatedAt = now,
                    UpdatedAt = now
                });
                await db.SaveChangesAsync();
            }

            foreach (var (name, amount) in CatalogAddons)
            {
                var key = $"addon:{name}";
                var present = await db.Addons.AnyAsync(a => a.BusinessId == business.Id && a.Name == name);

                if (!ledger.ShouldSeed(key, present))
                    continue;

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

            await db.SaveChangesAsync();
        }

        // -------------------------------------------------------------------- user tier

        private static async Task SeedUsersAsync(
            AppDbContext db,
            SeedLedger ledger,
            Business business,
            SeedOptions options,
            ILogger logger,
            DateTimeOffset now)
        {
            if (options.DemoUsers)
            {
                await SeedDemoUsersAsync(db, ledger, business, options, logger, now);
                return;
            }

            // Anything already there means someone can sign in; leave it alone.
            if (await db.Users.AnyAsync())
                return;

            if (string.IsNullOrWhiteSpace(options.AdminEmail) ||
                string.IsNullOrWhiteSpace(options.AdminPassword))
            {
                logger.LogWarning(
                    "No users exist and no bootstrap administrator is configured, so nobody can sign in. " +
                    "Set Seed:AdminEmail and Seed:AdminPassword (via secrets) and restart, " +
                    "or set Seed:DemoUsers to true in development.");
                return;
            }

            db.Users.Add(new User
            {
                Id = Guid.NewGuid(),
                BusinessId = business.Id,
                Name = string.IsNullOrWhiteSpace(options.AdminName) ? "Administrator" : options.AdminName!,
                Email = options.AdminEmail!.Trim(),
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(options.AdminPassword),
                Role = "admin",
                Active = true,
                TwoFactorEnabled = false,
                CreatedAt = now,
                UpdatedAt = now
            });
            await db.SaveChangesAsync();

            logger.LogWarning(
                "Created bootstrap administrator {Email} from configuration. Change this password now.",
                options.AdminEmail);
        }

        private static async Task SeedDemoUsersAsync(
            AppDbContext db, SeedLedger ledger, Business business, SeedOptions options, ILogger logger, DateTimeOffset now)
        {
            var demoUsers = options.DemoTeam
                ? new (string Name, string Email, string Role)[]
                {
                    ("Shavkat Mamadjonov", "admin@houseofpianos.ae", "admin"),
                    ("Layla Haddad", "layla@houseofpianos.ae", "manager"),
                    ("Omar Khan", "omar@houseofpianos.ae", "advisor"),
                    ("Sara Idris", "sara@houseofpianos.ae", "advisor")
                }
                : new (string Name, string Email, string Role)[]
                {
                    ("Shavkat Mamadjonov", "admin@houseofpianos.ae", "admin")
                };

            var hash = BCrypt.Net.BCrypt.HashPassword("Password123!");
            var added = 0;

            foreach (var (name, email, role) in demoUsers)
            {
                var key = $"user:{email}";
                var present = await db.Users.AnyAsync(u => u.Email == email);

                if (!ledger.ShouldSeed(key, present))
                    continue;

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
                added++;
            }

            await db.SaveChangesAsync();

            if (added > 0)
                logger.LogWarning("Seeded {Count} demo accounts with a shared, publicly known password.", added);
        }

        // ----------------------------------------------------------------------- copy

        /// <summary>
        /// Refreshes narrative copy on rows that already exist. Never inserts, so a deleted
        /// product does not reappear just because we have a blurb for it.
        /// </summary>
        private static async Task RefreshBlurbsAsync(AppDbContext db, DateTimeOffset now)
        {
            var business = await db.Businesses.FirstOrDefaultAsync(b => b.Slug == "house-of-pianos");
            if (business != null && business.Blurb != CatalogCopy.HouseOfPianosBlurb)
            {
                business.Blurb = CatalogCopy.HouseOfPianosBlurb;
                business.UpdatedAt = now;
            }

            foreach (var brand in await db.Brands.ToListAsync())
            {
                if (CatalogCopy.BrandBlurbs.TryGetValue(brand.Name, out var blurb) && brand.Blurb != blurb)
                {
                    brand.Blurb = blurb;
                    brand.UpdatedAt = now;
                }
            }

            foreach (var product in await db.Products.ToListAsync())
            {
                if (CatalogCopy.ProductBlurbs.TryGetValue(product.Model, out var blurb) && product.Blurb != blurb)
                {
                    product.Blurb = blurb;
                    product.UpdatedAt = now;
                }
            }

            await db.SaveChangesAsync();
        }

        // --------------------------------------------------------------------- ledger

        private static async Task<SeedLedger> LoadLedgerAsync(AppDbContext db)
        {
            var keys = await db.SeedMeta.Select(m => m.Key).ToListAsync();

            // A database holding content but no ledger was populated before per-item tracking
            // existed. Treat this run as adoption: record what the seed list covers without
            // inserting any of it, because we cannot tell a row that was never seeded from one
            // an admin deliberately deleted. Later releases then add only genuinely new items.
            var adopting = keys.Count == 0 &&
                (await db.Products.AnyAsync() || await db.Users.AnyAsync() || await db.Addons.AnyAsync());

            return new SeedLedger(keys, adopting);
        }

        private static async Task SaveLedgerAsync(AppDbContext db, SeedLedger ledger, DateTimeOffset now)
        {
            foreach (var key in ledger.Claimed)
            {
                db.SeedMeta.Add(new SeedMeta { Key = key, Value = "seeded", UpdatedAt = now });
            }

            if (ledger.Claimed.Count > 0)
                await db.SaveChangesAsync();
        }

        /// <summary>
        /// Remembers which seed items this database has ever been given, so the difference
        /// between "never seeded" and "seeded then deleted by an admin" survives a restart.
        /// </summary>
        private sealed class SeedLedger
        {
            private readonly HashSet<string> _known;
            private readonly bool _adopting;

            public SeedLedger(IEnumerable<string> existingKeys, bool adopting)
            {
                _known = new HashSet<string>(existingKeys, StringComparer.Ordinal);
                _adopting = adopting;
            }

            public List<string> Claimed { get; } = new();

            /// <summary>
            /// True only when the item has never been recorded, is genuinely absent, and this is
            /// not an adoption run. Every key asked about is recorded either way, so an item
            /// declined here is never reconsidered on a later boot.
            /// </summary>
            public bool ShouldSeed(string key, bool alreadyPresent)
            {
                if (!_known.Add(key))
                    return false;

                Claimed.Add(key);
                return !alreadyPresent && !_adopting;
            }
        }

        // ------------------------------------------------------------------ seed data

        private static readonly string[] DefaultIncludes =
        {
            "Delivery & white-glove installation, UAE",
            "First professional tuning, on site",
            "Adjustable artist bench",
            "12-month manufacturer warranty",
            "Piano Care Guide (English & Arabic)",
            "Lifetime advisory & service support"
        };

        private static readonly string[] DefaultExcludes =
        {
            "Ongoing tuning beyond the first visit",
            "Climate-control equipment",
            "Custom finishes & Crown Jewel veneers",
            "Inter-emirate & international freight"
        };

        private static readonly string[] SteinwayFinishes =
        {
            "Ebonised High Polish", "Ivory White", "Snow White", "Crown Jewel veneers"
        };

        private static readonly (string Name, decimal Amount)[] CatalogAddons =
        {
            ("Annual Maintenance (2 tunings/year)", 2400m),
            ("Concert artist bench, leather", 4500m),
            ("Fitted dust cover", 1200m),
            ("Dampp-Chaser climate system", 5200m)
        };

        /// <summary>Steinway 2026 AED retail; blurbs from the official Steinway model pages.</summary>
        private static readonly SeedProduct[] CatalogProducts =
        {
            new("Steinway & Sons", "Model D-274", "concert-grand", 1055670m, "Ebonised High Polish",
                "2026 retail list", "The concert standard", CatalogCopy.ProductBlurbs["Model D-274"],
                new[] { "Exclusive use of solid wood", "Continuous bent rim", "Diaphragmatic soundboard", "Hexagrip pinblock", "Laminated bridge", "Duplex scale" },
                SteinwayFinishes,
                """{"length":"274 cm","width":"158 cm","weight":"approx. 480 kg"}"""),

            new("Steinway & Sons", "Model C-227", "grand", 778470m, "Ebonised High Polish",
                "2026 retail list", "The recital grand", CatalogCopy.ProductBlurbs["Model C-227"],
                new[] { "Exclusive use of solid wood", "Continuous bent rim", "Diaphragmatic soundboard", "Hexagrip pinblock" },
                SteinwayFinishes,
                """{"length":"227 cm","width":"155 cm","weight":"approx. 425 kg"}"""),

            new("Steinway & Sons", "Model B-211", "grand", 690690m, "Ebonised High Polish",
                "2026 retail list", "“The perfect piano”", CatalogCopy.ProductBlurbs["Model B-211"],
                new[] { "Exclusive use of solid wood", "Continuous bent rim", "Diaphragmatic soundboard", "Hexagrip pinblock", "Laminated bridge", "Duplex scale" },
                SteinwayFinishes,
                """{"length":"211 cm (6′10½″)","width":"148 cm (58¼″)","weight":"approx. 345 kg","setting":"Salon · Studio · Recital"}"""),

            new("Steinway & Sons", "Model A-188", "grand", 593670m, "Ebonised High Polish",
                "2026 retail list", "The salon grand", CatalogCopy.ProductBlurbs["Model A-188"],
                new[] { "Exclusive use of solid wood", "Diaphragmatic soundboard", "Hexagrip pinblock" },
                SteinwayFinishes,
                """{"length":"188 cm","width":"146 cm","weight":"approx. 320 kg"}"""),

            new("Steinway & Sons", "Model O-180", "grand", 575190m, "Ebonised High Polish",
                "2026 retail list", "The living-room grand", CatalogCopy.ProductBlurbs["Model O-180"],
                new[] { "Exclusive use of solid wood", "Diaphragmatic soundboard", "Hexagrip pinblock" },
                SteinwayFinishes,
                """{"length":"180 cm","width":"146 cm","weight":"approx. 280 kg"}"""),

            new("Steinway & Sons", "Model M-170", "grand", 521829m, "Ebonised High Polish",
                "2026 retail list", "The studio grand", CatalogCopy.ProductBlurbs["Model M-170"],
                new[] { "Exclusive use of solid wood", "Diaphragmatic soundboard", "Hexagrip pinblock" },
                SteinwayFinishes,
                """{"length":"170 cm","width":"146 cm","weight":"approx. 275 kg"}"""),

            new("Steinway & Sons", "Model S-155", "baby-grand", 503349m, "Ebonised High Polish",
                "2026 retail list", "The baby grand", CatalogCopy.ProductBlurbs["Model S-155"],
                new[] { "Exclusive use of solid wood", "Diaphragmatic soundboard", "Hexagrip pinblock" },
                SteinwayFinishes,
                """{"length":"155 cm","width":"146 cm","weight":"approx. 255 kg"}"""),

            new("Steinway & Sons", "Model K-132", "upright", 263109m, "Ebonised High Polish",
                "2026 retail list", "The upright grand", CatalogCopy.ProductBlurbs["Model K-132"],
                new[] { "Exclusive use of solid wood", "Diaphragmatic soundboard", "Hexagrip pinblock" },
                SteinwayFinishes,
                """{"length":"132 cm (height)","width":"152 cm","weight":"approx. 295 kg"}"""),

            new("Blüthner", "Model 4", "grand", null, "Ebony Polish",
                "pending confirmation", "Aliquot stringing", CatalogCopy.ProductBlurbs["Model 4"],
                new[] { "Aliquot stringing", "Solid Saxon spruce soundboard", "Hand-notched bridges" },
                new[] { "Ebony Polish", "Walnut", "Mahogany", "White Polish" },
                """{"length":"210 cm","width":"151 cm","weight":"approx. 350 kg"}"""),
        };

        private record SeedProduct(
            string Brand,
            string Model,
            string CategorySlug,
            decimal? Amount,
            string PriceFinish,
            string PriceSource,
            string Tagline,
            string Blurb,
            string[] Features,
            string[] Finishes,
            string DimensionsJson);
    }
}
