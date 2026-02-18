using System.Security.Claims;

namespace Api.Extensions;

public static class ClaimsPrincipalExtensions
{
    public static Guid? GetUserId(this ClaimsPrincipal principal)
    {
        var subClaim = principal.FindFirst(ClaimTypes.NameIdentifier)
                    ?? principal.FindFirst("sub");

        if (subClaim == null || !Guid.TryParse(subClaim.Value, out var userId))
        {
            return null;
        }

        return userId;
    }

    public static string? GetUserEmail(this ClaimsPrincipal principal)
    {
        return principal.FindFirst(ClaimTypes.Email)?.Value
            ?? principal.FindFirst("email")?.Value;
    }

    public static bool IsAdmin(this ClaimsPrincipal principal)
    {
        return principal.IsInRole("admin");
    }
}
