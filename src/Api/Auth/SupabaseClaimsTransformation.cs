using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;

namespace Api.Auth;

public class SupabaseClaimsTransformation : IClaimsTransformation
{
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        var identity = principal.Identity as ClaimsIdentity;
        if (identity == null || !identity.IsAuthenticated)
            return Task.FromResult(principal);

        if (identity.HasClaim(ClaimTypes.Role, "admin"))
            return Task.FromResult(principal);

        var appMetadataClaim = identity.FindFirst("app_metadata");
        if (appMetadataClaim == null)
            return Task.FromResult(principal);

        try
        {
            using var doc = JsonDocument.Parse(appMetadataClaim.Value);
            if (doc.RootElement.TryGetProperty("role", out var roleProp))
            {
                var role = roleProp.GetString();
                if (!string.IsNullOrEmpty(role))
                {
                    identity.AddClaim(new Claim(ClaimTypes.Role, role));
                }
            }
        }
        catch (JsonException)
        {
            // Malformed app_metadata JSON — skip silently
        }

        return Task.FromResult(principal);
    }
}
