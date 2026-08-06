using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProposalStudio.Models;

namespace ProposalStudio.Data
{
    /// <summary>
    /// Idempotent seed for Phase 1: business, brands, Steinway 2026 prices, template, governance, demo users.
    /// </summary>
    public static class DbSeeder
    {
        public static async Task SeedAsync(AppDbContext db)
        {
            await EnsureGovernanceTableAsync(db);

            var now = DateTimeOffset.UtcNow;

            var business = await db.Businesses.FirstOrDefaultAsync(b => b.Slug == "house-of-pianos");
            if (business == null)
            {
                business = new Business
                {
                    Id = Guid.NewGuid(),
                    Name = "House of Pianos",
                    Slug = "house-of-pianos",
                    ReferencePrefix = "HOP",
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

            // Steinway 2026 AED retail (Ebonised) from brief §11
            var steinway = brands["Steinway & Sons"];
            var products = new[]
            {
                new SeedProduct("Model D-274", "concert-grand", 1055670m, "The concert standard",
                    "The concert grand of the world's great stages.",
                    new[] { "Exclusive use of solid wood", "Continuous bent rim", "Diaphragmatic soundboard", "Hexagrip pinblock", "Laminated bridge", "Duplex scale" },
                    """{"length":"274 cm","width":"158 cm","weight":"approx. 480 kg"}"""),
                new SeedProduct("Model C-227", "grand", 778470m, "The recital grand",
                    "A semi-concert grand with breadth for a hall and refinement for a salon.",
                    new[] { "Exclusive use of solid wood", "Continuous bent rim", "Diaphragmatic soundboard", "Hexagrip pinblock" },
                    """{"length":"227 cm","width":"155 cm","weight":"approx. 425 kg"}"""),
                new SeedProduct("Model B-211", "grand", 690690m, "“The perfect piano”",
                    "Often called “the perfect piano.” The Model B occupies the rare middle ground between chamber intimacy and concert authority.",
                    new[] { "Exclusive use of solid wood", "Continuous bent rim", "Diaphragmatic soundboard", "Hexagrip pinblock", "Laminated bridge", "Duplex scale" },
                    """{"length":"211 cm (6′10½″)","width":"148 cm (58¼″)","weight":"approx. 345 kg","setting":"Salon · Studio · Recital"}"""),
                new SeedProduct("Model A-188", "grand", 593670m, "The salon grand",
                    "Full grand voice in a footprint made for the home.",
                    new[] { "Exclusive use of solid wood", "Diaphragmatic soundboard", "Hexagrip pinblock" },
                    """{"length":"188 cm","width":"146 cm","weight":"approx. 320 kg"}"""),
                new SeedProduct("Model O-180", "grand", 575190m, "The living-room grand",
                    "The most popular Steinway grand for the home.",
                    new[] { "Exclusive use of solid wood", "Diaphragmatic soundboard", "Hexagrip pinblock" },
                    """{"length":"180 cm","width":"146 cm","weight":"approx. 280 kg"}"""),
                new SeedProduct("Model M-170", "grand", 521829m, "The studio grand",
                    "A medium grand of remarkable warmth.",
                    new[] { "Exclusive use of solid wood", "Diaphragmatic soundboard", "Hexagrip pinblock" },
                    """{"length":"170 cm","width":"146 cm","weight":"approx. 275 kg"}"""),
                new SeedProduct("Model S-155", "baby-grand", 503349m, "The baby grand",
                    "The smallest Steinway grand — true craftsmanship scaled for the intimate room.",
                    new[] { "Exclusive use of solid wood", "Diaphragmatic soundboard", "Hexagrip pinblock" },
                    """{"length":"155 cm","width":"146 cm","weight":"approx. 255 kg"}"""),
                new SeedProduct("Model K-132", "upright", 263109m, "The upright grand",
                    "The tallest Steinway upright: grand-scale soundboard in a vertical case.",
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

            // Only create catalog rows on first seed. Never resurrect products an admin deleted.
            var steinwayIsEmpty = !await db.Products.AnyAsync(p => p.BrandId == steinway.Id);

            foreach (var sp in products)
            {
                var product = await db.Products.FirstOrDefaultAsync(p =>
                    p.BrandId == steinway.Id && p.Model == sp.Model);

                if (product == null)
                {
                    if (!steinwayIsEmpty)
                        continue;

                    product = new Product
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
                }

                // Soft-archived products keep their row so we never recreate a new Id (broken images)
                if (string.Equals(product.Status, "archived", StringComparison.OrdinalIgnoreCase))
                    continue;

                var price = await db.Prices.FirstOrDefaultAsync(p =>
                    p.ProductId == product.Id && p.Currency == "AED" && p.Finish == "Ebonised High Polish");

                if (price == null)
                {
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
                else if (price.Amount != sp.Amount)
                {
                    price.Amount = sp.Amount;
                    price.Source = "2026 retail list";
                    price.UpdatedAt = now;
                }
            }

            // Blüthner placeholder (On request) — first seed only
            var bluthner = brands["Blüthner"];
            var bluModel = await db.Products.FirstOrDefaultAsync(p => p.BrandId == bluthner.Id && p.Model == "Model 4");
            if (bluModel == null && !await db.Products.AnyAsync(p => p.BrandId == bluthner.Id))
            {
                bluModel = new Product
                {
                    Id = Guid.NewGuid(),
                    BrandId = bluthner.Id,
                    CategoryId = categories["grand"].Id,
                    Model = "Model 4",
                    Dimensions = JsonDocument.Parse("""{"length":"210 cm","width":"151 cm","weight":"approx. 350 kg"}"""),
                    Features = new[] { "Aliquot stringing", "Solid Saxon spruce soundboard", "Hand-notched bridges" },
                    Blurb = "Hand-built in Leipzig since 1853. The golden, singing Blüthner tone.",
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
                    Amount = null, // On request
                    ValidFrom = new DateOnly(2026, 1, 1),
                    Source = "pending confirmation",
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }

            // Template
            var template = await db.Templates.FirstOrDefaultAsync(t =>
                t.BusinessId == business.Id && t.Key == "piano_luxury");
            if (template == null)
            {
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
            }

            // Addons
            var addonDefs = new (string Name, decimal Amount)[]
            {
                ("Annual Maintenance (2 tunings/year)", 2400m),
                ("Concert artist bench, leather", 4500m),
                ("Fitted dust cover", 1200m),
                ("Dampp-Chaser climate system", 5200m)
            };
            foreach (var (name, amount) in addonDefs)
            {
                var exists = await db.Addons.AnyAsync(a => a.BusinessId == business.Id && a.Name == name);
                if (!exists)
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
            }

            // Governance
            var gov = await db.GovernanceSettings.FirstOrDefaultAsync(g => g.BusinessId == business.Id);
            if (gov == null)
            {
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
            }

            // Demo users (password: Password123!)
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
                var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
                if (user == null)
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
                else if (string.IsNullOrWhiteSpace(user.PasswordHash))
                {
                    user.PasswordHash = hash;
                    user.UpdatedAt = now;
                }
            }

            await db.SaveChangesAsync();
        }

        private static async Task EnsureGovernanceTableAsync(AppDbContext db)
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
