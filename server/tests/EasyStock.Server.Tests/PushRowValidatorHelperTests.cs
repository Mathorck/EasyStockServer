using Xunit;
using EasyStock.Server.DB.Entities;
using EasyStock.Server.Helpers;

namespace EasyStock.Server.Tests;

public sealed class PushRowValidatorHelperTests
{
    private static StockMovement Movement(long quantity, string? comment = null) => new()
    {
        Id = Guid.NewGuid(), ArticleId = Guid.NewGuid(), LocationId = Guid.NewGuid(), Quantity = quantity,
        Reason = "purchase", Comment = comment, OccurredAt = 5, CreatedAt = 5,
    };

    [Theory]
    [InlineData(1000, true)]
    [InlineData(-2000, true)]
    [InlineData(PushRowValidatorHelper.QuantityMax, true)]
    [InlineData(-PushRowValidatorHelper.QuantityMax, true)]
    [InlineData(0, false)]
    [InlineData(PushRowValidatorHelper.QuantityMax + 1, false)]
    [InlineData(-PushRowValidatorHelper.QuantityMax - 1, false)]
    [InlineData(long.MinValue, false)]
    [InlineData(long.MaxValue, false)]
    public void Movement_quantity_is_signed_non_zero_and_bounded(long quantity, bool valid) =>
        Assert.Equal(valid, PushRowValidatorHelper.IsValid(Movement(quantity)));

    [Fact]
    public void Threshold_and_review_quantities_are_bounded()
    {
        var article = new Article { Name = "a", Threshold = PushRowValidatorHelper.QuantityMax + 1 };
        Assert.False(PushRowValidatorHelper.IsValid(article));
        article.Threshold = 5;
        Assert.True(PushRowValidatorHelper.IsValid(article));
        Assert.False(PushRowValidatorHelper.IsValid(new StockReviewLine { ExpectedQuantity = long.MaxValue }));
        Assert.False(PushRowValidatorHelper.IsValid(new StockReviewLine { CountedQuantity = long.MaxValue }));
    }

    [Fact]
    public void Nul_characters_are_rejected_in_every_text_field()
    {
        Assert.False(PushRowValidatorHelper.IsValid(new Category { Name = "a\0b" }));
        Assert.False(PushRowValidatorHelper.IsValid(new Category { Name = "a", Description = "x\0" }));
        Assert.False(PushRowValidatorHelper.IsValid(new Location { Name = "a", Barcode = "\0" }));
        Assert.False(PushRowValidatorHelper.IsValid(new Article { Name = "a", Description = "\0" }));
        Assert.False(PushRowValidatorHelper.IsValid(new Item { SerialNumber = "s\0" }));
        Assert.False(PushRowValidatorHelper.IsValid(Movement(1000, "note\0")));
        Assert.False(PushRowValidatorHelper.IsValid(new StockReviewLine { Note = "\0" }));
        Assert.False(PushRowValidatorHelper.IsValid(new Article { Name = "a", Attributes = "{\"k\":\"v\\u0000\"}" }));
        Assert.True(PushRowValidatorHelper.IsValid(Movement(1000, "fine")));
        Assert.True(PushRowValidatorHelper.IsValid(new Article { Name = "a", Attributes = "{\"k\":\"v\"}" }));
    }
}
