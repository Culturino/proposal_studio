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
