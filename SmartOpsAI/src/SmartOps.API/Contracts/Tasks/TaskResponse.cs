namespace SmartOps.API.Contracts.Tasks;

public record TaskResponse(
    Guid Id,
    string Title,
    string? Description,
    string Status,
    int Priority,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt
);