using Microsoft.EntityFrameworkCore;
using ProposalStudio.Data;
using ProposalStudio.Models;

namespace ProposalStudio.Services
{
    /// <summary>
    /// Sent / viewed offers become expired once validity elapses.
    /// </summary>
    public class ProposalExpiryService
    {
        private readonly AppDbContext _db;

        public ProposalExpiryService(AppDbContext db)
        {
            _db = db;
        }

        public static DateTimeOffset Deadline(Proposal proposal)
        {
            if (proposal.ExpiresAt.HasValue)
                return proposal.ExpiresAt.Value;

            var days = proposal.ValidityDays > 0 ? proposal.ValidityDays : 14;
            return (proposal.SentAt ?? proposal.CreatedAt).AddDays(days);
        }

        public static bool IsOverdue(Proposal proposal, DateTimeOffset? now = null)
        {
            if (proposal.Status is not ("sent" or "viewed"))
                return proposal.Status == "expired";

            return Deadline(proposal) < (now ?? DateTimeOffset.UtcNow);
        }

        public bool ApplyIfOverdue(Proposal proposal, DateTimeOffset? now = null)
        {
            var t = now ?? DateTimeOffset.UtcNow;
            if (proposal.Status is not ("sent" or "viewed"))
                return false;
            if (Deadline(proposal) >= t)
                return false;

            proposal.Status = "expired";
            proposal.UpdatedAt = t;
            proposal.ExpiresAt ??= Deadline(proposal);
            return true;
        }

        /// <summary>
        /// Flip sent/viewed rows whose validity end date is already past.
        /// One SQL UPDATE — no timer, no full-table load.
        /// </summary>
        public Task<int> ExpireOverdueAsync(CancellationToken ct = default)
        {
            var now = DateTimeOffset.UtcNow;
            return _db.Proposals
                .Where(p =>
                    (p.Status == "sent" || p.Status == "viewed") &&
                    (
                        (p.ExpiresAt != null && p.ExpiresAt < now) ||
                        (p.ExpiresAt == null &&
                            (p.SentAt ?? p.CreatedAt).AddDays(p.ValidityDays > 0 ? p.ValidityDays : 14) < now)
                    ))
                .ExecuteUpdateAsync(
                    s => s
                        .SetProperty(p => p.Status, "expired")
                        .SetProperty(p => p.UpdatedAt, now),
                    ct);
        }
    }
}
