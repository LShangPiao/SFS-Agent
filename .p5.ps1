$ErrorActionPreference = "Continue"
$managed = "F:\SteamLibrary\steamapps\common\Spaceflight Simulator\Spaceflight Simulator Game\Spaceflight Simulator_Data\Managed"
$asm = [System.Reflection.Assembly]::LoadFrom((Join-Path $managed "Assembly-CSharp.dll"))
$allD = [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::NonPublic -bor [System.Reflection.BindingFlags]::Instance -bor [System.Reflection.BindingFlags]::Static -bor [System.Reflection.BindingFlags]::DeclaredOnly

function Dump($name) {
  $t = $asm.GetType($name)
  if (-not $t) { Write-Output ("## " + $name + " NOT FOUND"); Write-Output ""; return }
  Write-Output ("## " + $t.FullName + " (base " + $t.BaseType.Name + ")")
  foreach ($f in $t.GetFields($allD)) { "  " + $(if ($f.IsStatic) { "static " } else { "" }) + "field " + $f.FieldType.Name + " " + $f.Name }
  foreach ($p in $t.GetProperties($allD)) { "  prop " + $p.PropertyType.Name + " " + $p.Name + $(if ($p.CanWrite) { " [set]" } else { "" }) }
  foreach ($m in $t.GetMethods($allD)) { if (-not $m.IsSpecialName) { "  " + $(if ($m.IsStatic) { "static " } else { "" }) + $m.ReturnType.Name + " " + $m.Name + "(" + (($m.GetParameters() | ForEach-Object { $_.ParameterType.Name + " " + $_.Name }) -join ", ") + ")" } }
  foreach ($c in $t.GetConstructors($allD)) { "  ctor(" + (($c.GetParameters() | ForEach-Object { $_.ParameterType.Name + " " + $_.Name }) -join ", ") + ")" }
  Write-Output ""
}

Dump "SFS.World.StageSave"
Dump "SFS.World.Stage"
Write-Output "=== Part 上的 PartSave 相关 / 网格归属 ==="
$p = $asm.GetType("SFS.Parts.Part")
foreach ($m in $p.GetMethods($allD)) { if ($m.Name -match "Save|Grid|Stage|Holder") { "  " + $m.ReturnType.Name + " " + $m.Name + "(" + (($m.GetParameters() | ForEach-Object { $_.ParameterType.Name }) -join ", ") + ")" } }
