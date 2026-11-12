using EasyStock.Server.Enums;

namespace EasyStock.Server.Helpers;

/// <summary>
/// The one rule for who receives low-stock alerts (docs/03-server-spec.md, "Low-stock alerts"; plan 1.2 #12).
/// <c>users.alerts_enabled</c> null means the role default: Admin and Member on, Viewer off, Guest never.
/// </summary>
public static class AlertRecipientsHelper
{
    public static bool IsRecipient(Role? role, bool isGuest, bool? alertsEnabled)
    {
        if (isGuest || !Permissions.Can(role, Permission.ReceiveAlerts)) return false;
        return alertsEnabled ?? role is Role.Admin or Role.Member;
    }
}
