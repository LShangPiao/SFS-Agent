$ErrorActionPreference = "Continue"
function Get-Ui { try { (Invoke-WebRequest -Uri "http://127.0.0.1:21578/ui" -TimeoutSec 15 -UseBasicParsing).Content | ConvertFrom-Json } catch { $null } }
function Click-Lab($label, $wait = 3000) {
  $ui = Get-Ui
  if (-not $ui) { Write-Host ("  [no ui] " + $label); return $false }
  $el = $ui.elements | Where-Object { $_.label -eq $label } | Select-Object -First 1
  if (-not $el) { Write-Host ("  [miss] " + $label); return $false }
  $r = (Invoke-WebRequest -Uri "http://127.0.0.1:21578/ui_click" -Method POST -Body ('{"index":' + $el.index + '}') -ContentType "application/json" -TimeoutSec 25 -UseBasicParsing).Content
  Write-Host ("  [" + $label + "] -> " + $r)
  Start-Sleep -Milliseconds $wait
  return $true
}
function Click-Idx($i, $wait = 2500) {
  $r = (Invoke-WebRequest -Uri "http://127.0.0.1:21578/ui_click" -Method POST -Body ('{"index":' + $i + '}') -ContentType "application/json" -TimeoutSec 25 -UseBasicParsing).Content
  Write-Host ("  [#" + $i + "] -> " + $r)
  Start-Sleep -Milliseconds $wait
}
function PlaceStack($name, $stack, $x = 0, $y = 0) {
  $body = (@{name=$name; x=$x; y=$y; stack=$stack} | ConvertTo-Json -Compress)
  $r = (Invoke-WebRequest -Uri "http://127.0.0.1:21578/build_place" -Method POST -Body $body -ContentType "application/json" -TimeoutSec 40 -UseBasicParsing).Content
  Write-Host ("  " + $name.PadRight(16) + " [" + $stack + "] -> " + $r)
  Start-Sleep -Milliseconds 700
}
function Bd { (Invoke-WebRequest -Uri "http://127.0.0.1:21578/build" -TimeoutSec 10 -UseBasicParsing).Content | ConvertFrom-Json }

Write-Host "=== 进世界 ==="
Click-Lab "Play" 3000 | Out-Null
$ui = Get-Ui
$cand = $ui.elements | Where-Object { $_.label -and $_.y -ge 0.2 -and $_.y -le 0.85 } | Select-Object -First 1
Click-Idx $cand.index 2500
Click-Lab "Play" 8000 | Out-Null

Write-Host "=== 建造 ==="
Click-Lab "Build New Rocket" 6000 | Out-Null
$ui = Get-Ui
$new = $ui.elements | Where-Object { $_.label -eq "Build New Rocket" } | Select-Object -Last 1
if ($new) { Click-Idx $new.index 6000 }
Click-Lab "New" 2500 | Out-Null
Click-Lab "Clear" 3000 | Out-Null
Write-Host ("  清空后 parts=" + (Bd).part_count)

Write-Host "=== 按高度自动堆叠 ==="
(Invoke-WebRequest -Uri "http://127.0.0.1:21578/build_catalog" -TimeoutSec 60 -UseBasicParsing).Content | Out-Null
PlaceStack "Fuel Tank" "none" 0 40
PlaceStack "Cone" "top"
PlaceStack "Probe" "top"
PlaceStack "Fuel Tank" "bottom"
PlaceStack "Engine Valiant" "bottom"
Start-Sleep -Seconds 1
$b = Bd
Write-Host ("  parts=" + $b.part_count + " mass=" + $b.total_mass)
Write-Host ("  samples: " + (($b.part_samples) -join "  "))
