namespace SmartOps.API.Contracts.Auth;

public record RegisterRequest(string Email, string Password, string? DisplayName);
