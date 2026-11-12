namespace EasyStock.Server.Enums;


/// <summary>What a user can do. Same entries as the app's <c>Action</c> enum, in the same order.</summary>
public enum Permission
{
    /// <summary>Stock list, article and location details, search.</summary>
    ViewStock,

    /// <summary>Movement history, with authors.</summary>
    ViewHistory,

    /// <summary>Reviews and finished reviews.</summary>
    ViewReviews,

    /// <summary>Add, remove, transfer, lend, return stock.</summary>
    StockAction,

    /// <summary>Create and edit articles, units, locations, categories, attributes, name templates.</summary>
    Edit,

    /// <summary>Soft-delete articles, units, locations, categories, attributes.</summary>
    Delete,

    /// <summary>Link an NFC tag to an article or a location, or unlink it.</summary>
    LinkTag,

    /// <summary>Start, count and finish a stock review.</summary>
    RunReview,

    /// <summary>Settings that change data: categories, attributes, locations, tags, backup import.</summary>
    ManageSettings,

    /// <summary>Send changes to the server (sync push).</summary>
    PushChanges,

    /// <summary>Low-stock alert notifications.</summary>
    ReceiveAlerts,

    /// <summary>Users, invites, printed QR code, server backup, server settings.</summary>
    Administer,
}

