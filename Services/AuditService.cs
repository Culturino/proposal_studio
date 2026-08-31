using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProposalStudio.Data;
using ProposalStudio.Models;

namespace ProposalStudio.Services
{
    /// <summary>
    /// Spec §12: catalog / price / role changes are attributable via AuditLog.
    /// </summary>
    public class AuditService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        private readonly AppDbContext _db;

        public AuditService(AppDbContext db)
        {
            _db = db;
        }

        public async Task LogAsync(
            ClaimsPrincipal? user,
            string action,
            string entity,
            Guid? entityId,
            object? before = null,
            object? after = null)
        {
            Guid? actorId = null;
            var raw = user?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (Guid.TryParse(raw, out var parsed))
                actorId = parsed;

            _db.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                ActorId = actorId,
                Action = action,
                Entity = entity,
                EntityId = entityId,
                Before = ToJson(before),
                After = ToJson(after),
                OccurredAt = DateTimeOffset.UtcNow
            });

            await _db.SaveChangesAsync();
        }

        /// <summary>
        /// One-shot: leftover inbox rows become audit entries, then the inbox is emptied.
        /// Safe to call on every boot — a second pass finds nothing.
        /// </summary>
        public async Task<int> ArchiveNotificationsAsync()
        {
            var notes = await _db.Notifications
                .Where(n => n.Kind != "proposal_opened")
                .OrderBy(n => n.CreatedAt)
                .ToListAsync();
            if (notes.Count == 0)
                return 0;

            foreach (var note in notes)
            {
                var kind = note.Kind ?? "";
                var action = kind switch
                {
                    "below_floor_approval" or "high_value_approval" => "request",
                    "below_floor_decision" or "high_value_decision" =>
                        note.Title.Contains("approved", StringComparison.OrdinalIgnoreCase) ? "approve" : "reject",
                    _ => "notify"
                };
                var entity = kind.Contains("floor") || kind.Contains("high_value") || kind.Contains("approval")
                    ? "approval"
                    : "notification";

                _db.AuditLogs.Add(new AuditLog
                {
                    Id = Guid.NewGuid(),
                    ActorId = note.UserId,
                    Action = action,
                    Entity = entity,
                    EntityId = note.RelatedId ?? note.Id,
                    After = ToJson(new
                    {
                        title = note.Title,
                        body = note.Body,
                        kind = note.Kind
                    }),
                    OccurredAt = note.CreatedAt
                });
            }

            _db.Notifications.RemoveRange(notes);
            await _db.SaveChangesAsync();
            return notes.Count;
        }

        public static JsonDocument? ToJson(object? value)
        {
            if (value == null) return null;
            if (value is JsonDocument doc) return JsonDocument.Parse(doc.RootElement.GetRawText());
            var json = JsonSerializer.Serialize(value, JsonOptions);
            return JsonDocument.Parse(json);
        }

        public static object ProductSnapshot(Product p) => new
        {
            p.Id,
            p.BrandId,
            p.CategoryId,
            p.Model,
            Dimensions = p.Dimensions?.RootElement.GetRawText(),
            p.Features,
            p.Blurb,
            p.Tagline,
            p.Finishes,
            p.DefaultIncludes,
            p.DefaultExcludes,
            p.AvailabilityNote,
            p.Status
        };

        public static object UserRoleSnapshot(User u) => new
        {
            u.Id,
            u.Name,
            u.Email,
            u.Role,
            u.Active
        };

        public static object BrandSnapshot(Brand b) => new
        {
            b.Id,
            b.Name,
            b.Blurb,
            b.LogoAssetId
        };

        public static object CategorySnapshot(ProductCategory c) => new
        {
            c.Id,
            c.Name,
            c.Slug,
            c.Active,
            c.SortOrder
        };

        public static object PriceSnapshot(Price p) => new
        {
            p.Id,
            p.ProductId,
            p.Currency,
            p.Finish,
            p.Amount,
            p.ValidFrom,
            p.Source
        };
    }
}
