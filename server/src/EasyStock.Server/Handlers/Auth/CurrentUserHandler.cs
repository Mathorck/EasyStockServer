using EasyStock.Server.Enums;
using EasyStock.Server.Interfaces;

namespace EasyStock.Server.Handlers.Auth;

/// <summary>Default <see cref="ICurrentUser"/>, set by <see cref="SessionValidationHelper"/> once the session is checked.</summary>
public sealed class CurrentUserHandler : ICurrentUser
{
    private Guid _userId;
    private Guid _sessionId;

    public bool IsAuthenticated { get; private set; }
    public Guid UserId => IsAuthenticated ? _userId : throw NotAuthenticated();
    public Guid SessionId => IsAuthenticated ? _sessionId : throw NotAuthenticated();
    public Role Role { get; private set; } = Role.Guest;
    public bool IsGuest { get; private set; }

    internal void Set(Guid userId, Guid sessionId, Role role, bool isGuest)
    {
        _userId = userId;
        _sessionId = sessionId;
        Role = role;
        IsGuest = isGuest;
        IsAuthenticated = true;
    }

    private static InvalidOperationException NotAuthenticated() => new("The request is not authenticated.");
}
