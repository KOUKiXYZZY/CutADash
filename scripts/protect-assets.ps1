# CtrlVPlus/Assets配下(実行時に読み込まれる実データ)のファイルすべてに読み取り専用属性を
# 付ける。フォルダをエクスプローラー等で見た人がうっかり削除・編集してしまう事故を防ぐための
# もので、意図的な改ざんを防ぐものではない(その場合はDLLへの埋め込み+署名検証等が必要)。
#
# 各アセット生成スクリプト(AssetsSrc配下のbuild-*.ps1/render-*.ps1/generate_*.py)は、
# 出力ファイルを上書きする前に自分でこの属性を外すので、再生成の妨げにはならない。
#
#   使い方: pwsh -File scripts/protect-assets.ps1

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$assetsDir = Join-Path $repoRoot 'CtrlVPlus\Assets'

$files = Get-ChildItem -Path $assetsDir -Recurse -File
foreach ($file in $files) {
    Set-ItemProperty -Path $file.FullName -Name IsReadOnly -Value $true
}

Write-Host ("{0} 件のファイルを読み取り専用にしました: {1}" -f $files.Count, $assetsDir)
