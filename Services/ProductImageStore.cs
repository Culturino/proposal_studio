using System.Text.RegularExpressions;
using SkiaSharp;

namespace ProposalStudio.Services
{
    /// <summary>
    /// Owns the product image files under wwwroot/images/products.
    ///
    ///   {id}-main-profile.png     full profile (hero), full resolution — used by the PDF
    ///   {id}-additional-1.png     additional view
    ///   {id}-additional-2.png     additional view
    ///   thumbs/{id}-*.png         downscaled copies for catalog and admin listings
    ///
    /// Older builds wrote {id}.png / {id}-2.png / {id}-3.png. Those names are still
    /// resolved on read and promoted to the names above the first time the store runs.
    ///
    /// Uploads are decoded and re-encoded rather than written through, so whatever lands on
    /// disk is a real image and not merely a file that was named like one.
    /// </summary>
    public class ProductImageStore
    {
        /// <summary>Longest edge of a generated thumbnail, in pixels.</summary>
        private const int ThumbnailSize = 400;

        /// <summary>Largest upload accepted, before decoding.</summary>
        public const long MaxUploadBytes = 25 * 1024 * 1024;

        private static readonly int[] ValidSlots = { 1, 2, 3 };

        private static readonly Regex ProductFileName = new(
            @"^(?<id>[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})"
            + @"(?:-(?:2|3|main-profile|additional-1|additional-2))?\.png$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly string _root;
        private readonly ILogger<ProductImageStore> _logger;

        public ProductImageStore(IWebHostEnvironment environment, ILogger<ProductImageStore> logger)
        {
            var webRoot = environment.WebRootPath
                ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");

            _root = Path.Combine(webRoot, "images", "products");
            _logger = logger;
        }

        public static bool IsValidSlot(int slot) => ValidSlots.Contains(slot);

        public static string SlotLabel(int slot) => slot switch
        {
            1 => "main-profile",
            2 => "additional-1",
            3 => "additional-2",
            _ => throw new ArgumentOutOfRangeException(nameof(slot), "Slot must be 1, 2 or 3.")
        };

        public static string FileName(Guid productId, int slot) =>
            $"{productId}-{SlotLabel(slot)}.png";

        private static string LegacyFileName(Guid productId, int slot) =>
            slot == 1 ? $"{productId}.png" : $"{productId}-{slot}.png";

        /// <summary>Where a new write goes. Prefer <see cref="ResolveFullPath"/> when reading.</summary>
        public string FullPath(Guid productId, int slot) =>
            Path.Combine(_root, FileName(productId, slot));

        public string ThumbnailPath(Guid productId, int slot) =>
            Path.Combine(_root, "thumbs", FileName(productId, slot));

        public string? ResolveFullPath(Guid productId, int slot)
        {
            var canonical = FullPath(productId, slot);
            if (File.Exists(canonical)) return canonical;

            var legacy = Path.Combine(_root, LegacyFileName(productId, slot));
            return File.Exists(legacy) ? legacy : null;
        }

        public bool Exists(Guid productId, int slot) => ResolveFullPath(productId, slot) != null;

        /// <summary>
        /// Decodes the upload, writes the full-resolution PNG and its thumbnail, and reports
        /// whether the image carries transparency. The cover page composites the hero over
        /// black, so an opaque image renders as a visible rectangle — the caller surfaces that
        /// as a warning rather than refusing the upload.
        /// </summary>
        public async Task<ImageSaveResult> SaveAsync(Guid productId, int slot, Stream upload)
        {
            using var buffer = new MemoryStream();
            await upload.CopyToAsync(buffer);
            buffer.Position = 0;

            using var bitmap = SKBitmap.Decode(buffer);
            if (bitmap == null)
                return ImageSaveResult.Rejected("That file could not be read as an image.");

            Directory.CreateDirectory(_root);
            Directory.CreateDirectory(Path.Combine(_root, "thumbs"));

            var dest = FullPath(productId, slot);
            using (var image = SKImage.FromBitmap(bitmap))
            using (var encoded = image.Encode(SKEncodedImageFormat.Png, 100))
            await using (var file = File.Create(dest))
            {
                encoded.SaveTo(file);
            }

            WriteThumbnail(bitmap, ThumbnailPath(productId, slot));
            DeleteLegacy(productId, slot);

            return ImageSaveResult.Saved(bitmap.Width, bitmap.Height, HasTransparency(bitmap));
        }

        public void Delete(Guid productId, int slot)
        {
            foreach (var path in new[]
            {
                FullPath(productId, slot),
                Path.Combine(_root, LegacyFileName(productId, slot)),
                ThumbnailPath(productId, slot),
                Path.Combine(_root, "thumbs", LegacyFileName(productId, slot))
            })
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        /// <summary>
        /// Returns the thumbnail path, generating it from the full-resolution file when it is
        /// missing. This is what backfills images uploaded before thumbnails existed, so no
        /// separate migration step is needed.
        /// </summary>
        public string? ResolveThumbnail(Guid productId, int slot)
        {
            var full = ResolveFullPath(productId, slot);
            if (full == null)
                return null;

            var thumb = ThumbnailPath(productId, slot);
            var legacyThumb = Path.Combine(_root, "thumbs", LegacyFileName(productId, slot));

            if (File.Exists(thumb) && File.GetLastWriteTimeUtc(thumb) >= File.GetLastWriteTimeUtc(full))
                return thumb;

            if (File.Exists(legacyThumb) && File.GetLastWriteTimeUtc(legacyThumb) >= File.GetLastWriteTimeUtc(full))
                return legacyThumb;

            using var bitmap = SKBitmap.Decode(full);
            if (bitmap == null)
                return null;

            Directory.CreateDirectory(Path.GetDirectoryName(thumb)!);
            WriteThumbnail(bitmap, thumb);
            return thumb;
        }

        /// <summary>
        /// Promotes leftover {id}.png / {id}-2.png names, then copies each hero into empty
        /// additional slots so existing catalog rows have three views without a manual re-upload.
        /// Already-filled additional slots are left alone.
        /// </summary>
        public void AdoptExisting()
        {
            Directory.CreateDirectory(_root);
            Directory.CreateDirectory(Path.Combine(_root, "thumbs"));

            var ids = DiscoverProductIds();
            var copied = 0;
            var renamed = 0;

            foreach (var id in ids)
            {
                foreach (var slot in ValidSlots)
                {
                    if (PromoteSlot(id, slot))
                        renamed++;
                }

                var hero = ResolveFullPath(id, 1);
                if (hero == null)
                    continue;

                foreach (var slot in new[] { 2, 3 })
                {
                    if (Exists(id, slot))
                        continue;

                    File.Copy(hero, FullPath(id, slot));

                    var heroThumb = ResolveThumbnail(id, 1);
                    if (heroThumb != null && File.Exists(heroThumb))
                        File.Copy(heroThumb, ThumbnailPath(id, slot), overwrite: true);

                    copied++;
                }
            }

            if (renamed > 0 || copied > 0)
            {
                _logger.LogInformation(
                    "Product images: renamed {Renamed} files to the slot names, copied the hero into {Copied} empty additional slots.",
                    renamed, copied);
            }
        }

        private bool PromoteSlot(Guid productId, int slot)
        {
            var canonical = FullPath(productId, slot);
            var legacy = Path.Combine(_root, LegacyFileName(productId, slot));
            var moved = MoveIfNeeded(legacy, canonical);

            var canonicalThumb = ThumbnailPath(productId, slot);
            var legacyThumb = Path.Combine(_root, "thumbs", LegacyFileName(productId, slot));
            moved = MoveIfNeeded(legacyThumb, canonicalThumb) || moved;

            return moved;
        }

        private static bool MoveIfNeeded(string from, string to)
        {
            if (!File.Exists(from) || string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
                return false;

            if (File.Exists(to))
            {
                File.Delete(from);
                return true;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Move(from, to);
            return true;
        }

        private void DeleteLegacy(Guid productId, int slot)
        {
            foreach (var path in new[]
            {
                Path.Combine(_root, LegacyFileName(productId, slot)),
                Path.Combine(_root, "thumbs", LegacyFileName(productId, slot))
            })
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        private HashSet<Guid> DiscoverProductIds()
        {
            var ids = new HashSet<Guid>();
            if (!Directory.Exists(_root))
                return ids;

            foreach (var file in Directory.EnumerateFiles(_root, "*.png"))
            {
                var match = ProductFileName.Match(Path.GetFileName(file));
                if (match.Success && Guid.TryParse(match.Groups["id"].Value, out var id))
                    ids.Add(id);
            }

            return ids;
        }

        private static void WriteThumbnail(SKBitmap source, string destination)
        {
            var scale = Math.Min(
                1f,
                (float)ThumbnailSize / Math.Max(source.Width, source.Height));

            var width = Math.Max(1, (int)(source.Width * scale));
            var height = Math.Max(1, (int)(source.Height * scale));

            using var resized = source.Resize(
                new SKImageInfo(width, height), SKFilterQuality.High);

            if (resized == null)
                return;

            using var image = SKImage.FromBitmap(resized);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 90);
            using var file = File.Create(destination);
            encoded.SaveTo(file);
        }

        /// <summary>
        /// Samples the image for any non-opaque pixel. A full scan of a 13 MP upload is wasteful
        /// when a cut-out's transparency covers large regions, so this steps across the grid.
        /// </summary>
        private static bool HasTransparency(SKBitmap bitmap)
        {
            if (bitmap.AlphaType == SKAlphaType.Opaque)
                return false;

            var stepX = Math.Max(1, bitmap.Width / 128);
            var stepY = Math.Max(1, bitmap.Height / 128);

            for (var y = 0; y < bitmap.Height; y += stepY)
            {
                for (var x = 0; x < bitmap.Width; x += stepX)
                {
                    if (bitmap.GetPixel(x, y).Alpha < 255)
                        return true;
                }
            }

            return false;
        }
    }

    public record ImageSaveResult(
        bool Success,
        string? Error,
        int Width,
        int Height,
        bool HasTransparency)
    {
        public static ImageSaveResult Rejected(string error) =>
            new(false, error, 0, 0, false);

        public static ImageSaveResult Saved(int width, int height, bool hasTransparency) =>
            new(true, null, width, height, hasTransparency);
    }
}
