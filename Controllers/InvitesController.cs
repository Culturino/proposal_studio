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
    [Route("api/invites")]
    [Authorize]
    public class InvitesController : ControllerBase
    {
        private static readonly string[] Roles = { "advisor", "manager", "admin" };

        private readonly AppDbContext _context;
        private readonly AuditService _audit;

        public InvitesController(AppDbContext context, AuditService audit)
        {
            _context = context;
            _audit = audit;
        }

        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> List()
        {
            var rows = await (
                from i in _context.UserInvites
                join u in _context.Users on i.CreatedBy equals u.Id into creators
                from u in creators.DefaultIfEmpty()
                orderby i.CreatedAt descending
                select new
                {
                    i.Id,
                    i.Role,
                    i.CreatedAt,
                    i.ExpiresAt,
                    i.UsedAt,
                    i.Revoked,
                    i.CreatedUserId,
                    CreatedByName = u != null ? u.Name : null,
                    Status = i.Revoked ? "revoked"
                        : i.UsedAt != null ? "used"
                        : i.ExpiresAt < DateTimeOffset.UtcNow ? "expired"
                        : "pending",
                    Path = $"/join/{i.Token}"
                }
            ).Take(100).ToListAsync();

            return Ok(rows);
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create([FromBody] CreateInviteRequest? body)
        {
            var role = (body?.Role ?? "advisor").Trim().ToLowerInvariant();
            if (!Roles.Contains(role))
                return BadRequest("Role must be advisor, manager, or admin.");

            var actorId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid)
                ? uid
                : Guid.Empty;

            var days = body?.ExpiryDays is > 0 and <= 90 ? body.ExpiryDays.Value : 14;

            var invite = new UserInvite
            {
                Id = Guid.NewGuid(),
                Token = ShareTokenFactory.Create(),
                Role = role,
                CreatedBy = actorId,
                CreatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(days),
                Revoked = false
            };

            _context.UserInvites.Add(invite);
            await _context.SaveChangesAsync();
            await _audit.LogAsync(User, "create", "invite", invite.Id, null, new
            {
                invite.Role,
                invite.ExpiresAt
            });

            return Ok(new
            {
                invite.Id,
                invite.Role,
                invite.ExpiresAt,
                token = invite.Token,
                path = $"/join/{invite.Token}"
            });
        }

        [HttpDelete("{id:guid}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Revoke(Guid id)
        {
            var invite = await _context.UserInvites.FirstOrDefaultAsync(i => i.Id == id);
            if (invite == null)
                return NotFound();

            invite.Revoked = true;
            await _context.SaveChangesAsync();
            await _audit.LogAsync(User, "revoke", "invite", invite.Id, null, new { invite.Role });
            return NoContent();
        }

        [HttpGet("{token}")]
        [AllowAnonymous]
        public async Task<IActionResult> Peek(string token)
        {
            var invite = await FindUsableAsync(token);
            if (invite == null)
                return NotFound(new { error = "invalid", message = "This invite is invalid, expired, or already used." });

            return Ok(new { role = invite.Role, expiresAt = invite.ExpiresAt });
        }

        [HttpPost("{token}/complete")]
        [AllowAnonymous]
        public async Task<IActionResult> Complete(string token, [FromBody] CompleteInviteRequest body)
        {
            var invite = await FindUsableAsync(token);
            if (invite == null)
                return NotFound(new { error = "invalid", message = "This invite is invalid, expired, or already used." });

            if (string.IsNullOrWhiteSpace(body.Name) ||
                string.IsNullOrWhiteSpace(body.Email) ||
                string.IsNullOrWhiteSpace(body.Password) ||
                string.IsNullOrWhiteSpace(body.Phone))
            {
                return BadRequest("Name, email, password, and phone number are required.");
            }

            if (body.Password.Trim().Length < 8)
                return BadRequest("Password must be at least 8 characters.");

            var email = body.Email.Trim().ToLowerInvariant();
            var taken = await _context.Users.AnyAsync(u => u.Email == email);
            if (taken)
                return Conflict("That email is already in use.");

            var business = await _context.Businesses
                .FirstOrDefaultAsync(b => b.Slug == "house-of-pianos");
            if (business == null)
                return BadRequest("Default business was not found.");

            var now = DateTimeOffset.UtcNow;
            var user = new User
            {
                Id = Guid.NewGuid(),
                BusinessId = business.Id,
                Name = body.Name.Trim(),
                Email = email,
                Phone = body.Phone.Trim(),
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(body.Password),
                Role = invite.Role,
                Active = true,
                TwoFactorEnabled = false,
                CreatedAt = now,
                UpdatedAt = now
            };

            invite.UsedAt = now;
            invite.CreatedUserId = user.Id;

            _context.Users.Add(user);
            await _context.SaveChangesAsync();
            await _audit.LogAsync(null, "complete", "invite", invite.Id, null, AuditService.UserRoleSnapshot(user));

            return Ok(new
            {
                user.Id,
                user.Name,
                user.Email,
                user.Role,
                message = "Account created. You can sign in now."
            });
        }

        private async Task<UserInvite?> FindUsableAsync(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return null;

            var invite = await _context.UserInvites.FirstOrDefaultAsync(i => i.Token == token);
            if (invite == null || invite.Revoked || invite.UsedAt != null)
                return null;
            if (invite.ExpiresAt <= DateTimeOffset.UtcNow)
                return null;

            return invite;
        }
    }

    public class CreateInviteRequest
    {
        public string? Role { get; set; }
        public int? ExpiryDays { get; set; }
    }

    public class CompleteInviteRequest
    {
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
    }
}
