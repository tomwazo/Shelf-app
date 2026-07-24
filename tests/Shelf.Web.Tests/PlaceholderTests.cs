using Shelf.Web.Data;

namespace Shelf.Web.Tests;

public class PlaceholderTests
{
    [Fact]
    public void ProjectReference_ResolvesShelfWebTypes()
    {
        Assert.Equal("ShelfDbContext", typeof(ShelfDbContext).Name);
    }
}
