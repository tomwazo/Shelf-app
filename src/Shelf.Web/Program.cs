using Microsoft.EntityFrameworkCore;
using Shelf.Web.Data;
using Shelf.Web.Identity;
using Shelf.Web.Search;
using Shelf.Web.Search.Providers;
using Shelf.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddDbContext<ShelfDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("ShelfDb")));
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<ShelfService>();
builder.Services.AddScoped<StatsService>();
builder.Services.AddMemoryCache();

builder.Services.AddHttpClient<TmdbSearchProvider>(client =>
{
    client.BaseAddress = new Uri("https://api.themoviedb.org/3/");
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddScoped<IMediaSearchProvider>(sp => sp.GetRequiredService<TmdbSearchProvider>());

builder.Services.AddHttpClient<OpenLibrarySearchProvider>(client =>
{
    client.BaseAddress = new Uri("https://openlibrary.org/");
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Shelf/1.0 (tom87moore@gmail.com)");
});
builder.Services.AddScoped<IMediaSearchProvider>(sp => sp.GetRequiredService<OpenLibrarySearchProvider>());

builder.Services.AddHttpClient(nameof(IgdbTokenProvider), client => client.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddSingleton<IgdbTokenProvider>();

builder.Services.AddHttpClient<IgdbSearchProvider>(client =>
{
    client.BaseAddress = new Uri("https://api.igdb.com/v4/");
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddScoped<IMediaSearchProvider>(sp => sp.GetRequiredService<IgdbSearchProvider>());

builder.Services.AddScoped<MediaSearchService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
