using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProposalStudio.Data;
using ProposalStudio.Models;
using ProposalStudio.Services;

namespace ProposalStudio.Controllers
{
    [ApiController]
    [Route("api/approvals")]
    [Authorize]
    public class ApprovalsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly PricingGovernance _pricing;

        public ApprovalsController(AppDbContext context, PricingGovernance pricing)
        {
            _context = context;
            _pricing = pricing;
        }

        // POST: api/approvals/below-floor — advisor requests manager/admin permission
        [HttpPost("below-floor")]
        public async Task<IActionResult> RequestBelowFloor([FromBody] BelowFloorRequest body)
        {
            if (body.ProposalId == Guid.Empty)
                return BadRequest("ProposalId is required.");

            var userId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid)
                ? uid
                : (Guid?)null;
            if (userId == null)
                return Unauthorized();

            var proposal = await _context.Proposals.FirstOrDefaultAsync(p => p.Id == body.ProposalId);
            if (proposal == null)
                return NotFound("Proposal was not found.");

            var role = User.FindFirstValue(ClaimTypes.Role);
            if (role is not ("Admin" or "Manager") && proposal.AdvisorId != userId)
                return Forbid();

            var item = await _context.ProposalItems.FirstOrDefaultAsync(i => i.ProposalId == proposal.Id);
            if (item == null)
                return BadRequest("Add an instrument before requesting approval.");

            var catalogAmount = await _context.Prices
                .Where(p => p.ProductId == item.ProductId && p.Currency == proposal.Currency)
                .OrderByDescending(p => p.ValidFrom)
                .Select(p => p.Amount)
                .FirstOrDefaultAsync();

            var gov = await _pricing.GetForBusinessAsync(proposal.BusinessId);
            var offered = item.PriceOverride ?? item.UnitPrice;

            if (!_pricing.IsBelowFloor(offered, catalogAmount, gov.DiscountFloorPercent))
            {
                return BadRequest(new
                {
                    error = "not_below_floor",
                    message = "This offer is within the discount floor — no approval is needed."
                });
            }

            var existing = await _context.ApprovalRequests
                .Where(a => a.ProposalId == proposal.Id && a.Kind == "below_floor" && a.Status == "pending")
                .OrderByDescending(a => a.CreatedAt)
                .FirstOrDefaultAsync();

            if (existing != null)
            {
                return Ok(new
                {
                    existing.Id,
                    existing.Status,
                    existing.CreatedAt,
                    reused = true,
                    message = "An approval request is already pending for this proposal."
                });
            }

            var requester = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            var floor = _pricing.FloorUnitPrice(catalogAmount, gov.DiscountFloorPercent);

            var request = new ApprovalRequest
            {
                Id = Guid.NewGuid(),
                ProposalId = proposal.Id,
                Kind = "below_floor",
                RequestedBy = userId.Value,
                Status = "pending",
                OfferedPrice = offered,
                FloorPrice = floor,
                CatalogPrice = catalogAmount,
                Message = string.IsNullOrWhiteSpace(body.Message) ? null : body.Message.Trim(),
                CreatedAt = DateTimeOffset.UtcNow
            };

            _context.ApprovalRequests.Add(request);

            var managers = await _context.Users
                .Where(u => u.Active && (u.Role.ToLower() == "admin" || u.Role.ToLower() == "manager"))
                .Select(u => u.Id)
                .ToListAsync();

            var title = "Below-floor price approval";
            var bodyText =
                $"{requester?.Name ?? "An advisor"} requests approval to send {proposal.Reference} " +
                $"at {proposal.Currency} {(offered ?? 0):N0} " +
                $"(floor {proposal.Currency} {(floor ?? 0):N0}).";

            foreach (var managerId in managers.Where(id => id != userId))
            {
                _context.Notifications.Add(new AppNotification
                {
                    Id = Guid.NewGuid(),
                    UserId = managerId,
                    Title = title,
                    Body = bodyText,
                    Kind = "below_floor_approval",
                    RelatedId = request.Id,
                    Read = false,
                    CreatedAt = DateTimeOffset.UtcNow
                });
            }

            await _context.SaveChangesAsync();

            return Ok(new
            {
                request.Id,
                request.Status,
                request.CreatedAt,
                reused = false,
                notified = managers.Count(id => id != userId),
                message = "Managers and admins have been notified."
            });
        }

        // GET: api/approvals/pending — managers/admins
        [HttpGet("pending")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> GetPending()
        {
            var rows = await (
                from a in _context.ApprovalRequests
                where a.Status == "pending" && a.Kind == "below_floor"
                join p in _context.Proposals on a.ProposalId equals p.Id
                join u in _context.Users on a.RequestedBy equals u.Id
                join c in _context.Clients on p.ClientId equals c.Id into clients
                from c in clients.DefaultIfEmpty()
                orderby a.CreatedAt descending
                select new
                {
                    a.Id,
                    a.ProposalId,
                    a.Kind,
                    a.Status,
                    a.OfferedPrice,
                    a.FloorPrice,
                    a.CatalogPrice,
                    a.Message,
                    a.CreatedAt,
                    ProposalReference = p.Reference,
                    Currency = p.Currency,
                    ClientName = c != null ? c.Name : null,
                    RequestedByName = u.Name,
                    RequestedByEmail = u.Email
                }
            ).Take(100).ToListAsync();

            return Ok(rows);
        }

        // GET: api/approvals/for-proposal/{proposalId}
        [HttpGet("for-proposal/{proposalId:guid}")]
        public async Task<IActionResult> ForProposal(Guid proposalId)
        {
            var latest = await _context.ApprovalRequests
                .Where(a => a.ProposalId == proposalId && a.Kind == "below_floor")
                .OrderByDescending(a => a.CreatedAt)
                .Select(a => new
                {
                    a.Id,
                    a.Status,
                    a.OfferedPrice,
                    a.FloorPrice,
                    a.CreatedAt,
                    a.DecidedAt,
                    a.ApproverId
                })
                .FirstOrDefaultAsync();

            return Ok(latest);
        }

        // POST: api/approvals/{id}/decide
        [HttpPost("{id:guid}/decide")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> Decide(Guid id, [FromBody] DecideRequest body)
        {
            var decision = (body.Status ?? "").Trim().ToLowerInvariant();
            if (decision is not ("approved" or "rejected"))
                return BadRequest("Status must be approved or rejected.");

            var request = await _context.ApprovalRequests.FirstOrDefaultAsync(a => a.Id == id);
            if (request == null)
                return NotFound();

            if (request.Status != "pending")
                return BadRequest($"Request is already {request.Status}.");

            var approverId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var aid)
                ? aid
                : (Guid?)null;

            request.Status = decision;
            request.ApproverId = approverId;
            request.DecidedAt = DateTimeOffset.UtcNow;

            var proposal = await _context.Proposals.FirstOrDefaultAsync(p => p.Id == request.ProposalId);
            var approver = approverId == null
                ? null
                : await _context.Users.FirstOrDefaultAsync(u => u.Id == approverId);

            if (proposal != null)
            {
                _context.Notifications.Add(new AppNotification
                {
                    Id = Guid.NewGuid(),
                    UserId = request.RequestedBy,
                    Title = decision == "approved" ? "Below-floor approved" : "Below-floor rejected",
                    Body = decision == "approved"
                        ? $"{approver?.Name ?? "A manager"} approved {proposal.Reference}. You can send or share it now."
                        : $"{approver?.Name ?? "A manager"} rejected the below-floor request for {proposal.Reference}.",
                    Kind = "below_floor_decision",
                    RelatedId = request.Id,
                    Read = false,
                    CreatedAt = DateTimeOffset.UtcNow
                });
            }

            // Mark related manager notifications as read
            var related = await _context.Notifications
                .Where(n => n.RelatedId == request.Id && n.Kind == "below_floor_approval" && !n.Read)
                .ToListAsync();
            foreach (var n in related)
                n.Read = true;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                request.Id,
                request.Status,
                request.DecidedAt,
                request.ApproverId
            });
        }
    }

    public class BelowFloorRequest
    {
        public Guid ProposalId { get; set; }
        public string? Message { get; set; }
    }

    public class DecideRequest
    {
        public string Status { get; set; } = string.Empty;
    }
}
