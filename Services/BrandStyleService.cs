using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProposalStudio.Data;
using ProposalStudio.Models;
using SkiaSharp;

namespace ProposalStudio.Services
{
    public class BrandStyleService
    {
        public const string DefaultLogoFile = "hop-logo.png";
        public const long MaxLogoBytes = 8 * 1024 * 1024;

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private readonly AppDbContext _db;
        private readonly ObjectMediaStore _media;
        private readonly string _brandDir;

        public BrandStyleService(AppDbContext db, IWebHostEnvironment env, ObjectMediaStore media)
        {
            _db = db;
            _media = media;
            var webRoot = env.WebRootPath
                ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            _brandDir = Path.Combine(webRoot, "brand");
        }

        public async Task<BrandKit?> GetKitAsync(Guid businessId) =>
            await _db.BrandKits.FirstOrDefaultAsync(k => k.BusinessId == businessId);

        public async Task<PdfPalette> GetCurrentAsync(Guid businessId)
        {
            var kit = await GetKitAsync(businessId);
            return kit == null ? PdfPalette.Defaults : PdfPalette.FromJson(kit.Colors);
        }

        public static string PublicLogoPath(string? logoFile) =>
            $"/brand/{SafeFileName(logoFile) ?? DefaultLogoFile}";

        public async Task<(PdfPalette Palette, int TemplateVersion)> SaveAsync(
            Guid businessId, Dictionary<string, string> colors)
        {
            var palette = PdfPalette.FromMap(colors);
            var json = JsonSerializer.SerializeToDocument(palette.ToMap(), JsonOpts);

            var kit = await EnsureKitAsync(businessId);
            kit.Colors = json;
            kit.UpdatedAt = DateTimeOffset.UtcNow;

            var version = await VersionTemplatesAsync(businessId, palette, kit.LogoFile);
            await _db.SaveChangesAsync();
            return (palette, version);
        }

        public async Task<(string FileName, string Url, int TemplateVersion)> SaveLogoAsync(
            Guid businessId, Stream upload)
        {
            var fileName = await WriteLogoAsync(upload);
            var kit = await EnsureKitAsync(businessId);
            kit.LogoFile = fileName;
            kit.UpdatedAt = DateTimeOffset.UtcNow;

            var palette = PdfPalette.FromJson(kit.Colors);
            var version = await VersionTemplatesAsync(businessId, palette, fileName);
            await _db.SaveChangesAsync();
            return (fileName, PublicLogoPath(fileName), version);
        }

        /// <summary>
        /// Snapshots palette + logo onto a new template row and retires the previous version.
        /// Existing proposals keep their TemplateId (old row).
        /// </summary>
        public async Task<int> VersionTemplatesAsync(Guid businessId, PdfPalette palette, string? logoFile)
        {
            var now = DateTimeOffset.UtcNow;
            var latest = await _db.Templates
                .Where(t => t.BusinessId == businessId && t.Active)
                .ToListAsync();

            var versionCeiling = await _db.Templates
                .Where(t => t.BusinessId == businessId)
                .GroupBy(t => t.Key)
                .Select(g => new { Key = g.Key, Max = g.Max(t => t.Version) })
                .ToDictionaryAsync(x => x.Key, x => x.Max);

            var maxVersion = 1;
            foreach (var current in latest)
            {
                var existingPalette = PdfPalette.FromSchema(current.PageSchema);
                var existingLogo = LogoFromSchema(current.PageSchema);
                if (Same(existingPalette, palette) &&
                    SameLogo(existingLogo, logoFile) &&
                    SchemaHasColors(current.PageSchema))
                {
                    maxVersion = Math.Max(maxVersion, current.Version);
                    continue;
                }

                current.Active = false;
                current.UpdatedAt = now;

                var nextNumber = versionCeiling.TryGetValue(current.Key, out var ceiling)
                    ? ceiling + 1
                    : current.Version + 1;
                versionCeiling[current.Key] = nextNumber;

                var next = new Template
                {
                    Id = Guid.NewGuid(),
                    BusinessId = current.BusinessId,
                    Key = current.Key,
                    Name = current.Name,
                    Version = nextNumber,
                    PageSchema = MergeSchema(current.PageSchema, palette, logoFile),
                    StylingLocked = current.StylingLocked,
                    Active = true,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                _db.Templates.Add(next);
                maxVersion = Math.Max(maxVersion, next.Version);
            }

            return maxVersion;
        }

        public static JsonDocument MergeSchema(
            JsonDocument? existing,
            PdfPalette palette,
            string? logoFile = null,
            string? rendererKey = null)
        {
            var renderer = ProposalRendererCatalog.Normalize(
                rendererKey ?? RendererFromSchema(existing));
            var info = ProposalRendererCatalog.Require(renderer);

            JsonElement? pages = null;
            if (existing != null && existing.RootElement.TryGetProperty("pages", out var p))
                pages = p.Clone();

            var logo = string.IsNullOrWhiteSpace(logoFile)
                ? LogoFromSchema(existing)
                : SafeFileName(logoFile);

            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteString("renderer", renderer);
                writer.WritePropertyName("pages");
                if (pages.HasValue)
                    pages.Value.WriteTo(writer);
                else
                {
                    writer.WriteStartArray();
                    foreach (var name in info.Pages)
                        writer.WriteStringValue(name);
                    writer.WriteEndArray();
                }

                writer.WritePropertyName("colors");
                writer.WriteRawValue(JsonSerializer.Serialize(palette.ToMap(), JsonOpts));

                if (!string.IsNullOrWhiteSpace(logo))
                    writer.WriteString("logo", logo);

                writer.WriteEndObject();
            }

            return JsonDocument.Parse(stream.ToArray());
        }

        public static string? RendererFromSchema(JsonDocument? schema)
        {
            if (schema == null) return null;
            if (!schema.RootElement.TryGetProperty("renderer", out var renderer) ||
                renderer.ValueKind != JsonValueKind.String)
                return null;
            var value = renderer.GetString();
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        public static string? LogoFromSchema(JsonDocument? schema)
        {
            if (schema == null) return null;
            if (!schema.RootElement.TryGetProperty("logo", out var logo) ||
                logo.ValueKind != JsonValueKind.String)
                return null;
            return SafeFileName(logo.GetString());
        }

        public static bool SchemaHasColors(JsonDocument? schema) =>
            schema != null &&
            schema.RootElement.TryGetProperty("colors", out var c) &&
            c.ValueKind == JsonValueKind.Object &&
            c.EnumerateObject().Any();

        private async Task<BrandKit> EnsureKitAsync(Guid businessId)
        {
            var kit = await _db.BrandKits.FirstOrDefaultAsync(k => k.BusinessId == businessId);
            if (kit != null)
            {
                var business = await _db.Businesses.FirstOrDefaultAsync(b => b.Id == businessId);
                if (business != null)
                    business.BrandKitId = kit.Id;
                return kit;
            }

            kit = new BrandKit
            {
                Id = Guid.NewGuid(),
                BusinessId = businessId,
                Colors = JsonSerializer.SerializeToDocument(PdfPalette.DefaultMap(), JsonOpts),
                UpdatedAt = DateTimeOffset.UtcNow
            };
            _db.BrandKits.Add(kit);

            var house = await _db.Businesses.FirstOrDefaultAsync(b => b.Id == businessId);
            if (house != null)
                house.BrandKitId = kit.Id;

            return kit;
        }

        private async Task<string> WriteLogoAsync(Stream upload)
        {
            using var buffer = new MemoryStream();
            await upload.CopyToAsync(buffer);
            buffer.Position = 0;

            using var bitmap = SKBitmap.Decode(buffer);
            if (bitmap == null)
                throw new InvalidOperationException("That file could not be read as an image.");

            Directory.CreateDirectory(_brandDir);
            var fileName = $"logo-{Guid.NewGuid():N}.png";
            var dest = Path.Combine(_brandDir, fileName);
            using var image = SKImage.FromBitmap(bitmap);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            await using (var file = File.Create(dest))
            {
                encoded.SaveTo(file);
            }
            await _media.UploadFileAsync($"brand/{fileName}", dest);
            return fileName;
        }

        private static string? SafeFileName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var name = Path.GetFileName(value.Trim());
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }

        private static bool SameLogo(string? a, string? b) =>
            string.Equals(
                SafeFileName(a) ?? DefaultLogoFile,
                SafeFileName(b) ?? DefaultLogoFile,
                StringComparison.OrdinalIgnoreCase);

        private static bool Same(PdfPalette a, PdfPalette b) =>
            string.Equals(a.Ink, b.Ink, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(a.Ink2, b.Ink2, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(a.Paper, b.Paper, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(a.Cream, b.Cream, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(a.Gold, b.Gold, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(a.GoldBright, b.GoldBright, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(a.Text, b.Text, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(a.Muted, b.Muted, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(a.Line, b.Line, StringComparison.OrdinalIgnoreCase);
    }
}
