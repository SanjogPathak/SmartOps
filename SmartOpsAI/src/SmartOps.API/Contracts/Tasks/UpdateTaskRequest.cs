namespace SmartOps.API.Contracts.Tasks;

public record UpdateTaskRequest(string Title, string? Description, string Status, int Priority);