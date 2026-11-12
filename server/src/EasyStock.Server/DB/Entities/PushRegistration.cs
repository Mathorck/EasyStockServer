namespace EasyStock.Server.DB.Entities;

/// <summary>
/// UnifiedPush endpoint of one device session (docs/03-server-spec.md, "Server-only tables").
/// Server-only, so no server_seq and no pull. Timestamps are epoch milliseconds UTC, like the shared tables.
/// One row per session: the primary key is the session ID, so registering again replaces the endpoint.
/// </summary>
public sealed class PushRegistration
{
    public Guid DeviceSessionId { get; set; }
    public string Endpoint { get; set; } = "";
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }
}
