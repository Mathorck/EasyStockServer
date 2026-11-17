namespace EasyStock.Server.DB.Entities;

/// <summary>
/// An attribute_definition ID that a push merged into the server attribute with the same key (ATTRIBUTE_MERGED): a
/// later batch that still uses the phone's old ID is redirected to the server one.
/// </summary>
public sealed class AttributeAlias
{
    /// <summary>The phone's ID, not in attribute_definition.</summary>
    public Guid AliasId { get; set; }

    /// <summary>The server attribute it was merged into.</summary>
    public Guid AttributeId { get; set; }
}
