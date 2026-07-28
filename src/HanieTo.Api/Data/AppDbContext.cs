using HanieTo.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace HanieTo.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Content> Contents => Set<Content>();
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<PublishAttempt> PublishAttempts => Set<PublishAttempt>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<ChatPreference> ChatPreferences => Set<ChatPreference>();
    public DbSet<CartItem> CartItems => Set<CartItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Content>()
            .HasMany(c => c.PublishAttempts)
            .WithOne()
            .HasForeignKey(pa => pa.ContentId);

        modelBuilder.Entity<PublishAttempt>()
            .HasOne(pa => pa.Channel)
            .WithMany()
            .HasForeignKey(pa => pa.ChannelId);

        modelBuilder.Entity<Order>()
            .HasMany(o => o.Items)
            .WithOne()
            .HasForeignKey(i => i.OrderId);

        modelBuilder.Entity<Order>()
            .Ignore(o => o.Total);

        modelBuilder.Entity<ChatPreference>()
            .HasIndex(cp => cp.ChatId)
            .IsUnique();
    }
}
