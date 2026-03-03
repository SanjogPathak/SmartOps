using System.Security.Claims;

namespace SmartOps.API.Security;

public static class ClaimsPrincipalExtensions
{
    public static string GetUserId(this ClaimsPrincipal user)
    {
        // In our JWT we set sub = user.Id
        return user.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? user.FindFirstValue("sub")
               ?? throw new InvalidOperationException("User id claim not found.");
    }
}