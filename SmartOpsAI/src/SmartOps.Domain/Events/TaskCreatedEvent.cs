using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SmartOps.Domain.Events;

public record TaskCreatedEvent(
    Guid TaskId,
    string Title,
    string? Description,
    string CreatedByUserId,
    DateTimeOffset CreatedAt
);
