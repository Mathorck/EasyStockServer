using EasyStock.Server.DB;

namespace EasyStock.Server.Helpers;

/// <summary>Hand-written SQL of the LowStockAlertSyncPush migration.</summary>
internal static class PushSqlHelper
{
    /// <summary>low_stock_alert is pulled by server_seq like the shared tables, so it gets the same trigger.</summary>
    public static string CreateLowStockAlertTrigger => ServerSeq.CreateTriggerSql("low_stock_alert");

    public const string DropLowStockAlertTrigger = "DROP TRIGGER low_stock_alert_server_seq ON low_stock_alert;";
}
