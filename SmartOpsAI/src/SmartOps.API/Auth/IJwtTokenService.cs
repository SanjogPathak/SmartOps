using SmartOps.Infrastructure.Identity;

namespace SmartOps.API.Auth;

public interface IJwtTokenService
{
    Task<string> CreateTokenAsync(ApplicationUser user);
}
