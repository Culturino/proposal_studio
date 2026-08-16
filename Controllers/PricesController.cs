using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProposalStudio.Data;
using ProposalStudio.Models;
using ProposalStudio.Services;

namespace ProposalStudio.Controllers
{
    [ApiController]
    [Route("api/prices")]
    [Authorize]
    public class PricesController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly AuditService _audit;

        public PricesController(AppDbContext context, AuditService audit)
        {
            _context = context;
            _audit = audit;
        }

        // PATCH: api/prices/{productId} — upsert catalog retail for a product (Admin)
        [HttpPatch("{productId:guid}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpsertPrice(Guid productId, [FromBody] UpsertPriceRequest request)
        {
            var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == productId);
            if (product == null)
                return NotFound("Product was not found.");

            var currency = string.IsNullOrWhiteSpace(request.Currency) ? "AED" : request.Currency.Trim().ToUpperInvariant();
            var finish = string.IsNullOrWhiteSpace(request.Finish) ? null : request.Finish.Trim();

            var existing = await _context.Prices
                .Where(p => p.ProductId == productId && p.Currency == currency)
                .OrderByDescending(p => p.ValidFrom)
                .ThenByDescending(p => p.UpdatedAt)
                .FirstOrDefaultAsync();

            var before = existing == null ? null : AuditService.PriceSnapshot(existing);
            var now = DateTimeOffset.UtcNow;

            if (existing == null)
            {
                existing = new Price
                {
                    Id = Guid.NewGuid(),
                    ProductId = productId,
                    Currency = currency,
                    Finish = finish,
                    Amount = request.Amount,
                    ValidFrom = DateOnly.FromDateTime(DateTime.UtcNow),
                    Source = request.Source ?? "admin",
                    CreatedAt = now,
                    UpdatedAt = now
                };
                _context.Prices.Add(existing);
            }
            else
            {
                existing.Amount = request.Amount;
                if (finish != null) existing.Finish = finish;
                if (!string.IsNullOrWhiteSpace(request.Source)) existing.Source = request.Source.Trim();
                existing.UpdatedAt = now;
            }

            await _context.SaveChangesAsync();
            await _audit.LogAsync(
                User,
                before == null ? "create" : "update",
                "price",
                existing.Id,
                before,
                AuditService.PriceSnapshot(existing));

            return Ok(existing);
        }
    }

    public class UpsertPriceRequest
    {
        public decimal? Amount { get; set; }
        public string? Currency { get; set; }
        public string? Finish { get; set; }
        public string? Source { get; set; }
    }
}
