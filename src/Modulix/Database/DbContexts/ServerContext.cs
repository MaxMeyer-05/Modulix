using Microsoft.EntityFrameworkCore;

using Modulix.Database.Entities;

namespace Modulix.Database.DbContexts;

/// <summary>
/// Represents the database context for modules and their endpoints.
/// </summary>
public class ServerContext : DbContext
{
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

        modelBuilder.Entity<Module>(entity =>
        {
            entity.Property(m => m.Status)
                .HasConversion<string>();

            entity.HasMany(m => m.SubEndpoints)
                .WithOne(e => e.Module)
                .HasForeignKey(e => e.ModuleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ModuleEndpoint>(entity =>
        {
            entity.Property(e => e.Status)
                .HasConversion<string>();
        });
    }
}