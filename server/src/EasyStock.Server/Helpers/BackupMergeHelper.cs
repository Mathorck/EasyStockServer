using EasyStock.Server.Interfaces;
using EasyStock.Server.Models;
using EasyStock.Server.Handlers.Backup;
using EasyStock.Server.Enums;

namespace EasyStock.Server.Helpers;

/// <summary>
/// Port of the app's <c>planMerge</c> (BackupMerge.kt), rule for rule. Merges <c>incoming</c> into <c>local</c> by id
/// (every row of both, soft-deleted ones included):
/// a row on one side only is added; a row on both sides: the newer <c>updated_at</c> wins (a soft delete is an update),
/// the local one on a tie; <c>stock_movement</c> is append-only; unique columns are resolved deterministically (rows
/// sorted by id) and reported as conflicts; <c>item.location_id</c> is recomputed from the merged movements; reviews
/// and their lines merge like the other rows. Incoming must be valid. Pure: nothing is written.
/// </summary>
public static class BackupMergeHelper
{
    public const int MaxCategoryNameLength = 100;
    public const int MaxSkuLength = 50;
    public const int MaxSerialLength = 100;

    public static MergePlan Plan(BackupData local, BackupData incoming) => new Merger(local, incoming).Plan();

    /// <summary>
    /// Current location of each serial unit, from its movements: the location where its sum is positive (lowest id when
    /// several), null when it is out of stock (lent, lost).
    /// </summary>
    public static Dictionary<string, string?> ItemLocations(IEnumerable<BackupItem> items, IEnumerable<BackupStockMovement> movements)
    {
        var sums = new Dictionary<string, Dictionary<string, long>>(StringComparer.Ordinal);
        foreach (var m in movements)
        {
            if (m.ItemId is null) continue;
            if (!sums.TryGetValue(m.ItemId, out var perLocation)) sums[m.ItemId] = perLocation = new(StringComparer.Ordinal);
            perLocation[m.LocationId] = perLocation.GetValueOrDefault(m.LocationId) + m.Quantity;
        }
        return items.ToDictionary(i => i.Id, i => sums.TryGetValue(i.Id, out var per)
            ? per.Where(kv => kv.Value > 0).Select(kv => kv.Key).Order(StringComparer.Ordinal).FirstOrDefault()
            : null, StringComparer.Ordinal);
    }

    /// <summary>Data with every <c>item.location_id</c> recomputed from the movements.</summary>
    public static BackupData WithItemLocations(this BackupData data)
    {
        var locations = ItemLocations(data.Items, data.Movements);
        return data with { Items = data.Items.Select(i => i with { LocationId = locations[i.Id] }).ToList() };
    }

    /// <summary>Same content, ignoring the server-only columns created_by and device_id.</summary>
    public static bool SameMovement(BackupStockMovement a, BackupStockMovement b) =>
        a with { CreatedBy = null, DeviceId = null } == b with { CreatedBy = null, DeviceId = null };

    /// <summary>Insertion-ordered map (the Kotlin LinkedHashMap): putting an existing key keeps its place.</summary>
    private sealed class OrderedMap<T>
    {
        private readonly Dictionary<string, int> _index = new(StringComparer.Ordinal);
        private readonly List<(string Id, T Row)> _rows = [];

        public int Count => _rows.Count;
        public IEnumerable<T> Values => _rows.Select(r => r.Row);
        public IEnumerable<string> Keys => _rows.Select(r => r.Id);
        public bool Contains(string id) => _index.ContainsKey(id);
        public T Get(string id) => _rows[_index[id]].Row;
        public bool TryGet(string id, out T row)
        {
            if (_index.TryGetValue(id, out var i)) { row = _rows[i].Row; return true; }
            row = default!;
            return false;
        }

        public void Set(string id, T row)
        {
            if (_index.TryGetValue(id, out var i)) { _rows[i] = (id, row); return; }
            _index[id] = _rows.Count;
            _rows.Add((id, row));
        }

        public void Remove(string id)
        {
            if (!_index.Remove(id, out var removed)) return;
            _rows.RemoveAt(removed);
            foreach (var key in _index.Keys.ToList())
                if (_index[key] > removed) _index[key]--;
        }
    }

    private sealed class Table<T>(IReadOnlyList<T> rows, Func<T, string> id) where T : class
    {
        public Func<T, string> Id { get; } = id;
        public Dictionary<string, T> Local { get; } = rows.ToDictionary(id, StringComparer.Ordinal);
        public OrderedMap<T> Final { get; } = Fill(rows, id);
        public OrderedMap<T> Inserted { get; } = new();
        public OrderedMap<T> Updated { get; } = new();

        private static OrderedMap<T> Fill(IReadOnlyList<T> rows, Func<T, string> id)
        {
            var map = new OrderedMap<T>();
            foreach (var r in rows) map.Set(id(r), r);
            return map;
        }

        public void Insert(T row)
        {
            Inserted.Set(Id(row), row);
            Final.Set(Id(row), row);
        }

        /// <summary>A row back to its local content is no update.</summary>
        public void Update(T row)
        {
            if (Local.TryGetValue(Id(row), out var local) && row.Equals(local)) Updated.Remove(Id(row));
            else Updated.Set(Id(row), row);
            Final.Set(Id(row), row);
        }
    }

    /// <summary>Owner id of each value of a unique column, in the merged state.</summary>
    private sealed class UniqueIndex<T>(IEnumerable<T> rows, Func<T, string> id, Func<T, string?> value, Func<string, string> normalize)
    {
        private readonly Dictionary<string, string> _owners = Build(rows, id, value, normalize);

        private static Dictionary<string, string> Build(IEnumerable<T> rows, Func<T, string> id, Func<T, string?> value, Func<string, string> normalize)
        {
            var owners = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var row in rows)
                if (value(row) is { } v) owners[normalize(v)] = id(row);
            return owners;
        }

        public string? TakenBy(string candidate, string selfId) =>
            _owners.TryGetValue(normalize(candidate), out var owner) && owner != selfId ? owner : null;

        public bool IsFree(string candidate, string selfId) => TakenBy(candidate, selfId) is null;

        /// <summary>Moves selfId from its previous value to the new one.</summary>
        public void Replace(T? previous, T next)
        {
            if (previous is not null && value(previous) is { } old && _owners.TryGetValue(normalize(old), out var o) && o == id(next))
                _owners.Remove(normalize(old));
            if (value(next) is { } v) _owners[normalize(v)] = id(next);
        }
    }

    private sealed class Merger(BackupData local, BackupData incoming)
    {
        private readonly List<MergeConflict> _conflicts = [];
        private readonly Dictionary<string, string> _tagOwners = ReleaseTags(local, incoming);

        private static string LocationKey(string id) => $"location:{id}";
        private static string ArticleKey(string id) => $"article:{id}";

        /// <summary>
        /// One tag = one thing, across articles and locations: owner key of each NFC tag in the merged state. Tags of local
        /// rows that a newer incoming row replaces are released up front, so a tag moved from an article to a location on
        /// the other device follows it.
        /// </summary>
        private static Dictionary<string, string> ReleaseTags(BackupData local, BackupData incoming)
        {
            var owners = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var row in local.Locations) if (row.Barcode is { } b) owners[b] = LocationKey(row.Id);
            foreach (var row in local.Articles) if (row.Barcode is { } b) owners[b] = ArticleKey(row.Id);
            var localLocations = local.Locations.ToDictionary(l => l.Id, StringComparer.Ordinal);
            foreach (var row in incoming.Locations)
            {
                if (!localLocations.TryGetValue(row.Id, out var previous)) continue;
                if (Newer(row, previous, x => x.UpdatedAt) && previous.Barcode is { } b && owners.GetValueOrDefault(b) == LocationKey(row.Id))
                    owners.Remove(b);
            }
            var localArticles = local.Articles.ToDictionary(a => a.Id, StringComparer.Ordinal);
            foreach (var row in incoming.Articles)
            {
                if (!localArticles.TryGetValue(row.Id, out var previous)) continue;
                if (Newer(row, previous, x => x.UpdatedAt) && previous.Barcode is { } b && owners.GetValueOrDefault(b) == ArticleKey(row.Id))
                    owners.Remove(b);
            }
            return owners;
        }

        /// <summary>Takes tag for key; false (nothing changed) when another row holds it.</summary>
        private bool ClaimTag(string tag, string key)
        {
            if (_tagOwners.TryGetValue(tag, out var owner) && owner != key) return false;
            _tagOwners[tag] = key;
            return true;
        }

        /// <summary>"value (2)", "value (3)"... the first free one, cut to maxLength.</summary>
        private static string Suffixed<T>(string value, string selfId, int maxLength, UniqueIndex<T> index)
        {
            for (var n = 2; ; n++)
            {
                var suffix = $" ({n})";
                var candidate = value[..Math.Min(value.Length, maxLength - suffix.Length)].TrimEnd() + suffix;
                if (index.IsFree(candidate, selfId)) return candidate;
            }
        }

        /// <summary>Ids on both sides first (they may free unique values), then new ids, each sorted by id.</summary>
        private static List<T> Ordered<T>(IEnumerable<T> rows, Table<T> table) where T : class
        {
            var sorted = rows.OrderBy(table.Id, StringComparer.Ordinal).ToList();
            return [.. sorted.Where(r => table.Local.ContainsKey(table.Id(r))), .. sorted.Where(r => !table.Local.ContainsKey(table.Id(r)))];
        }

        private static bool Newer<T>(T incomingRow, T? localRow, Func<T, long> updatedAt) where T : class =>
            localRow is null || updatedAt(incomingRow) > updatedAt(localRow);

        public MergePlan Plan()
        {
            var categories = MergeCategories();
            var (attributes, attributeIds) = MergeAttributes();
            var categoryAttributes = MergeCategoryAttributes(attributeIds);
            var locations = MergeLocations();
            var articles = MergeArticles();
            var items = MergeItems();
            var movements = MergeMovements();
            var reviews = MergeRows(local.Reviews, incoming.Reviews, x => x.Id, x => x.UpdatedAt);
            var reviewLines = MergeRows(local.ReviewLines, incoming.ReviewLines, x => x.Id, x => x.UpdatedAt);

            // Cached item location, from the merged movements
            var fixes = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var (id, location) in ItemLocations(items.Final.Values, movements.Final.Values))
            {
                var item = items.Final.Get(id);
                if (item.LocationId == location) continue;
                var fixedItem = item with { LocationId = location };
                if (items.Inserted.Contains(id)) items.Insert(fixedItem);
                else if (items.Updated.Contains(id)) items.Update(fixedItem);
                else
                {
                    fixes[id] = location;
                    items.Final.Set(id, fixedItem);
                }
            }

            var result = new BackupData
            {
                Categories = categories.Final.Values.ToList(),
                AttributeDefinitions = attributes.Final.Values.ToList(),
                CategoryAttributes = categoryAttributes.Final.Values.ToList(),
                Locations = locations.Final.Values.ToList(),
                Articles = articles.Final.Values.ToList(),
                Items = items.Final.Values.ToList(),
                Movements = movements.Final.Values.ToList(),
                Reviews = reviews.Final.Values.ToList(),
                ReviewLines = reviewLines.Final.Values.ToList(),
            };
            // Defensive: the plan must satisfy every database constraint before anything is written
            BackupValidatorHelper.Validate(result);

            var changedArticles = articles.Inserted.Values.Concat(articles.Updated.Values);
            return new MergePlan(
                new BackupData
                {
                    Categories = categories.Inserted.Values.ToList(),
                    AttributeDefinitions = attributes.Inserted.Values.ToList(),
                    CategoryAttributes = categoryAttributes.Inserted.Values.ToList(),
                    Locations = BackupLocationsHelper.ParentsFirst(locations.Inserted.Values.ToList()),
                    Articles = articles.Inserted.Values.ToList(),
                    Items = items.Inserted.Values.ToList(),
                    Movements = movements.Inserted.Values.ToList(),
                    Reviews = reviews.Inserted.Values.ToList(),
                    ReviewLines = reviewLines.Inserted.Values.ToList(),
                },
                new BackupData
                {
                    Categories = categories.Updated.Values.ToList(),
                    AttributeDefinitions = attributes.Updated.Values.ToList(),
                    CategoryAttributes = categoryAttributes.Updated.Values.ToList(),
                    Locations = locations.Updated.Values.ToList(),
                    Articles = articles.Updated.Values.ToList(),
                    Items = items.Updated.Values.ToList(),
                    Reviews = reviews.Updated.Values.ToList(),
                    ReviewLines = reviewLines.Updated.Values.ToList(),
                },
                fixes,
                _conflicts.ToList(),
                result,
                changedArticles.Where(a => a.ImagePath is not null).Select(a => a.ImagePath!).ToHashSet(StringComparer.Ordinal));
        }

        private Table<BackupCategory> MergeCategories()
        {
            var table = new Table<BackupCategory>(local.Categories, x => x.Id);
            var names = new UniqueIndex<BackupCategory>(table.Final.Values, x => x.Id, x => x.Name, s => s.ToLowerInvariant());
            foreach (var row in Ordered(incoming.Categories, table))
            {
                table.Final.TryGet(row.Id, out var previous);
                if (!Newer(row, previous, x => x.UpdatedAt)) continue;
                var candidate = row;
                if (!names.IsFree(row.Name, row.Id))
                {
                    candidate = row with { Name = Suffixed(row.Name, row.Id, MaxCategoryNameLength, names) };
                    _conflicts.Add(new(ConflictType.CategoryRenamed, BackupTable.Category, row.Id, row.Name, candidate.Name));
                }
                names.Replace(previous, candidate);
                if (previous is null) table.Insert(candidate); else table.Update(candidate);
            }
            return table;
        }

        /// <summary>Returns the table and the incoming attribute ids mapped to a local attribute with the same key.</summary>
        private (Table<BackupAttributeDefinition>, Dictionary<string, string>) MergeAttributes()
        {
            var table = new Table<BackupAttributeDefinition>(local.AttributeDefinitions, x => x.Id);
            var keys = new UniqueIndex<BackupAttributeDefinition>(table.Final.Values, x => x.Id, x => x.Key, s => s);
            var remap = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var row in Ordered(incoming.AttributeDefinitions, table))
            {
                if (table.Final.TryGet(row.Id, out var previous))
                {
                    // Same id: the key is immutable
                    if (row.Key != previous.Key)
                        _conflicts.Add(new(ConflictType.AttributeKeyKept, BackupTable.AttributeDefinition, row.Id, row.Key, previous.Key));
                    if (Newer(row, previous, x => x.UpdatedAt))
                        table.Update(KeepMeasure(row with { Key = previous.Key, CreatedAt = previous.CreatedAt }, previous));
                    else if (KeepMeasure(previous, row) != previous)
                        table.Update(KeepMeasure(previous, row));
                    continue;
                }
                var owner = keys.TakenBy(row.Key, row.Id);
                if (owner is null)
                {
                    keys.Replace(null, row);
                    table.Insert(row);
                    continue;
                }
                // Same key, other id: the same attribute created on both phones. Values in `attributes` JSON are keyed
                // by the key, so they stay valid. Links to it use the local id.
                remap[row.Id] = owner;
                _conflicts.Add(new(ConflictType.AttributeMerged, BackupTable.AttributeDefinition, row.Id, row.Key, row.Key));
                var existing = table.Final.Get(owner);
                var merged = row.UpdatedAt > existing.UpdatedAt
                    ? KeepMeasure(row with { Id = existing.Id, Key = existing.Key, CreatedAt = existing.CreatedAt }, existing)
                    : KeepMeasure(existing, row);
                if (merged != existing)
                {
                    if (table.Inserted.Contains(owner)) table.Insert(merged); else table.Update(merged);
                }
            }
            return (table, remap);
        }

        /// <summary>
        /// Winner of two versions of one attribute, still a measure when other is one and winner a number: number to measure
        /// is one way (measure values don't read as numbers), plain numbers read in the default unit.
        /// </summary>
        private static BackupAttributeDefinition KeepMeasure(BackupAttributeDefinition winner, BackupAttributeDefinition other) =>
            winner.DataType == "number" && other.DataType == "measure"
                ? winner with { DataType = "measure", Unit = other.Unit, UpdatedAt = Math.Max(winner.UpdatedAt, other.UpdatedAt) }
                : winner;

        private Table<BackupCategoryAttribute> MergeCategoryAttributes(Dictionary<string, string> attributeIds)
        {
            var table = new Table<BackupCategoryAttribute>(local.CategoryAttributes, x => x.Id);
            static string Pair(BackupCategoryAttribute r) => $"{r.CategoryId}/{r.AttributeId}";
            var pairs = new UniqueIndex<BackupCategoryAttribute>(table.Final.Values, x => x.Id, Pair, s => s);
            var rows = incoming.CategoryAttributes
                .Select(r => attributeIds.TryGetValue(r.AttributeId, out var mapped) ? r with { AttributeId = mapped } : r)
                .ToList();
            foreach (var row in Ordered(rows, table))
            {
                table.Final.TryGet(row.Id, out var previous);
                if (!Newer(row, previous, x => x.UpdatedAt)) continue;
                if (!pairs.IsFree(Pair(row), row.Id))
                {
                    _conflicts.Add(new(ConflictType.CategoryAttributeSkipped, BackupTable.CategoryAttribute, row.Id));
                    continue;
                }
                pairs.Replace(previous, row);
                if (previous is null) table.Insert(row); else table.Update(row);
            }
            return table;
        }

        private Table<BackupLocation> MergeLocations()
        {
            var table = new Table<BackupLocation>(local.Locations, x => x.Id);
            foreach (var row in Ordered(incoming.Locations, table))
            {
                table.Final.TryGet(row.Id, out var previous);
                if (!Newer(row, previous, x => x.UpdatedAt)) continue;
                var candidate = row;
                if (row.Barcode is { } barcode && !ClaimTag(barcode, LocationKey(row.Id)))
                {
                    candidate = candidate with { Barcode = null };
                    _conflicts.Add(new(ConflictType.BarcodeCleared, BackupTable.Location, row.Id, barcode, row.Name));
                }
                if (previous is null) table.Insert(candidate); else table.Update(candidate);
            }
            // A newer parent on each side can make a cycle: keep the local parent of one updated row until none is left
            while (BackupLocationsHelper.Cycle(table.Final.Values) is { } inCycle)
            {
                var cycle = new List<string>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (string? c = inCycle; c is not null && cycle.Count <= table.Final.Count && seen.Add(c);
                     c = table.Final.TryGet(c, out var l) ? l.ParentId : null)
                    cycle.Add(c);
                var revert = cycle.Where(id => table.Updated.Contains(id)).Order(StringComparer.Ordinal).FirstOrDefault()
                    ?? throw new BackupException(BackupError.Damaged, "Location cycle");
                var current = table.Final.Get(revert);
                var kept = current with { ParentId = table.Local[revert].ParentId };
                _conflicts.Add(new(ConflictType.LocationParentKept, BackupTable.Location, revert, current.ParentId, kept.ParentId));
                table.Update(kept);
            }
            return table;
        }

        private Table<BackupArticle> MergeArticles()
        {
            var table = new Table<BackupArticle>(local.Articles, x => x.Id);
            var skus = new UniqueIndex<BackupArticle>(table.Final.Values, x => x.Id, x => x.Sku, s => s.ToLowerInvariant());
            foreach (var row in Ordered(incoming.Articles, table))
            {
                table.Final.TryGet(row.Id, out var previous);
                if (!Newer(row, previous, x => x.UpdatedAt)) continue;
                var candidate = row;
                if (row.Sku is { } sku && !skus.IsFree(sku, row.Id))
                {
                    candidate = candidate with { Sku = Suffixed(sku, row.Id, MaxSkuLength, skus) };
                    _conflicts.Add(new(ConflictType.SkuRenamed, BackupTable.Article, row.Id, sku, candidate.Sku));
                }
                if (row.Barcode is { } barcode && !ClaimTag(barcode, ArticleKey(row.Id)))
                {
                    candidate = candidate with { Barcode = null };
                    _conflicts.Add(new(ConflictType.BarcodeCleared, BackupTable.Article, row.Id, barcode, row.Name));
                }
                skus.Replace(previous, candidate);
                if (previous is null) table.Insert(candidate); else table.Update(candidate);
            }
            return table;
        }

        private Table<BackupItem> MergeItems()
        {
            var table = new Table<BackupItem>(local.Items, x => x.Id);
            var serials = new UniqueIndex<BackupItem>(table.Final.Values, x => x.Id, x => x.SerialNumber, s => s.ToLowerInvariant());
            foreach (var row in Ordered(incoming.Items, table))
            {
                table.Final.TryGet(row.Id, out var previous);
                if (!Newer(row, previous, x => x.UpdatedAt)) continue;
                var candidate = row;
                if (row.SerialNumber is { } serial && !serials.IsFree(serial, row.Id))
                {
                    candidate = row with { SerialNumber = Suffixed(serial, row.Id, MaxSerialLength, serials) };
                    _conflicts.Add(new(ConflictType.SerialRenamed, BackupTable.Item, row.Id, serial, candidate.SerialNumber));
                }
                serials.Replace(previous, candidate);
                if (previous is null) table.Insert(candidate); else table.Update(candidate);
            }
            return table;
        }

        /// <summary>Rows without unique columns: added when new, replaced when the incoming one is newer.</summary>
        private static Table<T> MergeRows<T>(IReadOnlyList<T> localRows, IReadOnlyList<T> incomingRows, Func<T, string> id, Func<T, long> updatedAt)
            where T : class
        {
            var table = new Table<T>(localRows, id);
            foreach (var row in incomingRows.OrderBy(id, StringComparer.Ordinal))
            {
                table.Final.TryGet(id(row), out var previous);
                if (!Newer(row, previous, updatedAt)) continue;
                if (previous is null) table.Insert(row); else table.Update(row);
            }
            return table;
        }

        /// <summary>Append-only: never updated, never deleted. Server-only columns are ignored when comparing.</summary>
        private Table<BackupStockMovement> MergeMovements()
        {
            var table = new Table<BackupStockMovement>(local.Movements, x => x.Id);
            foreach (var row in incoming.Movements.OrderBy(m => m.Id, StringComparer.Ordinal))
            {
                if (!table.Local.TryGetValue(row.Id, out var existing)) table.Insert(row);
                else if (!SameMovement(existing, row))
                    _conflicts.Add(new(ConflictType.MovementKept, BackupTable.StockMovement, row.Id));
            }
            return table;
        }
    }
}
