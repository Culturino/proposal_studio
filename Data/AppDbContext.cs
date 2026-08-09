using Microsoft.EntityFrameworkCore;
using ProposalStudio.Models;

namespace ProposalStudio.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options)
        {
        }

        public DbSet<Business> Businesses { get; set; }
        public DbSet<Brand> Brands { get; set; }
        public DbSet<ProductCategory> ProductCategories { get; set; }
        public DbSet<Product> Products { get; set; }
        public DbSet<Price> Prices { get; set; }

        public DbSet<Addon> Addons { get; set; }
        public DbSet<Client> Clients { get; set; }

        public DbSet<User> Users { get; set; }
        public DbSet<Template> Templates { get; set; }
        public DbSet<Proposal> Proposals { get; set; }
        public DbSet<ProposalItem> ProposalItems { get; set; }
        public DbSet<GovernanceSettings> GovernanceSettings { get; set; }
        public DbSet<ShareLink> ShareLinks { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Business>(entity =>
            {
                entity.ToTable("businesses");

                entity.HasKey(e => e.Id);

                entity.Property(e => e.Id).HasColumnName("id");
                entity.Property(e => e.Name).HasColumnName("name");
                entity.Property(e => e.Slug).HasColumnName("slug");
                entity.Property(e => e.ReferencePrefix).HasColumnName("reference_prefix");
                entity.Property(e => e.Blurb).HasColumnName("blurb");
                entity.Property(e => e.BrandKitId).HasColumnName("brand_kit_id");
                entity.Property(e => e.Active).HasColumnName("active");
                entity.Property(e => e.CreatedAt).HasColumnName("created_at");
                entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            });

            modelBuilder.Entity<Brand>(entity =>
            {
                entity.ToTable("brands");

                entity.HasKey(e => e.Id);

                entity.Property(e => e.Id).HasColumnName("id");
                entity.Property(e => e.BusinessId).HasColumnName("business_id");
                entity.Property(e => e.Name).HasColumnName("name");
                entity.Property(e => e.Blurb).HasColumnName("blurb");
                entity.Property(e => e.LogoAssetId).HasColumnName("logo_asset_id");
                entity.Property(e => e.CreatedAt).HasColumnName("created_at");
                entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            });

            modelBuilder.Entity<Addon>(entity =>
            {
                entity.ToTable("addons");

                entity.HasKey(e => e.Id);

                entity.Property(e => e.Id).HasColumnName("id");
                entity.Property(e => e.BusinessId).HasColumnName("business_id");
                entity.Property(e => e.Name).HasColumnName("name");
                entity.Property(e => e.Amount).HasColumnName("price"); // has to be moved to the prices table?
                entity.Property(e => e.Currency).HasColumnName("currency").HasConversion<string>();
                entity.Property(e => e.Active).HasColumnName("active");
                entity.Property(e => e.CreatedAt).HasColumnName("created_at");
                entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            });

            modelBuilder.Entity<ProductCategory>(entity =>
            {
                entity.ToTable("product_categories");

                entity.HasKey(e => e.Id);

                entity.Property(e => e.Id).HasColumnName("id");
                entity.Property(e => e.BusinessId).HasColumnName("business_id");
                entity.Property(e => e.Name).HasColumnName("name");
                entity.Property(e => e.Slug).HasColumnName("slug");
                entity.Property(e => e.Active).HasColumnName("active");
                entity.Property(e => e.SortOrder).HasColumnName("sort_order");
                entity.Property(e => e.CreatedAt).HasColumnName("created_at");
                entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            });

            modelBuilder.Entity<Product>(entity =>
            {
                entity.ToTable("products");

                entity.HasKey(e => e.Id);

                entity.Property(e => e.Id).HasColumnName("id");
                entity.Property(e => e.BrandId).HasColumnName("brand_id");
                entity.Property(e => e.CategoryId).HasColumnName("category_id");
                entity.Property(e => e.Model).HasColumnName("model");
                entity.Property(e => e.Dimensions).HasColumnName("dimensions").HasColumnType("jsonb");
                entity.Property(e => e.Features).HasColumnName("features");
                entity.Property(e => e.Blurb).HasColumnName("blurb");
                entity.Property(e => e.Tagline).HasColumnName("tagline");
                entity.Property(e => e.Finishes).HasColumnName("finishes");
                entity.Property(e => e.HeroImageId).HasColumnName("hero_image_id");
                entity.Property(e => e.GalleryImageIds).HasColumnName("gallery_image_ids");
                entity.Property(e => e.DefaultIncludes).HasColumnName("default_includes");
                entity.Property(e => e.DefaultExcludes).HasColumnName("default_excludes");
                entity.Property(e => e.AvailabilityNote).HasColumnName("availability_note");
                entity.Property(e => e.Status).HasColumnName("status").HasConversion<string>();
                entity.Property(e => e.CreatedAt).HasColumnName("created_at");
                entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            });

            modelBuilder.Entity<Price>(entity =>
            {
                entity.ToTable("prices");

                entity.HasKey(e => e.Id);

                entity.Property(e => e.Id).HasColumnName("id");
                entity.Property(e => e.ProductId).HasColumnName("product_id");
                entity.Property(e => e.Currency).HasColumnName("currency").HasConversion<string>();
                entity.Property(e => e.Finish).HasColumnName("finish");
                entity.Property(e => e.Amount).HasColumnName("amount");
                entity.Property(e => e.ValidFrom).HasColumnName("valid_from");
                entity.Property(e => e.Source).HasColumnName("source");
                entity.Property(e => e.CreatedAt).HasColumnName("created_at");
                entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            });

            modelBuilder.Entity<Client>(entity =>
            {
                entity.ToTable("clients");

                entity.HasKey(e => e.Id);

                entity.Property(e => e.Id).HasColumnName("id");
                entity.Property(e => e.BusinessId).HasColumnName("business_id");
                entity.Property(e => e.Name).HasColumnName("name");
                entity.Property(e => e.Email).HasColumnName("email");
                entity.Property(e => e.Phone).HasColumnName("phone");
                entity.Property(e => e.Notes).HasColumnName("notes");
                entity.Property(e => e.CreatedBy).HasColumnName("created_by");
                entity.Property(e => e.CreatedAt).HasColumnName("created_at");
                entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            });

            modelBuilder.Entity<User>(entity =>
            {
                entity.ToTable("users");

                entity.HasKey(e => e.Id);

                entity.Property(e => e.Id).HasColumnName("id");
                entity.Property(e => e.BusinessId).HasColumnName("business_id");
                entity.Property(e => e.Name).HasColumnName("name");
                entity.Property(e => e.Email).HasColumnName("email");
                entity.Property(e => e.Phone).HasColumnName("phone");
                entity.Property(e => e.PasswordHash).HasColumnName("password_hash");
                entity.Property(e => e.Role).HasColumnName("role").HasConversion<string>();
                entity.Property(e => e.Active).HasColumnName("active");
                entity.Property(e => e.TwoFactorEnabled).HasColumnName("two_factor_enabled");
                entity.Property(e => e.CreatedAt).HasColumnName("created_at");
                entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            });

            modelBuilder.Entity<Template>(entity =>
            {
                entity.ToTable("templates");

                entity.HasKey(e => e.Id);

                entity.Property(e => e.Id).HasColumnName("id");
                entity.Property(e => e.BusinessId).HasColumnName("business_id");
                entity.Property(e => e.Key).HasColumnName("key");
                entity.Property(e => e.Name).HasColumnName("name");
                entity.Property(e => e.Version).HasColumnName("version");
                entity.Property(e => e.PageSchema).HasColumnName("page_schema").HasColumnType("jsonb");
                entity.Property(e => e.StylingLocked).HasColumnName("styling_locked");
                entity.Property(e => e.Active).HasColumnName("active");
                entity.Property(e => e.CreatedAt).HasColumnName("created_at");
                entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            });

            modelBuilder.Entity<Proposal>(entity =>
            {
                entity.ToTable("proposals");

                entity.HasKey(e => e.Id);

                entity.Property(e => e.Id).HasColumnName("id");
                entity.Property(e => e.BusinessId).HasColumnName("business_id");
                entity.Property(e => e.ReferenceNumber).HasColumnName("reference_number");
                entity.Property(e => e.Reference).HasColumnName("reference");
                entity.Property(e => e.TemplateId).HasColumnName("template_id");
                entity.Property(e => e.TemplateVersion).HasColumnName("template_version");
                entity.Property(e => e.ClientId).HasColumnName("client_id");
                entity.Property(e => e.AdvisorId).HasColumnName("advisor_id");
                entity.Property(e => e.Currency).HasColumnName("currency").HasConversion<string>();
                entity.Property(e => e.VatMode).HasColumnName("vat_mode").HasConversion<string>();
                entity.Property(e => e.ValidityDays).HasColumnName("validity_days");
                entity.Property(e => e.Status).HasColumnName("status").HasConversion<string>();
                entity.Property(e => e.PriceTotal).HasColumnName("price_total");
                entity.Property(e => e.CreatedAt).HasColumnName("created_at");
                entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
                entity.Property(e => e.SentAt).HasColumnName("sent_at");
                entity.Property(e => e.ExpiresAt).HasColumnName("expires_at");
                entity.Property(e => e.Snapshot).HasColumnName("snapshot").HasColumnType("jsonb");
                entity.Property(e => e.PdfUrl).HasColumnName("pdf_url");
            });

            modelBuilder.Entity<ProposalItem>(entity =>
            {
                entity.ToTable("proposal_items");

                entity.HasKey(e => e.Id);

                entity.Property(e => e.Id).HasColumnName("id");
                entity.Property(e => e.ProposalId).HasColumnName("proposal_id");
                entity.Property(e => e.ProductId).HasColumnName("product_id");
                entity.Property(e => e.Finish).HasColumnName("finish");
                entity.Property(e => e.Qty).HasColumnName("qty");
                entity.Property(e => e.UnitPrice).HasColumnName("unit_price");
                entity.Property(e => e.PriceOverride).HasColumnName("price_override");
                entity.Property(e => e.Included).HasColumnName("included");
                entity.Property(e => e.Excluded).HasColumnName("excluded");
                entity.Property(e => e.Addons).HasColumnName("addons").HasColumnType("jsonb");
                entity.Property(e => e.CreatedAt).HasColumnName("created_at");
                entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            });

            modelBuilder.Entity<GovernanceSettings>(entity =>
            {
                entity.ToTable("governance_settings");

                entity.HasKey(e => e.Id);

                entity.Property(e => e.Id).HasColumnName("id");
                entity.Property(e => e.BusinessId).HasColumnName("business_id");
                entity.Property(e => e.DiscountFloorPercent).HasColumnName("discount_floor_percent");
                entity.Property(e => e.HighValueThreshold).HasColumnName("high_value_threshold");
                entity.Property(e => e.VatDefaultMode).HasColumnName("vat_default_mode");
                entity.Property(e => e.AllowPublicPrices).HasColumnName("allow_public_prices");
                entity.Property(e => e.CreatedAt).HasColumnName("created_at");
                entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            });

            modelBuilder.Entity<ShareLink>(entity =>
            {
                entity.ToTable("share_links");

                entity.HasKey(e => e.Id);

                entity.Property(e => e.Id).HasColumnName("id");
                entity.Property(e => e.ProposalId).HasColumnName("proposal_id");
                entity.Property(e => e.Token).HasColumnName("token");
                entity.Property(e => e.CreatedAt).HasColumnName("created_at");
                entity.Property(e => e.ExpiresAt).HasColumnName("expires_at");
                entity.Property(e => e.Revoked).HasColumnName("revoked");

                entity.HasIndex(e => e.Token).IsUnique();
            });
        }
    }
}