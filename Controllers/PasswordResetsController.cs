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
    [Route("api/password-resets")]
    [Authorize]
    public class PasswordResetsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly AuditService _audit;

        public PasswordResetsController(AppDbContext context, AuditService audit)
        {
            _context = context;
            _audit = audit;
        }

        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> List()
        {
            var rows = await (
                from r in _context.PasswordResets
                join u in _context.Users on r.UserId equals u.Id into users
                from u in users.DefaultIfEmpty()
                orderby r.CreatedAt descending
                select new
                {
                    r.Id,
                    r.UserId,
                    UserName = u != null ? u.Name : null,
                    UserEmail = u != null ? u.Email : null,
                    r.CreatedAt,
                    r.ExpiresAt,
                    r.UsedAt,
                    r.Revoked,
                    Status = r.Revoked ? "revoked"
                        : r.UsedAt != null ? "used"
                        : r.ExpiresAt < DateTimeOffset.UtcNow ? "expired"
                        : "pending",
                    Path = $"/reset/{r.Token}"
                }
            ).Take(100).ToListAsync();

            return Ok(rows);
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create([FromBody] CreatePasswordResetRequest? body)
        {
            if (body?.UserId is not Guid userId || userId == Guid.Empty)
                return BadRequest("User is required.");

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
                return NotFound("User was not found.");
            if (!user.Active)
                return BadRequest("That account is deactivated.");

            var actorId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid)
                ? uid
                : Guid.Empty;

            var pending = await _context.PasswordResets
                .Where(r => r.UserId == userId && !r.Revoked && r.UsedAt == null)
                .ToListAsync();
            foreach (var old in pending)
                old.Revoked = true;

            var hours = body.ExpiryHours is > 0 and <= 168 ? body.ExpiryHours.Value : 48;

            var reset = new PasswordReset
            {
                Id = Guid.NewGuid(),
                Token = ShareTokenFactory.Create(),
                UserId = user.Id,
                CreatedBy = actorId,
                CreatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(hours),
                Revoked = false
            };

            _context.PasswordResets.Add(reset);
            await _context.SaveChangesAsync();
            await _audit.LogAsync(User, "create", "password_reset", reset.Id, null, new
            {
                user.Id,
                user.Name,
                user.Email,
                reset.ExpiresAt
            });

            return Ok(new
            {
                reset.Id,
                reset.UserId,
                user.Name,
                user.Email,
                reset.ExpiresAt,
                token = reset.Token,
                path = $"/reset/{reset.Token}"
            });
        }

        [HttpDelete("{id:guid}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Revoke(Guid id)
        {
            var reset = await _context.PasswordResets.FirstOrDefaultAsync(r => r.Id == id);
            if (reset == null)
                return NotFound();

            reset.Revoked = true;
            await _context.SaveChangesAsync();
            await _audit.LogAsync(User, "revoke", "password_reset", reset.Id, null, new { reset.UserId });
            return NoContent();
        }

        [HttpGet("{token}")]
        [AllowAnonymous]
        public async Task<IActionResult> Peek(string token)
        {
            var found = await FindUsableAsync(token);
            if (found == null)
                return NotFound(new { error = "invalid", message = "This reset link is invalid, expired, or already used." });

            var (reset, user) = found.Value;
            return Ok(new { name = user.Name, email = user.Email, expiresAt = reset.ExpiresAt });
        }

        [HttpPost("{token}/complete")]
        [AllowAnonymous]
        public async Task<IActionResult> Complete(string token, [FromBody] CompletePasswordResetRequest body)
        {
            var found = await FindUsableAsync(token);
            if (found == null)
                return NotFound(new { error = "invalid", message = "This reset link is invalid, expired, or already used." });

            if (string.IsNullOrWhiteSpace(body.Password) || body.Password.Trim().Length < 8)
                return BadRequest("Password must be at least 8 characters.");

            var (reset, user) = found.Value;
            if (!user.Active)
                return BadRequest("That account is deactivated.");

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(body.Password.Trim());
            user.UpdatedAt = DateTimeOffset.UtcNow;
            reset.UsedAt = DateTimeOffset.UtcNow;

            await _context.SaveChangesAsync();
            await _audit.LogAsync(null, "complete", "password_reset", reset.Id, null, new
            {
                user.Id,
                user.Name,
                user.Email
            });

            return Ok(new { message = "Password updated. You can sign in now." });
        }

        private async Task<(PasswordReset reset, User user)?> FindUsableAsync(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return null;

            var reset = await _context.PasswordResets.FirstOrDefaultAsync(r => r.Token == token);
            if (reset == null || reset.Revoked || reset.UsedAt != null)
                return null;
            if (reset.ExpiresAt <= DateTimeOffset.UtcNow)
                return null;

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == reset.UserId);
            if (user == null)
                return null;

            return (reset, user);
        }
    }

    public class CreatePasswordResetRequest
    {
        public Guid UserId { get; set; }
        public int? ExpiryHours { get; set; }
    }

    public class CompletePasswordResetRequest
    {
        public string Password { get; set; } = string.Empty;
    }
}
