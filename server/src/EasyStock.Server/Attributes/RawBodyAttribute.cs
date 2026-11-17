
namespace EasyStock.Server.Attributes;

/// <summary>Documents a request body that the action reads from <c>Request.Body</c> instead of binding it.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RawBodyAttribute(params string[] contentTypes) : Attribute
{
    public string[] ContentTypes { get; } = contentTypes;
    public string? Description { get; init; }
}
