using Microsoft.EntityFrameworkCore;

namespace EasyStock.Server.Data;

/// <summary>Stub context. Entities and migrations arrive in later phases.</summary>
public sealed class EasyStockDbContext(DbContextOptions<EasyStockDbContext> options) : DbContext(options);
