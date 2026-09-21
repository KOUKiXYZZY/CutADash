namespace CutADash.Messages
{
    // タスクトレイ(TaskTrayViewModel)がWindowを直接知らずに「画面を開いてほしい」意思だけを
    // 送るためのメッセージ。実際にWindowを生成/表示するのはWindowService側の役目
    // (WeakReferenceMessenger経由でViewModel⇔Window管理を疎結合にする)。

    /// <summary>メインウィンドウを開いてほしい。</summary>
    public sealed class OpenMainWindowMessage
    {
    }

    /// <summary>設定画面を開いてほしい。</summary>
    public sealed class OpenSettingsMessage
    {
    }

    /// <summary>シーケンシャルペーストウィンドウを開閉してほしい。</summary>
    public sealed class OpenSequentialPasteMessage
    {
    }
}
