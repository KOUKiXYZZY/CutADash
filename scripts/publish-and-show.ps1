# CtrlVPlusをpublishし、生成されたCutADash.exeをExplorerで選択表示する。
# 実行中のCutADash.exe/Migration.exeがあるとpublish先のファイルがロックされて
# 失敗するため、事前に終了させてから行う。
param(
    [ValidateSet("x64", "x86", "arm64")]
    [string]$Platform = "x64",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$csproj = Join-Path $repoRoot "CtrlVPlus/020_CtrlVPlus.csproj"

foreach ($procName in @("CutADash", "Migration")) {
    Get-Process -Name $procName -ErrorAction SilentlyContinue | Stop-Process -Force
}

$publishDir = Join-Path $repoRoot "CtrlVPlus/bin/win-$Platform/publish/win-$Platform/win-$Platform/win-$Platform"

# 古いpublish結果を残したままだと、削除済みのDLL(例: 依存関係を外したパッケージのDLL)が
# 消えずに残って混入してしまうため、publish前に一度フォルダごと消してから作り直す
if (Test-Path $publishDir) {
    Remove-Item -Path $publishDir -Recurse -Force
}

dotnet publish $csproj -p:PublishProfile="win-$Platform" -c $Configuration

# 通常のアプリ実行には不要な、デバッガ用のCoreCLR診断コンポーネントを配布物から除く。
# (デバッガのアタッチやクラッシュダンプ生成にのみ使われ、自己完結型publishには
# トリミングの有無に関わらず常に含まれてしまうため、手動で削る)
$debugOnlyFiles = @(
    "createdump.exe",
    "mscordbi.dll",
    "mscordaccore.dll",
    "mscordaccore_*.dll",
    "Microsoft.DiaSymReader.Native.*.dll"
)
$removedCount = 0
$removedBytes = 0
foreach ($pattern in $debugOnlyFiles) {
    Get-ChildItem -Path $publishDir -Filter $pattern -ErrorAction SilentlyContinue | ForEach-Object {
        $removedBytes += $_.Length
        $removedCount++
        Remove-Item $_.FullName -Force
    }
}
if ($removedCount -gt 0) {
    Write-Host ("配布に不要なデバッグ用ファイルを{0}件削除しました({1:N1} MB)" -f $removedCount, ($removedBytes / 1MB))
}

# 配布先でフォルダを覗いた人がうっかりAssetsを削除/編集してしまわないよう読み取り専用にする
# (意図的な改ざん防止ではなく事故防止のため。次回publish時はフォルダごと作り直すので
# 読み取り専用のまま残っていても問題にならない)
$publishAssetsDir = Join-Path $publishDir "Assets"
if (Test-Path $publishAssetsDir) {
    Get-ChildItem -Path $publishAssetsDir -Recurse -File | ForEach-Object {
        Set-ItemProperty -Path $_.FullName -Name IsReadOnly -Value $true
    }
}

$exePath = Join-Path $publishDir "CutADash.exe"

if (-not (Test-Path $exePath)) {
    throw "publish後もCutADash.exeが見つかりません: $exePath"
}

Start-Process explorer.exe -ArgumentList "/select,`"$exePath`""
