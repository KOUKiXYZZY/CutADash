namespace SelectionToolbar
{
    /// <summary>
    /// テキスト選択ツールバー機能の起動/停止を管理する、外部(CutADash)向けの公開窓口。
    /// PreferenceWindowでのON/OFF切り替えに合わせて、CutADash側からStart/Stopを呼ぶ想定。
    /// </summary>
    public sealed class SelectionToolbarService
    {
        private readonly SelectionWatcher _watcher = new();
        private SelectionToolbarWindow? _selectionToolbarWindow;

        public bool IsRunning => _watcher.IsRunning;

        public void Start()
        {
            if (IsRunning)
                return;

            _watcher.SelectionFound += OnSelectionFound;
            _watcher.Start();
        }

        public void Stop()
        {
            if (!IsRunning)
                return;

            _watcher.Stop();
            _watcher.SelectionFound -= OnSelectionFound;

            _selectionToolbarWindow?.HideNoActivate();
        }

        private void OnSelectionFound(object? sender, SelectionFoundEventArgs e)
        {
            // 遅延生成し、以降は使い回す(選択のたびに新しいウィンドウを作らない)
            _selectionToolbarWindow ??= new SelectionToolbarWindow();
            _selectionToolbarWindow.ShowNear(e.ScreenX, e.ScreenY, e.Text);
        }
    }
}
