# NavigationView のアイコンPNGを、このフォルダ(AssetsSrc/Nav/Source、リポジトリ直下)配下の
# SVGマスターから各サイズで描き起こし、実際にビルドで使われるCutADash/Assets/Navへ出力する。
#
# アイコンごとにサブフォルダ(Emoji/Favorite/History等)へ分け、Selected/Unselectedの
# それぞれをDark/Light別のSVGとして手動編集する構成になっているため、フォルダ構成は
# 問わずサブフォルダを再帰的に探索する。出力ファイル名はSVGのファイル名(拡張子抜き。
# 例: HistorySelectedDark.svg -> HistorySelectedDark.png)から決まるので、
# フォルダ名自体はMainWindow側のアセット参照とは無関係(自由に整理してよい)。
#
# 1SVGにつき16/24/32/48pxの4サイズを描き起こすが、ファイル数を減らすため、
# 4枚を縦に結合した1枚のPNG(幅48px、高さ16+24+32+48=120px、上から昇順)として
# 保存する。MainWindow側は表示に必要なサイズ分だけを、そのPNGの該当するY位置から
# 切り出してデコードする(GetNavIconAsync参照。切り出しのY位置の計算方法を変える場合は
# 両方合わせて直すこと)。
#
# 以前は48x48のPNG1枚だけを用意し、16x16 DIPの ImageIcon に流し込んでいたが、
# 100%スケールでは 48px -> 16px の3:1縮小を描画時にコンポジタが潰すことになり、
# 輪郭がボケていた。表示に必要な物理ピクセル数ちょうどで描き起こしておくことで、
# 縮小によるボケを避けている。
#
# SVGのラスタライズには Edge のヘッドレスモードを使う(追加インストール不要)。
# 新しい --headless=new では背景が透過しないため、従来の --headless を使うこと。
#
#   使い方: pwsh -File AssetsSrc/Nav/Source/render-nav-icons.ps1

param(
    # 描き起こすSVGのファイル名の絞り込み。既定は全て。例: -Filter 'History*.svg'
    [string]$Filter = '*.svg'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
$svgDir = $PSScriptRoot
$outDir = Join-Path $repoRoot 'CutADash\Assets\Nav'

# 16 DIP 表示に対して、拡大率 100% / 150% / 200% / 300% で必要になる物理ピクセル数。
# 中間の拡大率(125%など)では、これより大きい最小のものを選んでデコード時に縮める。
# 昇順であることが縦結合の前提(MainWindow側のY位置計算と対応させるため)
$sizes = @(16, 24, 32, 48)
$maxSize = ($sizes | Measure-Object -Maximum).Maximum

$edge = @(
    "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
    "${env:ProgramFiles}\Microsoft\Edge\Application\msedge.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $edge) { throw 'msedge.exe が見つかりませんでした。' }

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

# Edgeはスクリーンショット対象をURLで受け取るため、SVGを実寸で1枚だけ置いた
# HTMLを作業ディレクトリに書き出してから読み込ませる
$work = Join-Path ([System.IO.Path]::GetTempPath()) "navicon-render-$(Get-Random)"
New-Item -ItemType Directory -Force -Path $work | Out-Null

try {
    Get-ChildItem -Path $svgDir -Filter $Filter -Recurse | ForEach-Object {
        $svg = $_
        Copy-Item $svg.FullName -Destination $work -Force

        $sizePngs = @()
        foreach ($size in $sizes) {
            $html = Join-Path $work "wrap_$size.html"
            @"
<!doctype html><meta charset="utf-8">
<style>html,body{margin:0;padding:0;background:transparent;overflow:hidden}
img{display:block;width:${size}px;height:${size}px}</style>
<img src="$($svg.Name)">
"@ | Set-Content -Path $html -Encoding UTF8

            $sizePng = Join-Path $work "$($svg.BaseName)_$size.png"
            $uri = 'file:///' + ($html -replace '\\', '/')

            # --window-size の値はカンマ区切りだが、クォートしないとPowerShellが
            # 配列の区切りとして解釈して引数が分割され、Edgeが既定のウィンドウサイズで
            # 撮ってしまう(748x484のPNGができる)。必ず1つの文字列として渡すこと
            & $edge --headless --disable-gpu --hide-scrollbars `
                --force-device-scale-factor=1 --default-background-color=00000000 `
                "--window-size=$size,$size" "--screenshot=$sizePng" $uri 2>&1 | Out-Null

            if (-not (Test-Path $sizePng)) { throw "描画に失敗しました: $sizePng" }
            $sizePngs += $sizePng
        }

        $out = Join-Path $outDir "$($svg.BaseName).png"

        # 読み取り専用にされている場合に備えて、書き込み前に解除しておく
        if (Test-Path $out) {
            Set-ItemProperty -Path $out -Name IsReadOnly -Value $false
        }

        # 4サイズを縦に結合した1枚のPNGにする(System.Drawingを使うので、Pythonは不要)
        Add-Type -AssemblyName System.Drawing
        $canvas = [System.Drawing.Bitmap]::new($maxSize, ($sizes | Measure-Object -Sum).Sum, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [System.Drawing.Graphics]::FromImage($canvas)
        $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $y = 0
        for ($i = 0; $i -lt $sizes.Count; $i++) {
            $tile = [System.Drawing.Image]::FromFile($sizePngs[$i])
            $graphics.DrawImage($tile, 0, $y, $sizes[$i], $sizes[$i])
            $tile.Dispose()
            $y += $sizes[$i]
        }
        $graphics.Dispose()
        $canvas.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
        $canvas.Dispose()

        if (-not (Test-Path $out)) { throw "結合に失敗しました: $out" }
        Write-Host ("  {0}" -f (Split-Path $out -Leaf))
    }
}
finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host ("完了: {0} に {1} ファイル" -f $outDir, (Get-ChildItem $outDir -Filter *.png).Count)
