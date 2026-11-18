# CutADash

Windows向けクリップボードマネージャー(WinUI 3 / .NET 10)。履歴・お気に入り・絵文字・シーケンシャルペースト・テキスト選択ツールバーを持ち、タスクトレイに常駐する。リポジトリ名は `CtrlVPlus` だが、製品名・名前空間は `CutADash`(旧名称 `CtrlV+` のデータ移行は `Migration` が担当)。

## ビルド

```bash
dotnet build CutADash.sln -c Debug -p:Platform=x64
```

- 構成は `Debug` / `DevDebug` / `Release`、プラットフォームは `x64` / `ARM64`。
- `DevDebug` では `DEVDEBUG` が定義される(例: `App.xaml.cs` の「他アプリがアクティブになったらパレットを閉じる」処理が無効になる)。
- テストプロジェクトは無い。変更後はビルドが通ることを確認し、UIや貼り付けに関わる変更は実際に起動して確認する。
- バージョンは `Directory.Build.props` の `Version` だけを更新する。リリース手順は `scripts/publish-and-show.ps1` を参照。

## プロジェクト構成

プロジェクト名には番号プレフィックスが付く(`020_CutADash.csproj` など)。フォルダ名は番号なし。

| フォルダ | 役割 |
|---|---|
| `CutADash` | アプリ本体(`App.xaml.cs`、`Views`、`ViewModels`、`Services`、`Utils`、`Infra/Win32`) |
| `Common` | 共通処理・モデル・Win32ラッパー(`HotKeyMonitor`、`WindowMessageDispatcher` など) |
| `WinAPI` | P/Invoke宣言(`WinUser.cs` など) |
| `Repositories` | SQLite(SQLCipher暗号化)の永続化。履歴検索はFTS5テーブルを併用 |
| `Preferences` | 設定画面と `PreferencesGateway`(設定の読み書きの入口) |
| `Migration` / `MigrationCli` | 旧データ・旧スキーマからの移行 |
| `SelectionToolbar` | テキスト選択ツールバー(UI Automation + マウスフック) |
| `SequentialPaste` | シーケンシャルペースト |
| `UiLibrary` / `Theming` | 共通UIコントロール / テーマ(猫テーマなど) |
| `QrDecoding` | QRコードのデコード |

## コードの書き方

- コメントと識別子まわりの説明は**日本語**。周囲のコメント密度に合わせ、「何をするか」より「なぜそうしているか(過去に踏んだ不具合を含む)」を書く。
- **publicなメソッドには、必ずdocコメント(`/// <summary>`)を付ける。** 新規追加時はもちろん、既存のpublicメソッドを触るときにdocコメントが無ければ併せて追加する。引数や戻り値に意味の読み取りにくいもの(単位、null/0の扱いなど)があれば `<param>` / `<returns>` も書く。
- 既存コメントは、挙動の理由を記録していることが多い。コードを動かすときはコメントも一緒に移し、勝手に削らない。
- UIスレッド・Win32フォーカス・フォアグラウンド制御まわりは、タイミングに依存する不具合の歴史が長い。リファクタリングでは処理順序を変えない。

## 注意点

- `MainWindow.Activate()` は `new` による隠蔽(`Window.Activate()` は virtual ではない)。`Window` 型の変数越しに呼ぶと、ペースト先の記憶(`PasteTarget.Remember`)が働かない。必ず `MainWindow` 型で呼ぶ。
- ペースト先の記憶は `MainWindow.PasteTarget`(`CutADash/Utils/PasteTargetTracker.cs`)が持つ。ペースト中は `PasteTarget.IsPasting` を立てて、パレットの自動クローズを抑止する。
- 履歴の本文を更新する処理は、実テーブルだけでなくFTS5の索引も更新すること(`Repositories/ClipboardRepository.cs`)。
- 既知のレビュー指摘と未対応項目は `改善点.md` にまとめてある。

## Git

- 開発ブランチは `develop`、メインは `master`。
- コミットメッセージは日本語で、変更内容を一文で書く(例: 「アップデートの確認を、メニューを押した時だけにする」)。
- コミットメッセージやPR説明に、`Co-Authored-By: Claude ...` や「Generated with Claude Code」などのClaude帰属行は**付けない**。
