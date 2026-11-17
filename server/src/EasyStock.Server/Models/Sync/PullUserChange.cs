namespace EasyStock.Server.Models.Sync;

/// <summary>A user whose <c>server_seq</c> is in the pull window (display name or deleted state changed).</summary>
public readonly record struct PullUserChange(Guid Id, string DisplayName, long ServerSeq);
