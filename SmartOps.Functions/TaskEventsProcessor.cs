using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using SmartOps.Domain.Events;
using SmartOps.Domain.Entities;
using SmartOps.Infrastructure.Persistence;
using System.Text.Json;

namespace SmartOps.Functions;

public class TaskEventsProcessor
{
    private readonly SmartOpsDbContext _db;
    private readonly ILogger<TaskEventsProcessor> _logger;

    public TaskEventsProcessor(SmartOpsDbContext db, ILogger<TaskEventsProcessor> logger)
    {
        _db = db;
        _logger = logger;
    }

    [Function(nameof(TaskEventsProcessor))]
    public async Task Run(
        [ServiceBusTrigger("task-events", Connection = "ServiceBusConnection")] string message)
    {
        _logger.LogInformation("Received message: {Message}", message);

        var evt = JsonSerializer.Deserialize<TaskCreatedEvent>(message);
        if (evt is null) return;

        _db.AuditLogs.Add(new AuditLog
        {
            Action = "TaskCreated",
            EntityType = "TaskItem",
            EntityId = evt.TaskId.ToString(),
            PerformedByUserId = evt.CreatedByUserId,
            MetadataJson = message
        });

        await _db.SaveChangesAsync();
    }
}