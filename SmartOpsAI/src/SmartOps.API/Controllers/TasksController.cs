using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using SmartOps.API.Caching;
using SmartOps.API.Contracts.Tasks;
using SmartOps.API.Messaging;
using SmartOps.API.Security;
using SmartOps.Domain.Entities;
using SmartOps.Domain.Events;
using SmartOps.Infrastructure.Persistence;
using System.Text.Json;

namespace SmartOps.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TasksController : ControllerBase
{
    private readonly SmartOpsDbContext _db;
    private readonly ITaskEventPublisher _publisher;
    private readonly IDistributedCache _cache;
    private readonly RedisLockService _lockService;

    public TasksController(
        SmartOpsDbContext db,
        ITaskEventPublisher publisher,
        IDistributedCache cache,
        RedisLockService lockService)
    {
        _db = db;
        _publisher = publisher;
        _cache = cache;
        _lockService = lockService;
    }

    // -----------------------
    // Cache key helpers
    // -----------------------
    private static string TasksVersionKey(string userId) => $"tasks:ver:{userId}";

    private static string TasksListCacheKey(
        string userId,
        string ver,
        int page,
        int pageSize,
        string? status,
        string? search)
        => $"tasks:list:{userId}:v{ver}:p{page}:s{pageSize}:st{status ?? ""}:q{search ?? ""}";

    private static string TaskByIdCacheKey(string userId, string ver, Guid taskId)
        => $"tasks:byid:{userId}:v{ver}:{taskId}";

    private static string LockKey(string cacheKey) => $"lock:{cacheKey}";

    private Task BumpTasksVersionAsync(string userId) =>
        _cache.SetStringAsync(TasksVersionKey(userId), Guid.NewGuid().ToString("N"));

    // -----------------------
    // POST: api/tasks
    // -----------------------
    [HttpPost]
    public async Task<ActionResult<TaskResponse>> Create(CreateTaskRequest request)
    {
        var userId = User.GetUserId();

        var task = new TaskItem
        {
            Title = request.Title.Trim(),
            Description = request.Description,
            Status = "New",
            Priority = 3,
            CreatedByUserId = userId
        };

        _db.Tasks.Add(task);
        await _db.SaveChangesAsync();

        // Invalidate user caches
        await BumpTasksVersionAsync(userId);

        // Publish event (best-effort; we’ll harden with Outbox later)
        await _publisher.PublishTaskCreatedAsync(new TaskCreatedEvent(
            task.Id,
            task.Title,
            task.Description,
            task.CreatedByUserId,
            task.CreatedAt));

        return CreatedAtAction(nameof(GetById), new { id = task.Id }, Map(task));
    }

    // -----------------------
    // GET: api/tasks?page=1&pageSize=10&status=New&search=foo
    // -----------------------
    [HttpGet]
    public async Task<ActionResult<object>> GetList(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? status = null,
        [FromQuery] string? search = null)
    {
        var userId = User.GetUserId();

        // Normalize paging BEFORE cache key
        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 100 ? 10 : pageSize;

        var ver = await _cache.GetStringAsync(TasksVersionKey(userId)) ?? "0";
        var cacheKey = TasksListCacheKey(userId, ver, page, pageSize, status, search);

        // Cache hit
        var cachedJson = await _cache.GetStringAsync(cacheKey);
        if (!string.IsNullOrWhiteSpace(cachedJson))
            return Ok(JsonSerializer.Deserialize<object>(cachedJson)!);

        // Atomic lock to prevent stampede
        var lockKey = LockKey(cacheKey);
        var token = await _lockService.TryAcquireAsync(lockKey, TimeSpan.FromSeconds(10));

        if (token is null)
        {
            // Someone else is building cache; wait and retry
            await Task.Delay(200);

            cachedJson = await _cache.GetStringAsync(cacheKey);
            if (!string.IsNullOrWhiteSpace(cachedJson))
                return Ok(JsonSerializer.Deserialize<object>(cachedJson)!);

            // Still missing (rare): proceed without lock
        }

        try
        {
            var query = _db.Tasks.AsNoTracking()
                .Where(t => t.CreatedByUserId == userId);

            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(t => t.Status == status);

            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(t =>
                    t.Title.Contains(search) ||
                    (t.Description != null && t.Description.Contains(search)));

            var total = await query.CountAsync();

            var items = await query
                .OrderByDescending(t => t.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(t => Map(t))
                .ToListAsync();

            var response = new
            {
                page,
                pageSize,
                total,
                items
            };

            await _cache.SetStringAsync(
                cacheKey,
                JsonSerializer.Serialize(response),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2)
                });

            return Ok(response);
        }
        finally
        {
            // Release only if acquired
            if (token is not null)
                await _lockService.ReleaseAsync(lockKey, token);
        }
    }

    // -----------------------
    // GET: api/tasks/{id}
    // -----------------------
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TaskResponse>> GetById(Guid id)
    {
        var userId = User.GetUserId();

        var ver = await _cache.GetStringAsync(TasksVersionKey(userId)) ?? "0";
        var cacheKey = TaskByIdCacheKey(userId, ver, id);

        // Cache hit
        var cachedJson = await _cache.GetStringAsync(cacheKey);
        if (!string.IsNullOrWhiteSpace(cachedJson))
        {
            var cached = JsonSerializer.Deserialize<TaskResponse>(cachedJson);
            if (cached is not null) return Ok(cached);
        }

        // Optional: lock by-id rebuild too (helps under load)
        var lockKey = LockKey(cacheKey);
        var token = await _lockService.TryAcquireAsync(lockKey, TimeSpan.FromSeconds(10));

        if (token is null)
        {
            await Task.Delay(150);
            cachedJson = await _cache.GetStringAsync(cacheKey);
            if (!string.IsNullOrWhiteSpace(cachedJson))
            {
                var cached = JsonSerializer.Deserialize<TaskResponse>(cachedJson);
                if (cached is not null) return Ok(cached);
            }
        }

        try
        {
            var task = await _db.Tasks.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == id && t.CreatedByUserId == userId);

            if (task is null) return NotFound();

            var response = Map(task);

            await _cache.SetStringAsync(
                cacheKey,
                JsonSerializer.Serialize(response),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
                });

            return Ok(response);
        }
        finally
        {
            if (token is not null)
                await _lockService.ReleaseAsync(lockKey, token);
        }
    }

    // -----------------------
    // PUT: api/tasks/{id}
    // -----------------------
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateTaskRequest request)
    {
        var userId = User.GetUserId();

        var task = await _db.Tasks
            .FirstOrDefaultAsync(t => t.Id == id && t.CreatedByUserId == userId);

        if (task is null) return NotFound();

        task.Title = request.Title.Trim();
        task.Description = request.Description;
        task.Status = request.Status;
        task.Priority = request.Priority;
        task.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync();

        // Invalidate caches for this user
        await BumpTasksVersionAsync(userId);

        return NoContent();
    }

    // -----------------------
    // DELETE: api/tasks/{id}
    // -----------------------
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var userId = User.GetUserId();

        var task = await _db.Tasks
            .FirstOrDefaultAsync(t => t.Id == id && t.CreatedByUserId == userId);

        if (task is null) return NotFound();

        _db.Tasks.Remove(task);
        await _db.SaveChangesAsync();

        // Invalidate caches for this user
        await BumpTasksVersionAsync(userId);

        return NoContent();
    }

    private static TaskResponse Map(TaskItem t) =>
        new(t.Id, t.Title, t.Description, t.Status, t.Priority, t.CreatedAt, t.UpdatedAt);
}