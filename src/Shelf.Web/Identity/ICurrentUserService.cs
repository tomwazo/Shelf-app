using Shelf.Web.Data.Entities;

namespace Shelf.Web.Identity;

public interface ICurrentUserService
{
    Task<User> GetCurrentUserAsync(CancellationToken ct = default);
}
