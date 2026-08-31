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
        private readonly AuditService _audit;

        public ApprovalsController(AppDbContext context, PricingGovernance pricing, AuditService audit)
        {
            _context = context;
            _pricing = pricing;
            _audit = audit;
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

            var access = await BusinessScope.ResolveAsync(_context, User);
            if (!access.Ok)
                return BadRequest(BusinessScope.MissingMessage);

            var proposal = await BusinessScope.Filter(_context.Proposals, access, p => p.BusinessId)
                .FirstOrDefaultAsync(p => p.Id == body.ProposalId);
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
            await _context.SaveChangesAsync();

            await _audit.LogAsync(User, "request", "approval", request.Id, null, new
            {
                kind = "below_floor",
                reference = proposal.Reference,
                offered,
                floor
            });

            return Ok(new
            {
                request.Id,
                request.Status,
                request.CreatedAt,
                reused = false,
                message = "Request logged. A manager can approve it from the bell."
            });
        }

        // POST: api/approvals/high-value — spec §5.6, deals at/above the threshold
        [HttpPost("high-value")]
        public async Task<IActionResult> RequestHighValue([FromBody] BelowFloorRequest body)
        {
            if (body.ProposalId == Guid.Empty)
                return BadRequest("ProposalId is required.");

            var userId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid)
                ? uid
                : (Guid?)null;
            if (userId == null)
                return Unauthorized();

            var access = await BusinessScope.ResolveAsync(_context, User);
            if (!access.Ok)
                return BadRequest(BusinessScope.MissingMessage);

            var proposal = await BusinessScope.Filter(_context.Proposals, access, p => p.BusinessId)
                .FirstOrDefaultAsync(p => p.Id == body.ProposalId);
            if (proposal == null)
                return NotFound("Proposal was not found.");

            var role = User.FindFirstValue(ClaimTypes.Role);
            if (role is not ("Admin" or "Manager") && proposal.AdvisorId != userId)
                return Forbid();

            var item = await _context.ProposalItems.FirstOrDefaultAsync(i => i.ProposalId == proposal.Id);
            if (item == null)
                return BadRequest("Add an instrument before requesting approval.");

            var gov = await _pricing.GetForBusinessAsync(proposal.BusinessId);
            if (!_pricing.IsHighValue(proposal.PriceTotal, gov.HighValueThreshold))
            {
                return BadRequest(new
                {
                    error = "not_high_value",
                    message = "This offer is below the high-value threshold — no approval is needed."
                });
            }

            var existing = await _context.ApprovalRequests
                .Where(a => a.ProposalId == proposal.Id && a.Kind == "high_value" && a.Status == "pending")
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

            var request = new ApprovalRequest
            {
                Id = Guid.NewGuid(),
                ProposalId = proposal.Id,
                Kind = "high_value",
                RequestedBy = userId.Value,
                Status = "pending",
                OfferedPrice = proposal.PriceTotal,
                FloorPrice = gov.HighValueThreshold,
                CatalogPrice = proposal.PriceTotal,
                Message = string.IsNullOrWhiteSpace(body.Message) ? null : body.Message.Trim(),
                CreatedAt = DateTimeOffset.UtcNow
            };

            _context.ApprovalRequests.Add(request);
            await _context.SaveChangesAsync();

            await _audit.LogAsync(User, "request", "approval", request.Id, null, new
            {
                kind = "high_value",
                reference = proposal.Reference,
                offered = proposal.PriceTotal,
                threshold = gov.HighValueThreshold
            });

            return Ok(new
            {
                request.Id,
                request.Status,
                request.CreatedAt,
                reused = false,
                message = "Request logged. A manager can approve it from the bell."
            });
        }

        // GET: api/approvals/pending — managers/admins
        [HttpGet("pending")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> GetPending()
        {
            var access = await BusinessScope.ResolveAsync(_context, User);
            if (!access.Ok)
                return BadRequest(BusinessScope.MissingMessage);

            var rows = await (
                from a in _context.ApprovalRequests
                where a.Status == "pending" && (a.Kind == "below_floor" || a.Kind == "high_value")
                join p in BusinessScope.Filter(_context.Proposals, access, x => x.BusinessId)
                    on a.ProposalId equals p.Id
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
            var access = await BusinessScope.ResolveAsync(_context, User);
            if (!access.Ok)
                return BadRequest(BusinessScope.MissingMessage);

            var owned = await BusinessScope.Filter(_context.Proposals, access, p => p.BusinessId)
                .AnyAsync(p => p.Id == proposalId);
            if (!owned)
                return NotFound();

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

            var access = await BusinessScope.ResolveAsync(_context, User);
            var proposal = await BusinessScope.Filter(_context.Proposals, access, p => p.BusinessId)
                .FirstOrDefaultAsync(p => p.Id == request.ProposalId);
            if (!access.Ok || proposal == null)
                return NotFound();

            var approverId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var aid)
                ? aid
                : (Guid?)null;

            var note = string.IsNullOrWhiteSpace(body.Message) ? null : body.Message.Trim();

            request.Status = decision;
            request.ApproverId = approverId;
            request.DecidedAt = DateTimeOffset.UtcNow;
            if (note != null)
            {
                request.Message = string.IsNullOrWhiteSpace(request.Message)
                    ? note
                    : $"{request.Message}\n— Decision: {note}";
            }

            // The request has been dealt with — drop every notification it produced.
            var related = await _context.Notifications
                .Where(n => n.RelatedId == request.Id)
                .ToListAsync();
            _context.Notifications.RemoveRange(related);

            await _context.SaveChangesAsync();

            await _audit.LogAsync(
                User,
                decision == "approved" ? "approve" : "reject",
                "approval",
                request.Id,
                new
                {
                    status = "pending",
                    kind = request.Kind,
                    reference = proposal?.Reference,
                    offered = request.OfferedPrice
                },
                new
                {
                    status = decision,
                    kind = request.Kind,
                    reference = proposal?.Reference,
                    offered = request.OfferedPrice,
                    note
                });

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
        public string? Message { get; set; }
    }
}
