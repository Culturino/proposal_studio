using System.Text.Json;

namespace ProposalStudio.Models
{
    public class AuditLog
    {
        public Guid Id { get; set; }

        public Guid? ActorId { get; set; }

        public string Action { get; set; } = string.Empty;

        public string Entity { get; set; } = string.Empty;

        public Guid? EntityId { get; set; }

        public JsonDocument? Before { get; set; }

        public JsonDocument? After { get; set; }

        public DateTimeOffset OccurredAt { get; set; }
    }
}
