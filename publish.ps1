# Publishes BlockCraft to your GitHub repository so every launcher can update itself.
# Run it with "Publish to GitHub.bat". It:
#   1. remembers which repository to use (update-source.txt)
#   2. copies index.html into versions\<version>\ and writes versions.json with checksums
#   3. commits everything except your saves and pushes it to GitHub (Git asks you to sign in the first time)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
  Write-Host "Git is not installed. Get it from https://git-scm.com/download/win and run this again." -ForegroundColor Red
  exit 1
}

$repo = $null
if (Test-Path 'update-source.txt') { $repo = (Get-Content 'update-source.txt' -Raw).Trim() }
if (-not $repo) {
  Write-Host "Make an empty repository on github.com first (no README), then paste its address here."
  $url = Read-Host "Repository address (like https://github.com/yourname/blockcraft)"
  $m = [regex]::Match($url, 'github\.com[/:]([A-Za-z0-9_.-]+)/([A-Za-z0-9_.-]+?)(\.git)?/?$')
  if (-not $m.Success) { Write-Host "That does not look like a GitHub repository address." -ForegroundColor Red; exit 1 }
  $repo = $m.Groups[1].Value + '/' + $m.Groups[2].Value
  [IO.File]::WriteAllText((Join-Path $PSScriptRoot 'update-source.txt'), $repo)
}
Write-Host "Publishing to https://github.com/$repo"

# checksums + copy of the current game into versions\
$p = Start-Process -FilePath (Join-Path $PSScriptRoot 'BlockCraft.exe') -ArgumentList '--make-manifest' -Wait -PassThru
if ($p.ExitCode -ne 0) { Write-Host "Could not build versions.json (see manifest-error.txt)." -ForegroundColor Red; exit 1 }
$ver = ([regex]::Match((Get-Content 'index.html' -Raw), "const GAME_VERSION='([0-9.]+)'")).Groups[1].Value

$ignore = "saves/`r`n*.tmp`r`nmanifest-error.txt`r`nBlockCraft-new.exe`r`n.claude/`r`n"
[IO.File]::WriteAllText((Join-Path $PSScriptRoot '.gitignore'), $ignore)

if (-not (Test-Path '.git')) {
  git init -q
  git checkout -q -b main
  git remote add origin "https://github.com/$repo.git"
}
if (-not (git config user.name)) { git config user.name ($repo.Split('/')[0]) }
if (-not (git config user.email)) { git config user.email (($repo.Split('/')[0]) + '@users.noreply.github.com') }

git add -A
git commit -q -m "BlockCraft $ver"
if ($LASTEXITCODE -ne 0) { Write-Host "Nothing new to publish." }
git push -u origin main
if ($LASTEXITCODE -ne 0) { Write-Host "Push failed. Check the repository address and that you signed in to GitHub." -ForegroundColor Red; exit 1 }
Write-Host ""
Write-Host "Done! BlockCraft $ver is on GitHub. Launchers will download it the next time they start." -ForegroundColor Green
