using Shelf.Web.Data;
using Shelf.Web.Data.Entities;

namespace Shelf.Web.Tests.TestInfrastructure;

public static class TestUsers
{
    public static async Task<User> SeedAsync(ShelfDbContext db, string externalIdentity = "test-user", string name = "Test User")
    {
        var user = new User { ExternalIdentity = externalIdentity, Name = name };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }
}
