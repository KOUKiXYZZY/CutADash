# このフォルダ(AssetsSrc/AppIcon/Source、リポジトリ直下)のAppIcon.svgから、
# 実際にビルドで使われるCtrlVPlus/Assets/AppIcon.png(1024x1024の1枚絵)と
# AppIcon.ico(複数解像度をまとめたアイコンリソース。020_CtrlVPlus.csprojの
# ApplicationIconが参照)を作り直す。
#
# SVGのラスタライズは、Navアイコンと同じくEdgeのヘッドレスモードで行う
# (render-nav-icons.ps1参照。追加インストール不要)。
# ICOへのパッキングだけはPowerShell単体では扱えないため、Pillow(py -3)を使う。
#
#   使い方: pwsh -File AssetsSrc/AppIcon/Source/build-app-icon.ps1

$ErrorActionPreference = 'Stop'

$svgDir = $PSScriptRoot
$repoRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
$outDir = Join-Path $repoRoot 'CtrlVPlus\Assets'
$svgPath = Join-Path $svgDir 'AppIcon.svg'

# ICOに詰める解像度。16/20/24/32/40/48/64/128/256はWindowsの標準DPI(100%~300%程度)で
# 実際に使われるサイズの一式(元のAppIcon.icoに合わせている)
$icoSizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$renderSize = 1024

$edge = @(
    "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
    "${env:ProgramFiles}\Microsoft\Edge\Application\msedge.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $edge) { throw 'msedge.exe が見つかりませんでした。' }

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$work = Join-Path ([System.IO.Path]::GetTempPath()) "appicon-render-$(Get-Random)"
New-Item -ItemType Directory -Force -Path $work | Out-Null

try {
    Copy-Item $svgPath -Destination $work -Force

    $html = Join-Path $work 'wrap.html'
    @"
<!doctype html><meta charset="utf-8">
<style>html,body{margin:0;padding:0;background:transparent;overflow:hidden}
img{display:block;width:${renderSize}px;height:${renderSize}px}</style>
<img src="AppIcon.svg">
"@ | Set-Content -Path $html -Encoding UTF8

    $pngOut = Join-Path $outDir 'AppIcon.png'

    # protect-assets.ps1で読み取り専用にされている場合、書き込み前に解除しておく
    if (Test-Path $pngOut) {
        Set-ItemProperty -Path $pngOut -Name IsReadOnly -Value $false
    }

    $uri = 'file:///' + ($html -replace '\\', '/')

    & $edge --headless --disable-gpu --hide-scrollbars `
        --force-device-scale-factor=1 --default-background-color=00000000 `
        "--window-size=$renderSize,$renderSize" "--screenshot=$pngOut" $uri 2>&1 | Out-Null

    if (-not (Test-Path $pngOut)) { throw "描画に失敗しました: $pngOut" }
    Write-Host ("  {0} ({1}x{1})" -f (Split-Path $pngOut -Leaf), $renderSize)
}
finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}

Write-Host 'ICOへパッキング中...'

$icoOut = Join-Path $outDir 'AppIcon.ico'
if (Test-Path $icoOut) {
    Set-ItemProperty -Path $icoOut -Name IsReadOnly -Value $false
}
$sizesArg = ($icoSizes -join ',')

$py = @"
from PIL import Image
import sys

src = Image.open(r'$pngOut').convert('RGBA')
sizes = [int(s) for s in '$sizesArg'.split(',')]
src.save(r'$icoOut', sizes=[(s, s) for s in sizes])
print('  AppIcon.ico (' + ', '.join(f'{s}x{s}' for s in sizes) + ')')
"@

$py | py -3 -

Write-Host ''
Write-Host ("完了: {0}" -f $outDir)
