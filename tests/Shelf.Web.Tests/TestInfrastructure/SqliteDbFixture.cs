using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shelf.Web.Data;

namespace Shelf.Web.Tests.TestInfrastructure;

/// <summary>
/// A fresh SQLite in-memory database per instance. Construct one per test
/// (xUnit creates a new test class instance per test method by default) and
/// dispose it at teardown - disposing the connection destroys the database.
/// </summary>
public sealed class SqliteDbFixture : IDisposable
{
    private readonly SqliteConnection _connection;

    public ShelfDbContext Db { get; }

    public SqliteDbFixture()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ShelfDbContext>()
            .UseSqlite(_connection)
            .Options;

        Db = new TestShelfDbContext(options);
        Db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
    }
}
