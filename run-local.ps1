<#
.SYNOPSIS
    Gets Shelf running locally: restores tools, applies migrations to
    LocalDB, then starts the dev server with hot reload.

.NOTES
    TMDB/IGDB search needs API secrets first (one-time, run yourself so
    they never end up in a shared script or chat history):

        cd src\Shelf.Web
        dotnet user-secrets set "Tmdb:ApiKey" "<your-tmdb-key>"
        dotnet user-secrets set "Igdb:ClientId" "<your-igdb-client-id>"
        dotnet user-secrets set "Igdb:ClientSecret" "<your-igdb-client-secret>"

    Without them, OpenLibrary search still works; TMDB/IGDB just show
    "Search is unavailable right now."
#>

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

Write-Host "Restoring .NET tools..." -ForegroundColor Cyan
dotnet tool restore

Write-Host "Applying database migrations to LocalDB..." -ForegroundColor Cyan
dotnet ef database update --project src\Shelf.Web

Write-Host "Starting Shelf at http://localhost:5186 (Ctrl+C to stop)..." -ForegroundColor Cyan
dotnet watch --project src\Shelf.Web
