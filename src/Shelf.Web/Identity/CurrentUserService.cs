using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Shelf.Web.Data;
using Shelf.Web.Data.Entities;

namespace Shelf.Web.Identity;

/// <summary>
/// Resolves the current <see cref="User"/> from the Easy Auth principal header
/// (production) or a fixed local identity (Development), auto-creating the
/// User row on first sight. See TECHNICAL_DESIGN.md § User identity /
/// § Local Development.
/// </summary>
public class CurrentUserService(
    IHttpContextAccessor httpContextAccessor,
    ShelfDbContext db,
    IHostEnvironment env) : ICurrentUserService
{
    private const string EasyAuthHeaderName = "X-MS-CLIENT-PRINCIPAL";
    private const string LocalDevExternalIdentity = "local-dev";
    private const string LocalDevName = "Tom";

    // Priority order: the stable Entra object id first, never name/email
    // (those can change and would silently split one person into two rows).
    private static readonly string[] ExternalIdentityClaimTypes =
    [
        "http://schemas.microsoft.com/identity/claims/objectidentifier",
        "sub",
        "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier",
    ];

    private static readonly string[] NameClaimTypes =
    [
        "name",
        "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name",
        "preferred_username",
    ];

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private User? _cachedUser;

    public async Task<User> GetCurrentUserAsync(CancellationToken ct = default)
    {
        if (_cachedUser is not null)
        {
            return _cachedUser;
        }

        var (externalIdentity, name) = ResolveIdentity();

        var user = await db.Users.SingleOrDefaultAsync(u => u.ExternalIdentity == externalIdentity, ct);
        if (user is null)
        {
            user = new User { ExternalIdentity = externalIdentity, Name = name };
            db.Users.Add(user);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Lost a race with another request creating the same row first
                // (e.g. two near-simultaneous requests on a user's very first
                // visit, each with its own DbContext). Drop our failed insert
                // and read back the row the other request committed instead
                // of failing the request entirely.
                db.Entry(user).State = EntityState.Detached;
                user = await db.Users.SingleAsync(u => u.ExternalIdentity == externalIdentity, ct);
            }
        }

        _cachedUser = user;
        return user;
    }

    private (string ExternalIdentity, string Name) ResolveIdentity()
    {
        var headerValue = httpContextAccessor.HttpContext?.Request.Headers[EasyAuthHeaderName].ToString();

        if (!string.IsNullOrWhiteSpace(headerValue))
        {
            var claims = ParseClaims(headerValue);

            var externalIdentity = FindClaim(claims, ExternalIdentityClaimTypes);
            if (string.IsNullOrEmpty(externalIdentity))
            {
                throw new InvalidOperationException(
                    $"Easy Auth principal header present but no usable identity claim found among: {string.Join(", ", ExternalIdentityClaimTypes)}.");
            }

            var name = FindClaim(claims, NameClaimTypes) ?? "User";
            return (externalIdentity, name);
        }

        if (env.IsDevelopment())
        {
            return (LocalDevExternalIdentity, LocalDevName);
        }

        throw new InvalidOperationException(
            $"No '{EasyAuthHeaderName}' header present. Easy Auth should guarantee every production request is authenticated.");
    }

    private static IReadOnlyList<ClientPrincipalClaim> ParseClaims(string headerValue)
    {
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(headerValue));
        var principal = JsonSerializer.Deserialize<ClientPrincipal>(json, JsonOptions);
        return principal?.Claims ?? [];
    }

    private static string? FindClaim(IReadOnlyList<ClientPrincipalClaim> claims, IReadOnlyList<string> claimTypesInPriorityOrder)
    {
        foreach (var claimType in claimTypesInPriorityOrder)
        {
            var match = claims.FirstOrDefault(c => string.Equals(c.Typ, claimType, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(match?.Val))
            {
                return match.Val;
            }
        }

        return null;
    }

    private sealed record ClientPrincipal(List<ClientPrincipalClaim>? Claims);

    private sealed record ClientPrincipalClaim(string? Typ, string? Val);
}
