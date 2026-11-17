using System.ComponentModel.DataAnnotations;

namespace EasyStock.Server.Models.Auth;

public sealed record CompletePromotionRequest([Required] string Username, [Required] string Password)
{
    public override string ToString() => $"CompletePromotionRequest(username={Username})";
}
