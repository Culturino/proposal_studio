using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProposalStudio.Data;
using ProposalStudio.Models;

namespace ProposalStudio.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProposalsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public ProposalsController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetProposals()
        {
            var proposals = await (
                from proposal in _context.Proposals
                join client in _context.Clients
                    on proposal.ClientId equals client.Id
                join advisor in _context.Users
                    on proposal.AdvisorId equals advisor.Id
                join template in _context.Templates
                    on proposal.TemplateId equals template.Id
                orderby proposal.CreatedAt descending
                select new
                {
                    proposal.Id,
                    proposal.Reference,
                    proposal.ReferenceNumber,
                    proposal.Status,
                    proposal.Currency,
                    proposal.VatMode,
                    proposal.ValidityDays,
                    proposal.PriceTotal,
                    proposal.CreatedAt,
                    proposal.UpdatedAt,
                    Client = new
                    {
                        client.Id,
                        client.Name,
                        client.Email,
                        client.Phone
                    },
                    Advisor = new
                    {
                        advisor.Id,
                        advisor.Name,
                        advisor.Email
                    },
                    Template = new
                    {
                        template.Id,
                        template.Name,
                        template.Version
                    }
                }
            ).ToListAsync();

            return Ok(proposals);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetProposal(Guid id)
        {
            var proposal = await (
                from p in _context.Proposals
                join client in _context.Clients
                    on p.ClientId equals client.Id
                join advisor in _context.Users
                    on p.AdvisorId equals advisor.Id
                join template in _context.Templates
                    on p.TemplateId equals template.Id
                where p.Id == id
                select new
                {
                    p.Id,
                    p.BusinessId,
                    p.Reference,
                    p.ReferenceNumber,
                    p.Status,
                    p.Currency,
                    p.VatMode,
                    p.ValidityDays,
                    p.PriceTotal,
                    p.CreatedAt,
                    p.UpdatedAt,
                    p.SentAt,
                    p.ExpiresAt,
                    p.PdfUrl,
                    Client = new
                    {
                        client.Id,
                        client.Name,
                        client.Email,
                        client.Phone,
                        client.Notes
                    },
                    Advisor = new
                    {
                        advisor.Id,
                        advisor.Name,
                        advisor.Email
                    },
                    Template = new
                    {
                        template.Id,
                        template.Key,
                        template.Name,
                        template.Version
                    },
                    Items = _context.ProposalItems
                        .Where(i => i.ProposalId == p.Id)
                        .Select(i => new
                        {
                            i.Id,
                            i.ProductId,
                            i.Finish,
                            i.Qty,
                            i.UnitPrice,
                            i.PriceOverride,
                            i.Included,
                            i.Excluded,
                            i.Addons
                        })
                        .ToList()
                }
            ).FirstOrDefaultAsync();

            if (proposal == null)
            {
                return NotFound();
            }

            return Ok(proposal);
        }

        [HttpPost]
        public async Task<IActionResult> CreateProposal(CreateProposalRequest request)
        {
            var business = await _context.Businesses
                .FirstOrDefaultAsync(b => b.Slug == "house-of-pianos");

            if (business == null)
            {
                return BadRequest("Default business was not found.");
            }

            var client = await _context.Clients
                .FirstOrDefaultAsync(c => c.Id == request.ClientId && c.BusinessId == business.Id);

            if (client == null)
            {
                return BadRequest("Client was not found.");
            }

            var template = await _context.Templates
                .FirstOrDefaultAsync(t =>
                    t.BusinessId == business.Id &&
                    t.Key == "piano_luxury" &&
                    t.Active);

            if (template == null)
            {
                return BadRequest("Piano Luxury template was not found.");
            }

            User? advisor;

            if (request.AdvisorId.HasValue)
            {
                advisor = await _context.Users
                    .FirstOrDefaultAsync(u =>
                        u.Id == request.AdvisorId.Value &&
                        u.BusinessId == business.Id &&
                        u.Active);
            }
            else
            {
                advisor = await _context.Users
                    .FirstOrDefaultAsync(u =>
                        u.BusinessId == business.Id &&
                        u.Active &&
                        u.Role == "advisor");
            }

            if (advisor == null)
            {
                return BadRequest("Advisor was not found.");
            }

            var lastReferenceNumber = await _context.Proposals
                .Where(p => p.BusinessId == business.Id)
                .MaxAsync(p => (int?)p.ReferenceNumber);

            var nextReferenceNumber = (lastReferenceNumber ?? 1000) + 1;
            var reference = $"{business.ReferencePrefix}-{nextReferenceNumber}";

            var now = DateTimeOffset.UtcNow;

            var proposal = new Proposal
            {
                Id = Guid.NewGuid(),
                BusinessId = business.Id,
                ReferenceNumber = nextReferenceNumber,
                Reference = reference,
                TemplateId = template.Id,
                TemplateVersion = template.Version,
                ClientId = client.Id,
                AdvisorId = advisor.Id,
                Currency = string.IsNullOrWhiteSpace(request.Currency) ? "AED" : request.Currency,
                VatMode = string.IsNullOrWhiteSpace(request.VatMode) ? "line" : request.VatMode,
                ValidityDays = request.ValidityDays <= 0 ? 14 : request.ValidityDays,
                Status = "draft",
                PriceTotal = null,
                CreatedAt = now,
                UpdatedAt = now,
                SentAt = null,
                ExpiresAt = now.AddDays(request.ValidityDays <= 0 ? 14 : request.ValidityDays),
                Snapshot = null,
                PdfUrl = null
            };

            _context.Proposals.Add(proposal);

            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetProposal), new { id = proposal.Id }, new
            {
                proposal.Id,
                proposal.Reference,
                proposal.Status,
                proposal.ClientId,
                proposal.AdvisorId,
                proposal.TemplateId,
                proposal.TemplateVersion,
                proposal.Currency,
                proposal.VatMode,
                proposal.ValidityDays,
                proposal.ExpiresAt
            });
        }

        [HttpPut("{id}/item")]
        public async Task<IActionResult> UpsertProposalItem(Guid id, UpsertProposalItemRequest request)
        {
            var proposal = await _context.Proposals
                .FirstOrDefaultAsync(p => p.Id == id);

            if (proposal == null)
            {
                return NotFound("Proposal was not found.");
            }

            if (proposal.Status != "draft")
            {
                return BadRequest("Only draft proposals can be edited.");
            }

            var product = await _context.Products
                .FirstOrDefaultAsync(p => p.Id == request.ProductId && p.Status == "active");

            if (product == null)
            {
                return BadRequest("Product was not found.");
            }

            var catalogPrice = await _context.Prices
                .Where(p => p.ProductId == product.Id && p.Currency == proposal.Currency)
                .OrderByDescending(p => p.ValidFrom)
                .FirstOrDefaultAsync();

            var unitPrice = request.UnitPrice ?? catalogPrice?.Amount;

            var existingItem = await _context.ProposalItems
                .FirstOrDefaultAsync(i => i.ProposalId == proposal.Id);

            var now = DateTimeOffset.UtcNow;

            if (existingItem == null)
            {
                existingItem = new ProposalItem
                {
                    Id = Guid.NewGuid(),
                    ProposalId = proposal.Id,
                    ProductId = product.Id,
                    Finish = request.Finish,
                    Qty = request.Qty <= 0 ? 1 : request.Qty,
                    UnitPrice = unitPrice,
                    PriceOverride = request.PriceOverride,
                    Included = request.Included ?? product.DefaultIncludes,
                    Excluded = request.Excluded ?? product.DefaultExcludes,
                    Addons = System.Text.Json.JsonDocument.Parse(request.AddonsJson ?? "[]"),
                    CreatedAt = now,
                    UpdatedAt = now
                };

                _context.ProposalItems.Add(existingItem);
            }
            else
            {
                existingItem.ProductId = product.Id;
                existingItem.Finish = request.Finish;
                existingItem.Qty = request.Qty <= 0 ? 1 : request.Qty;
                existingItem.UnitPrice = unitPrice;
                existingItem.PriceOverride = request.PriceOverride;
                existingItem.Included = request.Included ?? product.DefaultIncludes;
                existingItem.Excluded = request.Excluded ?? product.DefaultExcludes;
                existingItem.Addons = System.Text.Json.JsonDocument.Parse(request.AddonsJson ?? "[]");
                existingItem.UpdatedAt = now;
            }

            var finalUnitPrice = existingItem.PriceOverride ?? existingItem.UnitPrice ?? 0;
            proposal.PriceTotal = finalUnitPrice * existingItem.Qty;
            proposal.UpdatedAt = now;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                ProposalId = proposal.Id,
                proposal.Reference,
                proposal.Status,
                proposal.PriceTotal,
                Item = new
                {
                    existingItem.Id,
                    existingItem.ProductId,
                    ProductModel = product.Model,
                    existingItem.Finish,
                    existingItem.Qty,
                    existingItem.UnitPrice,
                    existingItem.PriceOverride,
                    FinalUnitPrice = finalUnitPrice,
                    existingItem.Included,
                    existingItem.Excluded,
                    existingItem.Addons
                }
            });
        }

        [HttpGet("{id}/preview")]
        public async Task<IActionResult> Preview(Guid id)
        {
            var proposal = await _context.Proposals
                .FirstOrDefaultAsync(p => p.Id == id);

            if (proposal == null)
            {
                return NotFound();
            }

            var business = await _context.Businesses
                .FirstOrDefaultAsync(b => b.Id == proposal.BusinessId);

            var client = await _context.Clients
                .FirstOrDefaultAsync(c => c.Id == proposal.ClientId);

            var advisor = await _context.Users
                .FirstOrDefaultAsync(u => u.Id == proposal.AdvisorId);

            var template = await _context.Templates
                .FirstOrDefaultAsync(t => t.Id == proposal.TemplateId);

            var item = await _context.ProposalItems
                .FirstOrDefaultAsync(i => i.ProposalId == proposal.Id);

            if (item == null)
            {
                return Ok(new
                {
                    Business = business,
                    Proposal = proposal,
                    Client = client,
                    Advisor = advisor,
                    Template = template,
                    Item = (object?)null
                });
            }

            var product = await _context.Products
                .FirstOrDefaultAsync(p => p.Id == item.ProductId);

            var brand = product == null
                ? null
                : await _context.Brands.FirstOrDefaultAsync(b => b.Id == product.BrandId);

            var category = product == null
                ? null
                : await _context.ProductCategories.FirstOrDefaultAsync(c => c.Id == product.CategoryId);

            return Ok(new
            {
                Business = business,
                Proposal = proposal,
                Client = client,
                Advisor = advisor,
                Template = template,
                Item = new
                {
                    item.Id,
                    item.Qty,
                    item.Finish,
                    item.UnitPrice,
                    item.PriceOverride,
                    item.Included,
                    item.Excluded,
                    item.Addons,
                    Product = product,
                    Brand = brand,
                    Category = category
                }
            });
        }

        // PATCH: api/proposals/{id}
        [HttpPatch("{id}")]
        public async Task<IActionResult> UpdateProposal(Guid id, [FromBody] UpdateProposalRequest request)
        {
            var proposal = await _context.Proposals.FirstOrDefaultAsync(p => p.Id == id);

            if (proposal == null)
            {
                return NotFound();
            }

            if (request.Status != null)
            {
                proposal.Status = request.Status;
            }

            if (request.ValidityDays.HasValue)
            {
                proposal.ValidityDays = request.ValidityDays.Value;
                proposal.ExpiresAt = proposal.SentAt ?? proposal.CreatedAt;
                proposal.ExpiresAt = proposal.ExpiresAt.Value.AddDays(request.ValidityDays.Value);
            }

            if (request.Currency != null && !string.IsNullOrWhiteSpace(request.Currency))
            {
                proposal.Currency = request.Currency.Trim();
            }

            if (request.AdvisorId.HasValue)
            {
                proposal.AdvisorId = request.AdvisorId.Value;
            }

            proposal.UpdatedAt = DateTimeOffset.UtcNow;

            await _context.SaveChangesAsync();

            return Ok(proposal);
        }

        // DELETE: api/proposals/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteProposal(Guid id)
        {
            var proposal = await _context.Proposals.FirstOrDefaultAsync(p => p.Id == id);

            if (proposal == null)
            {
                return NotFound();
            }

            _context.Proposals.Remove(proposal);
            await _context.SaveChangesAsync();

            return NoContent();
        }
    }

    public class CreateProposalRequest
    {
        public Guid ClientId { get; set; }
        public Guid? AdvisorId { get; set; }
        public string Currency { get; set; } = "AED";
        public string VatMode { get; set; } = "line";
        public int ValidityDays { get; set; } = 14;
    }

    public class UpsertProposalItemRequest
    {
        public Guid ProductId { get; set; }
        public string? Finish { get; set; }
        public int Qty { get; set; } = 1;
        public decimal? UnitPrice { get; set; }
        public decimal? PriceOverride { get; set; }
        public string[]? Included { get; set; }
        public string[]? Excluded { get; set; }
        public string? AddonsJson { get; set; }
    }

    public class UpdateProposalRequest
    {
        public string? Status { get; set; }
        public int? ValidityDays { get; set; }
        public string? Currency { get; set; }
        public Guid? AdvisorId { get; set; }
    }
}