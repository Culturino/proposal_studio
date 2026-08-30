using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProposalStudio.Data;
using ProposalStudio.Services;

namespace ProposalStudio.Controllers
{
    [ApiController]
    [Route("api/analytics")]
    [Authorize]
    public class AnalyticsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ProposalExpiryService _expiry;

        public AnalyticsController(AppDbContext context, ProposalExpiryService expiry)
        {
            _context = context;
            _expiry = expiry;
        }

        // GET: api/analytics/summary?from=&to=
        [HttpGet("summary")]
        public async Task<IActionResult> Summary(
            [FromQuery] DateTimeOffset? from = null,
            [FromQuery] DateTimeOffset? to = null)
        {
            var role = User.FindFirstValue(ClaimTypes.Role);
            var userId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid)
                ? uid
                : (Guid?)null;

            var canSeeAll = role is "Admin" or "Manager";

            await _expiry.ExpireOverdueAsync();

            var fromDate = from ?? new DateTimeOffset(DateTime.UtcNow.Date.AddDays(-28), TimeSpan.Zero);
            var toDate = to ?? DateTimeOffset.UtcNow;

            var proposalsQuery = _context.Proposals.AsQueryable();

            if (!canSeeAll && userId.HasValue)
            {
                proposalsQuery = proposalsQuery.Where(p => p.AdvisorId == userId.Value);
            }

            var inRange = proposalsQuery.Where(p => p.CreatedAt >= fromDate && p.CreatedAt <= toDate);
            var proposals = await inRange.ToListAsync();

            var monthStart = new DateTimeOffset(new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1), TimeSpan.Zero);
            var createdMtd = await proposalsQuery.CountAsync(p => p.CreatedAt >= monthStart);

            var created = proposals.Count;
            var sentLike = proposals.Count(p => p.Status is "sent" or "viewed" or "accepted" or "expired");
            var viewedLike = proposals.Count(p => p.Status is "viewed" or "accepted");
            var accepted = proposals.Count(p => p.Status == "accepted");
            var draft = proposals.Count(p => p.Status == "draft");

            var pipelineValue = proposals
                .Where(p => p.Status is "draft" or "sent" or "viewed")
                .Sum(p => p.PriceTotal ?? 0);

            double? viewRate = sentLike > 0 ? Math.Round(100.0 * viewedLike / sentLike, 1) : null;
            double? winRate = sentLike > 0 ? Math.Round(100.0 * accepted / sentLike, 1) : null;

            // Approximate avg hours from sent → first status update after send
            var viewDurations = proposals
                .Where(p => p.SentAt.HasValue && p.Status is "viewed" or "accepted")
                .Select(p => (p.UpdatedAt - p.SentAt!.Value).TotalHours)
                .Where(h => h >= 0 && h < 720)
                .ToList();

            double? avgHoursToView = viewDurations.Count > 0
                ? Math.Round(viewDurations.Average(), 1)
                : null;

            // Weekly series (ISO-ish week labels)
            var weekly = proposals
                .GroupBy(p => ISOWeek.GetYear(p.CreatedAt.UtcDateTime) * 100 + ISOWeek.GetWeekOfYear(p.CreatedAt.UtcDateTime))
                .OrderBy(g => g.Key)
                .Select(g => new
                {
                    w = $"W{g.Key % 100}",
                    created = g.Count(),
                    sent = g.Count(p => p.Status is not "draft"),
                    accepted = g.Count(p => p.Status == "accepted")
                })
                .ToList();

            if (weekly.Count == 0)
            {
                weekly.Add(new { w = "—", created = 0, sent = 0, accepted = 0 });
            }

            // By brand
            var proposalIds = proposals.Select(p => p.Id).ToList();
            var byBrand = await (
                from item in _context.ProposalItems
                join product in _context.Products on item.ProductId equals product.Id
                join brand in _context.Brands on product.BrandId equals brand.Id
                where proposalIds.Contains(item.ProposalId)
                group item by brand.Name into g
                orderby g.Count() descending
                select new
                {
                    brand = ShortBrand(g.Key),
                    v = g.Count()
                }
            ).ToListAsync();

            return Ok(new
            {
                scope = canSeeAll ? "team" : "own",
                from = fromDate,
                to = toDate,
                kpis = new
                {
                    createdMtd,
                    created,
                    sent = sentLike,
                    viewed = viewedLike,
                    accepted,
                    draft,
                    viewRate,
                    winRate,
                    avgHoursToView,
                    pipelineValue
                },
                weekly,
                byBrand,
                funnel = new[]
                {
                    new { label = "Created", count = created },
                    new { label = "Sent", count = sentLike },
                    new { label = "Viewed", count = viewedLike },
                    new { label = "Accepted", count = accepted }
                }
            });
        }

        private static string ShortBrand(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Other";
            if (name.Contains("Steinway", StringComparison.OrdinalIgnoreCase)) return "Steinway";
            if (name.Contains("Blüthner", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Bluthner", StringComparison.OrdinalIgnoreCase)) return "Blüthner";
            if (name.Contains("Boston", StringComparison.OrdinalIgnoreCase)) return "Boston";
            if (name.Contains("Essex", StringComparison.OrdinalIgnoreCase)) return "Essex";
            if (name.Contains("Kurzweil", StringComparison.OrdinalIgnoreCase)) return "Kurzweil";
            return name.Split(' ')[0];
        }
    }
}
