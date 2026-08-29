using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProposalStudio.Data;
using ProposalStudio.Models;
using ProposalStudio.Services;

namespace ProposalStudio.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ProductsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly AuditService _audit;
        private readonly ProductImageStore _images;

        public ProductsController(AppDbContext context, AuditService audit, ProductImageStore images)
        {
            _context = context;
            _audit = audit;
            _images = images;
        }

        // GET: api/products?brand=&category=&q=
        [HttpGet]
        public async Task<IActionResult> GetProducts(
            [FromQuery] string? brand = null,
            [FromQuery] string? category = null,
            [FromQuery] string? q = null)
        {
            var query =
                from product in _context.Products
                join b in _context.Brands on product.BrandId equals b.Id
                join cat in _context.ProductCategories on product.CategoryId equals cat.Id
                where product.Status != "archived" && product.Status != "deleted"
                select new { product, b, cat };

            if (!string.IsNullOrWhiteSpace(brand))
            {
                var term = brand.Trim().ToLower();
                query = query.Where(x => x.b.Name.ToLower().Contains(term));
            }

            if (!string.IsNullOrWhiteSpace(category))
            {
                var term = category.Trim().ToLower();
                query = query.Where(x => x.cat.Name.ToLower().Contains(term) || x.cat.Slug.ToLower() == term);
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim().ToLower();
                query = query.Where(x =>
                    x.product.Model.ToLower().Contains(term) ||
                    (x.product.Tagline != null && x.product.Tagline.ToLower().Contains(term)) ||
                    (x.product.Blurb != null && x.product.Blurb.ToLower().Contains(term)));
            }

            var products = await query
                .OrderBy(x => x.b.Name)
                .ThenBy(x => x.product.Model)
                .Select(x => new
                {
                    x.product.Id,
                    x.product.BrandId,
                    x.product.CategoryId,
                    Brand = x.b.Name,
                    Category = x.cat.Name,
                    x.product.Model,
                    x.product.Dimensions,
                    x.product.Features,
                    x.product.Blurb,
                    x.product.Tagline,
                    x.product.Finishes,
                    x.product.DefaultIncludes,
                    x.product.DefaultExcludes,
                    x.product.AvailabilityNote,
                    x.product.Status,
                    Price = _context.Prices
                        .Where(p => p.ProductId == x.product.Id)
                        .OrderByDescending(p => p.ValidFrom)
                        .Select(p => new
                        {
                            p.Currency,
                            p.Finish,
                            p.Amount,
                            p.ValidFrom,
                            p.Source
                        })
                        .FirstOrDefault()
                })
                .ToListAsync();

            return Ok(products);
        }

        // GET: api/products/{id}
        [HttpGet("{id}")]
        public async Task<IActionResult> GetProduct(Guid id)
        {
            var product = await (
                from p in _context.Products
                join brand in _context.Brands
                    on p.BrandId equals brand.Id
                join category in _context.ProductCategories
                    on p.CategoryId equals category.Id
                where p.Id == id
                select new
                {
                    p.Id,
                    p.BrandId,
                    p.CategoryId,
                    Brand = brand.Name,
                    Category = category.Name,
                    p.Model,
                    p.Dimensions,
                    p.Features,
                    p.Blurb,
                    p.Tagline,
                    p.Finishes,
                    p.DefaultIncludes,
                    p.DefaultExcludes,
                    p.AvailabilityNote,
                    p.Status,
                    Price = _context.Prices
                        .Where(price => price.ProductId == p.Id)
                        .OrderByDescending(price => price.ValidFrom)
                        .Select(price => new
                        {
                            price.Currency,
                            price.Finish,
                            price.Amount,
                            price.ValidFrom,
                            price.Source
                        })
                        .FirstOrDefault()
                }
            ).FirstOrDefaultAsync();

            if (product == null)
            {
                return NotFound();
            }

            return Ok(product);
        }

        // GET: api/products/{id}/finishes
        [HttpGet("{id}/finishes")]
        public async Task<IActionResult> GetFinishes(Guid id)
        {
            var product = await _context.Products
                .FirstOrDefaultAsync(p => p.Id == id);

            if (product == null)
            {
                return NotFound("Product not found");
            }

            return Ok(product.Finishes ?? Array.Empty<string>());
        }

        // GET: api/products/{id}/image?slot=1&variant=thumb (public)
        // slot 1 is the full profile shot; 2 and 3 are the additional views.
        [HttpGet("{id}/image")]
        [AllowAnonymous]
        public IActionResult GetProductImage(
            Guid id,
            [FromQuery] int slot = 1,
            [FromQuery] string? variant = null)
        {
            if (!ProductImageStore.IsValidSlot(slot))
            {
                return BadRequest("Slot must be 1, 2 or 3.");
            }

            var wantsThumbnail = string.Equals(variant, "thumb", StringComparison.OrdinalIgnoreCase);

            var path = wantsThumbnail
                ? _images.ResolveThumbnail(id, slot)
                : _images.FullPath(id, slot);

            if (path == null || !System.IO.File.Exists(path))
            {
                return NotFound("Image not found");
            }

            // Revalidate rather than cache blindly: an admin replacing a photo previously kept
            // seeing the old one for the lifetime of the cache entry.
            var stamp = System.IO.File.GetLastWriteTimeUtc(path);
            var etag = $"\"{stamp.Ticks:x}-{new FileInfo(path).Length:x}\"";

            if (Request.Headers.IfNoneMatch.Contains(etag))
            {
                return StatusCode(StatusCodes.Status304NotModified);
            }

            Response.Headers.ETag = etag;
            Response.Headers.CacheControl = "public, max-age=60, must-revalidate";

            return PhysicalFile(path, "image/png");
        }

        // POST: api/products/{id}/image?slot=1 (Admin)
        [HttpPost("{id}/image")]
        [Authorize(Roles = "Admin")]
        [RequestSizeLimit(ProductImageStore.MaxUploadBytes)]
        public async Task<IActionResult> UploadProductImage(
            Guid id, IFormFile file, [FromQuery] int slot = 1)
        {
            if (!ProductImageStore.IsValidSlot(slot))
            {
                return BadRequest("Slot must be 1, 2 or 3.");
            }

            if (file == null || file.Length == 0)
            {
                return BadRequest("No file was uploaded.");
            }

            if (file.Length > ProductImageStore.MaxUploadBytes)
            {
                return BadRequest("Image must be 25 MB or smaller.");
            }

            if (!await _context.Products.AnyAsync(p => p.Id == id))
            {
                return NotFound("Product not found");
            }

            await using var stream = file.OpenReadStream();
            var result = await _images.SaveAsync(id, slot, stream);

            if (!result.Success)
            {
                return BadRequest(result.Error);
            }

            await _audit.LogAsync(User, "product.image.upload", "Product", id, null, new { slot });

            return Ok(new
            {
                slot,
                result.Width,
                result.Height,
                result.HasTransparency,
                // The cover composites the hero over black, so a flat image shows as a rectangle.
                warning = slot == 1 && !result.HasTransparency
                    ? "This image has no transparent background, so it will appear as a rectangle on the proposal cover. A cut-out PNG works best."
                    : null
            });
        }

        // DELETE: api/products/{id}/image?slot=1 (Admin)
        [HttpDelete("{id}/image")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteProductImage(Guid id, [FromQuery] int slot = 1)
        {
            if (!ProductImageStore.IsValidSlot(slot))
            {
                return BadRequest("Slot must be 1, 2 or 3.");
            }

            if (!_images.Exists(id, slot))
            {
                return NotFound("Image not found");
            }

            _images.Delete(id, slot);
            await _audit.LogAsync(User, "product.image.delete", "Product", id, new { slot }, null);

            return NoContent();
        }

        // POST: api/products (Admin)
        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CreateProduct([FromBody] CreateProductRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Model))
            {
                return BadRequest("Product model is required.");
            }

            if (request.BrandId == Guid.Empty || request.CategoryId == Guid.Empty)
            {
                return BadRequest("BrandId and CategoryId are required.");
            }

            var brandExists = await _context.Brands.AnyAsync(b => b.Id == request.BrandId);
            if (!brandExists)
            {
                return BadRequest("Brand was not found.");
            }

            var categoryExists = await _context.ProductCategories.AnyAsync(c => c.Id == request.CategoryId);
            if (!categoryExists)
            {
                return BadRequest("Category was not found.");
            }

            var model = request.Model.Trim();

            // The (brand_id, model) unique index would otherwise surface as an opaque 500.
            var duplicate = await _context.Products
                .AnyAsync(p => p.BrandId == request.BrandId && p.Model == model);
            if (duplicate)
            {
                return Conflict($"This brand already has a product called \"{model}\".");
            }

            var now = DateTimeOffset.UtcNow;
            var product = new Product
            {
                Id = Guid.NewGuid(),
                BrandId = request.BrandId,
                CategoryId = request.CategoryId,
                Model = model,
                Dimensions = ParseDimensions(request.Dimensions),
                Features = request.Features ?? Array.Empty<string>(),
                Blurb = string.IsNullOrWhiteSpace(request.Blurb) ? null : request.Blurb.Trim(),
                Tagline = string.IsNullOrWhiteSpace(request.Tagline) ? null : request.Tagline.Trim(),
                Finishes = request.Finishes ?? Array.Empty<string>(),
                DefaultIncludes = request.DefaultIncludes ?? Array.Empty<string>(),
                DefaultExcludes = request.DefaultExcludes ?? Array.Empty<string>(),
                AvailabilityNote = string.IsNullOrWhiteSpace(request.AvailabilityNote)
                    ? null
                    : request.AvailabilityNote.Trim(),
                Status = string.IsNullOrWhiteSpace(request.Status) ? "active" : request.Status.Trim(),
                CreatedAt = now,
                UpdatedAt = now
            };

            _context.Products.Add(product);
            await _context.SaveChangesAsync();
            await _audit.LogAsync(User, "create", "product", product.Id, null, AuditService.ProductSnapshot(product));

            return CreatedAtAction(nameof(GetProduct), new { id = product.Id }, product);
        }

        // PATCH: api/products/{id} (Admin)
        [HttpPatch("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateProduct(Guid id, [FromBody] UpdateProductRequest request)
        {
            var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == id);

            if (product == null)
            {
                return NotFound();
            }

            var before = AuditService.ProductSnapshot(product);

            if (request.BrandId.HasValue)
            {
                var brandExists = await _context.Brands.AnyAsync(b => b.Id == request.BrandId.Value);
                if (!brandExists)
                {
                    return BadRequest("Brand was not found.");
                }

                product.BrandId = request.BrandId.Value;
            }

            if (request.CategoryId.HasValue)
            {
                var categoryExists = await _context.ProductCategories
                    .AnyAsync(c => c.Id == request.CategoryId.Value);
                if (!categoryExists)
                {
                    return BadRequest("Category was not found.");
                }

                product.CategoryId = request.CategoryId.Value;
            }

            if (request.Model != null && !string.IsNullOrWhiteSpace(request.Model))
            {
                product.Model = request.Model.Trim();
            }

            // Either half of the natural key may have just moved, so re-check the pair.
            var collides = await _context.Products
                .AnyAsync(p => p.Id != product.Id
                    && p.BrandId == product.BrandId
                    && p.Model == product.Model);
            if (collides)
            {
                return Conflict($"This brand already has a product called \"{product.Model}\".");
            }

            if (request.Dimensions != null)
            {
                product.Dimensions = ParseDimensions(request.Dimensions);
            }

            if (request.Features != null)
            {
                product.Features = request.Features;
            }

            if (request.Blurb != null)
            {
                product.Blurb = string.IsNullOrWhiteSpace(request.Blurb) ? null : request.Blurb.Trim();
            }

            if (request.Tagline != null)
            {
                product.Tagline = string.IsNullOrWhiteSpace(request.Tagline) ? null : request.Tagline.Trim();
            }

            if (request.Finishes != null)
            {
                product.Finishes = request.Finishes;
            }

            if (request.DefaultIncludes != null)
            {
                product.DefaultIncludes = request.DefaultIncludes;
            }

            if (request.DefaultExcludes != null)
            {
                product.DefaultExcludes = request.DefaultExcludes;
            }

            if (request.AvailabilityNote != null)
            {
                product.AvailabilityNote = string.IsNullOrWhiteSpace(request.AvailabilityNote)
                    ? null
                    : request.AvailabilityNote.Trim();
            }

            if (request.Status != null && !string.IsNullOrWhiteSpace(request.Status))
            {
                product.Status = request.Status.Trim();
            }

            product.UpdatedAt = DateTimeOffset.UtcNow;

            await _context.SaveChangesAsync();
            await _audit.LogAsync(User, "update", "product", product.Id, before, AuditService.ProductSnapshot(product));

            return Ok(product);
        }

        // DELETE: api/products/{id} (Admin) — always soft-archive (survives restarts; seeder will not resurrect)
        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteProduct(Guid id)
        {
            var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == id);

            if (product == null)
            {
                return NotFound();
            }

            var before = AuditService.ProductSnapshot(product);
            product.Status = "archived";
            product.UpdatedAt = DateTimeOffset.UtcNow;
            await _context.SaveChangesAsync();
            await _audit.LogAsync(User, "delete", "product", product.Id, before, AuditService.ProductSnapshot(product));
            return NoContent();
        }

        // Parse dimensions JSON from a string, object, or null (works with Newtonsoft + STJ).
        private static JsonDocument ParseDimensions(object? dimensions)
        {
            if (dimensions == null)
            {
                return JsonDocument.Parse("{}");
            }

            if (dimensions is string raw)
            {
                return Sanitize(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
            }

            if (dimensions is JsonElement element)
            {
                if (element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
                {
                    return JsonDocument.Parse("{}");
                }

                if (element.ValueKind == JsonValueKind.String)
                {
                    var text = element.GetString();
                    return Sanitize(string.IsNullOrWhiteSpace(text) ? "{}" : text!);
                }

                return Sanitize(element.GetRawText());
            }

            // Newtonsoft typically deserializes anonymous JSON objects as JObject/Dictionary
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(dimensions);
            return Sanitize(string.IsNullOrWhiteSpace(json) || json == "null" ? "{}" : json);
        }

        /// <summary>
        /// Reduces a dimensions payload to a flat label/value map. Nested values cannot be
        /// rendered on the proposal, and clients that read a mis-serialised jsonb column echo
        /// its wrapper back on save, so both are dropped rather than stored.
        /// </summary>
        private static JsonDocument Sanitize(string json)
        {
            JsonDocument parsed;
            try
            {
                parsed = JsonDocument.Parse(json);
            }
            catch (JsonException)
            {
                return JsonDocument.Parse("{}");
            }

            using (parsed)
            {
                if (parsed.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return JsonDocument.Parse("{}");
                }

                var clean = new Dictionary<string, string>();
                foreach (var prop in parsed.RootElement.EnumerateObject())
                {
                    if (string.Equals(prop.Name, "RootElement", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var value = prop.Value.ValueKind switch
                    {
                        JsonValueKind.String => prop.Value.GetString(),
                        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => prop.Value.ToString(),
                        _ => null
                    };

                    if (!string.IsNullOrWhiteSpace(value))
                        clean[prop.Name] = value!.Trim();
                }

                return JsonDocument.Parse(JsonSerializer.Serialize(clean));
            }
        }
    }

    public class CreateProductRequest
    {
        public Guid BrandId { get; set; }

        public Guid CategoryId { get; set; }

        public string Model { get; set; } = string.Empty;

        public object? Dimensions { get; set; }

        public string[]? Features { get; set; }

        public string? Blurb { get; set; }

        public string? Tagline { get; set; }

        public string[]? Finishes { get; set; }

        public string[]? DefaultIncludes { get; set; }

        public string[]? DefaultExcludes { get; set; }

        public string? AvailabilityNote { get; set; }

        public string? Status { get; set; }
    }

    public class UpdateProductRequest
    {
        public Guid? BrandId { get; set; }

        public Guid? CategoryId { get; set; }

        public string? Model { get; set; }

        public object? Dimensions { get; set; }

        public string[]? Features { get; set; }

        public string? Blurb { get; set; }

        public string? Tagline { get; set; }

        public string[]? Finishes { get; set; }

        public string[]? DefaultIncludes { get; set; }

        public string[]? DefaultExcludes { get; set; }

        public string? AvailabilityNote { get; set; }

        public string? Status { get; set; }
    }
}
