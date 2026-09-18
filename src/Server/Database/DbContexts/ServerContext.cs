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
    /// Initializes a new instance of the <see cref="ServerContext"/> class with the specified options.
    /// </summary>
    public ServerContext(DbContextOptions<ServerContext> options) 
        : base(options)
    {
    }
}