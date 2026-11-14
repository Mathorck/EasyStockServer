using EasyStock.Server.Enums;
using EasyStock.Server.Helpers;
using EasyStock.Server.Models;
using Xunit;

namespace EasyStock.Server.Tests;

/// <summary><see cref="BackupMergeHelper.Plan"/> on unique columns: pure, no database.</summary>
public sealed class BackupMergeHelperTests
{
    private static string Id(int n) => $"00000000-0000-0000-0000-{n:D12}";

    private static BackupCategory Category(int id, string name, long at = 1) => new(Id(id), name, 1, at);
    private static BackupArticle Article(int id, string? sku = null, long at = 1) => new(Id(id), $"Article {id}", 1, at, Sku: sku);
    private static BackupItem Item(int id, string serial, long at = 1) => new(Id(id), Id(100), 1, at, SerialNumber: serial);
    private static BackupAttributeDefinition Attribute(int id) => new(Id(id), $"key{id}", $"Label {id}", "text", 1, 1);
    private static BackupCategoryAttribute Link(int id, int category, int attribute, long at = 1) => new(Id(id), Id(category), Id(attribute), 1, at);

    [Fact]
    public void Skus_swapped_by_newer_rows_are_kept_without_suffix()
    {
        var local = new BackupData { Articles = [Article(1, "X"), Article(2, "Y")] };
        var incoming = new BackupData { Articles = [Article(1, "Y", at: 2), Article(2, "X", at: 2)] };

        var plan = BackupMergeHelper.Plan(local, incoming);

        Assert.Empty(plan.Conflicts);
        Assert.Equal("Y", plan.Result.Articles.Single(a => a.Id == Id(1)).Sku);
        Assert.Equal("X", plan.Result.Articles.Single(a => a.Id == Id(2)).Sku);
        Assert.Equal(2, plan.Updates.Articles.Count);
    }

    [Fact]
    public void Category_names_swapped_by_newer_rows_are_kept_without_suffix()
    {
        var local = new BackupData { Categories = [Category(1, "Tools"), Category(2, "Parts")] };
        var incoming = new BackupData { Categories = [Category(1, "Parts", at: 2), Category(2, "Tools", at: 2)] };

        var plan = BackupMergeHelper.Plan(local, incoming);

        Assert.Empty(plan.Conflicts);
        Assert.Equal("Parts", plan.Result.Categories.Single(c => c.Id == Id(1)).Name);
        Assert.Equal("Tools", plan.Result.Categories.Single(c => c.Id == Id(2)).Name);
    }

    [Fact]
    public void Serials_swapped_by_newer_rows_are_kept_without_suffix()
    {
        var article = new BackupArticle(Id(100), "Drill", 1, 1, Tracking: "serial");
        var local = new BackupData { Articles = [article], Items = [Item(1, "S1"), Item(2, "S2")] };
        var incoming = new BackupData { Items = [Item(1, "S2", at: 2), Item(2, "S1", at: 2)] };

        var plan = BackupMergeHelper.Plan(local, incoming);

        Assert.Empty(plan.Conflicts);
        Assert.Equal("S2", plan.Result.Items.Single(i => i.Id == Id(1)).SerialNumber);
        Assert.Equal("S1", plan.Result.Items.Single(i => i.Id == Id(2)).SerialNumber);
    }

    [Fact]
    public void A_value_freed_by_a_newer_row_is_taken_by_a_lower_id()
    {
        // Article 1 takes the SKU article 2 moves away from, although article 2 is processed after it
        var local = new BackupData { Articles = [Article(1, "A"), Article(2, "B")] };
        var incoming = new BackupData { Articles = [Article(1, "B", at: 2), Article(2, "C", at: 2)] };

        var plan = BackupMergeHelper.Plan(local, incoming);

        Assert.Empty(plan.Conflicts);
        Assert.Equal(["B", "C"], plan.Result.Articles.OrderBy(a => a.Id, StringComparer.Ordinal).Select(a => a.Sku));
    }

    [Fact]
    public void Category_attribute_pairs_swapped_by_newer_links_are_both_kept()
    {
        var local = new BackupData
        {
            Categories = [Category(1, "One"), Category(2, "Two")],
            AttributeDefinitions = [Attribute(10)],
            CategoryAttributes = [Link(20, 1, 10), Link(21, 2, 10)],
        };
        var incoming = new BackupData { CategoryAttributes = [Link(20, 2, 10, at: 2), Link(21, 1, 10, at: 2)] };

        var plan = BackupMergeHelper.Plan(local, incoming);

        Assert.Empty(plan.Conflicts);
        Assert.Equal(Id(2), plan.Result.CategoryAttributes.Single(l => l.Id == Id(20)).CategoryId);
        Assert.Equal(Id(1), plan.Result.CategoryAttributes.Single(l => l.Id == Id(21)).CategoryId);
    }

    [Fact]
    public void A_skipped_link_keeps_its_local_pair_even_when_another_link_wanted_it()
    {
        // Link 21 moves to category 3's pair, held by link 22: skipped, it keeps category 2's pair, so link 20 can't
        // move there either and keeps category 1's.
        var local = new BackupData
        {
            Categories = [Category(1, "One"), Category(2, "Two"), Category(3, "Three")],
            AttributeDefinitions = [Attribute(10)],
            CategoryAttributes = [Link(20, 1, 10), Link(21, 2, 10), Link(22, 3, 10)],
        };
        var incoming = new BackupData { CategoryAttributes = [Link(20, 2, 10, at: 2), Link(21, 3, 10, at: 2)] };

        var plan = BackupMergeHelper.Plan(local, incoming);

        Assert.Equal([Id(20), Id(21)], plan.Conflicts.Select(c => c.RowId).Order(StringComparer.Ordinal));
        Assert.All(plan.Conflicts, c => Assert.Equal(ConflictType.CategoryAttributeSkipped, c.Type));
        Assert.Equal(local.CategoryAttributes, plan.Result.CategoryAttributes.OrderBy(l => l.Id, StringComparer.Ordinal));
        Assert.True(plan.IsEmpty);
    }

    [Fact]
    public void Names_differing_only_in_case_still_clash_like_in_the_app()
    {
        var local = new BackupData { Categories = [Category(1, "Milk")] };
        var incoming = new BackupData { Categories = [Category(2, "milk")] };

        var plan = BackupMergeHelper.Plan(local, incoming);

        Assert.Equal(new MergeConflict(ConflictType.CategoryRenamed, BackupTable.Category, Id(2), "milk", "milk (2)"), plan.Conflicts.Single());
    }

    [Fact]
    public void Local_case_variants_both_hold_their_value()
    {
        // The database allows "Milk" and "milk": when one moves away, the other still holds the name
        var local = new BackupData { Categories = [Category(1, "Milk"), Category(2, "milk")] };
        var incoming = new BackupData { Categories = [Category(2, "Dairy", at: 2), Category(3, "MILK")] };

        var plan = BackupMergeHelper.Plan(local, incoming);

        Assert.Equal("MILK (2)", plan.Result.Categories.Single(c => c.Id == Id(3)).Name);
        Assert.Equal(ConflictType.CategoryRenamed, plan.Conflicts.Single().Type);
    }

    [Fact]
    public void Suffixes_continue_after_the_taken_ones()
    {
        var local = new BackupData { Categories = [Category(1, "Tools"), Category(2, "Tools (2)"), Category(3, "Tools (4)")] };
        var incoming = new BackupData { Categories = [Category(4, "Tools"), Category(5, "Tools"), Category(6, "tools")] };

        var plan = BackupMergeHelper.Plan(local, incoming);

        Assert.Equal(["Tools (3)", "Tools (5)", "tools (6)"],
            plan.Inserts.Categories.OrderBy(c => c.Id, StringComparer.Ordinal).Select(c => c.Name));
    }
}
