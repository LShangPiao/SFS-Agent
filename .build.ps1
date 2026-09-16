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
function Place($name, $x, $y) {
  $body = (@{name=$name; x=$x; y=$y} | ConvertTo-Json -Compress)
  $r = (Invoke-WebRequest -Uri "http://127.0.0.1:21578/build_place" -Method POST -Body $body -ContentType "application/json" -TimeoutSec 40 -UseBasicParsing).Content
  Write-Host ("  " + $name.PadRight(16) + " @(" + $x + "," + $y + ") -> " + $r)
  Start-Sleep -Milliseconds 700
}

Write-Host "=== 进世界 ==="
Click-Lab "Play" 3000 | Out-Null
$ui = Get-Ui
$cand = $ui.elements | Where-Object { $_.label -and $_.y -ge 0.2 -and $_.y -le 0.85 } | Select-Object -First 1
Write-Host ("  选 [" + $cand.label + "]")
Click-Idx $cand.index 2500
Click-Lab "Play" 8000 | Out-Null

Write-Host "=== 世界中心 -> Build New Rocket ==="
Click-Lab "Build New Rocket" 6000 | Out-Null
$ui = Get-Ui
Write-Host ("  对话框 (" + $ui.count + "): " + (($ui.elements | Select-Object -First 8 | ForEach-Object { "[" + $_.label + "]" }) -join " "))
# 对话框里选 "Build New Rocket"（全新设计）
$new = $ui.elements | Where-Object { $_.label -eq "Build New Rocket" } | Select-Object -Last 1
if ($new) { Click-Idx $new.index 6000 }
$ui = Get-Ui
Write-Host ("  建造界面 (" + $ui.count + "): " + (($ui.elements | Select-Object -First 10 | ForEach-Object { "[" + $_.label + "]" }) -join " "))
Write-Host ""
Write-Host "=== 搭火箭（官方间距：Fuel Tank 高 4.0）==="
(Invoke-WebRequest -Uri "http://127.0.0.1:21578/build_catalog" -TimeoutSec 60 -UseBasicParsing).Content | Out-Null
Place "Fuel Tank" 0 4
Place "Engine Valiant" 0 4
Place "Cone" 0 8
Start-Sleep -Seconds 1
Write-Host ""
Write-Host "=== /build ==="
(Invoke-WebRequest -Uri "http://127.0.0.1:21578/build" -TimeoutSec 10 -UseBasicParsing).Content
