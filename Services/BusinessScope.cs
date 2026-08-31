using System.Linq.Expressions;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ProposalStudio.Data;
using ProposalStudio.Models;

namespace ProposalStudio.Services;

/// <summary>
/// Resolves which business rows a caller may see. Admins are platform-wide
/// (every house). Managers and advisors stay on the business linked to their
/// user row. Runtime code must not hardcode a slug.
/// </summary>
public static class BusinessScope
{
    public const string MissingMessage = "No business is linked to this account.";

    public readonly struct Access
    {
        public bool Unrestricted { get; init; }
        public Guid? HomeId { get; init; }

        public bool Ok => Unrestricted || HomeId.HasValue;

        /// <summary>
        /// House to attach new rows to. Admins may pass another id; everyone
        /// else is always their own house.
        /// </summary>
        public Guid? TargetId(Guid? requested = null)
        {
            if (Unrestricted && requested is Guid g && g != Guid.Empty)
                return g;
            return HomeId;
        }
    }

    private static readonly AsyncLocal<Guid?> RequestedId = new();

    public static void SetRequestBusinessId(Guid? id) => RequestedId.Value = id;

    public static bool IsPlatformAdmin(ClaimsPrincipal user) =>
        user.IsInRole("Admin");

    public static async Task<Access> ResolveAsync(AppDbContext db, ClaimsPrincipal user)
    {
        var home = await ForUserAsync(db, user);
        var admin = IsPlatformAdmin(user);
        if (admin && RequestedId.Value is Guid pick && pick != Guid.Empty)
        {
            var exists = await db.Businesses.AsNoTracking()
                .AnyAsync(b => b.Id == pick && b.Active);
            if (exists)
            {
                return new Access
                {
                    Unrestricted = false,
                    HomeId = pick
                };
            }
        }

        return new Access
        {
            Unrestricted = admin,
            HomeId = home?.Id
        };
    }

    public static IQueryable<T> Filter<T>(
        IQueryable<T> source,
        Access access,
        Expression<Func<T, Guid>> businessId)
    {
        if (access.Unrestricted)
            return source;

        if (access.HomeId is not Guid id)
            return source.Where(_ => false);

        var parameter = businessId.Parameters[0];
        var equal = Expression.Equal(businessId.Body, Expression.Constant(id));
        return source.Where(Expression.Lambda<Func<T, bool>>(equal, parameter));
    }

    public static async Task<Business?> ForUserAsync(AppDbContext db, ClaimsPrincipal user)
    {
        var idClaim = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(idClaim, out var userId))
            return null;

        return await ForUserIdAsync(db, userId);
    }

    public static async Task<Business?> ForUserIdAsync(AppDbContext db, Guid userId)
    {
        return await (
            from u in db.Users.AsNoTracking()
            join b in db.Businesses on u.BusinessId equals b.Id
            where u.Id == userId && u.Active && b.Active
            select b
        ).FirstOrDefaultAsync();
    }
}
