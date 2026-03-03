using SmartOps.Domain.Events;

namespace SmartOps.API.Messaging;

public interface ITaskEventPublisher
{
    Task PublishTaskCreatedAsync(TaskCreatedEvent evt, CancellationToken ct = default);
}