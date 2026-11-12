namespace EasyStock.Server.Enums;

/// <summary>
/// Role of a user. Same roles as the app's <c>auth/Permissions.kt</c>: <see cref="Member"/> is the app's Editor,
/// sent as "member" on the wire (the app maps it both ways with WireRole).
/// </summary>
public enum Role
{
    /// <summary>Everything, including administration.</summary>
    Admin,

    /// <summary>Changes the stock and the catalogue, no administration. The app calls it Editor.</summary>
    Member,

    /// <summary>Reads everything, movement history included, changes nothing.</summary>
    Viewer,

    /// <summary>Printed QR code, no account: reads the stock only.</summary>
    Guest,
}
