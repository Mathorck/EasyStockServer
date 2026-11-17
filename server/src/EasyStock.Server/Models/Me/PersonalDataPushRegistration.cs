namespace EasyStock.Server.Models.Me;

/// <summary>Only the host of the UnifiedPush endpoint: the full URL holds a secret.</summary>
public sealed record PersonalDataPushRegistration(Guid DeviceId, string? EndpointHost, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
