using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProposalStudio.Data;
using ProposalStudio.Models;
using ProposalStudio.Services;
using System.Security.Claims;

namespace ProposalStudio.Controllers
{
    [ApiController]
    [Route("api/users")]
    [Authorize]
    public class UserController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly AuditService _audit;

        public UserController(AppDbContext context, AuditService audit)
        {
            _context = context;
            _audit = audit;
        }

        // GET: api/users — advisors listed for builder picker; full manage is Admin UI
        [HttpGet]
        public async Task<IActionResult> GetUsers()
        {
            // Hide deactivated users from the admin directory (soft-deleted with proposal history)
            var users = await _context.Users
                .Where(u => u.Active)
                .Select(u => new
                {
                    u.Id,
                    u.Name,
                    u.Email,
                    u.Role,
                    u.Active,
                    u.CreatedAt
                })
                .ToListAsync();

            return Ok(users);
        }

        // GET: api/users/{id}
        [HttpGet("{id}")]
        public async Task<IActionResult> GetUser(Guid id)
        {
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Id == id);

            if (user == null)
                return NotFound();

            return Ok(new
            {
                user.Id,
                user.Name,
                user.Email,
                user.Phone,
                user.Role,
                user.Active,
                user.BusinessId,
                user.CreatedAt
            });
        }

        // POST: api/users (Admin)
        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest("Email and password are required.");
            }

            var business = await _context.Businesses
                .FirstOrDefaultAsync(b => b.Slug == "house-of-pianos");

            if (business == null)
            {
                return BadRequest("Default business was not found.");
            }

            var user = new User
            {
                Id = Guid.NewGuid(),
                BusinessId = request.BusinessId ?? business.Id,
                Name = request.Name?.Trim() ?? request.Email.Trim(),
                Email = request.Email.Trim().ToLower(),
                Phone = request.Phone,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
                Role = string.IsNullOrWhiteSpace(request.Role) ? "advisor" : request.Role.Trim().ToLower(),
                Active = true,
                TwoFactorEnabled = false,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();
            await _audit.LogAsync(User, "create", "user", user.Id, null, AuditService.UserRoleSnapshot(user));

            return CreatedAtAction(nameof(GetUser), new { id = user.Id }, new
            {
                user.Id,
                user.Name,
                user.Email,
                user.Role,
                user.Active
            });
        }

        // PATCH: api/users/{id} (Admin)
        [HttpPatch("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateUser(Guid id, [FromBody] UpdateUserRequest request)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id);

            if (user == null)
                return NotFound();

            var before = AuditService.UserRoleSnapshot(user);
            var prevRole = user.Role;
            var prevActive = user.Active;

            if (request.Name != null && !string.IsNullOrWhiteSpace(request.Name))
            {
                user.Name = request.Name.Trim();
            }

            if (request.Email != null && !string.IsNullOrWhiteSpace(request.Email))
            {
                user.Email = request.Email.Trim().ToLower();
            }

            if (request.Role != null && !string.IsNullOrWhiteSpace(request.Role))
            {
                user.Role = request.Role.Trim().ToLower();
            }

            if (request.Active.HasValue)
            {
                user.Active = request.Active.Value;
            }

            user.UpdatedAt = DateTimeOffset.UtcNow;

            await _context.SaveChangesAsync();

            var roleChanged = !string.Equals(prevRole, user.Role, StringComparison.OrdinalIgnoreCase);
            var activeChanged = prevActive != user.Active;
            if (roleChanged || activeChanged)
            {
                await _audit.LogAsync(
                    User,
                    roleChanged ? "update_role" : "update",
                    "user",
                    user.Id,
                    before,
                    AuditService.UserRoleSnapshot(user));
            }

            return Ok(user);
        }

        // PATCH: api/users/{id}/password
        [HttpPatch("{id}/password")]
        [Authorize]
        public async Task<IActionResult> UpdatePassword(Guid id, [FromBody] UpdatePasswordRequest request)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
            if (user == null)
            {
                return NotFound();
            }

            // Security Check: Ensure the caller is either an Admin OR updating their own password
            var callerId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var callerRole = User.FindFirst(ClaimTypes.Role)?.Value;

            if (callerId != id.ToString() && callerRole != "Admin")
            {
                return Forbid();
            }

            // Hash the password using BCrypt
            var hashedPassword = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);

            user.PasswordHash = hashedPassword;
            user.UpdatedAt = DateTimeOffset.UtcNow;

            await _context.SaveChangesAsync();

            return Ok(new { message = "Password updated successfully" });
        }

        // DELETE: api/users/{id} (Admin)
        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteUser(Guid id)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id);

            if (user == null)
                return NotFound();

            var callerId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var cid)
                ? cid
                : (Guid?)null;
            if (callerId == id)
                return BadRequest("You cannot delete your own account.");

            // Always soft-delete — hard remove + seeder used to resurrect demo users on restart
            var before = AuditService.UserRoleSnapshot(user);
            user.Active = false;
            user.UpdatedAt = DateTimeOffset.UtcNow;
            await _context.SaveChangesAsync();
            await _audit.LogAsync(User, "delete", "user", user.Id, before, AuditService.UserRoleSnapshot(user));
            return NoContent();
        }
    }

    public class CreateUserRequest
    {
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string? Role { get; set; }
        public Guid? BusinessId { get; set; }
    }

    public class UpdateUserRequest
    {
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? Role { get; set; }
        public bool? Active { get; set; }
    }

    public class UpdatePasswordRequest
    {
        public string NewPassword { get; set; } = string.Empty;
    }
}