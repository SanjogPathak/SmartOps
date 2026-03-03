using StackExchange.Redis;

namespace SmartOps.API.Caching;

public class RedisLockService
{
    private readonly IDatabase _db;

    public RedisLockService(IConnectionMultiplexer mux)
    {
        _db = mux.GetDatabase();
    }

    public async Task<string?> TryAcquireAsync(string lockKey, TimeSpan ttl)
    {
        var token = Guid.NewGuid().ToString("N");
        var acquired = await _db.StringSetAsync(lockKey, token, ttl, When.NotExists);
        return acquired ? token : null;
    }

    public async Task ReleaseAsync(string lockKey, string token)
    {
        // Release only if token matches (safe unlock)
        const string script = @"
if redis.call('get', KEYS[1]) == ARGV[1] then
  return redis.call('del', KEYS[1])
else
  return 0
end";
        await _db.ScriptEvaluateAsync(script, new RedisKey[] { lockKey }, new RedisValue[] { token });
    }
}