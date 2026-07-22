using Microsoft.EntityFrameworkCore;
using Shelf.Web.Data.Entities;

namespace Shelf.Web.Data;

public class ShelfDbContext(DbContextOptions<ShelfDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<Genre> Genres => Set<Genre>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<Event> Events => Set<Event>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Enums stored as strings: readable in the DB and in Event history,
        // and stable if enum members are reordered.
        modelBuilder.Entity<User>(user =>
        {
            user.Property(u => u.Name).HasMaxLength(200);
            user.Property(u => u.ExternalIdentity).HasMaxLength(200);
            user.HasIndex(u => u.ExternalIdentity).IsUnique();
        });

        modelBuilder.Entity<Item>(item =>
        {
            item.Property(i => i.MediaType).HasConversion<string>().HasMaxLength(20);
            item.Property(i => i.ExternalSource).HasConversion<string>().HasMaxLength(20);
            item.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);
            item.Property(i => i.Title).HasMaxLength(500);
            item.Property(i => i.CoverArtUrl).HasMaxLength(2000);
            item.Property(i => i.ExternalId).HasMaxLength(100);

            // Backs the "already on Shelf" duplicate check; a removed item's
            // row still holds the key — re-adding un-removes it (see
            // TECHNICAL_DESIGN.md § Re-adding a removed item).
            item.HasIndex(i => new { i.ExternalSource, i.ExternalId }).IsUnique();

            item.HasMany(i => i.Genres)
                .WithMany(g => g.Items)
                .UsingEntity("ItemGenre");
        });

        modelBuilder.Entity<Genre>(genre =>
        {
            genre.Property(g => g.Name).HasMaxLength(100);
            genre.HasIndex(g => g.Name).IsUnique();
        });

        modelBuilder.Entity<Review>(review =>
        {
            review.HasIndex(r => r.ItemId).IsUnique();
            review.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Review_RatingOrText",
                    "[Rating] IS NOT NULL OR [Text] IS NOT NULL");
                t.HasCheckConstraint("CK_Review_RatingRange",
                    "[Rating] IS NULL OR [Rating] BETWEEN 1 AND 5");
            });
            review.HasOne(r => r.User).WithMany().OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Comment>(comment =>
        {
            comment.HasOne(c => c.User).WithMany().OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Event>(evt =>
        {
            evt.Property(e => e.Type).HasConversion<string>().HasMaxLength(40);
            evt.Property(e => e.FromStatus).HasConversion<string>().HasMaxLength(20);
            evt.Property(e => e.ToStatus).HasConversion<string>().HasMaxLength(20);

            // Global Timeline reads by time; item detail pages by item + time.
            evt.HasIndex(e => e.OccurredAt);
            evt.HasIndex(e => new { e.ItemId, e.OccurredAt });

            // Events are append-only history and must survive anything else
            // being deleted.
            evt.HasOne(e => e.Item).WithMany(i => i.Events).OnDelete(DeleteBehavior.Restrict);
            evt.HasOne(e => e.User).WithMany().OnDelete(DeleteBehavior.Restrict);
        });
    }
}
