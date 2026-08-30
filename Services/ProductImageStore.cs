using System.Text.RegularExpressions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
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

        /// <summary>Longest edge stored for print. Gallery PDFs are 960 pt; 4k is plenty.</summary>
        private const int MaxStoredEdge = 4096;

        /// <summary>Largest upload accepted, before decoding. Print TIFFs run larger than JPEGs.</summary>
        public const long MaxUploadBytes = 100 * 1024 * 1024;

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
            try
            {
                using var buffer = new MemoryStream();
                await upload.CopyToAsync(buffer);
                buffer.Position = 0;

                Directory.CreateDirectory(_root);
                Directory.CreateDirectory(Path.Combine(_root, "thumbs"));

                var dest = FullPath(productId, slot);
                var thumb = ThumbnailPath(productId, slot);

                // TIFF (and anything Skia cannot read) is converted here and written
                // straight to PNG. Going through Skia doubled memory and killed the
                // request on typical print files — the browser then reports Failed to fetch.
                if (LooksLikeTiff(buffer))
                {
                    var converted = SaveWithImageSharp(buffer, dest, thumb);
                    if (converted.Success)
                        DeleteLegacy(productId, slot);
                    return converted;
                }

                using var bitmap = DecodeWithSkia(buffer);
                if (bitmap == null)
                {
                    var converted = SaveWithImageSharp(buffer, dest, thumb);
                    if (converted.Success)
                        DeleteLegacy(productId, slot);
                    return converted;
                }

                using (var image = SKImage.FromBitmap(bitmap))
                using (var encoded = image.Encode(SKEncodedImageFormat.Png, 100))
                await using (var file = File.Create(dest))
                {
                    encoded.SaveTo(file);
                }

                WriteThumbnail(bitmap, thumb);
                DeleteLegacy(productId, slot);

                return ImageSaveResult.Saved(bitmap.Width, bitmap.Height, HasTransparency(bitmap));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not save product image {ProductId} slot {Slot}", productId, slot);
                return ImageSaveResult.Rejected(
                    "That image could not be converted. Try a PNG or JPEG, or a TIFF under 100 MB.");
            }
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

        private static SKBitmap? DecodeWithSkia(MemoryStream buffer)
        {
            try
            {
                buffer.Position = 0;
                return SKBitmap.Decode(buffer);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool LooksLikeTiff(MemoryStream buffer)
        {
            if (buffer.Length < 4) return false;
            buffer.Position = 0;
            var a = buffer.ReadByte();
            var b = buffer.ReadByte();
            var c = buffer.ReadByte();
            var d = buffer.ReadByte();
            // II*\0  or  MM\0*  or BigTIFF II+\0
            return (a == 'I' && b == 'I' && (c == 0x2A || c == 0x2B) && d == 0)
                || (a == 'M' && b == 'M' && c == 0 && (d == 0x2A || d == 0x2B));
        }

        /// <summary>
        /// TIFF / fallback: decode, cap the long edge, write PNG + thumb. No Skia round-trip.
        /// </summary>
        private ImageSaveResult SaveWithImageSharp(MemoryStream buffer, string dest, string thumb)
        {
            try
            {
                buffer.Position = 0;
                using var image = Image.Load<Rgba32>(buffer);

                // CMYK TIFFs keep a print ICC after decode. Left on an RGB PNG, browsers
                // and the PDF treat paper-white as grey. Strip it and lift the backdrop.
                image.Metadata.IccProfile = null;
                image.Metadata.GetPngMetadata().Gamma = 0;
                RestorePaperWhite(image);

                if (Math.Max(image.Width, image.Height) > MaxStoredEdge)
                {
                    image.Mutate(c => c.Resize(new ResizeOptions
                    {
                        Mode = ResizeMode.Max,
                        Size = new Size(MaxStoredEdge, MaxStoredEdge)
                    }));
                }

                var width = image.Width;
                var height = image.Height;
                var png = new PngEncoder { CompressionLevel = PngCompressionLevel.BestSpeed };

                image.Save(dest, png);

                image.Mutate(c => c.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(ThumbnailSize, ThumbnailSize)
                }));
                image.Save(thumb, png);

                return ImageSaveResult.Saved(width, height, false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ImageSharp could not convert an upload");
                return ImageSaveResult.Rejected(
                    "That TIFF could not be converted. Export a JPEG or an 8-bit TIFF and try again.");
            }
        }

        /// <summary>
        /// Print CMYK → RGB maps coated-paper white to ~220 grey. If the corners look like
        /// a studio backdrop, scale the highlights so that backdrop becomes white.
        /// </summary>
        private static void RestorePaperWhite(Image<Rgba32> image)
        {
            var patch = Math.Clamp(Math.Min(image.Width, image.Height) / 30, 8, 48);
            long r = 0, g = 0, b = 0, n = 0;

            void Sample(int x0, int y0)
            {
                var x1 = Math.Min(image.Width, x0 + patch);
                var y1 = Math.Min(image.Height, y0 + patch);
                for (var y = y0; y < y1; y++)
                {
                    for (var x = x0; x < x1; x++)
                    {
                        var p = image[x, y];
                        r += p.R;
                        g += p.G;
                        b += p.B;
                        n++;
                    }
                }
            }

            Sample(0, 0);
            Sample(image.Width - patch, 0);
            Sample(0, image.Height - patch);
            Sample(image.Width - patch, image.Height - patch);

            if (n == 0) return;

            var avgR = r / (float)n;
            var avgG = g / (float)n;
            var avgB = b / (float)n;
            var brightest = Math.Max(avgR, Math.Max(avgG, avgB));
            var chroma = brightest - Math.Min(avgR, Math.Min(avgG, avgB));

            // Only lift a near-neutral, already-light backdrop — not a dark or colored scene.
            if (brightest < 198 || brightest >= 254 || chroma > 30)
                return;

            var gainR = Math.Clamp(255f / Math.Max(avgR, 1f), 1f, 1.22f);
            var gainG = Math.Clamp(255f / Math.Max(avgG, 1f), 1f, 1.22f);
            var gainB = Math.Clamp(255f / Math.Max(avgB, 1f), 1f, 1.22f);

            image.ProcessPixelRows(accessor =>
            {
                for (var y = 0; y < accessor.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (var x = 0; x < row.Length; x++)
                    {
                        var p = row[x];
                        p.R = (byte)Math.Min(255, p.R * gainR + 0.5f);
                        p.G = (byte)Math.Min(255, p.G * gainG + 0.5f);
                        p.B = (byte)Math.Min(255, p.B * gainB + 0.5f);
                        row[x] = p;
                    }
                }
            });
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
