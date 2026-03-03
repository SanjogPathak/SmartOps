using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Configuration;
using SmartOps.Domain.Events;
using System.Text.Json;

namespace SmartOps.API.Messaging;

public class ServiceBusTaskEventPublisher : ITaskEventPublisher
{
    private readonly ServiceBusSender _sender;

    public ServiceBusTaskEventPublisher(IConfiguration config)
    {
        var conn = config["ServiceBus:ConnectionString"]!;
        var queue = config["ServiceBus:TaskEventsQueue"]!;

        var client = new ServiceBusClient(conn);
        _sender = client.CreateSender(queue);
    }

    public async Task PublishTaskCreatedAsync(TaskCreatedEvent evt, CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(evt);

        var msg = new ServiceBusMessage(body)
        {
            Subject = "TaskCreated",
            ContentType = "application/json",
            MessageId = evt.TaskId.ToString()
        };

        await _sender.SendMessageAsync(msg, ct);
    }
}
