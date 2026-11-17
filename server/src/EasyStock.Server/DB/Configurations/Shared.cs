using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Server.DB.Configurations;

// Column names come from the snake_case convention; lengths, uniques and indexes follow schema.dbml.
// Foreign keys are real but never cascade: rows are soft-deleted, and movements are append-only.

internal static class Shared
{
    public const string Json = "jsonb";

    /// <summary>
    /// server_seq column and its index, on every shared table. Set by the database trigger (see DB/ServerSeq.cs):
    /// EF never writes it and reads it back after each insert or update.
    /// </summary>
    public static void ServerSeq<T>(this EntityTypeBuilder<T> e) where T : class
    {
        e.Property<long>("ServerSeq").ValueGeneratedOnAddOrUpdate();
        e.HasIndex("ServerSeq");
    }

    public static ReferenceCollectionBuilder<TPrincipal, T> Ref<T, TPrincipal>(
        this EntityTypeBuilder<T> e, string foreignKey, bool required)
        where T : class where TPrincipal : class =>
        e.HasOne<TPrincipal>().WithMany().HasForeignKey(foreignKey).IsRequired(required).OnDelete(DeleteBehavior.Restrict);
}
