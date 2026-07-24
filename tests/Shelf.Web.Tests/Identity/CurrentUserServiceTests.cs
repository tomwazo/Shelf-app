using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Shelf.Web.Data.Entities;
using Shelf.Web.Identity;
using Shelf.Web.Tests.TestInfrastructure;

namespace Shelf.Web.Tests.Identity;

public class CurrentUserServiceTests
{
    private const string HeaderName = "X-MS-CLIENT-PRINCIPAL";

    [Fact]
    public async Task GetCurrentUser_FirstRequest_AutoCreatesUserRow()
    {
        using var fixture = new SqliteDbFixture();
        var service = CreateService(fixture, principalHeader: null, isDevelopment: true);

        var user = await service.GetCurrentUserAsync();

        Assert.Equal("local-dev", user.ExternalIdentity);
        Assert.Equal("Tom", user.Name);
        Assert.Single(fixture.Db.Users);
    }

    [Fact]
    public async Task GetCurrentUser_ExistingExternalIdentity_ReturnsSameRow()
    {
        using var fixture = new SqliteDbFixture();
        var existing = new Data.Entities.User { ExternalIdentity = "local-dev", Name = "Tom" };
        fixture.Db.Users.Add(existing);
        await fixture.Db.SaveChangesAsync();

        var service = CreateService(fixture, principalHeader: null, isDevelopment: true);

        var user = await service.GetCurrentUserAsync();

        Assert.Equal(existing.Id, user.Id);
        Assert.Single(fixture.Db.Users);
    }

    [Fact]
    public async Task GetCurrentUser_EasyAuthHeader_UsesOidClaimForExternalIdentity()
    {
        using var fixture = new SqliteDbFixture();
        var header = BuildPrincipalHeader(
            ("http://schemas.microsoft.com/identity/claims/objectidentifier", "oid-123"),
            ("name", "Alice"));
        var service = CreateService(fixture, principalHeader: header, isDevelopment: false);

        var user = await service.GetCurrentUserAsync();

        Assert.Equal("oid-123", user.ExternalIdentity);
        Assert.Equal("Alice", user.Name);
    }

    [Fact]
    public async Task GetCurrentUser_NoHeader_InDevelopment_UsesLocalDevIdentity()
    {
        using var fixture = new SqliteDbFixture();
        var service = CreateService(fixture, principalHeader: null, isDevelopment: true);

        var user = await service.GetCurrentUserAsync();

        Assert.Equal("local-dev", user.ExternalIdentity);
        Assert.Equal("Tom", user.Name);
    }

    [Fact]
    public async Task GetCurrentUser_NoHeader_InProduction_Throws()
    {
        using var fixture = new SqliteDbFixture();
        var service = CreateService(fixture, principalHeader: null, isDevelopment: false);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetCurrentUserAsync());
    }

    private static CurrentUserService CreateService(SqliteDbFixture fixture, string? principalHeader, bool isDevelopment)
    {
        var httpContext = new DefaultHttpContext();
        if (principalHeader is not null)
        {
            httpContext.Request.Headers[HeaderName] = principalHeader;
        }

        var accessor = new FakeHttpContextAccessor(httpContext);
        var env = new FakeHostEnvironment(isDevelopment ? Environments.Development : Environments.Production);

        return new CurrentUserService(accessor, fixture.Db, env);
    }

    private static string BuildPrincipalHeader(params (string Typ, string Val)[] claims)
    {
        var claimsJson = string.Join(",", claims.Select(c => $$"""{"typ":"{{c.Typ}}","val":"{{c.Val}}"}"""));
        var json = $$"""{"auth_typ":"aad","claims":[{{claimsJson}}]}""";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    private sealed class FakeHttpContextAccessor(HttpContext httpContext) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = httpContext;
    }

    private sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Shelf.Web.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
