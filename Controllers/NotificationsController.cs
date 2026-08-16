using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProposalStudio.Data;

namespace ProposalStudio.Controllers
{
    [ApiController]
    [Route("api/notifications")]
    [Authorize]
    public class NotificationsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public NotificationsController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/notifications
        [HttpGet]
        public async Task<IActionResult> List([FromQuery] int limit = 30)
        {
            var userId = CurrentUserId();
            if (userId == null) return Unauthorized();

            limit = Math.Clamp(limit, 1, 100);

            var rows = await _context.Notifications
                .Where(n => n.UserId == userId)
                .OrderByDescending(n => n.CreatedAt)
                .Take(limit)
                .Select(n => new
                {
                    n.Id,
                    n.Title,
                    n.Body,
                    n.Kind,
                    n.RelatedId,
                    n.Read,
                    n.CreatedAt
                })
                .ToListAsync();

            var unread = await _context.Notifications.CountAsync(n => n.UserId == userId && !n.Read);

            return Ok(new { unread, items = rows });
        }

        // POST: api/notifications/{id}/read
        [HttpPost("{id:guid}/read")]
        public async Task<IActionResult> MarkRead(Guid id)
        {
            var userId = CurrentUserId();
            if (userId == null) return Unauthorized();

            var note = await _context.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId);
            if (note == null) return NotFound();

            note.Read = true;
            await _context.SaveChangesAsync();
            return Ok(new { note.Id, note.Read });
        }

        // POST: api/notifications/read-all
        [HttpPost("read-all")]
        public async Task<IActionResult> MarkAllRead()
        {
            var userId = CurrentUserId();
            if (userId == null) return Unauthorized();

            var unread = await _context.Notifications
                .Where(n => n.UserId == userId && !n.Read)
                .ToListAsync();
            foreach (var n in unread)
                n.Read = true;

            await _context.SaveChangesAsync();
            return Ok(new { cleared = unread.Count });
        }

        private Guid? CurrentUserId()
        {
            return Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
        }
    }
}
