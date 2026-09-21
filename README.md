<p align="center">
  <img src="docs/images/AppIcon.png" width="96" height="96" alt="CutADash icon">
</p>

# CutA'

Windows向けの高機能クリップボードマネージャーです。コピーした履歴を自動で保存し、素早く検索・再利用できます。

<p align="center">
  <img src="docs/images/mainWindow.png" alt="CutADashのメイン画面(履歴タブ)">
</p>

> [!NOTE]
> **このプロジェクトはほぼバイブコーディング(Vibe Coding)で開発されています。**
> 実装のほとんどをAIアシスタント(Claude Code)との対話を通じて生成しており、
> 人間が一行ずつ書き下ろしたコードベースではありません。(私が実装したのは精々、
> HotKeyMonitor.cs と ClipboardMonitor.cs、WindowMessageDispatcherくらい）コード中のコメントや
> 設計判断の記録もAIとのやり取りの中で残されたものです。実運用に使う場合は、
> その点を踏まえた上でご利用ください。
>
> ……今後、何をして人間は食っていけばいいんだろうか。

## 主な機能

- **クリップボード履歴の自動保存**: テキスト・リッチテキスト・画像・ファイル・図形(OLEオブジェクト)など、様々な形式のコピー内容を自動的に記録します。
- **お気に入り(フォルダ管理)**: よく使う項目をフォルダに整理して保存できます。フォルダの開閉状態も記憶されます。
- **絵文字ピッカー**: Microsoft Fluent Emoji(MITライセンス)を使った絵文字一覧から、クリックひとつで貼り付けられます。肌の色バリエーションは右クリックで選択可能です。
- **シーケンシャルペースト**: 複数の項目をキューに積んでおき、順番に連続で貼り付けられます。
- **テキスト選択ツールバー**: 他のアプリでテキストを選択すると、近くに小さなツールバー(コピー/検索)が表示されます(試験的機能)。
- **ホットキー起動**: 任意のショートカットキーでパレットを呼び出せます。
- **タスクトレイ常駐**: バックグラウンドで動作し、必要な時だけ呼び出せます。
- **暗号化保存**: 履歴・お気に入りはSQLCipherで暗号化してローカルに保存されます。
- **自動アップデート**: Velopackによる自動更新に対応しています。

## 使い方(How to Use)

1. アプリを起動すると、タスクトレイに常駐します。ウィンドウは普段表示されず、必要な時だけ呼び出す運用です。
2. **ショートカットキーでパレットを開く**: 設定画面(タスクトレイアイコンを右クリック→「設定」)から、パレットを呼び出すショートカットキーを好きなキーに割り当てられます。割り当てたキーを押すと、フォーカスを奪わずにパレットが前面に表示されます。
3. パレットには3つのタブがあります。
   - **履歴**: コピーした内容が自動的に一覧に並びます。項目をクリック、またはEnterキーで直前にフォーカスしていたアプリへそのまま貼り付けられます。
   - **お気に入り**: 履歴の項目を右クリックして「お気に入りに追加」すると、フォルダに整理して保存できます。フォルダはドラッグ&ドロップで並べ替え・移動でき、開閉状態も記憶されます。
   - **絵文字**: カテゴリ別の絵文字一覧から、クリックで直前のアプリへ絵文字を貼り付けられます。バッジが付いている絵文字は肌の色バリエーションを持っており、右クリックで選択できます。
4. **シーケンシャルペースト**: 履歴/お気に入りの項目を右クリックして「シーケンシャルペーストに追加」するとキューに積まれます。タスクトレイメニューの「シーケンシャルペースト」からキュー画面を開き、貼り付けたい順に並べて1件ずつ連続で貼り付けられます。
5. **テキスト選択ツールバー**(試験的機能、既定は無効): 設定画面でONにすると、他のアプリでテキストを選択した際に近くへ小さなツールバー(コピー/検索)がポップアップ表示されます。
6. 履歴の保存件数・除外アプリ・テーマ(Mica/Acrylic等)・言語といった各種設定は、すべて設定画面から変更できます。

## 技術スタック

- **WinUI 3** / .NET 10(アンパッケージ配布、MSIXなし)
- **SQLite + SQLCipher**(暗号化ローカルDB)
- **CommunityToolkit.Mvvm**(MVVMパターン)
- **Velopack**(自動更新)

## プロジェクト構成

| プロジェクト | 役割 |
| --- | --- |
| `CutADash` | メインアプリケーション(UI、タスクトレイ、ホットキー等) |
| `Common` | Win32相互運用など共通インフラ |
| `Repositories` | クリップボード履歴・お気に入り・絵文字データのDBアクセス層 |
| `Preferences` | 設定画面と設定値の永続化 |
| `SelectionToolbar` | テキスト選択時のポップアップツールバー |
| `SequentialPaste` | 複数項目を順番に貼り付けるキュー機能 |
| `Migration` / `MigrationCli` | 旧バージョンからのデータ移行 |
| `QrDecoding` | QRコードの読み取り |
| `WinAPI` | Win32 APIのラッパー |
| `AssetsSrc` | 絵文字アセット等、ビルド前に生成する素材のソース |

## ビルド方法

Visual Studio 2022以降(WinUI 3開発ワークロード)で `CutADash.sln` を開いてビルドしてください。

```bash
dotnet build CutADash.sln
```

絵文字アセットはビルド前に `AssetsSrc/EmojiGenerateScript` 配下のスクリプトで生成する必要があります(詳細は同フォルダ内を参照)。

## 使用しているオープンソースライブラリ

| パッケージ | バージョン | ライセンス | 著作権者 |
| --- | --- | --- | --- |
| [Accessibility](https://github.com/dotnet/runtime) | 4.6.0-preview3-27504-2 | MIT | .NET Foundation and Contributors |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | 8.4.2 | MIT | .NET Foundation and Contributors |
| [fluentui-emoji](https://github.com/microsoft/fluentui-emoji)(submodule、絵文字画像) | - | MIT | (c) Microsoft Corporation |
| [Microsoft.Extensions.DependencyInjection](https://github.com/dotnet/runtime) | 10.0.9 | MIT | .NET Foundation and Contributors |
| [Microsoft.Windows.SDK.BuildTools](https://www.nuget.org/packages/Microsoft.Windows.SDK.BuildTools) | 10.0.28000.1721 | Microsoft独自ライセンス | (c) Microsoft Corporation |
| [Microsoft.WindowsAppSDK](https://github.com/microsoft/WindowsAppSDK) | 1.8.260416003 | Microsoft独自ライセンス(配布パッケージ) | (c) Microsoft Corporation |
| [Microsoft.Xaml.Behaviors.WinUI.Managed](https://github.com/microsoft/XamlBehaviors) | 3.0.1 | MIT | (c) 2015 Microsoft |
| [Newtonsoft.Json](https://github.com/JamesNK/Newtonsoft.Json) | 13.0.4 | MIT | (c) 2007 James Newton-King |
| [SQLitePCLRaw](https://github.com/ericsink/SQLitePCL.raw)(bundle_e_sqlcipher / core / lib.e_sqlite3) | 2.1.11 / 2.1.11 / 2.1.13 | Apache-2.0 | (c) Eric Sink, Zumero, LLC and Contributors |
| [SQLCipher](https://github.com/sqlcipher/sqlcipher)(SQLitePCLRaw.lib.e_sqlcipher同梱) | - | BSD-3-Clause | (c) Zetetic LLC |
| [System.Drawing.Common](https://github.com/dotnet/runtime) | 8.0.10 | MIT | .NET Foundation and Contributors |
| [System.Security.Cryptography.ProtectedData](https://github.com/dotnet/runtime) | 8.0.0 | MIT | .NET Foundation and Contributors |
| [Velopack](https://github.com/velopack/velopack) | 0.0.1298 | MIT | (c) Velopack Contributors |
| [WinUIEx](https://github.com/dotmorten/WinUIEx) | 2.9.0 | MIT | (c) 2021 Morten Nielsen |
| [ZXing.Net](https://github.com/micjahn/ZXing.Net) / ZXing.Net.Bindings.Windows.Compatibility | 0.16.10 / 0.16.12 | Apache-2.0 | (c) 2007-2009 ZXing authors / Michael Jahn |
| [sqlite-net-pcl](https://github.com/praeclarum/sqlite-net) | 1.9.172 | MIT | (c) Krueger Systems, Inc. |

各ライブラリの詳細なライセンス全文は、アプリ内の設定画面(About)からも確認できます([Preferences/Utils/OpenSourceLicenses.cs](Preferences/Utils/OpenSourceLicenses.cs))。

## ライセンス

[MIT License](LICENSE)
