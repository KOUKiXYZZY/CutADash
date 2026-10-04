# AppIcon.svg / TaskTrayIconLight.svg / TaskTrayIconDark.svg から、ビルドで使うアイコン一式を作る。
#
#   CutADash/Assets/AppIcon.png            (1024x1024の1枚絵)
#   CutADash/Assets/AppIcon.ico            (アプリ本体・exeのアイコン)
#   CutADash/Assets/AppIconTrayLight.ico   (ライトのタスクバー用。濃い色のグリフ)
#   CutADash/Assets/AppIconTrayDark.ico    (ダークのタスクバー用。白いグリフ)
#   AssetsSrc/AppIcon/Source/*.png         (各SVGの1024px版。参照・確認用)
#
# 画像を縮小するのではなく、ベクター(SVG)から各サイズを、ヘッドレスEdgeで直接描画する。
# 縮小による輪郭のにじみ・リンギングが起きず、16pxでも線がくっきりする。
# ICOは、PNG形式のフレームを並べて自前で組み立てる(Pythonやイメージ用ライブラリは不要)。
#
#   使い方: pwsh AssetsSrc/AppIcon/Source/build-icons-from-svg.ps1

param(
    [string]$Edge = "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$srcDir = $PSScriptRoot
$repoRoot = (Resolve-Path (Join-Path $srcDir "..\..\..")).Path
$assetsDir = Join-Path $repoRoot "CutADash\Assets"
$tmpDir = Join-Path ([System.IO.Path]::GetTempPath()) "cutadash-icons"

# ICOに入れる解像度。通知領域のアイコンは、100%〜400%相当の実サイズ(16/20/24/32/40/48/64)
$appSizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$traySizes = 16, 20, 24, 32, 40, 48, 64

if (-not (Test-Path $Edge)) {
    throw "Microsoft Edgeが見つかりません: $Edge"
}
New-Item -ItemType Directory -Force $tmpDir | Out-Null

# SVGを、指定した正方形のサイズで、背景透明のPNGとして描画する
function Render-Svg([string]$svgPath, [int]$size, [string]$pngPath) {
    $text = (Get-Content $svgPath -Raw -Encoding UTF8) -replace 'width="1024" height="1024"', "width=`"$size`" height=`"$size`""
    $baseName = [System.IO.Path]::GetFileNameWithoutExtension($svgPath)
    $tmpSvg = Join-Path $tmpDir "${baseName}_$size.svg"
    Set-Content -Path $tmpSvg -Value $text -Encoding UTF8

    if (Test-Path $pngPath) { Remove-Item $pngPath -Force }

    $url = "file:///" + $tmpSvg.Replace('\', '/')
    Start-Process -FilePath $Edge -Wait -NoNewWindow -ArgumentList @(
        "--headless=new", "--disable-gpu", "--hide-scrollbars",
        "--default-background-color=00000000",
        "--window-size=$size,$size",
        "--screenshot=$pngPath",
        $url
    ) | Out-Null

    if (-not (Test-Path $pngPath)) {
        throw "描画に失敗しました: $svgPath ($size px)"
    }
}

# PNGフレームを並べたICOを書き出す(Windows Vista以降はPNG形式のフレームを読める)
function Write-Ico([string[]]$pngPaths, [int[]]$sizes, [string]$icoPath) {
    $stream = [System.IO.MemoryStream]::new()
    $writer = [System.IO.BinaryWriter]::new($stream)

    $writer.Write([uint16]0)               # reserved
    $writer.Write([uint16]1)               # type: icon
    $writer.Write([uint16]$pngPaths.Count) # count

    $offset = 6 + 16 * $pngPaths.Count
    $datas = @()
    for ($i = 0; $i -lt $pngPaths.Count; $i++) {
        $data = [System.IO.File]::ReadAllBytes($pngPaths[$i])
        $datas += , $data

        $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dim)          # width (0 = 256)
        $writer.Write([byte]$dim)          # height
        $writer.Write([byte]0)             # palette
        $writer.Write([byte]0)             # reserved
        $writer.Write([uint16]1)           # planes
        $writer.Write([uint16]32)          # bit count
        $writer.Write([uint32]$data.Length)
        $writer.Write([uint32]$offset)
        $offset += $data.Length
    }
    foreach ($data in $datas) { $writer.Write($data) }

    $writer.Flush()
    if (Test-Path $icoPath) { Set-ItemProperty $icoPath -Name IsReadOnly -Value $false }
    [System.IO.File]::WriteAllBytes($icoPath, $stream.ToArray())
    $writer.Dispose()
}

function Build-Ico([string]$svgPath, [int[]]$sizes, [string]$icoPath) {
    $baseName = [System.IO.Path]::GetFileNameWithoutExtension($svgPath)
    $pngs = foreach ($size in $sizes) {
        $png = Join-Path $tmpDir "${baseName}_$size.png"
        Render-Svg $svgPath $size $png
        $png
    }
    Write-Ico @($pngs) $sizes $icoPath
    Write-Host ("  {0} ({1})" -f (Split-Path $icoPath -Leaf), ($sizes -join ", "))
}

function Build-Png([string]$svgPath, [string[]]$outPaths) {
    $png = Join-Path $tmpDir ([System.IO.Path]::GetFileNameWithoutExtension($svgPath) + "_1024.png")
    Render-Svg $svgPath 1024 $png
    foreach ($out in $outPaths) {
        if (Test-Path $out) { Set-ItemProperty $out -Name IsReadOnly -Value $false }
        Copy-Item $png $out -Force
        Write-Host ("  {0}" -f (Split-Path $out -Leaf))
    }
}

Write-Host "PNG(1024px)"
Build-Png (Join-Path $srcDir "AppIcon.svg") @((Join-Path $srcDir "AppIcon.png"), (Join-Path $assetsDir "AppIcon.png"))
Build-Png (Join-Path $srcDir "TaskTrayIconLight.svg") @(Join-Path $srcDir "TaskTrayIconLight.png")
Build-Png (Join-Path $srcDir "TaskTrayIconDark.svg") @(Join-Path $srcDir "TaskTrayIconDark.png")

Write-Host "ICO"
Build-Ico (Join-Path $srcDir "AppIcon.svg") $appSizes (Join-Path $assetsDir "AppIcon.ico")
Build-Ico (Join-Path $srcDir "TaskTrayIconLight.svg") $traySizes (Join-Path $assetsDir "AppIconTrayLight.ico")
Build-Ico (Join-Path $srcDir "TaskTrayIconDark.svg") $traySizes (Join-Path $assetsDir "AppIconTrayDark.ico")

Write-Host ""
Write-Host "完了: $assetsDir"
