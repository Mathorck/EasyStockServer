namespace EasyStock.Server.DB.Entities;

// The ten tables shared with the app. Source of truth: docs/schema.dbml in the app repository.
// Server rules (docs/03-server-spec.md, "Shared tables"):
//   - timestamps are bigint epoch milliseconds UTC, like the app;
//   - decimal(12,3) quantities are bigint thousandths (1.5 kg = 1500), like the app;
//   - json columns are jsonb, kept as raw JSON text;
//   - every table has server_seq; article.image_path is image_sha256 on the server.
// SchemaDriftTests compares the migrated database against schema.dbml.

public sealed class Category
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? NameTemplate { get; set; }
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }
    public long? DeletedAt { get; set; }
    public long ServerSeq { get; set; }
}
