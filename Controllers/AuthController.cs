using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ProposalStudio.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace ProposalStudio.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _configuration;

        public AuthController(AppDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest("Email and password are required.");
            }

            var email = request.Email.Trim().ToLowerInvariant();
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Active && u.Email == email);

            if (user == null || string.IsNullOrWhiteSpace(user.PasswordHash))
            {
                return Unauthorized("Invalid credentials");
            }

            if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            {
                return Unauthorized("Invalid credentials");
            }

            var tokenHandler = new JwtSecurityTokenHandler();
            var jwtKey = _configuration["JwtSettings:SecretKey"];
            if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Length < 32)
                throw new InvalidOperationException("JwtSettings__SecretKey is not configured.");
            var key = Encoding.UTF8.GetBytes(jwtKey);

            // JWT role claim (Authorize Roles=) vs app role (frontend capabilities)
            var (jwtRole, appRole) = MapRole(user.Role);

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new Claim(ClaimTypes.Email, user.Email ?? string.Empty),
                    new Claim(ClaimTypes.Name, user.Name ?? string.Empty),
                    new Claim(ClaimTypes.Role, jwtRole)
                }),
                Issuer = _configuration["JwtSettings:Issuer"] ?? "ProposalStudio",
                Audience = _configuration["JwtSettings:Audience"] ?? "ProposalStudio",
                Expires = DateTime.UtcNow.AddHours(8),
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(key),
                    SecurityAlgorithms.HmacSha256Signature
                )
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);

            return Ok(new
            {
                token = tokenHandler.WriteToken(token),
                role = appRole,
                userId = user.Id,
                businessId = user.BusinessId,
                email = user.Email,
                name = user.Name
            });
        }

        // GET: api/auth/me
        [HttpGet("me")]
        public async Task<IActionResult> Me()
        {
            var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(idClaim, out var userId))
            {
                return Unauthorized();
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId && u.Active);
            if (user == null)
            {
                return Unauthorized();
            }

            var business = await _context.Businesses.FirstOrDefaultAsync(b => b.Id == user.BusinessId);
            var (_, appRole) = MapRole(user.Role);

            return Ok(new
            {
                user.Id,
                user.Name,
                user.Email,
                user.Phone,
                Role = appRole,
                user.BusinessId,
                Business = business == null ? null : new
                {
                    business.Id,
                    business.Name,
                    business.Slug,
                    business.ReferencePrefix
                }
            });
        }

        private static (string JwtRole, string AppRole) MapRole(string? role)
        {
            return (role ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "admin" => ("Admin", "admin"),
                "manager" => ("Manager", "manager"),
                "advisor" or "salesadvisor" => ("SalesAdvisor", "advisor"),
                _ => ("SalesAdvisor", "advisor")
            };
        }
    }

    public class LoginRequest
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }
}
