using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProposalStudio.Data;
using ProposalStudio.Services;

namespace ProposalStudio.Controllers
{
    [ApiController]
    [Route("api/audit")]
    [Authorize(Roles = "Admin")]
    public class AuditController : ControllerBase
    {
        private readonly AppDbContext _context;

        public AuditController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/audit?entity=&action=&from=&to=&limit=
        [HttpGet]
        public async Task<IActionResult> GetAudit(
            [FromQuery] string? entity = null,
            [FromQuery] string? action = null,
            [FromQuery] DateTimeOffset? from = null,
            [FromQuery] DateTimeOffset? to = null,
            [FromQuery] int limit = 100)
        {
            limit = Math.Clamp(limit, 1, 500);

            var access = await BusinessScope.ResolveAsync(_context, User);
            if (!access.Ok)
                return BadRequest(BusinessScope.MissingMessage);

            var open = access.Unrestricted;
            var home = access.HomeId ?? Guid.Empty;

            var query =
                from log in _context.AuditLogs
                join actor in _context.Users on log.ActorId equals actor.Id into actors
                from actor in actors.DefaultIfEmpty()
                where open || (actor != null && actor.BusinessId == home)
                select new { log, actor };

            if (!string.IsNullOrWhiteSpace(entity))
            {
                var term = entity.Trim().ToLower();
                query = query.Where(x => x.log.Entity.ToLower() == term);
            }

            if (!string.IsNullOrWhiteSpace(action))
            {
                var term = action.Trim().ToLower();
                query = query.Where(x => x.log.Action.ToLower() == term);
            }

            if (from.HasValue)
                query = query.Where(x => x.log.OccurredAt >= from.Value);

            if (to.HasValue)
                query = query.Where(x => x.log.OccurredAt <= to.Value);

            var rows = await query
                .OrderByDescending(x => x.log.OccurredAt)
                .Take(limit)
                .Select(x => new
                {
                    x.log.Id,
                    x.log.ActorId,
                    ActorName = x.actor != null ? x.actor.Name : null,
                    ActorEmail = x.actor != null ? x.actor.Email : null,
                    x.log.Action,
                    x.log.Entity,
                    x.log.EntityId,
                    Before = x.log.Before,
                    After = x.log.After,
                    x.log.OccurredAt
                })
                .ToListAsync();

            return Ok(rows);
        }
    }
}
