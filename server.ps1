# BlockCraft local server
# Serves the game to your browser and stores every world as a file in the "saves" folder
# next to this script. Started by "Play BlockCraft.bat". Only reachable from this computer.
param([int]$Port = 8777, [switch]$NoBrowser)

$root  = Split-Path -Parent $MyInvocation.MyCommand.Path
$saves = Join-Path $root 'saves'
$trash = Join-Path $saves 'deleted'
New-Item -ItemType Directory -Force -Path $saves | Out-Null
$utf8 = New-Object System.Text.UTF8Encoding($false)

# find a free port
$listener = $null
for ($i = 0; $i -lt 20; $i++) {
    $l = New-Object System.Net.HttpListener
    $l.Prefixes.Add("http://localhost:$Port/")
    try { $l.Start(); $listener = $l; break } catch { $l.Close(); $Port++ }
}
if (-not $listener) { Write-Host 'Could not start the BlockCraft server.'; exit 1 }

$url = "http://localhost:$Port/"
Write-Host ''
Write-Host '  ____  _            _     ____            __ _   '
Write-Host ' | __ )| | ___   ___| | __/ ___|_ __ __ _ / _| |_ '
Write-Host ' |  _ \| |/ _ \ / __| |/ / |   | ''__/ _` | |_| __|'
Write-Host ' | |_) | | (_) | (__|   <| |___| | | (_| |  _| |_ '
Write-Host ' |____/|_|\___/ \___|_|\_\\____|_|  \__,_|_|  \__|'
Write-Host ''
Write-Host "  Game running at: $url"
Write-Host "  Worlds saved in: $saves"
Write-Host ''
Write-Host '  Keep this window open while you play. Close it when you are done.'
if (-not $NoBrowser) { Start-Process $url }

$types = @{ '.html' = 'text/html; charset=utf-8'; '.js' = 'application/javascript'; '.css' = 'text/css';
            '.json' = 'application/json'; '.png' = 'image/png'; '.ico' = 'image/x-icon'; '.txt' = 'text/plain; charset=utf-8' }

function Send-Bytes($ctx, [int]$code, [string]$type, [byte[]]$bytes) {
    $r = $ctx.Response
    $r.StatusCode = $code
    $r.ContentType = $type
    $r.Headers.Add('Cache-Control', 'no-store')
    $r.ContentLength64 = $bytes.Length
    $r.OutputStream.Write($bytes, 0, $bytes.Length)
    $r.OutputStream.Close()
}
function Send-Text($ctx, [int]$code, [string]$type, [string]$text) { Send-Bytes $ctx $code $type $utf8.GetBytes($text) }
function Get-Slot([string]$s) { if ($s -match '^[A-Za-z0-9_-]{1,64}$') { return $s } else { return $null } }

while ($listener.IsListening) {
    $ctx = $listener.GetContext()
    try {
        $req  = $ctx.Request
        $path = [Uri]::UnescapeDataString($req.Url.AbsolutePath)

        if ($path -eq '/api/ping') {
            Send-Text $ctx 200 'application/json' '{"blockcraft":true}'
        }
        elseif ($path -eq '/api/saves') {
            # list worlds: every <id>.info.json is a small description of one world
            $items = @(Get-ChildItem -Path $saves -Filter '*.info.json' -File | ForEach-Object { [IO.File]::ReadAllText($_.FullName, $utf8) })
            Send-Text $ctx 200 'application/json' ('[' + ($items -join ',') + ']')
        }
        elseif ($path -eq '/api/save') {
            $slot = Get-Slot $req.QueryString['slot']
            if (-not $slot) { Send-Text $ctx 400 'text/plain' 'bad save slot name' }
            else {
                $data = Join-Path $saves "$slot.json"
                $info = Join-Path $saves "$slot.info.json"
                switch ($req.HttpMethod) {
                    'GET' {
                        if (Test-Path $data) { Send-Bytes $ctx 200 'application/json' ([IO.File]::ReadAllBytes($data)) }
                        else { Send-Text $ctx 404 'text/plain' 'not found' }
                    }
                    'POST' {
                        $reader = New-Object IO.StreamReader($req.InputStream, $utf8)
                        $body = $reader.ReadToEnd(); $reader.Close()
                        $target = if ($req.QueryString['kind'] -eq 'info') { $info } else { $data }
                        # write to a temp file first so a crash can never leave a half-written save
                        $tmp = "$target.tmp"
                        [IO.File]::WriteAllText($tmp, $body, $utf8)
                        Move-Item -Force -LiteralPath $tmp -Destination $target
                        Send-Text $ctx 200 'application/json' '{"ok":true}'
                    }
                    'DELETE' {
                        # deleted worlds are moved to saves\deleted, not destroyed
                        New-Item -ItemType Directory -Force -Path $trash | Out-Null
                        foreach ($f in @($data, $info)) {
                            if (Test-Path $f) { Move-Item -Force -LiteralPath $f -Destination (Join-Path $trash ([IO.Path]::GetFileName($f))) }
                        }
                        Send-Text $ctx 200 'application/json' '{"ok":true}'
                    }
                    default { Send-Text $ctx 405 'text/plain' 'method not allowed' }
                }
            }
        }
        else {
            if ($path -eq '/') { $path = '/index.html' }
            $file = [IO.Path]::GetFullPath((Join-Path $root $path.TrimStart('/')))
            if ($file.StartsWith($root, [StringComparison]::OrdinalIgnoreCase) -and -not $file.StartsWith($saves, [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $file -PathType Leaf)) {
                $type = $types[[IO.Path]::GetExtension($file).ToLower()]
                if (-not $type) { $type = 'application/octet-stream' }
                Send-Bytes $ctx 200 $type ([IO.File]::ReadAllBytes($file))
            }
            else { Send-Text $ctx 404 'text/plain' 'Not found' }
        }
    }
    catch {
        try { Send-Text $ctx 500 'text/plain' $_.Exception.Message } catch {}
    }
}
