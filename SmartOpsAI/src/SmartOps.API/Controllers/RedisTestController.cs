using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;

namespace SmartOps.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RedisTestController : ControllerBase
{
    private readonly IDistributedCache _cache;

    public RedisTestController(IDistributedCache cache) => _cache = cache;

    [HttpGet("ping")]
    public async Task<IActionResult> Ping()
    {
        try
        {
            var key = "SmartOps.redis";
            await _cache.SetStringAsync(key, DateTime.UtcNow.ToString("O"));
            var val = await _cache.GetStringAsync(key);
            return Ok(new { key, val });
        }
        catch (Exception ex)
        {
            return Problem(
                detail: ex.ToString(),
                title: "Redis ping failed",
                statusCode: 500);
        }
    }
}