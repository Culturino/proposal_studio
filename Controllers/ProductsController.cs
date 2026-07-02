using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProposalStudio.Data;

namespace ProposalStudio.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
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
    }
}