using Shelf.Web.Data.Entities;
using Shelf.Web.Identity;

namespace Shelf.Web.Tests.TestInfrastructure;

public class StubCurrentUserService(User user) : ICurrentUserService
{
    public Task<User> GetCurrentUserAsync(CancellationToken ct = default) => Task.FromResult(user);
}
