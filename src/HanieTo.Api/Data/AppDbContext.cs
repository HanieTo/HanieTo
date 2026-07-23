using HanieTo.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace HanieTo.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Content> Contents => Set<Content>();
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<PublishAttempt> PublishAttempts => Set<PublishAttempt>();

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
    }
}
