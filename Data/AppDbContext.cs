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
                entity.Property(e => e.LogoAssetId).HasColumnName("logo_asset_id");
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
        }
    }
}