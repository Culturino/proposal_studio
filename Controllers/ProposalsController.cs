using System.Security.Claims;
using System.Text.Json;
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
    public class ProposalsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly PricingGovernance _pricing;
        private readonly ProposalPdfService _pdf;
        private readonly ProposalExpiryService _expiry;
        private readonly AuditService _audit;

        public ProposalsController(
            AppDbContext context,
            PricingGovernance pricing,
            ProposalPdfService pdf,
            ProposalExpiryService expiry,
            AuditService audit)
        {
            _context = context;
            _pricing = pricing;
            _pdf = pdf;
            _expiry = expiry;
            _audit = audit;
        }

        // GET: api/proposals?status=&advisor=&q=
        [HttpGet]
        public async Task<IActionResult> GetProposals(
            [FromQuery] string? status = null,
            [FromQuery] Guid? advisor = null,
            [FromQuery] string? q = null)
        {
            var role = User.FindFirstValue(ClaimTypes.Role);
            var userId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid)
                ? uid
                : (Guid?)null;

            await _expiry.ExpireOverdueAsync();

            var query =
                from proposal in _context.Proposals
                join client in _context.Clients on proposal.ClientId equals client.Id
                join adv in _context.Users on proposal.AdvisorId equals adv.Id
                join template in _context.Templates on proposal.TemplateId equals template.Id
                select new { proposal, client, adv, template };

            // Managers & Admins see all; everyone else only their own
            var canSeeAll = role is "Admin" or "Manager";
            if (!canSeeAll && userId.HasValue)
            {
                query = query.Where(x => x.proposal.AdvisorId == userId.Value);
            }
            else if (canSeeAll && advisor.HasValue)
            {
                query = query.Where(x => x.proposal.AdvisorId == advisor.Value);
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(x => x.proposal.Status == status);
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim().ToLower();
                query = query.Where(x =>
                    x.proposal.Reference.ToLower().Contains(term) ||
                    x.client.Name.ToLower().Contains(term));
            }

            var proposals = await query
                .OrderByDescending(x => x.proposal.CreatedAt)
                .Select(x => new
                {
                    x.proposal.Id,
                    x.proposal.Reference,
                    x.proposal.ReferenceNumber,
                    x.proposal.Status,
                    x.proposal.Currency,
                    x.proposal.VatMode,
                    x.proposal.ValidityDays,
                    x.proposal.PriceTotal,
                    x.proposal.CreatedAt,
                    x.proposal.UpdatedAt,
                    x.proposal.ExpiresAt,
                    x.proposal.PdfUrl,
                    Client = new { x.client.Id, x.client.Name, x.client.Email, x.client.Phone },
                    Advisor = new { x.adv.Id, x.adv.Name, x.adv.Email },
                    Template = new { x.template.Id, x.template.Name, x.template.Version },
                    Instrument = _context.ProposalItems
                        .Where(i => i.ProposalId == x.proposal.Id)
                        .Join(_context.Products, i => i.ProductId, p => p.Id, (i, p) => p.Model)
                        .FirstOrDefault()
                })
                .ToListAsync();

            return Ok(proposals);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetProposal(Guid id)
        {
            if (!await CanAccessProposalAsync(id))
            {
                return Forbid();
            }

            await _expiry.ExpireOverdueAsync();

            var proposal = await (
                from p in _context.Proposals
                join client in _context.Clients on p.ClientId equals client.Id
                join advisor in _context.Users on p.AdvisorId equals advisor.Id
                join template in _context.Templates on p.TemplateId equals template.Id
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
                    Client = new { client.Id, client.Name, client.Email, client.Phone, client.Notes },
                    Advisor = new { advisor.Id, advisor.Name, advisor.Email },
                    Template = new { template.Id, template.Key, template.Name, template.Version },
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

            Template? template = null;
            if (request.TemplateId.HasValue)
            {
                template = await _context.Templates.FirstOrDefaultAsync(t =>
                    t.Id == request.TemplateId.Value &&
                    t.BusinessId == business.Id &&
                    t.Active);
            }

            template ??= await _context.Templates
                .Where(t =>
                    t.BusinessId == business.Id &&
                    t.Key == ProposalRendererCatalog.PianoLuxury &&
                    t.Active)
                .OrderByDescending(t => t.Version)
                .FirstOrDefaultAsync();

            if (template == null)
            {
                return BadRequest("Piano Luxury template was not found.");
            }

            var callerId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var cid)
                ? cid
                : (Guid?)null;

            User? advisor = null;
            if (request.AdvisorId.HasValue)
            {
                advisor = await _context.Users.FirstOrDefaultAsync(u =>
                    u.Id == request.AdvisorId.Value &&
                    u.BusinessId == business.Id &&
                    u.Active);
            }
            else if (callerId.HasValue)
            {
                advisor = await _context.Users.FirstOrDefaultAsync(u =>
                    u.Id == callerId.Value && u.Active);
            }

            advisor ??= await _context.Users.FirstOrDefaultAsync(u =>
                u.BusinessId == business.Id && u.Active && u.Role == "advisor");

            if (advisor == null)
            {
                return BadRequest("Advisor was not found.");
            }

            var gov = await _pricing.GetForBusinessAsync(business.Id);
            var lastReferenceNumber = await _context.Proposals
                .Where(p => p.BusinessId == business.Id)
                .MaxAsync(p => (int?)p.ReferenceNumber);

            var nextReferenceNumber = (lastReferenceNumber ?? 1000) + 1;
            var reference = $"{business.ReferencePrefix}-{nextReferenceNumber}";
            var now = DateTimeOffset.UtcNow;
            var validity = request.ValidityDays <= 0 ? 14 : request.ValidityDays;

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
                VatMode = string.IsNullOrWhiteSpace(request.VatMode)
                    ? gov.VatDefaultMode
                    : request.VatMode,
                ValidityDays = validity,
                Status = "draft",
                PriceTotal = null,
                CreatedAt = now,
                UpdatedAt = now,
                SentAt = null,
                ExpiresAt = now.AddDays(validity),
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
            if (!await CanAccessProposalAsync(id))
            {
                return Forbid();
            }

            var proposal = await _context.Proposals.FirstOrDefaultAsync(p => p.Id == id);
            if (proposal == null)
            {
                return NotFound("Proposal was not found.");
            }

            if (proposal.Status is not ("draft" or "sent" or "viewed"))
            {
                return BadRequest("Only draft, sent, or viewed proposals can be edited.");
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
            var offered = request.PriceOverride ?? unitPrice;

            var gov = await _pricing.GetForBusinessAsync(proposal.BusinessId);
            var belowFloor = _pricing.IsBelowFloor(offered, catalogPrice?.Amount, gov.DiscountFloorPercent);
            var floor = _pricing.FloorUnitPrice(catalogPrice?.Amount, gov.DiscountFloorPercent);

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
                    Addons = JsonDocument.Parse(request.AddonsJson ?? "[]"),
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
                existingItem.Addons = JsonDocument.Parse(request.AddonsJson ?? "[]");
                existingItem.UpdatedAt = now;
            }

            var finalUnitPrice = existingItem.PriceOverride ?? existingItem.UnitPrice ?? 0;
            proposal.PriceTotal = ProposalQuote.Total(
                existingItem.PriceOverride ?? existingItem.UnitPrice,
                existingItem.Qty,
                existingItem.Addons);
            proposal.UpdatedAt = now;

            await _context.SaveChangesAsync();
            await _pdf.RefreshContentAsync(_context, proposal.Id);

            return Ok(new
            {
                ProposalId = proposal.Id,
                proposal.Reference,
                proposal.Status,
                proposal.PriceTotal,
                Governance = new
                {
                    BelowFloor = belowFloor,
                    FloorUnitPrice = floor,
                    CatalogAmount = catalogPrice?.Amount,
                    DiscountFloorPercent = gov.DiscountFloorPercent,
                    HighValue = _pricing.IsHighValue(proposal.PriceTotal, gov.HighValueThreshold),
                    gov.HighValueThreshold
                },
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
            if (!await CanAccessProposalAsync(id))
            {
                return Forbid();
            }

            var proposal = await _context.Proposals.FirstOrDefaultAsync(p => p.Id == id);
            if (proposal == null)
            {
                return NotFound();
            }

            var business = await _context.Businesses.FirstOrDefaultAsync(b => b.Id == proposal.BusinessId);
            var client = await _context.Clients.FirstOrDefaultAsync(c => c.Id == proposal.ClientId);
            var advisor = await _context.Users.FirstOrDefaultAsync(u => u.Id == proposal.AdvisorId);
            var template = await _context.Templates.FirstOrDefaultAsync(t => t.Id == proposal.TemplateId);
            var item = await _context.ProposalItems.FirstOrDefaultAsync(i => i.ProposalId == proposal.Id);
            var gov = await _pricing.GetForBusinessAsync(proposal.BusinessId);

            if (item == null)
            {
                return Ok(new
                {
                    Business = business,
                    Proposal = proposal,
                    Client = client,
                    Advisor = advisor,
                    Template = template,
                    Item = (object?)null,
                    Governance = new
                    {
                        BelowFloor = false,
                        FloorUnitPrice = (decimal?)null,
                        DiscountFloorPercent = gov.DiscountFloorPercent,
                        HighValue = false,
                        gov.HighValueThreshold
                    }
                });
            }

            var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == item.ProductId);
            var brand = product == null
                ? null
                : await _context.Brands.FirstOrDefaultAsync(b => b.Id == product.BrandId);
            var category = product == null
                ? null
                : await _context.ProductCategories.FirstOrDefaultAsync(c => c.Id == product.CategoryId);

            var catalogAmount = product == null
                ? null
                : await _context.Prices
                    .Where(p => p.ProductId == product.Id && p.Currency == proposal.Currency)
                    .OrderByDescending(p => p.ValidFrom)
                    .Select(p => p.Amount)
                    .FirstOrDefaultAsync();

            var offered = item.PriceOverride ?? item.UnitPrice;
            var belowFloor = _pricing.IsBelowFloor(offered, catalogAmount, gov.DiscountFloorPercent);
            var highValue = _pricing.IsHighValue(proposal.PriceTotal, gov.HighValueThreshold);
            var approval = await GetLatestApprovalAsync(id, "below_floor");
            var highValueApproval = await GetLatestApprovalAsync(id, "high_value");

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
                },
                Governance = new
                {
                    BelowFloor = belowFloor,
                    FloorUnitPrice = _pricing.FloorUnitPrice(catalogAmount, gov.DiscountFloorPercent),
                    CatalogAmount = catalogAmount,
                    DiscountFloorPercent = gov.DiscountFloorPercent,
                    HighValue = highValue,
                    gov.HighValueThreshold,
                    ApprovalStatus = approval?.Status,
                    ApprovalId = approval?.Id,
                    FloorApproved = approval?.Status == "approved",
                    HighValueApprovalStatus = highValueApproval?.Status,
                    HighValueApprovalId = highValueApproval?.Id,
                    HighValueApproved = highValueApproval?.Status == "approved"
                }
            });
        }

        // POST: api/proposals/{id}/generate-pdf — refresh stored content snapshot (PDF rendered on demand)
        [HttpPost("{id}/generate-pdf")]
        public async Task<IActionResult> GeneratePdf(Guid id)
        {
            if (!await CanAccessProposalAsync(id))
            {
                return Forbid();
            }

            var ok = await _pdf.RefreshContentAsync(_context, id);
            if (!ok)
            {
                return NotFound("Proposal or item was not found.");
            }

            var proposal = await _context.Proposals.FirstAsync(p => p.Id == id);
            return Ok(new
            {
                proposal.Id,
                proposal.Reference,
                snapshotStored = proposal.Snapshot != null,
                message = "Proposal content snapshot refreshed. PDF is rendered on demand from the latest data."
            });
        }

        // GET: api/proposals/{id}/pdf — render from stored content snapshot (immutable view)
        [HttpGet("{id}/pdf")]
        public async Task<IActionResult> DownloadPdf(Guid id)
        {
            if (!await CanAccessProposalAsync(id))
            {
                return Forbid();
            }

            var rendered = await _pdf.RenderAsync(_context, id);
            if (rendered == null)
            {
                return NotFound("Proposal or item was not found.");
            }

            var (bytes, fileName) = rendered.Value;
            return File(bytes, "application/pdf", fileName);
        }

        // POST: api/proposals/{id}/duplicate — new draft for the same client, copied config
        [HttpPost("{id}/duplicate")]
        public async Task<IActionResult> Duplicate(Guid id)
        {
            if (!await CanAccessProposalAsync(id))
            {
                return Forbid();
            }

            var source = await _context.Proposals.FirstOrDefaultAsync(p => p.Id == id);
            if (source == null)
                return NotFound();

            var callerId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var cid)
                ? cid
                : source.AdvisorId;

            var advisorExists = await _context.Users.AnyAsync(u => u.Id == callerId && u.Active);
            if (!advisorExists)
                callerId = source.AdvisorId;

            var lastReferenceNumber = await _context.Proposals
                .Where(p => p.BusinessId == source.BusinessId)
                .MaxAsync(p => (int?)p.ReferenceNumber);

            var business = await _context.Businesses.FirstOrDefaultAsync(b => b.Id == source.BusinessId);
            var nextReferenceNumber = (lastReferenceNumber ?? 1000) + 1;
            var prefix = business?.ReferencePrefix ?? "HOP";
            var now = DateTimeOffset.UtcNow;

            var copy = new Proposal
            {
                Id = Guid.NewGuid(),
                BusinessId = source.BusinessId,
                ReferenceNumber = nextReferenceNumber,
                Reference = $"{prefix}-{nextReferenceNumber}",
                TemplateId = source.TemplateId,
                TemplateVersion = source.TemplateVersion,
                ClientId = source.ClientId,
                AdvisorId = callerId,
                Currency = source.Currency,
                VatMode = source.VatMode,
                ValidityDays = source.ValidityDays,
                Status = "draft",
                PriceTotal = source.PriceTotal,
                CreatedAt = now,
                UpdatedAt = now,
                SentAt = null,
                ExpiresAt = now.AddDays(source.ValidityDays > 0 ? source.ValidityDays : 14),
                Snapshot = null,
                PdfUrl = null
            };

            _context.Proposals.Add(copy);

            var sourceItem = await _context.ProposalItems
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.ProposalId == id);
            if (sourceItem != null)
            {
                // jsonb JsonDocument from Npgsql is often already disposed on the tracked
                // entity — read the column as text so we can clone it safely.
                var addonsJson = await _context.Database
                    .SqlQueryRaw<string>(
                        """SELECT COALESCE(addons::text, '[]') AS "Value" FROM proposal_items WHERE proposal_id = {0}""",
                        id)
                    .FirstOrDefaultAsync();
                if (string.IsNullOrWhiteSpace(addonsJson))
                    addonsJson = "[]";

                JsonDocument addons;
                try
                {
                    addons = JsonDocument.Parse(addonsJson);
                }
                catch (JsonException)
                {
                    addons = JsonDocument.Parse("[]");
                }

                _context.ProposalItems.Add(new ProposalItem
                {
                    Id = Guid.NewGuid(),
                    ProposalId = copy.Id,
                    ProductId = sourceItem.ProductId,
                    Finish = sourceItem.Finish,
                    Qty = sourceItem.Qty <= 0 ? 1 : sourceItem.Qty,
                    UnitPrice = sourceItem.UnitPrice,
                    PriceOverride = sourceItem.PriceOverride,
                    Included = sourceItem.Included?.ToArray() ?? Array.Empty<string>(),
                    Excluded = sourceItem.Excluded?.ToArray() ?? Array.Empty<string>(),
                    Addons = addons,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }

            await _context.SaveChangesAsync();
            if (sourceItem != null)
            {
                try
                {
                    await _pdf.RefreshContentAsync(_context, copy.Id);
                }
                catch
                {
                    // The draft is already saved; preview can rebuild the snapshot later.
                }
            }

            return Ok(new
            {
                copy.Id,
                copy.Reference,
                copy.Status,
                copy.ClientId,
                sourceId = source.Id,
                sourceReference = source.Reference
            });
        }

        // POST: api/proposals/{id}/send — Phase 1: mark sent; block below-floor prices
        [HttpPost("{id}/send")]
        public async Task<IActionResult> Send(Guid id)
        {
            if (!await CanAccessProposalAsync(id))
            {
                return Forbid();
            }

            var proposal = await _context.Proposals.FirstOrDefaultAsync(p => p.Id == id);
            if (proposal == null)
            {
                return NotFound();
            }

            if (_expiry.ApplyIfOverdue(proposal))
                await _context.SaveChangesAsync();

            if (proposal.Status != "draft" && proposal.Status != "sent")
            {
                return BadRequest($"Cannot send a proposal in status '{proposal.Status}'.");
            }

            var item = await _context.ProposalItems.FirstOrDefaultAsync(i => i.ProposalId == id);
            if (item == null)
            {
                return BadRequest("Add an instrument before sending.");
            }

            var catalogAmount = await _context.Prices
                .Where(p => p.ProductId == item.ProductId && p.Currency == proposal.Currency)
                .OrderByDescending(p => p.ValidFrom)
                .Select(p => p.Amount)
                .FirstOrDefaultAsync();

            var blocked = await GovernanceSendBlockAsync(proposal, item, catalogAmount);
            if (blocked != null)
            {
                return blocked;
            }

            await _pdf.RefreshContentAsync(_context, id);

            var now = DateTimeOffset.UtcNow;
            proposal.Status = "sent";
            proposal.SentAt = now;
            proposal.ExpiresAt = now.AddDays(proposal.ValidityDays);
            proposal.UpdatedAt = now;
            await _context.SaveChangesAsync();

            return Ok(new
            {
                proposal.Id,
                proposal.Reference,
                proposal.Status,
                proposal.SentAt,
                proposal.ExpiresAt,
                snapshotStored = proposal.Snapshot != null
            });
        }

        // POST: api/proposals/{id}/share — create (or reuse) anonymous tracked link
        [HttpPost("{id}/share")]
        public async Task<IActionResult> CreateShareLink(Guid id, [FromBody] CreateShareRequest? request)
        {
            if (!await CanAccessProposalAsync(id))
            {
                return Forbid();
            }

            var proposal = await _context.Proposals.FirstOrDefaultAsync(p => p.Id == id);
            if (proposal == null)
            {
                return NotFound();
            }

            var item = await _context.ProposalItems.FirstOrDefaultAsync(i => i.ProposalId == id);
            if (item == null)
            {
                return BadRequest("Add an instrument before sharing.");
            }

            var catalogAmount = await _context.Prices
                .Where(p => p.ProductId == item.ProductId && p.Currency == proposal.Currency)
                .OrderByDescending(p => p.ValidFrom)
                .Select(p => p.Amount)
                .FirstOrDefaultAsync();

            if (_expiry.ApplyIfOverdue(proposal) || proposal.Status == "expired")
            {
                if (_context.ChangeTracker.HasChanges())
                    await _context.SaveChangesAsync();
                return BadRequest("This proposal has expired. Duplicate it to send a new offer.");
            }

            var blocked = await GovernanceSendBlockAsync(proposal, item, catalogAmount, sharing: true);
            if (blocked != null)
            {
                return blocked;
            }

            // Refresh content snapshot; PDF is rendered on demand for share viewers
            await _pdf.RefreshContentAsync(_context, id);

            var existing = await _context.ShareLinks
                .Where(s => s.ProposalId == id && !s.Revoked)
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefaultAsync();

            // Reuse active non-expired link unless forceNew
            if (existing != null &&
                !(request?.ForceNew ?? false) &&
                (!existing.ExpiresAt.HasValue || existing.ExpiresAt > DateTimeOffset.UtcNow))
            {
                return Ok(new
                {
                    existing.Token,
                    existing.ExpiresAt,
                    path = $"/p/{existing.Token}",
                    pdfPath = $"/api/p/{existing.Token}/pdf",
                    reused = true
                });
            }

            var days = request?.ExpiryDays is > 0 ? request.ExpiryDays.Value : proposal.ValidityDays;
            if (days <= 0) days = 14;

            var link = new ShareLink
            {
                Id = Guid.NewGuid(),
                ProposalId = id,
                Token = ShareTokenFactory.Create(),
                CreatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(days),
                Revoked = false
            };

            _context.ShareLinks.Add(link);

            // Creating a share link implies the proposal left pure draft
            if (proposal.Status == "draft")
            {
                proposal.Status = "sent";
                proposal.SentAt = DateTimeOffset.UtcNow;
                proposal.ExpiresAt = link.ExpiresAt;
            }

            proposal.UpdatedAt = DateTimeOffset.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new
            {
                link.Token,
                link.ExpiresAt,
                path = $"/p/{link.Token}",
                pdfPath = $"/api/p/{link.Token}/pdf",
                reused = false
            });
        }

        // GET: api/proposals/{id}/builder — hydrate builder draft from a saved proposal
        [HttpGet("{id}/builder")]
        public async Task<IActionResult> GetBuilderDraft(Guid id)
        {
            if (!await CanAccessProposalAsync(id))
            {
                return Forbid();
            }

            var proposal = await _context.Proposals.FirstOrDefaultAsync(p => p.Id == id);
            if (proposal == null)
            {
                return NotFound();
            }

            var item = await _context.ProposalItems.FirstOrDefaultAsync(i => i.ProposalId == id);
            object? itemPayload = null;

            if (item != null)
            {
                var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == item.ProductId);
                itemPayload = new
                {
                    productId = item.ProductId,
                    finish = item.Finish,
                    qty = item.Qty,
                    unitPrice = item.UnitPrice,
                    priceOverride = item.PriceOverride,
                    included = item.Included,
                    excluded = item.Excluded,
                    addons = item.Addons,
                    availableFinishes = product?.Finishes ?? Array.Empty<string>()
                };
            }

            // Infer step for resume
            var step = 1;
            if (proposal.TemplateId != Guid.Empty) step = 2;
            if (item != null) step = 3;
            if (proposal.ClientId != Guid.Empty && item != null) step = 4;
            if (item != null && proposal.ClientId != Guid.Empty) step = 5;

            return Ok(new
            {
                proposalId = proposal.Id,
                reference = proposal.Reference,
                status = proposal.Status,
                templateId = proposal.TemplateId,
                templateVersion = proposal.TemplateVersion,
                clientId = proposal.ClientId,
                advisorId = proposal.AdvisorId,
                currency = proposal.Currency,
                vatMode = proposal.VatMode,
                validityDays = proposal.ValidityDays,
                step,
                item = itemPayload
            });
        }

        [HttpPatch("{id}/status")]
        public async Task<IActionResult> SetStatus(Guid id, [FromBody] SetProposalStatusRequest request)
        {
            if (!await CanAccessProposalAsync(id))
            {
                return Forbid();
            }

            var next = ProposalStatuses.Normalize(request?.Status);
            if (next == null)
            {
                return BadRequest(new { message = "Unknown status. Use draft, sent, viewed, accepted, or expired." });
            }

            var proposal = await _context.Proposals.FirstOrDefaultAsync(p => p.Id == id);
            if (proposal == null)
            {
                return NotFound();
            }

            var role = User.FindFirstValue(ClaimTypes.Role);
            var staff = role is "Admin" or "Manager";

            if (!staff)
            {
                if (next != ProposalStatuses.Accepted)
                {
                    return StatusCode(StatusCodes.Status403Forbidden, new
                    {
                        message = "Only managers and admins can change a proposal to that status."
                    });
                }

                if (proposal.Status != ProposalStatuses.Viewed)
                {
                    return BadRequest(new
                    {
                        message = "A proposal can only be marked accepted after the client has viewed it."
                    });
                }
            }

            if (proposal.Status == next)
            {
                return Ok(new { proposal.Id, proposal.Reference, proposal.Status, proposal.UpdatedAt });
            }

            var before = proposal.Status;
            var now = DateTimeOffset.UtcNow;
            proposal.Status = next;
            proposal.UpdatedAt = now;

            if (next is ProposalStatuses.Sent or ProposalStatuses.Viewed)
            {
                proposal.SentAt ??= now;
                if (!proposal.ExpiresAt.HasValue || proposal.ExpiresAt < now)
                {
                    var days = proposal.ValidityDays > 0 ? proposal.ValidityDays : 14;
                    proposal.ExpiresAt = now.AddDays(days);
                }
            }

            await _audit.LogAsync(User, "update", "proposal_status", proposal.Id,
                new { status = before }, new { status = next });
            await _context.SaveChangesAsync();

            return Ok(new
            {
                proposal.Id,
                proposal.Reference,
                proposal.Status,
                proposal.UpdatedAt
            });
        }

        [HttpPatch("{id}")]
        public async Task<IActionResult> UpdateProposal(Guid id, [FromBody] UpdateProposalRequest request)
        {
            if (!await CanAccessProposalAsync(id))
            {
                return Forbid();
            }

            var proposal = await _context.Proposals.FirstOrDefaultAsync(p => p.Id == id);
            if (proposal == null)
            {
                return NotFound();
            }

            if (_expiry.ApplyIfOverdue(proposal))
                await _context.SaveChangesAsync();

            if (proposal.Status is not ("draft" or "sent" or "viewed"))
            {
                return BadRequest("Only draft, sent, or viewed proposals can be edited.");
            }

            if (request.ValidityDays.HasValue)
            {
                proposal.ValidityDays = request.ValidityDays.Value;
                var basis = proposal.SentAt ?? proposal.CreatedAt;
                proposal.ExpiresAt = basis.AddDays(request.ValidityDays.Value);
            }

            if (request.Currency != null && !string.IsNullOrWhiteSpace(request.Currency))
            {
                proposal.Currency = request.Currency.Trim();
            }

            if (request.VatMode != null && !string.IsNullOrWhiteSpace(request.VatMode))
            {
                proposal.VatMode = request.VatMode.Trim();
            }

            if (request.AdvisorId.HasValue)
            {
                proposal.AdvisorId = request.AdvisorId.Value;
            }

            if (request.ClientId.HasValue)
            {
                proposal.ClientId = request.ClientId.Value;
            }

            if (request.TemplateId.HasValue)
            {
                var template = await _context.Templates.FirstOrDefaultAsync(t => t.Id == request.TemplateId.Value);
                if (template != null)
                {
                    proposal.TemplateId = template.Id;
                    proposal.TemplateVersion = template.Version;
                }
            }

            proposal.UpdatedAt = DateTimeOffset.UtcNow;
            await _context.SaveChangesAsync();
            await _pdf.RefreshContentAsync(_context, proposal.Id);

            return Ok(new
            {
                proposal.Id,
                proposal.Reference,
                proposal.Status,
                proposal.ClientId,
                proposal.TemplateId,
                proposal.Currency,
                proposal.VatMode,
                proposal.ValidityDays,
                proposal.UpdatedAt
            });
        }

        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> DeleteProposal(Guid id)
        {
            var proposal = await _context.Proposals.FirstOrDefaultAsync(p => p.Id == id);
            if (proposal == null)
            {
                return NotFound();
            }

            var items = await _context.ProposalItems.Where(i => i.ProposalId == id).ToListAsync();
            _context.ProposalItems.RemoveRange(items);
            _context.Proposals.Remove(proposal);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        /// <summary>
        /// Spec §5.6: advisors cannot send or share a below-floor offer, or a deal at/above
        /// the high-value threshold, without manager/admin approval.
        /// </summary>
        private async Task<IActionResult?> GovernanceSendBlockAsync(
            Proposal proposal,
            ProposalItem item,
            decimal? catalogAmount,
            bool sharing = false)
        {
            var gov = await _pricing.GetForBusinessAsync(proposal.BusinessId);
            var offered = item.PriceOverride ?? item.UnitPrice;
            var role = User.FindFirstValue(ClaimTypes.Role);
            var canApprove = role is "Admin" or "Manager";
            var verb = sharing ? "sharing" : "sending";

            if (_pricing.IsBelowFloor(offered, catalogAmount, gov.DiscountFloorPercent)
                && !canApprove
                && !await HasApprovedAsync(proposal.Id, "below_floor", offered))
            {
                return BadRequest(new
                {
                    error = "below_floor",
                    message = $"Price is below the {gov.DiscountFloorPercent}% discount floor. Request manager approval before {verb}.",
                    floorUnitPrice = _pricing.FloorUnitPrice(catalogAmount, gov.DiscountFloorPercent),
                    catalogAmount,
                    offered,
                    canRequestApproval = true
                });
            }

            if (_pricing.IsHighValue(proposal.PriceTotal, gov.HighValueThreshold)
                && !canApprove
                && !await HasApprovedAsync(proposal.Id, "high_value", proposal.PriceTotal))
            {
                return BadRequest(new
                {
                    error = "high_value",
                    message = $"This proposal is at or above the {proposal.Currency} {gov.HighValueThreshold:N0} high-value threshold. Request manager approval before {verb}.",
                    highValueThreshold = gov.HighValueThreshold,
                    total = proposal.PriceTotal,
                    canRequestApproval = true
                });
            }

            return null;
        }

        private async Task<ApprovalRequest?> GetLatestApprovalAsync(Guid proposalId, string kind)
        {
            return await _context.ApprovalRequests
                .Where(a => a.ProposalId == proposalId && a.Kind == kind)
                .OrderByDescending(a => a.CreatedAt)
                .FirstOrDefaultAsync();
        }

        private async Task<bool> HasApprovedAsync(Guid proposalId, string kind, decimal? offered)
        {
            var latest = await GetLatestApprovalAsync(proposalId, kind);
            if (latest == null || latest.Status != "approved")
                return false;

            // If the offer moved since approval, require a fresh ask
            if (offered.HasValue && latest.OfferedPrice.HasValue &&
                offered.Value != latest.OfferedPrice.Value)
            {
                return false;
            }

            return true;
        }

        private async Task<bool> CanAccessProposalAsync(Guid proposalId)
        {
            var role = User.FindFirstValue(ClaimTypes.Role);
            if (role is "Admin" or "Manager")
            {
                return true;
            }

            var userId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid)
                ? uid
                : (Guid?)null;

            if (!userId.HasValue)
            {
                return false;
            }

            return await _context.Proposals.AnyAsync(p =>
                p.Id == proposalId && p.AdvisorId == userId.Value);
        }

    }

    public class CreateProposalRequest
    {
        public Guid ClientId { get; set; }
        public Guid? AdvisorId { get; set; }
        public Guid? TemplateId { get; set; }
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

    public class SetProposalStatusRequest
    {
        public string Status { get; set; } = string.Empty;
    }

    public class UpdateProposalRequest
    {
        public int? ValidityDays { get; set; }
        public string? Currency { get; set; }
        public string? VatMode { get; set; }
        public Guid? AdvisorId { get; set; }
        public Guid? ClientId { get; set; }
        public Guid? TemplateId { get; set; }
    }

    public class CreateShareRequest
    {
        public int? ExpiryDays { get; set; }
        public bool? ForceNew { get; set; }
    }
}
