
namespace EasyStock.Server.Models.Cleanup;

/// <summary>Rows touched by one cleanup run. Counts only: nothing identifying is ever logged.</summary>
public sealed record CleanupCounts(
    int PromotionsExpired, int Invites, int PasswordResets, int Promotions, int Sessions,
    int Guests, int SyncBatches, int Images);
