namespace EasyStock.Server.DB.Entities;

/// <summary>Table <c>setup_state</c>, one row (<see cref="SingletonId"/>): the setup token until the first Admin exists.</summary>
public sealed class SetupState
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;
    public byte[]? TokenHash { get; set; }
    public bool Done { get; set; }
    public DateTimeOffset? DoneAt { get; set; }
}
