using Microsoft.EntityFrameworkCore;

using Server.Database.Entities;

namespace Server.Database.DbContexts;

/// <summary>
/// Represents the database context for the server, including users and refresh tokens.
/// </summary>
public class ServerContext : DbContext
{
    /// <summary>
    /// Represents the collection of users in the database.
    /// </summary>
    public DbSet<User> Users { get; set; } = null!;

    /// <summary>
    /// Represents the collection of refresh tokens in the database.
    /// </summary>
    public DbSet<RefreshToken> RefreshTokens { get; set; } = null!;

    /// <summary>
    /// Represents the collection of modules in the database.
    /// </summary>
    public DbSet<Module> Modules { get; set; } = null!;

    /// <summary>
    /// Represents the collection of module endpoints in the database.
    /// </summary>
    public DbSet<ModuleEndpoint> ModuleEndpoints { get; set; } = null!;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServerContext"/> class with the specified options.
    /// </summary>
    public ServerContext(DbContextOptions<ServerContext> options) 
        : base(options)
    {
    }

    /// <summary>
    /// Configures the model for the database context, including relationships and cascading deletes.
    /// </summary>
    /// <param name="modelBuilder">The model builder used to configure the entities.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity => 
        {
            entity.Property(u => u.Role)
                .HasConversion<string>();

            entity.HasMany(u => u.RefreshTokens)
                .WithOne(rt => rt.User)
                .HasForeignKey(rt => rt.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Module>(entity =>
        {
            entity.Property(m => m.Status)
                .HasConversion<string>();

            entity.HasMany(m => m.SubEndpoints)
                .WithOne(e => e.Module)
                .HasForeignKey(e => e.ModuleId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}