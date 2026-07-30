using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProposalStudio.Data;
using Microsoft.AspNetCore.JsonPatch;
using ProposalStudio.Models;

namespace ProposalStudio.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    //[Authorize] // Requires user to be logged in to access the catalog
    public class ProductsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public ProductsController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetProducts()
        {
            var products = await (
                from product in _context.Products
                join brand in _context.Brands
                    on product.BrandId equals brand.Id
                join category in _context.ProductCategories
                    on product.CategoryId equals category.Id
                join price in _context.Prices
                    on product.Id equals price.ProductId into priceGroup
                from price in priceGroup.DefaultIfEmpty()
                where product.Status == "active"
                orderby brand.Name, product.Model
                select new
                {
                    product.Id,
                    Brand = brand.Name,
                    Category = category.Name,
                    product.Model,
                    product.Dimensions,
                    product.Features,
                    product.Blurb,
                    product.Tagline,
                    product.Finishes,
                    product.DefaultIncludes,
                    product.DefaultExcludes,
                    product.AvailabilityNote,
                    Price = price == null ? null : new
                    {
                        price.Currency,
                        price.Finish,
                        price.Amount,
                        price.ValidFrom,
                        price.Source
                    }
                }
            ).ToListAsync();

            return Ok(products);
        }

        [HttpGet("{id}")]
        [Authorize]
        public async Task<IActionResult> GetProduct(Guid id)
        {
            var product = await (
                from p in _context.Products
                join brand in _context.Brands
                    on p.BrandId equals brand.Id
                join category in _context.ProductCategories
                    on p.CategoryId equals category.Id
                join price in _context.Prices
                    on p.Id equals price.ProductId into priceGroup
                from price in priceGroup.DefaultIfEmpty()
                where p.Id == id
                select new
                {
                    p.Id,
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
                    Price = price == null ? null : new
                    {
                        price.Currency,
                        price.Finish,
                        price.Amount,
                        price.ValidFrom,
                        price.Source
                    }
                }
            ).FirstOrDefaultAsync();

            if (product == null)
            {
                return NotFound();
            }

            return Ok(product);
        }

        [HttpGet("{id}/finishes")]
        public async Task<IActionResult> GetFinishes(Guid id)
        {
            var product = await _context.Products
                .FirstOrDefaultAsync(p => p.Id == id);

            if (product == null)
                return NotFound("Product not found");

            return Ok(product.Finishes ?? Array.Empty<string>());
        }

        // ------------------------------------------------------------------
        // GET IMAGE: api/products/{id}/image (Public)
        // ------------------------------------------------------------------

        [HttpGet("{id}/image")]
        [AllowAnonymous]
        public IActionResult GetProductImage(Guid id)
        {
            var path = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/images/products", $"{id}.png");

            if (!System.IO.File.Exists(path))
            {
                return NotFound("Image not found");
            }

            // Cache for 1 hour to speed up loading
            Response.Headers.Append("Cache-Control", "public, max-age=3600");

            return PhysicalFile(path, "image/png");
        }

        [HttpGet("test-image/{id}")]
        [AllowAnonymous]
        public IActionResult TestImage(Guid id)
        {
            var path = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/images/products", $"{id}.png");

            return Ok(new
            {
                path,
                exists = System.IO.File.Exists(path)
            });
        }

        // ------------------------------------------------------------------
        // PATCH: api/products/{id} (Admin Only)
        // ------------------------------------------------------------------

        [HttpPatch("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> PatchProduct(Guid id, [FromBody] JsonPatchDocument<Product> patchDoc)
        {
            if (patchDoc == null)
            {
                return BadRequest("Patch document is null.");
            }

            var product = await _context.Products.FindAsync(id);
            if (product == null)
            {
                return NotFound();
            }

            patchDoc.ApplyTo(product, ModelState);

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            product.UpdatedAt = DateTimeOffset.UtcNow;

            await _context.SaveChangesAsync();

            return Ok(new { message = "Product updated successfully via Patch (Admin Only)" });
        }

        // ------------------------------------------------------------------
        // DELETE: api/products/{id} (Admin Only)
        // ------------------------------------------------------------------

        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteProduct(Guid id)
        {
            var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == id);

            if (product == null)
            {
                return NotFound();
            }

            _context.Products.Remove(product);
            await _context.SaveChangesAsync();

            return NoContent();
        }
    }
}