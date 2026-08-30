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
    [Route("api/[controller]")]
    [Authorize]
    public class ClientsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ProposalExpiryService _expiry;

        public ClientsController(AppDbContext context, ProposalExpiryService expiry)
        {
            _context = context;
            _expiry = expiry;
        }

        [HttpGet]
        public async Task<IActionResult> GetClients()
        {
            var visible = VisibleProposals();
            var stats = await visible
                .GroupBy(p => p.ClientId)
                .Select(g => new
                {
                    ClientId = g.Key,
                    ProposalCount = g.Count(),
                    LastActivityAt = g.Max(p => p.UpdatedAt)
                })
                .ToListAsync();

            var byClient = stats.ToDictionary(s => s.ClientId);

            var clients = await _context.Clients
                .OrderBy(c => c.Name)
                .Select(c => new
                {
                    c.Id,
                    c.BusinessId,
                    c.Name,
                    c.Email,
                    c.Phone,
                    c.Notes,
                    c.CreatedBy,
                    c.CreatedAt,
                    c.UpdatedAt
                })
                .ToListAsync();

            var rows = clients.Select(c =>
            {
                byClient.TryGetValue(c.Id, out var stat);
                return new
                {
                    c.Id,
                    c.BusinessId,
                    c.Name,
                    c.Email,
                    c.Phone,
                    c.Notes,
                    c.CreatedBy,
                    c.CreatedAt,
                    c.UpdatedAt,
                    ProposalCount = stat?.ProposalCount ?? 0,
                    LastActivityAt = stat?.LastActivityAt
                };
            });

            return Ok(rows);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetClient(Guid id)
        {
            var client = await _context.Clients
                .FirstOrDefaultAsync(c => c.Id == id);

            if (client == null)
            {
                return NotFound();
            }

            return Ok(client);
        }

        [HttpGet("{id}/activity")]
        public async Task<IActionResult> GetActivity(Guid id)
        {
            var client = await _context.Clients.FirstOrDefaultAsync(c => c.Id == id);
            if (client == null)
                return NotFound();

            await _expiry.ExpireOverdueAsync();

            var proposals = await (
                from p in VisibleProposals().Where(p => p.ClientId == id)
                join adv in _context.Users on p.AdvisorId equals adv.Id into advisors
                from adv in advisors.DefaultIfEmpty()
                orderby p.UpdatedAt descending
                select new
                {
                    p.Id,
                    p.Reference,
                    p.Status,
                    p.PriceTotal,
                    p.Currency,
                    p.CreatedAt,
                    p.UpdatedAt,
                    p.SentAt,
                    p.ExpiresAt,
                    AdvisorName = adv != null ? adv.Name : null,
                    Instrument = _context.ProposalItems
                        .Where(i => i.ProposalId == p.Id)
                        .Join(_context.Products, i => i.ProductId, pr => pr.Id, (i, pr) => pr.Model)
                        .FirstOrDefault()
                }
            ).ToListAsync();

            var proposalIds = proposals.Select(p => p.Id).ToList();
            var shares = proposalIds.Count == 0
                ? new List<ShareLink>()
                : await _context.ShareLinks
                    .Where(s => proposalIds.Contains(s.ProposalId) && !s.Revoked)
                    .ToListAsync();

            var activity = new List<object>
            {
                new
                {
                    at = client.CreatedAt,
                    kind = "client_created",
                    title = "Added to the directory",
                    detail = (string?)null,
                    proposalId = (Guid?)null,
                    reference = (string?)null
                }
            };

            foreach (var p in proposals)
            {
                var instrument = string.IsNullOrWhiteSpace(p.Instrument) ? null : p.Instrument;
                activity.Add(new
                {
                    at = p.CreatedAt,
                    kind = "proposal_created",
                    title = $"Proposal {p.Reference} created",
                    detail = instrument,
                    proposalId = (Guid?)p.Id,
                    reference = p.Reference
                });

                if (p.SentAt != null)
                {
                    activity.Add(new
                    {
                        at = p.SentAt.Value,
                        kind = "proposal_sent",
                        title = $"Proposal {p.Reference} sent",
                        detail = instrument,
                        proposalId = (Guid?)p.Id,
                        reference = p.Reference
                    });
                }

                if (string.Equals(p.Status, "viewed", StringComparison.OrdinalIgnoreCase))
                {
                    activity.Add(new
                    {
                        at = p.UpdatedAt,
                        kind = "proposal_viewed",
                        title = $"Client opened {p.Reference}",
                        detail = instrument,
                        proposalId = (Guid?)p.Id,
                        reference = p.Reference
                    });
                }

                if (string.Equals(p.Status, "expired", StringComparison.OrdinalIgnoreCase))
                {
                    activity.Add(new
                    {
                        at = p.ExpiresAt ?? p.UpdatedAt,
                        kind = "proposal_expired",
                        title = $"Proposal {p.Reference} expired",
                        detail = instrument,
                        proposalId = (Guid?)p.Id,
                        reference = p.Reference
                    });
                }

                foreach (var share in shares.Where(s => s.ProposalId == p.Id))
                {
                    activity.Add(new
                    {
                        at = share.CreatedAt,
                        kind = "share_created",
                        title = $"Share link created for {p.Reference}",
                        detail = instrument,
                        proposalId = (Guid?)p.Id,
                        reference = p.Reference
                    });
                }
            }

            var ordered = activity
                .OrderByDescending(a => ((dynamic)a).at)
                .ToList();

            return Ok(new
            {
                Client = new
                {
                    client.Id,
                    client.Name,
                    client.Email,
                    client.Phone,
                    client.Notes,
                    client.CreatedAt
                },
                Proposals = proposals,
                LastProposalId = proposals.FirstOrDefault()?.Id,
                Activity = ordered
            });
        }

        [HttpPost]
        public async Task<IActionResult> CreateClient(CreateClientRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest("Client name is required.");
            }

            var business = await _context.Businesses
                .FirstOrDefaultAsync(b => b.Slug == "house-of-pianos");

            if (business == null)
            {
                return BadRequest("Default business was not found.");
            }

            var client = new Client
            {
                Id = Guid.NewGuid(),
                BusinessId = business.Id,
                Name = request.Name.Trim(),
                Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
                Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
                Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
                CreatedBy = request.CreatedBy,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            _context.Clients.Add(client);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetClient), new { id = client.Id }, client);
        }

        [HttpPatch("{id}")]
        public async Task<IActionResult> UpdateClient(Guid id, UpdateClientRequest request)
        {
            var client = await _context.Clients
                .FirstOrDefaultAsync(c => c.Id == id);

            if (client == null)
            {
                return NotFound();
            }

            if (request.Name != null)
            {
                if (!string.IsNullOrWhiteSpace(request.Name))
                {
                    client.Name = request.Name.Trim();
                }
            }

            if (request.Email != null)
            {
                client.Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
            }

            if (request.Phone != null)
            {
                client.Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
            }

            if (request.Notes != null)
            {
                client.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
            }

            client.UpdatedAt = DateTimeOffset.UtcNow;

            await _context.SaveChangesAsync();

            return Ok(client);
        }

        // DELETE: api/clients/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteClient(Guid id)
        {
            var client = await _context.Clients
                .FirstOrDefaultAsync(c => c.Id == id);

            if (client == null)
            {
                return NotFound();
            }

            var proposalCount = await _context.Proposals.CountAsync(p => p.ClientId == id);
            if (proposalCount > 0)
            {
                return Conflict(new
                {
                    message = $"Cannot delete this client — {proposalCount} proposal(s) still reference them. Delete or reassign those proposals first."
                });
            }

            _context.Clients.Remove(client);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private IQueryable<Proposal> VisibleProposals()
        {
            var role = User.FindFirstValue(ClaimTypes.Role);
            var query = _context.Proposals.AsQueryable();
            if (role is "Admin" or "Manager")
                return query;

            if (Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid))
                return query.Where(p => p.AdvisorId == uid);

            return query.Where(_ => false);
        }
    }

    public class CreateClientRequest
    {
        public string Name { get; set; } = string.Empty;

        public string? Email { get; set; }

        public string? Phone { get; set; }

        public string? Notes { get; set; }

        public Guid? CreatedBy { get; set; }
    }

    public class UpdateClientRequest
    {
        public string? Name { get; set; }

        public string? Email { get; set; }

        public string? Phone { get; set; }

        public string? Notes { get; set; }
    }
}