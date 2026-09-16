# 编译 N.E.K.O Bridge（SFS 代码模组）
# 使用 Windows 自带的 C# 5 编译器，引用 SFS 的游戏程序集。

$ErrorActionPreference = "Stop"

$SfsGame   = "F:\SteamLibrary\steamapps\common\Spaceflight Simulator\Spaceflight Simulator Game"
$Managed   = Join-Path $SfsGame "Spaceflight Simulator_Data\Managed"
$ModsDir   = Join-Path $SfsGame "Mods"
$Csc       = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

$Root      = $PSScriptRoot
if (-not $Root) { $Root = Split-Path -Parent $MyInvocation.MyCommand.Path }
$SrcDir    = Join-Path $Root "src"
$OutDir    = Join-Path $Root "dist"
$OutDll    = Join-Path $OutDir "SFS-Agent.dll"

Write-Output "=== 环境检查 ==="
if (-not (Test-Path $Csc))     { throw "找不到 csc.exe: $Csc" }
if (-not (Test-Path $Managed)) { throw "找不到 SFS Managed 目录: $Managed" }
Write-Output "  csc     : $Csc"
Write-Output "  managed : $Managed"

# SFS 安装路径含空格，csc 的 /reference: 参数会被截断，
# 因此先把需要的程序集复制到无空格的本地 refs 目录。
$RefDir = Join-Path $Root "refs"
New-Item -ItemType Directory -Force -Path $RefDir | Out-Null

$refs = @()
foreach ($name in @("Assembly-CSharp.dll", "0Harmony.dll")) {
    $src = Join-Path $Managed $name
    if (-not (Test-Path $src)) { throw "缺少引用程序集: $src" }
    $dst = Join-Path $RefDir $name
    Copy-Item $src $dst -Force
    $refs += $dst
}
foreach ($r in $refs) {
    if (-not (Test-Path $r)) { throw "缺少引用程序集: $r" }
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$srcFiles = Get-ChildItem -Path $SrcDir -Filter "*.cs" | Select-Object -ExpandProperty FullName
Write-Output ""
Write-Output "=== 源文件 ==="
$srcFiles | ForEach-Object { "  " + (Split-Path -Leaf $_) }
Write-Output ""
Write-Output "=== 引用 ==="
$refs | ForEach-Object { "  $_" }

$argList = @(
  "/target:library",
  "/out:$OutDll",
  "/nologo",
  "/optimize+",
  "/reference:System.dll",
  "/reference:System.Core.dll"
)
foreach ($r in $refs) { $argList += "/reference:$r" }
$argList += $srcFiles

Write-Output ""
Write-Output "=== 编译 ==="
& $Csc $argList
$code = $LASTEXITCODE
Write-Output "csc exit: $code"

if ($code -ne 0) {
    throw "编译失败"
}

$dll = Get-Item $OutDll
Write-Output ""
Write-Output "=== 产物 ==="
Write-Output "  $OutDll  ($([math]::Round($dll.Length/1KB,1)) KB)"

# 部署到 SFS Mods 目录
#
# SFS 的模组规范是「一目录一模组」：Mods/<模组名>/<模组名>.dll
# 把 dll 平铺在 Mods 根目录下也能被扫描到，但会和目录形式重复加载，
# 因此统一部署到独立子目录。
if (Test-Path $ModsDir) {
    $modDir = Join-Path $ModsDir "SFS-Agent"
    New-Item -ItemType Directory -Force -Path $modDir | Out-Null
    $target = Join-Path $modDir "SFS-Agent.dll"
    Copy-Item $OutDll $target -Force
    Write-Output ""
    Write-Output "=== 已部署 ==="
    Write-Output "  $target"

    # 清理可能残留的旧模组目录（避免两个模组抢同一个端口）
    $legacy = Join-Path $ModsDir "NekoBridge"
    if (Test-Path $legacy) {
        Write-Output ""
        Write-Output "检测到旧模组目录（与 SFS-Agent 冲突）：$legacy"
        try {
            Remove-Item $legacy -Recurse -Force -ErrorAction Stop
            Write-Output "  已删除"
        } catch {
            Write-Output "  删除失败（游戏可能正在运行）：请先关闭游戏再重跑本脚本"
        }
    }
} else {
    Write-Output ""
    Write-Output "警告：未找到 Mods 目录，跳过部署：$ModsDir"
}
