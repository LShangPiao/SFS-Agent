$ErrorActionPreference = "Continue"
$gameDir = "F:\SteamLibrary\steamapps\common\Spaceflight Simulator\Spaceflight Simulator Game"
$exe = Join-Path $gameDir "Spaceflight Simulator.exe"
$target = Join-Path $gameDir "Mods\SFS-Agent\SFS-Agent.dll"
$src = "C:\Users\Administrator\N.E.K.O\sfs-agent\dist\SFS-Agent.dll"

Write-Host "=== kill ==="
$p = Get-Process -Name "Spaceflight Simulator" -ErrorAction SilentlyContinue
if ($p) { Stop-Process -Id $p.Id -Force; Write-Host ("  killed " + $p.Id) }
for ($i = 0; $i -lt 20; $i++) { Start-Sleep -Milliseconds 500; if (-not (Get-Process -Name "Spaceflight Simulator" -ErrorAction SilentlyContinue)) { break } }
Write-Host "=== deploy ==="
Copy-Item $src $target -Force
Write-Host ("  " + (Get-Item $target).Length + " bytes  match=" + ((Get-FileHash $src).Hash -eq (Get-FileHash $target).Hash))
Write-Host "=== launch ==="
Start-Process -FilePath $exe -WorkingDirectory $gameDir
for ($i = 1; $i -le 40; $i++) {
  Start-Sleep -Seconds 3
  try { Invoke-WebRequest -Uri "http://127.0.0.1:21578/ping" -TimeoutSec 4 -UseBasicParsing | Out-Null; Write-Host ("  ready ~" + ($i*3) + "s"); break } catch { }
}
Start-Sleep -Seconds 10
Write-Host "=== done ==="
