using SkiaSharp;

namespace ProposalStudio.Services
{
    /// <summary>
    /// Owns the product image files under wwwroot/images/products.
    ///
    /// Layout is a filename convention rather than a database table, because the PDF renderer
    /// and the public image endpoint already resolve images that way:
    ///
    ///   {id}.png            full profile (hero), full resolution — used by the PDF
    ///   {id}-2.png          additional view
    ///   {id}-3.png          additional view
    ///   thumbs/{id}*.png    downscaled copies for catalog and admin listings
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

        private readonly string _root;

        public ProductImageStore(IWebHostEnvironment environment)
        {
            var webRoot = environment.WebRootPath
                ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");

            _root = Path.Combine(webRoot, "images", "products");
        }

        public static bool IsValidSlot(int slot) => ValidSlots.Contains(slot);

        /// <summary>Slot 1 is the hero and keeps the bare id so existing links stay valid.</summary>
        private static string FileName(Guid productId, int slot) =>
            slot == 1 ? $"{productId}.png" : $"{productId}-{slot}.png";

        public string FullPath(Guid productId, int slot) =>
            Path.Combine(_root, FileName(productId, slot));

        public string ThumbnailPath(Guid productId, int slot) =>
            Path.Combine(_root, "thumbs", FileName(productId, slot));

        public bool Exists(Guid productId, int slot) => File.Exists(FullPath(productId, slot));

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

            using (var image = SKImage.FromBitmap(bitmap))
            using (var encoded = image.Encode(SKEncodedImageFormat.Png, 100))
            await using (var file = File.Create(FullPath(productId, slot)))
            {
                encoded.SaveTo(file);
            }

            WriteThumbnail(bitmap, ThumbnailPath(productId, slot));

            return ImageSaveResult.Saved(bitmap.Width, bitmap.Height, HasTransparency(bitmap));
        }

        public void Delete(Guid productId, int slot)
        {
            foreach (var path in new[] { FullPath(productId, slot), ThumbnailPath(productId, slot) })
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
            var thumb = ThumbnailPath(productId, slot);
            var full = FullPath(productId, slot);

            if (!File.Exists(full))
                return null;

            if (File.Exists(thumb) && File.GetLastWriteTimeUtc(thumb) >= File.GetLastWriteTimeUtc(full))
                return thumb;

            using var bitmap = SKBitmap.Decode(full);
            if (bitmap == null)
                return null;

            Directory.CreateDirectory(Path.GetDirectoryName(thumb)!);
            WriteThumbnail(bitmap, thumb);
            return thumb;
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
