using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProposalStudio.Data;
using ProposalStudio.Services;

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
        private readonly ProposalPdfService _pdf;

        public PublicShareController(AppDbContext context, ProposalPdfService pdf)
        {
            _context = context;
            _pdf = pdf;
        }

        // GET: api/p/{token}
        [HttpGet("{token}")]
        public async Task<IActionResult> GetByToken(string token)
        {
            var link = await ResolveLinkAsync(token);
            if (link == null)
            {
                return NotFound("This link is invalid or has been revoked.");
            }

            var proposal = await _context.Proposals.FirstOrDefaultAsync(p => p.Id == link.ProposalId);
            if (proposal == null)
            {
                return NotFound();
            }

            // Auto-advance Sent → Viewed on first open
            if (proposal.Status == "sent")
            {
                proposal.Status = "viewed";
                proposal.UpdatedAt = DateTimeOffset.UtcNow;
                await _context.SaveChangesAsync();
            }

            var hasItem = await _context.ProposalItems.AnyAsync(i => i.ProposalId == proposal.Id);

            var client = await _context.Clients.FirstOrDefaultAsync(c => c.Id == proposal.ClientId);
            var advisor = await _context.Users.FirstOrDefaultAsync(u => u.Id == proposal.AdvisorId);
            var business = await _context.Businesses.FirstOrDefaultAsync(b => b.Id == proposal.BusinessId);

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
                PdfAvailable = hasItem,
                PdfPath = $"/api/p/{link.Token}/pdf"
            });
        }

        // GET: api/p/{token}/pdf — always rebuild from newest proposal + catalog data
        [HttpGet("{token}/pdf")]
        public async Task<IActionResult> GetPdf(string token)
        {
            var link = await ResolveLinkAsync(token);
            if (link == null)
            {
                return NotFound("This link is invalid or has been revoked.");
            }

            var rendered = await _pdf.RenderAsync(_context, link.ProposalId);
            if (rendered == null)
            {
                return NotFound("PDF is not available for this proposal.");
            }

            var (bytes, fileName) = rendered.Value;

            Response.Headers["X-Robots-Tag"] = "noindex, nofollow, noarchive";
            Response.Headers["Cache-Control"] = "no-store, private";
            Response.Headers["Content-Disposition"] = $"inline; filename=\"{fileName}\"";

            return File(bytes, "application/pdf");
        }

        // POST: api/p/{token}/event — lightweight open tracking
        [HttpPost("{token}/event")]
        public async Task<IActionResult> LogEvent(string token, [FromBody] PublicEventRequest? request)
        {
            var link = await ResolveLinkAsync(token);
            if (link == null)
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

        private async Task<Models.ShareLink?> ResolveLinkAsync(string token)
        {
            if (string.IsNullOrWhiteSpace(token) || token.Length < 16)
            {
                return null;
            }

            var link = await _context.ShareLinks.FirstOrDefaultAsync(s => s.Token == token);
            if (link == null || link.Revoked)
            {
                return null;
            }

            if (link.ExpiresAt.HasValue && link.ExpiresAt.Value < DateTimeOffset.UtcNow)
            {
                return null;
            }

            return link;
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
