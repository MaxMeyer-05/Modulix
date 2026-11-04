using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

using Modulix.Database.DbContexts;
using Modulix.Database.Entities;

namespace Modulix.Tests.Database;

[Trait("Category", "Database")]
[Trait("SubCategory", "DockerfileTemplate")]
public class DockerfileTemplateTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServerContext _context;

    public DockerfileTemplateTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _context = new ServerContext(new DbContextOptionsBuilder<ServerContext>().UseSqlite(_connection).Options);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Model_MapsTemplateMetadataAndUniqueStoragePath()
    {
        var entity = _context.Model.FindEntityType(typeof(DockerfileTemplate))!;

        Assert.Equal("dockerfile_templates", entity.GetTableName());
        Assert.Equal(nameof(DockerfileTemplate.Id), Assert.Single(entity.FindPrimaryKey()!.Properties).Name);
        Assert.Equal(typeof(int), entity.FindProperty(nameof(DockerfileTemplate.Id))!.ClrType);
        Assert.Equal(ValueGenerated.OnAdd, entity.FindProperty(nameof(DockerfileTemplate.Id))!.ValueGenerated);

        var fileName = entity.FindProperty(nameof(DockerfileTemplate.FileName))!;
        Assert.False(fileName.IsNullable);
        Assert.Equal(100, fileName.GetMaxLength());

        var description = entity.FindProperty(nameof(DockerfileTemplate.Description))!;
        Assert.True(description.IsNullable);
        Assert.Equal(500, description.GetMaxLength());

        var storagePath = entity.FindProperty(nameof(DockerfileTemplate.StoragePath))!;
        Assert.False(storagePath.IsNullable);
        Assert.Equal(500, storagePath.GetMaxLength());
        var index = Assert.Single(entity.GetIndexes());
        Assert.True(index.IsUnique);
        Assert.Equal(nameof(DockerfileTemplate.StoragePath), Assert.Single(index.Properties).Name);

        Assert.Equal(
            new[] { "CreatedAtUtc", "Description", "FileName", "Id", "StoragePath", "UpdatedAtUtc" },
            entity.GetProperties().Select(property => property.Name).OrderBy(name => name).ToArray());
        Assert.Empty(entity.GetForeignKeys());
    }

    [Fact]
    public async Task SaveChanges_PersistsTemplateMetadataWithoutModuleOrFile()
    {
        var createdAt = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var updatedAt = createdAt.AddDays(1);
        var template = new DockerfileTemplate
        {
            FileName = ".NET with SQLite Directory",
            Description = "Prepares /app/data for SQLite.",
            StoragePath = "dockerfiles/sqlite/Dockerfile",
            CreatedAtUtc = createdAt,
            UpdatedAtUtc = updatedAt
        };

        _context.DockerfileTemplates.Add(template);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var stored = await _context.DockerfileTemplates.SingleAsync();
        Assert.True(stored.Id > 0);
        Assert.Equal(template.Id, stored.Id);
        Assert.Equal(template.FileName, stored.FileName);
        Assert.Equal(template.Description, stored.Description);
        Assert.Equal(template.StoragePath, stored.StoragePath);
        Assert.Equal(createdAt, stored.CreatedAtUtc);
        Assert.Equal(updatedAt, stored.UpdatedAtUtc);
    }

    [Fact]
    public async Task SaveChanges_DuplicateStoragePath_ThrowsUniqueConstraintViolation()
    {
        _context.DockerfileTemplates.Add(new DockerfileTemplate
        {
            FileName = "First template",
            StoragePath = "dockerfiles/shared/Dockerfile"
        });
        await _context.SaveChangesAsync();

        _context.DockerfileTemplates.Add(new DockerfileTemplate
        {
            FileName = "Second template",
            StoragePath = "dockerfiles/shared/Dockerfile"
        });

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => _context.SaveChangesAsync());
        var sqliteException = Assert.IsType<SqliteException>(exception.InnerException);
        Assert.Equal(19, sqliteException.SqliteErrorCode);
        Assert.Equal(2067, sqliteException.SqliteExtendedErrorCode);
    }
}
