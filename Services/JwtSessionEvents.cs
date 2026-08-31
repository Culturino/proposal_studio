using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using ProposalStudio.Data;

namespace ProposalStudio.Services;

/// <summary>
/// JWTs are otherwise valid until expiry. Soft-delete only sets Active=false,
/// so every authenticated request re-checks that the account still exists.
/// </summary>
public static class JwtSessionEvents
{
    public static async Task RejectInactiveUsers(TokenValidatedContext context)
    {
        var idClaim = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(idClaim, out var userId))
        {
            context.Fail("Invalid token.");
            return;
        }

        var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        var stillActive = await db.Users.AsNoTracking()
            .AnyAsync(u => u.Id == userId && u.Active);
        if (!stillActive)
            context.Fail("Account is no longer active.");
    }
}
