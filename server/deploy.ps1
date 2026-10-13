# Puts the BlockCraft big server on Cloudflare (free plan, always online).
# Run it with "Deploy Big Server.bat" in the game folder.
#   1. signs you in to Cloudflare (a browser window opens - make a free account if you don't have one)
#   2. uploads the server (worker.js + world.js)
#   3. writes the server's address into index.html so the game joins it by default
$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8   # so Cloudflare's symbols show properly
Set-Location $PSScriptRoot

if (-not (Get-Command npx -ErrorAction SilentlyContinue)) {
  Write-Host "Node.js is not installed. Get it from https://nodejs.org (the LTS version) and run this again." -ForegroundColor Red
  exit 1
}

Write-Host "Checking your Cloudflare sign-in..."
$who = (npx --yes wrangler@4 whoami 2>&1 | Out-String)
if ($who -match 'not authenticated' -or $who -match 'not logged in') {
  Write-Host "A browser window will open. Sign in to Cloudflare (or make a free account), then click Allow."
  npx --yes wrangler@4 login
  if ($LASTEXITCODE -ne 0) { Write-Host "Sign-in did not finish." -ForegroundColor Red; exit 1 }
}

for ($try = 1; ; $try++) {
  Write-Host "Uploading the BlockCraft server..."
  $lines = @()
  npx --yes wrangler@4 deploy 2>&1 | ForEach-Object {
    $line = if ($_ -is [System.Management.Automation.ErrorRecord]) { $_.Exception.Message } else { "$_" }
    if ($line -and $line -ne 'System.Management.Automation.RemoteException') { Write-Host $line; $lines += $line }
  }
  if ($LASTEXITCODE -eq 0) { break }
  $all = $lines -join "`n"
  # a new Cloudflare account first needs its own workers.dev address (a free web address for the server)
  $onboard = [regex]::Match($all, 'https://dash\.cloudflare\.com/\S*workers/onboarding')
  if ($all -match 'register a workers\.dev subdomain' -and $try -le 3) {
    Write-Host ""
    Write-Host "Cloudflare needs you to pick a name for your free web address (your-name.workers.dev)." -ForegroundColor Yellow
    Write-Host "A browser page will open. Type a name you like, click Set up / Continue, and come back here."
    if ($onboard.Success) { Start-Process $onboard.Value } else { Start-Process 'https://dash.cloudflare.com/?to=/:account/workers/onboarding' }
    Read-Host "When you've picked the name, press Enter to try again"
    continue
  }
  Write-Host "Upload failed (see the messages above)." -ForegroundColor Red; exit 1
}

$m = [regex]::Match(($lines -join "`n"), 'https://[A-Za-z0-9.-]+\.workers\.dev')
if (-not $m.Success) { Write-Host "Uploaded, but I could not find the server address in the output above. Type it into the game's Multiplayer screen." -ForegroundColor Yellow; exit 0 }
$ws = 'wss://' + $m.Value.Substring(8) + '/ws'

$game = Join-Path $PSScriptRoot '..\index.html'
$text = [IO.File]::ReadAllText($game)
$text = [regex]::Replace($text, "const DEFAULT_SERVER='[^']*';", "const DEFAULT_SERVER='$ws';")
[IO.File]::WriteAllText($game, $text, (New-Object System.Text.UTF8Encoding($false)))

Write-Host ""
Write-Host "Done! Your big server is online at $ws" -ForegroundColor Green
Write-Host "The game now joins it from Multiplayer > Join Server."
Write-Host "Run ""Publish to GitHub.bat"" so everyone's launcher gets the update with the server address."
