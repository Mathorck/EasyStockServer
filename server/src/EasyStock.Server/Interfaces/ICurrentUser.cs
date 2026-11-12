using EasyStock.Server.Enums;

namespace EasyStock.Server.Interfaces;

/// <summary>
/// The caller of the current request (scoped). Filled in by the JWT session validation, which reads the session
/// and the user from the database: <see cref="Role"/> is the role in <c>users.role</c>, never the token claim.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>Throws if not authenticated.</summary>
    Guid UserId { get; }

    /// <summary>The <c>device_session</c> ID. Throws if not authenticated.</summary>
    Guid SessionId { get; }

    /// <summary>Read from the database (<c>users.role</c>); <see cref="Role.Guest"/> if unknown.</summary>
    Role Role { get; }

    /// <summary><c>users.kind</c> is guest.</summary>
    bool IsGuest { get; }
}
