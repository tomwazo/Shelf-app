using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Shelf.Web.Data;

namespace Shelf.Web.Tests.TestInfrastructure;

/// <summary>
/// SQLite can't compare/order raw <see cref="DateTimeOffset"/> values in SQL.
/// The app only ever writes UTC (offset always zero), so converting every
/// DateTimeOffset column to its sortable binary representation preserves
/// ordering safely for tests without diverging from production behavior.
/// </summary>
public class TestShelfDbContext(DbContextOptions<ShelfDbContext> options) : ShelfDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var converter = new DateTimeOffsetToBinaryConverter();
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTimeOffset) || property.ClrType == typeof(DateTimeOffset?))
                {
                    property.SetValueConverter(converter);
                }
            }
        }
    }
}
