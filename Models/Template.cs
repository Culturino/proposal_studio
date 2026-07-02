using System.Text.Json;

namespace ProposalStudio.Models
{
    public class Template
    {
        public Guid Id { get; set; }

        public Guid BusinessId { get; set; }

        public string Key { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public int Version { get; set; }

        public JsonDocument PageSchema { get; set; } = JsonDocument.Parse("{}");

        public bool StylingLocked { get; set; }

        public bool Active { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }
    }
}