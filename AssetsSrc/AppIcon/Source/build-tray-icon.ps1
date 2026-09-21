# システムトレイ(通知領域)専用のアイコンを、AppIcon.svgから単色版として作る。
#
# 背景: 通知領域の背景色はWindowsのテーマ(タスクバーの明暗)に連動するため、
# アプリ本体のアイコン(AppIcon.ico、角丸タイル+グラデーション背景)をそのまま使うと、
# 小さいサイズでは潰れて見づらい。Windows標準の通知領域アイコンと同じく、背景タイルを
# 持たない単色グリフのみ(透過背景)をライト/ダーク別に用意し、実行時に
# TrayIcon.SetIcon()でテーマに応じて切り替える(App.xaml.cs CreateTrayIcon参照)。
#
# 作り方: SVGの内部構造(fill="..."かstyle="fill:...;"か、viewBoxの数値、<g>の有無など)を
# 直接パースするのは、Affinity Designer等での書き出し方が変わるたびに壊れて壊れやすいので、
# 代わりに画像処理でグリフだけを取り出す:
#   1. AppIcon.svgをそのまま高解像度でラスタライズする(タイル背景+白いグリフの1枚絵)
#   2. ほぼ白(グリフ)のピクセルだけを抽出してマスクにする
#   3. マスクの外接矩形でトリミングし、余白少なめで256x256に敷き詰める
#   4. マスクをテーマの単色(Light=黒/Dark=白)で塗り、透過PNG→ICOへパッキングする
# グリフが白(またはほぼ白)で描かれている限り、SVGの内部構造が変わっても動く。
#
#   使い方: pwsh -File AssetsSrc/AppIcon/Source/build-tray-icon.ps1

$ErrorActionPreference = 'Stop'

$svgDir = $PSScriptRoot
$repoRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
$outDir = Join-Path $repoRoot 'CtrlVPlus\Assets'
$svgPath = Join-Path $svgDir 'AppIcon.svg'

# 通知領域アイコンの実サイズ(100%/125%/150%/200%相当)。Navアイコンと同じ一式を流用する
$icoSizes = @(16, 20, 24, 32)

# グリフ抽出は精度確保のため高解像度でラスタライズしてから縮小する
$captureSize = 1024
$canvasSize = 256

# 「Light」「Dark」はシステムのテーマ名(=通知領域の背景の明暗)。背景が明るいLightでは
# 視認できるよう黒一色、背景が暗いDarkでは白一色にする(逆にすると背景に溶けて見えなくなる)
$themes = @(
    @{ Name = 'Light'; Color = '0,0,0' }
    @{ Name = 'Dark';  Color = '255,255,255' }
)

$edge = @(
    "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
    "${env:ProgramFiles}\Microsoft\Edge\Application\msedge.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $edge) { throw 'msedge.exe が見つかりませんでした。' }

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$work = Join-Path ([System.IO.Path]::GetTempPath()) "trayicon-render-$(Get-Random)"
New-Item -ItemType Directory -Force -Path $work | Out-Null

try {
    Copy-Item $svgPath -Destination $work -Force

    $html = Join-Path $work 'wrap.html'
    @"
<!doctype html><meta charset="utf-8">
<style>html,body{margin:0;padding:0;background:transparent;overflow:hidden}
img{display:block;width:${captureSize}px;height:${captureSize}px}</style>
<img src="AppIcon.svg">
"@ | Set-Content -Path $html -Encoding UTF8

    $capturePng = Join-Path $work 'capture.png'
    $uri = 'file:///' + ($html -replace '\\', '/')

    & $edge --headless --disable-gpu --hide-scrollbars `
        --force-device-scale-factor=1 --default-background-color=00000000 `
        "--window-size=$captureSize,$captureSize" "--screenshot=$capturePng" $uri 2>&1 | Out-Null

    if (-not (Test-Path $capturePng)) { throw "描画に失敗しました: $capturePng" }

    foreach ($theme in $themes) {
        $themeName = $theme.Name
        $rgb = $theme.Color

        $icoOut = Join-Path $outDir "AppIconTray$themeName.ico"
        if (Test-Path $icoOut) {
            # protect-assets.ps1で読み取り専用にされている場合、書き込み前に解除しておく
            Set-ItemProperty -Path $icoOut -Name IsReadOnly -Value $false
        }
        $sizesArg = ($icoSizes -join ',')

        $py = @"
from PIL import Image

src = Image.open(r'$capturePng').convert('RGBA')

# ほぼ白(グリフ)のピクセルだけを抽出してマスクにする。しきい値は220/255
r, g, b, a = src.split()
mask = Image.eval(r, lambda v: 255 if v >= 220 else 0)
for ch in (g, b):
    ch_mask = Image.eval(ch, lambda v: 255 if v >= 220 else 0)
    mask = Image.composite(mask, Image.new('L', mask.size, 0), ch_mask)

bbox = mask.getbbox()
if bbox is None:
    raise SystemExit('AppIcon.svgから白いグリフ部分を検出できませんでした(グリフが白系の色で描かれているか確認してください)')

glyph = mask.crop(bbox)

# 余白少なめ(canvasの上下左右8%程度)で正方形キャンバスへ収める
pad_ratio = 0.08
canvas = $canvasSize
inner = int(canvas * (1 - pad_ratio * 2))
gw, gh = glyph.size
scale = inner / max(gw, gh)
new_size = (max(1, round(gw * scale)), max(1, round(gh * scale)))
glyph_resized = glyph.resize(new_size, Image.LANCZOS)

alpha = Image.new('L', (canvas, canvas), 0)
offset = ((canvas - new_size[0]) // 2, (canvas - new_size[1]) // 2)
alpha.paste(glyph_resized, offset)

color = tuple(int(c) for c in '$rgb'.split(','))
out = Image.new('RGBA', (canvas, canvas), color + (0,))
out.putalpha(alpha)

sizes = [int(s) for s in '$sizesArg'.split(',')]
out.save(r'$icoOut', sizes=[(s, s) for s in sizes])
"@
        $py | py -3 -

        Write-Host ("  AppIconTray{0}.ico ({1})" -f $themeName, ($icoSizes -join ', '))
    }
}
finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host ("完了: {0}" -f $outDir)
