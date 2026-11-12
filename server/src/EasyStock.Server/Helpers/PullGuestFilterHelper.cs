using EasyStock.Server.Interfaces;
using EasyStock.Server.Enums;
using EasyStock.Server.Helpers;
using EasyStock.Server.Models;

namespace EasyStock.Server.Helpers;

/// <summary>
/// The Guest filter (docs/03-server-spec.md, "Pull building"; plan 3.10). The only place that removes data a role
/// may not read from a pull answer, called only by <see cref="PullHandler"/>, after the page cut so the cursor still
/// advances past filtered rows. Driven by <see cref="Permissions"/>, plus <see cref="ICurrentUser.IsGuest"/>:
/// <list type="bullet">
/// <item>without ViewHistory: movements lose createdBy, deviceId and comment; users is empty;</item>
/// <item>without ViewReviews: stock_review and stock_review_line are left out;</item>
/// <item>without ReceiveAlerts: alerts is empty.</item>
/// </list>
/// </summary>
internal static class PullGuestFilterHelper
{
    public static SyncPullResponse Apply(SyncPullResponse response, ICurrentUser caller)
    {
        bool Can(Permission p) => !caller.IsGuest && Permissions.Can(caller.Role, p);

        var rows = new Dictionary<string, List<System.Text.Json.Nodes.JsonObject>>(response.Rows);
        var users = response.Users;
        var alerts = response.Alerts;

        if (!Can(Permission.ViewHistory))
        {
            if (rows.TryGetValue(PullTablesHelper.StockMovement, out var movements))
                foreach (var movement in movements)
                    foreach (var field in PullTablesHelper.MovementPrivateFields)
                        movement.Remove(field);
            users = [];
        }
        if (!Can(Permission.ViewReviews))
            foreach (var table in PullTablesHelper.ReviewTables)
                rows.Remove(table);
        if (!Can(Permission.ReceiveAlerts))
            alerts = [];

        return response with { Rows = rows, Users = users, Alerts = alerts };
    }
}
