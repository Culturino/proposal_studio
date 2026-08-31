using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
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
            var access = await BusinessScope.ResolveAsync(_context, User);
            if (!access.Ok)
                return BadRequest(BusinessScope.MissingMessage);

            // Hide deactivated users from the admin directory (soft-deleted with proposal history)
            var users = await BusinessScope.Filter(_context.Users, access, u => u.BusinessId)
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
            var access = await BusinessScope.ResolveAsync(_context, User);
            if (!access.Ok)
                return BadRequest(BusinessScope.MissingMessage);

            var user = await BusinessScope.Filter(_context.Users, access, u => u.BusinessId)
                .FirstOrDefaultAsync(u => u.Id == id);

            if (user == null)
                return NotFound();

            return Ok(PublicUser(user));
        }

        // PATCH: api/users/{id} (Admin)
        [HttpPatch("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateUser(Guid id, [FromBody] UpdateUserRequest request)
        {
            var access = await BusinessScope.ResolveAsync(_context, User);
            if (!access.Ok)
                return BadRequest(BusinessScope.MissingMessage);

            var user = await BusinessScope.Filter(_context.Users, access, u => u.BusinessId)
                .FirstOrDefaultAsync(u => u.Id == id);

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
                var email = request.Email.Trim().ToLowerInvariant();
                var taken = await _context.Users.AnyAsync(u => u.Email == email && u.Id != id);
                if (taken)
                    return Conflict("That email is already in use.");
                user.Email = email;
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

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (
                ex.InnerException is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                return Conflict("That email is already in use.");
            }

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

            return Ok(PublicUser(user));
        }

        // DELETE: api/users/{id} (Admin)
        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteUser(Guid id)
        {
            var access = await BusinessScope.ResolveAsync(_context, User);
            if (!access.Ok)
                return BadRequest(BusinessScope.MissingMessage);

            var user = await BusinessScope.Filter(_context.Users, access, u => u.BusinessId)
                .FirstOrDefaultAsync(u => u.Id == id);

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

        private static object PublicUser(User user) => new
        {
            user.Id,
            user.Name,
            user.Email,
            user.Phone,
            user.Role,
            user.Active,
            user.BusinessId,
            user.CreatedAt
        };
    }

    public class UpdateUserRequest
    {
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? Role { get; set; }
        public bool? Active { get; set; }
    }
}