namespace EasyStock.Server.Models;

/// <summary>Body of POST /api/v1/push/register. Validated in PushController, not by data annotations.</summary>
public sealed record PushRegisterRequest(string? Endpoint);
