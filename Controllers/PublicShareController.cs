using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProposalStudio.Data;

namespace ProposalStudio.Controllers
{
    /// <summary>
    /// Anonymous client-facing proposal view (brief §5.8 / §5.9).
    /// </summary>
    [ApiController]
    [Route("api/p")]
    [AllowAnonymous]
    public class PublicShareController : ControllerBase
    {
        private readonly AppDbContext _context;

        public PublicShareController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/p/{token}
        [HttpGet("{token}")]
        public async Task<IActionResult> GetByToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token) || token.Length < 16)
            {
                return NotFound();
            }

            var link = await _context.ShareLinks
                .FirstOrDefaultAsync(s => s.Token == token);

            if (link == null || link.Revoked)
            {
                return NotFound("This link is invalid or has been revoked.");
            }

            if (link.ExpiresAt.HasValue && link.ExpiresAt.Value < DateTimeOffset.UtcNow)
            {
                return NotFound("This link has expired.");
            }

            var proposal = await _context.Proposals.FirstOrDefaultAsync(p => p.Id == link.ProposalId);
            if (proposal == null)
            {
                return NotFound();
            }

            // Auto-advance Sent → Viewed on first open
            if (proposal.Status is "sent" or "draft")
            {
                if (proposal.Status == "sent")
                {
                    proposal.Status = "viewed";
                    proposal.UpdatedAt = DateTimeOffset.UtcNow;
                    await _context.SaveChangesAsync();
                }
            }

            var client = await _context.Clients.FirstOrDefaultAsync(c => c.Id == proposal.ClientId);
            var advisor = await _context.Users.FirstOrDefaultAsync(u => u.Id == proposal.AdvisorId);
            var business = await _context.Businesses.FirstOrDefaultAsync(b => b.Id == proposal.BusinessId);
            var item = await _context.ProposalItems.FirstOrDefaultAsync(i => i.ProposalId == proposal.Id);

            object? itemPayload = null;
            if (item != null)
            {
                var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == item.ProductId);
                var brand = product == null
                    ? null
                    : await _context.Brands.FirstOrDefaultAsync(b => b.Id == product.BrandId);

                itemPayload = new
                {
                    item.Finish,
                    item.Qty,
                    item.Included,
                    item.Excluded,
                    UnitPrice = item.PriceOverride ?? item.UnitPrice,
                    Product = product == null ? null : new
                    {
                        product.Id,
                        product.Model,
                        product.Tagline,
                        product.Blurb,
                        product.Features,
                        product.Finishes,
                        product.AvailabilityNote
                    },
                    Brand = brand == null ? null : new { brand.Name }
                };
            }

            // Privacy headers for client-facing view
            Response.Headers["X-Robots-Tag"] = "noindex, nofollow, noarchive";
            Response.Headers["Cache-Control"] = "no-store, private";

            return Ok(new
            {
                Reference = proposal.Reference,
                Status = proposal.Status,
                Currency = proposal.Currency,
                VatMode = proposal.VatMode,
                ValidityDays = proposal.ValidityDays,
                PriceTotal = proposal.PriceTotal,
                ExpiresAt = proposal.ExpiresAt ?? link.ExpiresAt,
                ClientName = client?.Name,
                AdvisorName = advisor?.Name,
                BusinessName = business?.Name ?? "House of Pianos",
                ContactLine = "Dubai, UAE · houseofpianos.ae",
                Item = itemPayload,
                PdfAvailable = !string.IsNullOrWhiteSpace(proposal.PdfUrl)
            });
        }

        // POST: api/p/{token}/event — lightweight open tracking
        [HttpPost("{token}/event")]
        public async Task<IActionResult> LogEvent(string token, [FromBody] PublicEventRequest? request)
        {
            var link = await _context.ShareLinks.FirstOrDefaultAsync(s => s.Token == token);
            if (link == null || link.Revoked)
            {
                return NotFound();
            }

            if (link.ExpiresAt.HasValue && link.ExpiresAt.Value < DateTimeOffset.UtcNow)
            {
                return NotFound();
            }

            var proposal = await _context.Proposals.FirstOrDefaultAsync(p => p.Id == link.ProposalId);
            if (proposal == null)
            {
                return NotFound();
            }

            var type = (request?.Type ?? "opened").Trim().ToLowerInvariant();
            if (type is "opened" or "section_view" or "download")
            {
                if (proposal.Status == "sent")
                {
                    proposal.Status = "viewed";
                    proposal.UpdatedAt = DateTimeOffset.UtcNow;
                    await _context.SaveChangesAsync();
                }
            }

            return Ok(new { logged = true, status = proposal.Status });
        }
    }

    public class PublicEventRequest
    {
        public string? Type { get; set; }
        public string? Section { get; set; }
    }

    public static class ShareTokenFactory
    {
        public static string Create()
        {
            var bytes = RandomNumberGenerator.GetBytes(32);
            return Convert.ToBase64String(bytes)
                .Replace("+", "-")
                .Replace("/", "_")
                .TrimEnd('=');
        }

        public static string HashIp(string? ip)
        {
            if (string.IsNullOrWhiteSpace(ip)) return string.Empty;
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(ip));
            return Convert.ToHexString(hash)[..16];
        }
    }
}
