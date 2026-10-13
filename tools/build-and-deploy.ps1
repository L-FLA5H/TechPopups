#Requires -Version 5.1
# TechPopups build helper (UTF-8 with BOM, do not strip the BOM)
param(
    [string]$ProjectDir = (Join-Path $PSScriptRoot '..'),
    [string]$GameDir = 'D:\Steam\steamapps\common\Celeste'
)

$ErrorActionPreference = 'Stop'

function Say($msg)      { Write-Host '==> ' + $msg -ForegroundColor Cyan }
function Ok($msg)       { Write-Host '    ' + $msg -ForegroundColor Green }
function Bad($msg)      { Write-Host '    ' + $msg -ForegroundColor Red }

$projectDir = (Resolve-Path $ProjectDir).Path
$modDir     = Join-Path $GameDir 'Mods\TechPopups'
$srcDir     = Join-Path $modDir  'Source'

Say '1/5 检查游戏是否已关闭'
$proc = Get-Process -Name Celeste -ErrorAction SilentlyContinue
if ($proc) {
    Bad '蔚蓝正在运行，必须先关掉它才能覆盖 DLL。'
    $ans = Read-Host '要不要现在强制关闭？(y/n)'
    if ($ans -ne 'y' -and $ans -ne 'Y') {
        Bad '已取消。请手动关闭游戏后再运行本脚本。'
        exit 1
    }
    Stop-Process -Name Celeste -Force
    Start-Sleep -Seconds 3
    Ok '已关闭。'
} else {
    Ok '游戏没在跑，可以继续。'
}

Say '2/5 检查 dotnet'
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Bad '没找到 dotnet，请在 PATH 里安装 .NET SDK (8.0 或更高)。'
    exit 1
}
Ok 'dotnet 可用。'

Say '3/5 把源码同步到游戏 Mods 目录'
if (-not (Test-Path $modDir)) { New-Item -ItemType Directory -Force -Path $modDir | Out-Null }
Copy-Item -Path (Join-Path $projectDir 'Source\*') -Destination $srcDir -Recurse -Force
Copy-Item -Path (Join-Path $projectDir 'everest.yaml')   -Destination $modDir -Force
Copy-Item -Path (Join-Path $projectDir '.everestignore') -Destination $modDir -Force
Copy-Item -Path (Join-Path $projectDir 'Dialog')         -Destination $modDir -Recurse -Force
Copy-Item -Path (Join-Path $projectDir 'Graphics')       -Destination $modDir -Recurse -Force
Remove-Item -Recurse -Force (Join-Path $srcDir 'bin') -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force (Join-Path $srcDir 'obj') -ErrorAction SilentlyContinue
Ok ('已同步到 ' + $modDir)

Say '4/5 编译 Release'
Push-Location $srcDir
try {
    dotnet build -c Release
    $code = $LASTEXITCODE
} finally {
    Pop-Location
}
if ($code -ne 0) {
    Bad ('编译失败，退出码 ' + $code)
    exit $code
}
Ok '编译成功。'

Say '5/5 清理多余产物'
Remove-Item -Recurse -Force (Join-Path $modDir 'TechPopups') -ErrorAction SilentlyContinue
Write-Host ''
Write-Host ('完成！Mod 目录：' + $modDir) -ForegroundColor Green
Write-Host '进游戏前记得确认：Mods 目录里那个旧的 TechPopups.zip 已经改名或删掉，' -ForegroundColor Yellow
Write-Host '否则 Everest 会同时加载 zip 和文件夹两份，mod 会被加载两次。' -ForegroundColor Yellow
Write-Host ''
Read-Host '按回车关闭'
