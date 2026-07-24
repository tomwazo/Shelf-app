using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Shelf.Web.Services;

namespace Shelf.Web.Pages;

public class StatsModel(StatsService statsService) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string Period { get; set; } = "week";

    public StatsResult Stats { get; private set; } = new(0, [], []);

    public async Task OnGetAsync(CancellationToken ct)
    {
        var since = Period switch
        {
            "month" => DateTimeOffset.UtcNow.AddDays(-30),
            "year" => DateTimeOffset.UtcNow.AddDays(-365),
            "all" => (DateTimeOffset?)null,
            _ => DateTimeOffset.UtcNow.AddDays(-7), // "week" default
        };

        Stats = await statsService.GetStatsAsync(since, ct);
    }
}
