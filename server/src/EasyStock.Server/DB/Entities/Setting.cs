namespace EasyStock.Server.DB.Entities;

/// <summary>A server-only setting (Admin). Value holds the JSON text of the setting, stored as jsonb.</summary>
public sealed class Setting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "null";
}
