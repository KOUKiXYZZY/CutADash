namespace SequentialPaste
{
    /// <summary>
    /// シーケンシャルペーストウィンドウの起動/表示切替を管理する、外部(CutADash)向けの
    /// 公開窓口。SelectionToolbarServiceと同じ形。トレイメニューからToggle()を呼ぶ想定。
    /// </summary>
    public sealed class SequentialPasteService
    {
        private readonly SequentialPasteQueueViewModel _viewModel;
        private SequentialPasteWindow? _window;

        public SequentialPasteService(SequentialPasteQueueViewModel viewModel)
        {
            _viewModel = viewModel;
        }

        public bool IsVisible => _window?.IsVisible == true;

        public void Toggle()
        {
            if (IsVisible)
                Hide();
            else
                Show();
        }

        public void Show()
        {
            // 遅延生成し、以降は使い回す(閉じるたびに新しいウィンドウを作らない)
            _window ??= new SequentialPasteWindow(_viewModel);
            _window.ShowNoActivate();
        }

        public void Hide()
        {
            _window?.HideNoActivate();
        }
    }
}
