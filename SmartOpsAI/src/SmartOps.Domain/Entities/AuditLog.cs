using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SmartOps.Domain.Entities
{
    public class AuditLog
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string Action { get; set; } = default!;
        public string EntityType { get; set; } = default!;
        public string EntityId { get; set; } = default!;
        public string PerformedByUserId { get; set; } = default!;
        public DateTimeOffset PerformedAt { get; set; } = DateTimeOffset.UtcNow;

        public string? MetadataJson { get; set; }
    }
}
