<p align="center">
  <img src="docs/images/AppIcon.png" width="96" height="96" alt="CutADash icon">
</p>

<h1 align="center">CutADash</h1>

<p align="center">コピーした瞬間から、次の貼り付けまでを最短距離で。</p>

<p align="center">
  <a href="https://github.com/KOUKiXYZZY/CutADash/releases/latest"><img src="https://img.shields.io/github/v/release/KOUKiXYZZY/CutADash?include_prereleases&label=release" alt="Latest release"></a>
  <img src="https://img.shields.io/badge/platform-Windows-0078D6" alt="Platform: Windows">
  <img src="https://img.shields.io/badge/.NET-10-512BD4" alt=".NET 10">
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-green" alt="License: MIT"></a>
</p>

Windows向けクリップボードマネージャー。コピー履歴を自動保存し、素早く検索・再利用できます。

<p align="center">
  <img src="docs/images/mainWindow.png" alt="CutADashのメイン画面(履歴タブ)">
</p>

<p align="center">
  <a href="https://github.com/KOUKiXYZZY/CutADash/releases/latest">⬇️ 最新版をダウンロード</a>
</p>

> [!NOTE]
> **このプロジェクトはほぼバイブコーディング(Vibe Coding)で開発されています。**
> 実装のほとんどをAIアシスタント(Claude Code)との対話で生成しており、人間が一行ずつ
> 書き下ろしたコードベースではありません(自分で書いたのはHotKeyMonitor.cs・
> ClipboardMonitor.cs・WindowMessageDispatcher程度)。実運用に使う場合はその点を踏まえてください。
>
> ……今後、何をして人間は食っていけばいいんだろうか。

## 主な機能

- 📋 **クリップボード履歴の自動保存**
  - テキスト・リッチテキスト・画像・ファイル・Officeのシェイプに対応
- ⭐ **お気に入り**
  - フォルダで整理
- 😀 **絵文字ピッカー**
  - 肌の色バリエーション対応
- 🔁 **シーケンシャルペースト**
  - キューに積んで順番に連続貼り付け
- 🖱️ **テキスト選択ツールバー**
  — 選択時にコピー/検索をポップアップ表示(試験的機能)
- 🔒 **暗号化保存**
  - SQLCipherでローカル暗号化
- 🔄 **自動更新**

## 使い方

1. ショートカットキーを設定する。
   起動するとタスクトレイに常駐します。
   設定画面(タスクトレイアイコン右クリック→「設定」)でパレットを呼び出すショートカットキーを設定してください。
2. メインウィンドウは履歴・お気に入り・絵文字の3構成です。
   項目をクリックまたはEnterで直前のアプリへ貼り付けます。
3. 「お気に入り」へ追加する。
   履歴の項目をリストから右クリックして追加できます。
4. シーケンシャルペースト
   「シーケンシャルペーストに追加」でキューに積み、
   タスクトレイメニューから「シーケンシャルペースト」画面を表示し、開始ボタンを押すと、Ctrl+Vで連続貼り付けできます。
5. 保存件数・除外アプリ・テーマ・言語などは設定画面から変更できます。

## 技術スタック

WinUI 3 / .NET 10(アンパッケージ配布)、SQLite + SQLCipher、CommunityToolkit.Mvvm、Velopack

## プロジェクト構成

- `CutADash` — メインアプリケーション(UI、タスクトレイ、ホットキー等)
- `Common` — Win32相互運用など共通インフラ
- `Repositories` — クリップボード履歴・お気に入り・絵文字データのDBアクセス層
- `Preferences` — 設定画面と設定値の永続化
- `SelectionToolbar` — テキスト選択時のポップアップツールバー
- `SequentialPaste` — 複数項目を順番に貼り付けるキュー機能
- `Migration` / `MigrationCli` — 旧バージョンからのデータ移行
- `QrDecoding` — QRコードの読み取り
- `WinAPI` — Win32 APIのラッパー
- `AssetsSrc` — 絵文字アセット等、ビルド前に生成する素材のソース

## ビルド方法

Visual Studio 2022以降(WinUI 3開発ワークロード)で `CutADash.sln` を開くか、以下を実行してください。

```bash
dotnet build CutADash.sln
```

絵文字アセットはビルド前に `AssetsSrc/EmojiGenerateScript` 配下のスクリプトで生成する必要があります。

## 使用しているオープンソースライブラリ

- [Accessibility](https://github.com/dotnet/runtime)
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet)
- [fluentui-emoji](https://github.com/microsoft/fluentui-emoji)(submodule、絵文字画像)
- [Microsoft.Extensions.DependencyInjection](https://github.com/dotnet/runtime)
- [Microsoft.Windows.SDK.BuildTools](https://www.nuget.org/packages/Microsoft.Windows.SDK.BuildTools)
- [Microsoft.WindowsAppSDK](https://github.com/microsoft/WindowsAppSDK)
- [Microsoft.Xaml.Behaviors.WinUI.Managed](https://github.com/microsoft/XamlBehaviors)
- [Newtonsoft.Json](https://github.com/JamesNK/Newtonsoft.Json)
- [SQLitePCLRaw](https://github.com/ericsink/SQLitePCL.raw)
- [SQLCipher](https://github.com/sqlcipher/sqlcipher)
- [System.Drawing.Common](https://github.com/dotnet/runtime)
- [System.Security.Cryptography.ProtectedData](https://github.com/dotnet/runtime)
- [Velopack](https://github.com/velopack/velopack)
- [WinUIEx](https://github.com/dotmorten/WinUIEx)
- [ZXing.Net](https://github.com/micjahn/ZXing.Net)
- [sqlite-net-pcl](https://github.com/praeclarum/sqlite-net)

各ライブラリのバージョン・ライセンス全文は、アプリ内の設定画面(About)、または
[Preferences/Utils/OpenSourceLicenses.cs](Preferences/Utils/OpenSourceLicenses.cs) を参照してください。

## ライセンス

[MIT License](LICENSE)
