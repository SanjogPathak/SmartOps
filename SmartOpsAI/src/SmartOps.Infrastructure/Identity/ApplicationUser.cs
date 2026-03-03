
using Microsoft.AspNetCore.Identity;

namespace SmartOps.Infrastructure.Identity
{
    public class ApplicationUser : IdentityUser
    {
        public string? DisplayName { get; set; }
    }
}
