using HanieTo.Api.Data.ShopCatalog;
using Microsoft.EntityFrameworkCore;

namespace HanieTo.Api.Data;

// Read-only connection to the bot's Postgres database - the single source of
// truth for the product catalog. The bot (AiogramShopBot) owns this schema
// via its own Alembic migrations; this context never migrates or writes to
// it, only queries.
public class ShopCatalogDbContext(DbContextOptions<ShopCatalogDbContext> options) : DbContext(options)
{
    public DbSet<CatalogItem> Items => Set<CatalogItem>();
    public DbSet<CatalogCategory> Categories => Set<CatalogCategory>();
    public DbSet<CatalogSubcategory> Subcategories => Set<CatalogSubcategory>();
    public DbSet<CatalogBuy> Buys => Set<CatalogBuy>();
    public DbSet<CatalogUser> Users => Set<CatalogUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CatalogItem>().ToTable("items");
        modelBuilder.Entity<CatalogCategory>().ToTable("categories");
        modelBuilder.Entity<CatalogSubcategory>().ToTable("subcategories");
        modelBuilder.Entity<CatalogBuy>().ToTable("buys");
        modelBuilder.Entity<CatalogUser>().ToTable("users");
    }
}
