using System.Drawing;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace HappyDDZ.Assistant;

internal sealed class MainForm : Form
{
    private const int ToggleHotkeyId = 0x4844;
    private const int WmHotkey = 0x0312;
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);

    private readonly AppSettings _settings = AppSettings.Load();
    private readonly ReadOnlyScanner _scanner = new();
    private readonly TextBox _connection = ReadOnlyValue("连接状态");
    private readonly TextBox _scene = ReadOnlyValue("当前场景");
    private readonly TextBox _detail = ReadOnlyValue("识别说明");
    private readonly ListBox _ocrLines = new() { Dock = DockStyle.Fill, AccessibleName = "当前画面公开文字" };
    private readonly TextBox _filter = new() { AccessibleName = "筛选识别文字", Width = 220, PlaceholderText = "输入文字筛选列表" };
    private readonly Button _clearFilter = new() { Text = "清除筛选", AccessibleName = "清除筛选", AutoSize = true };
    private readonly ListBox _hand = new() { Dock = DockStyle.Fill, AccessibleName = "手牌" };
    private readonly TextBox _handStatus = ReadOnlyValue("手牌识别与操作提示");
    private readonly Button _scanHand = new() { Text = "快速刷新手牌", AccessibleName = "快速刷新手牌", AutoSize = true };
    private readonly CheckBox _liveHandKeys = new() { Text = "上下键操作游戏单张（实验）", AccessibleName = "上下键操作游戏单张（实验）", AutoSize = true };
    private readonly Button _toggleGameCard = new() { Text = "在游戏拿起或放下当前一张（实验）", AccessibleName = "在游戏拿起或放下当前一张（实验）", AutoSize = true };
    private readonly Button _takeGameGroup = new() { Text = "在游戏拿起当前点数整组（实验）", AccessibleName = "在游戏拿起当前点数整组（实验）", AutoSize = true };
    private readonly Button _dropGameCards = new() { Text = "在游戏放下全部已抬起牌（实验）", AccessibleName = "在游戏放下全部已抬起牌（实验）", AutoSize = true };
    private readonly Button _cancelGroup = new() { Text = "停止整组操作（Escape）", AccessibleName = "停止整组操作", AutoSize = true, Enabled = false };
    private readonly Button _pass = new() { Text = "过牌（Ctrl+回车）", AccessibleName = "过牌", AutoSize = true, Enabled = false };
    private readonly Button _play = new() { Text = "出牌（回车）", AccessibleName = "出牌", AutoSize = true, Enabled = false };
    private readonly HandSelectionModel _handSelection = new();
    private readonly HashSet<Keys> _heldHandKeys = [];
    private readonly ListBox _events = new() { Dock = DockStyle.Fill, AccessibleName = "事件记录" };
    private readonly Button _connect = new() { Text = "连接游戏", AccessibleName = "连接游戏", AutoSize = true };
    private readonly Button _scan = new() { Text = "重新扫描", AccessibleName = "重新扫描", AutoSize = true };
    private readonly Button _activate = new() { Text = "点击所选文字", AccessibleName = "点击所选文字", AutoSize = true, Enabled = false };
    private readonly Button _confirmRoom = new() { Text = "确认进入场次", AccessibleName = "确认进入场次", AutoSize = true, Visible = false };
    private readonly Button _cancelRoom = new() { Text = "取消进入场次(&N)", AccessibleName = "取消进入场次，Alt+N 或 Escape", AutoSize = true, Visible = false };
    private readonly ComboBox _source = new() { DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "目标窗口", Width = 150 };
    private readonly ComboBox _backend = new() { DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "OCR 后端", Width = 180 };
    private readonly CheckBox _keepScreenshots = new() { Text = "保留诊断截图", AccessibleName = "保留诊断截图", AutoSize = true };
    private readonly CheckBox _autoQqLogin = new() { Text = "启动时使用已登录 QQ 自动登录大厅", AccessibleName = "启动时使用已登录 QQ 自动登录大厅", AutoSize = true };
    private readonly CheckBox _speakResults = new() { Text = "使用争渡自动朗读操作结果", AccessibleName = "使用争渡自动朗读操作结果", AutoSize = true };
    private readonly Button _testSpeech = new() { Text = "测试争渡朗读", AccessibleName = "测试争渡朗读", AutoSize = true };
    private readonly Button _repeatResult = new() { Text = "重读操作结果（F6）", AccessibleName = "重读操作结果", AutoSize = true };
    private readonly TextBox _speechStatus = ReadOnlyValue("争渡朗读状态");
    private readonly ZdsrSpeech _speech = new();
    private readonly System.Windows.Forms.Timer _speechTimer = new() { Interval = 200 };
    private readonly System.Windows.Forms.Timer _playModeTimer = new() { Interval = 1800 };
    private readonly GlobalKeyboardHook _globalKeyboard = new();
    private string? _pendingAnnouncement;
    private string? _lastAnnouncement;
    private bool _speechReady;
    private readonly Button _saveSettings = new() { Text = "保存设置", AccessibleName = "保存设置", AutoSize = true };
    private readonly Button _resetSettings = new() { Text = "恢复默认设置", AccessibleName = "恢复默认设置", AutoSize = true };
    private readonly Button _clearScreenshots = new() { Text = "清除诊断截图", AccessibleName = "清除诊断截图", AutoSize = true };
    private readonly TextBox _settingsStatus = ReadOnlyValue("设置状态");
    private readonly Button _hideHall = new() { Text = "隐藏 QQ 大厅窗口", AccessibleName = "隐藏 QQ 大厅窗口", AutoSize = true };
    private readonly Button _showHall = new() { Text = "显示 QQ 大厅窗口", AccessibleName = "显示 QQ 大厅窗口", AutoSize = true };
    private readonly TextBox _hallStatus = ReadOnlyValue("QQ 大厅窗口状态");
    private readonly Button _hideAssistant = new() { Text = "隐藏助手面板（Ctrl+Alt+D 唤回）", AccessibleName = "隐藏助手面板", AutoSize = true };
    private readonly TextBox _hotkeyStatus = ReadOnlyValue("助手面板快捷键状态");
    private GameWindow? _game;
    private ScanResult? _lastScan;
    private IReadOnlyList<OcrEntry> _allEntries = [];
    private bool _busy;
    private bool _closeRequested;
    private HandClickPlan? _raisedPlan;
    private readonly SortedSet<int> _raisedPositions = [];
    private DateTimeOffset _raisedAt;
    private OcrEntry? _pendingRoomEntry;
    private bool _hotkeyRegistered;
    private bool _groupActionActive;
    private bool _groupCancelRequested;
    private bool _autoLoginAttempted;
    private bool _playModeActive;
    private bool _playModeScanPending;
    private string? _lastPlayStateAnnouncement;
    private bool _suppressPlayModeScanSpeech;

    public MainForm()
    {
        Text = "欢乐斗地主无障碍助手 - 受限键盘导航原型";
        AccessibleName = Text;
        MinimumSize = new Size(680, 520);
        Size = new Size(850, 650);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 10);
        _handStatus.Multiline = true;

        var tabs = new TabControl { Dock = DockStyle.Fill, AccessibleName = "主功能选项卡" };
        tabs.TabPages.Add(MakeOverview());
        tabs.TabPages.Add(MakeHand());
        tabs.TabPages.Add(MakeEvents());
        tabs.TabPages.Add(MakeSettings());
        tabs.TabPages.Add(MakeHelp());
        Controls.Add(tabs);

        _connect.Click += (_, _) => Connect();
        _scan.Click += async (_, _) => await ScanAsync();
        _scanHand.Click += async (_, _) => await ScanHandAsync();
        _toggleGameCard.Click += async (_, _) => await ToggleGameCardAsync();
        _takeGameGroup.Click += async (_, _) => await ChangeGameGroupAsync(false);
        _dropGameCards.Click += async (_, _) => await ChangeGameGroupAsync(true);
        _cancelGroup.Click += (_, _) => CancelGroupAction();
        _testSpeech.Click += (_, _) => SpeakResult("欢乐斗地主助手语音测试。现在可使用键盘浏览，操作结束会播报结果。");
        _repeatResult.Click += (_, _) => SpeakResult(_lastAnnouncement ?? _detail.Text);
        _handStatus.TextChanged += (_, _) => QueueAnnouncement(_handStatus.Text);
        _detail.TextChanged += (_, _) => QueueAnnouncement(_detail.Text);
        _speechTimer.Tick += (_, _) =>
        {
            if (_busy) return;
            _speechTimer.Stop();
            if (_pendingAnnouncement is { } value)
            {
                _pendingAnnouncement = null;
                _lastAnnouncement = value;
                if (_speakResults.Checked) SpeakResult(value);
            }
        };
        _globalKeyboard.KeyDown = HandleGlobalKeyboard;
        _playModeTimer.Tick += async (_, _) => await PollPlayModeAsync();
        _pass.Click += async (_, _) => await SubmitPassAsync();
        _play.Click += async (_, _) => await SubmitPlayAsync();
        _activate.Click += async (_, _) => await ActivateSelectedAsync();
        _confirmRoom.Click += async (_, _) => await ActivateSelectedAsync(confirmRoom: true);
        _cancelRoom.Click += (_, _) => CancelRoomConfirmation();
        _hideHall.Click += (_, _) => SetHallVisibility(false);
        _showHall.Click += (_, _) => SetHallVisibility(true);
        _hideAssistant.Click += (_, _) => { if (_hotkeyRegistered) Hide(); };
        _hideHall.AccessibleDescription = "斗地主运行时隐藏 QQ 大厅的两个窗口，使它们不出现在 Alt 加 Tab 中；大厅进程继续运行。";
        _showHall.AccessibleDescription = "重新显示已隐藏的 QQ 大厅窗口；不会关闭斗地主。";
        _hideAssistant.AccessibleDescription = "助手从任务切换列表隐藏；按 Control 加 Alt 加 D 可显示，再按一次可隐藏。斗地主窗口仍在。";
        _source.Items.AddRange(["自动选择", "QQ 游戏大厅", "欢乐斗地主", "QQ 登录窗口"]);
        _source.SelectedIndex = 0;
        _source.SelectedIndexChanged += (_, _) => Connect();
        _ocrLines.SelectedIndexChanged += (_, _) =>
        {
            if (_pendingRoomEntry is not null &&
                !ReferenceEquals(_ocrLines.SelectedItem, _pendingRoomEntry))
                ClearRoomConfirmation();
            _activate.Enabled = !_busy && _pendingRoomEntry is null &&
                _ocrLines.SelectedItem is OcrEntry { CanClick: true };
        };
        _filter.TextChanged += (_, _) => ApplyFilter();
        _clearFilter.Click += (_, _) => { _filter.Clear(); _filter.Focus(); };
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (_pendingRoomEntry is not null && e.KeyCode == Keys.Escape)
            {
                CancelRoomConfirmation();
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }
            if (!e.Control || e.KeyCode != Keys.F) return;
            _filter.Focus();
            _filter.SelectAll();
            e.Handled = true;
            e.SuppressKeyPress = true;
        };
        _ocrLines.KeyDown += async (_, e) =>
        {
            if (e.KeyCode is Keys.Left or Keys.Right)
            {
                if (_ocrLines.Items.Count > 0)
                    _ocrLines.SelectedIndex = Math.Clamp(_ocrLines.SelectedIndex +
                        (e.KeyCode == Keys.Right ? 1 : -1), 0, _ocrLines.Items.Count - 1);
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }
            if (e.KeyCode != Keys.Enter) return;
            e.Handled = true;
            e.SuppressKeyPress = true;
            await ActivateSelectedAsync();
        };
        _ocrLines.AccessibleDescription = "左、右或上、下方向键浏览识别文字和中心坐标；可点击项按回车。点击前会重新识别并核对目标窗口。";
        _hand.KeyDown += async (_, e) => await HandleHandKeyAsync(e);
        _hand.KeyUp += (_, e) => _heldHandKeys.Remove(e.KeyCode);
        _hand.Leave += (_, _) => _heldHandKeys.Clear();
        _hand.AccessibleDescription = "左右按点数组浏览。默认上下键只在助手里预览拿放；勾选实验模式后，上键在游戏拿起一张、下键放下一张。本人出牌按钮、钟面和已抬牌确认时按回车出牌；本人不出按钮与钟面确认时，Control 加回车过牌。";
        _liveHandKeys.AccessibleDescription = "默认关闭。开启后，上下键逐张拿放；Control 加上键拿起当前点数整组，Control 加下键放下全部已抬起牌。每一张都单独核对画面。";
        _liveHandKeys.CheckedChanged += (_, _) =>
        {
            _handSelection.Load(_handSelection.Cards);
            RefreshHandItems();
            _handStatus.Text = _liveHandKeys.Checked
                ? "上下键操作游戏内单张；Ctrl 加上拿起当前点数整组，Ctrl 加下放下全部已抬起牌；每张都要单独核对。"
                : "上下键已切换回本地拿放预览；不会点击游戏手牌。";
        };
        _activate.AccessibleDescription = "仅对当前列表中标为可点击的文字生效；点击前重新识别，点击后重新读取。";
        _saveSettings.Click += (_, _) => SaveSettings();
        _resetSettings.Click += (_, _) => ResetSettings();
        _clearScreenshots.Click += (_, _) => ClearScreenshots();
        _backend.Items.AddRange(["CPU（本机较快）", "DirectML（本机较慢）"]);
        _backend.SelectedIndex = _settings.OcrBackend == "dml" ? 1 : 0;
        _keepScreenshots.Checked = _settings.KeepScreenshots;
        _autoQqLogin.Checked = _settings.AutoLoginWithQq;
        _speakResults.Checked = _settings.SpeakActionResults;
        _speechStatus.Text = "操作结果会通过本机争渡朗读；F6 可重读。";
        _settingsStatus.Text = "设置未修改";
        _keepScreenshots.AccessibleDescription = "默认关闭。开启后，游戏画面图片会保存在项目 captures 文件夹，可能包含头像和账户信息。";
        _autoQqLogin.AccessibleDescription = "默认开启。电脑已登录 QQ 时，助手启动 QQ 游戏大厅并只对双帧核验的官方账号头像尝试一次快捷登录；失败不重复，不输入密码。";
        _clearScreenshots.AccessibleDescription = "清除本项目 captures 文件夹内由本程序保存的所有诊断截图。";

        _hand.Items.Add("手牌候选尚未扫描；游戏内单张拿放与出牌暂不可用。 ");
        _handStatus.Text = "先完整扫描牌桌，再快速刷新手牌。方向键拿放只作本地预览；实验按钮可核对游戏内单张拿放。";
        _connection.Text = "未连接";
        _scene.Text = "尚未扫描";
        _pass.Enabled = false;
        _play.Enabled = false;
        _detail.Text = "可浏览 OCR 文字和位置；已验证的导航按钮可由用户按回车单次点击。游戏内单张拿放为实验功能，出牌禁用。 ";
        Shown += (_, _) =>
        {
            Connect();
            if (_hotkeyRegistered)
            {
                FormBorderStyle = FormBorderStyle.SizableToolWindow;
                ShowInTaskbar = false;
                _hotkeyStatus.Text = "Ctrl+Alt+D 显示或隐藏助手；Alt+Tab 只保留斗地主游戏窗口。";
            }
            else
            {
                _hideAssistant.Enabled = false;
                _hotkeyStatus.Text = "Ctrl+Alt+D 未能注册，助手保留在任务切换列表。";
            }
            if (_settings.LoadWarning is not null)
            {
                AddEvent(_settings.LoadWarning);
                _detail.Text = _settings.LoadWarning;
                _settingsStatus.Text = _settings.LoadWarning;
            }
        };
        Shown += async (_, _) => { _speechReady = true; await TryAutoQqLoginAsync(); };
        FormClosing += (_, e) =>
        {
            if (!_busy && !_groupActionActive) return;
            e.Cancel = true;
            _closeRequested = true;
            if (_groupActionActive) CancelGroupAction();
            _detail.Text = "正在完成当前识别，结束后退出。 ";
        };
        FormClosed += (_, _) =>
        {
            _playModeTimer.Stop();
            _globalKeyboard.Dispose();
            _speechTimer.Dispose();
            _playModeTimer.Dispose();
            _speech.Dispose();
            _scanner.Dispose();
        };
    }

    private static TextBox ReadOnlyValue(string name) => new()
    {
        AccessibleName = name, ReadOnly = true, Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.FixedSingle, TabStop = true
    };

    private static Label Label(string text) => new()
    {
        Text = text, AutoSize = true, Anchor = AnchorStyles.Left,
        Margin = new Padding(3, 7, 3, 3)
    };

    private TabPage MakeOverview()
    {
        var tab = new TabPage("当前局面") { AccessibleName = "当前局面" };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 9 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var height in new[] { 27, 35, 27, 35, 27, 55, 45, 45 })
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(Label("连接状态"), 0, 0);
        layout.Controls.Add(_connection, 0, 1);
        layout.Controls.Add(Label("当前场景"), 0, 2);
        layout.Controls.Add(_scene, 0, 3);
        layout.Controls.Add(Label("识别说明"), 0, 4);
        _detail.Multiline = true;
        layout.Controls.Add(_detail, 0, 5);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        buttons.Controls.Add(_source);
        buttons.Controls.Add(_connect);
        buttons.Controls.Add(_scan);
        buttons.Controls.Add(_activate);
        buttons.Controls.Add(_confirmRoom);
        buttons.Controls.Add(_cancelRoom);
        layout.Controls.Add(buttons, 0, 6);
        var filterRow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        filterRow.Controls.Add(Label("公开文字与坐标；左右或上下方向键浏览，回车点击"));
        filterRow.Controls.Add(_filter);
        filterRow.Controls.Add(_clearFilter);
        layout.Controls.Add(filterRow, 0, 7);
        layout.Controls.Add(_ocrLines, 0, 8);
        tab.Controls.Add(layout);
        return tab;
    }

    private TabPage MakeHand()
    {
        var tab = new TabPage("手牌") { AccessibleName = "手牌" };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), RowCount = 4, ColumnCount = 1 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill };
        toolbar.Controls.Add(_scanHand);
        toolbar.Controls.Add(Label("左右浏览点数；实验模式上下逐张，Ctrl 加上下整组拿放"));
        layout.Controls.Add(toolbar, 0, 0);
        layout.Controls.Add(_handStatus, 0, 1);
        layout.Controls.Add(_hand, 0, 2);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true };
        void AddPreviewButton(string label, Keys key, bool control = false)
        {
            var button = new Button { Text = label, AccessibleName = label, AutoSize = true };
            button.Click += (_, _) => RunHandAction(key, control);
            actions.Controls.Add(button);
        }
        AddPreviewButton("上一组", Keys.Left);
        AddPreviewButton("下一组", Keys.Right);
        AddPreviewButton("拿起一张（预览）", Keys.Up);
        AddPreviewButton("拿起整组（预览）", Keys.Up, true);
        AddPreviewButton("放下一张（预览）", Keys.Down);
        AddPreviewButton("全部放下（预览）", Keys.Down, true);
        actions.Controls.Add(_liveHandKeys);
        _toggleGameCard.AccessibleDescription = "实验性单张游戏点击。每次点击前后重新采集并确认整手牌与抬起位置；不会出牌或过牌。";
        actions.Controls.Add(_toggleGameCard);
        _takeGameGroup.AccessibleDescription = "按当前点数逐张拿起尚未抬起的牌，每张点击前后均核对；失败即停止。";
        _dropGameCards.AccessibleDescription = "逐张放下画面确认已抬起的牌，每张点击前后均核对；失败即停止。";
        actions.Controls.Add(_takeGameGroup);
        actions.Controls.Add(_dropGameCards);
        actions.Controls.Add(_cancelGroup);
        actions.Controls.Add(_repeatResult);
        _cancelGroup.AccessibleDescription = "停止后续点击；已经发送的单次点击会先完成读回。已抬起的牌保留，可重新扫描后放下。";
        _pass.AccessibleDescription = "仅在本人不出按钮与倒计时双帧确认后开放。由用户决定，点击前再次复核；点击后读取反馈，不自动重试。";
        actions.Controls.Add(_pass);
        _play.AccessibleDescription = "用户选好游戏中已稳定抬起的牌后，仅本人出牌按钮、钟面、手牌与位置通过复核才会单次提交；不自动选牌或重试。";
        actions.Controls.Add(_play);
        layout.Controls.Add(actions, 0, 3);
        tab.Controls.Add(layout);
        return tab;
    }

    private TabPage MakeEvents()
    {
        var tab = new TabPage("事件记录") { AccessibleName = "事件记录" };
        _events.Items.Add("事件记录已就绪。扫描结果可在这里回看。 ");
        tab.Controls.Add(_events);
        return tab;
    }

    private TabPage MakeSettings()
    {
        var tab = new TabPage("设置") { AccessibleName = "设置" };
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(15), FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        layout.Controls.Add(Label("OCR 计算后端"));
        layout.Controls.Add(_backend);
        layout.Controls.Add(_keepScreenshots);
        layout.Controls.Add(_autoQqLogin);
        layout.Controls.Add(_speakResults);
        layout.Controls.Add(_testSpeech);
        _speechStatus.Width = 600;
        layout.Controls.Add(_speechStatus);
        layout.Controls.Add(_saveSettings);
        layout.Controls.Add(_resetSettings);
        _settingsStatus.Width = 600;
        layout.Controls.Add(_settingsStatus);
        layout.Controls.Add(_clearScreenshots);
        layout.Controls.Add(Label("QQ 大厅窗口：隐藏后进程继续运行，游戏窗口保留。"));
        layout.Controls.Add(_hideHall);
        layout.Controls.Add(_showHall);
        _hallStatus.Width = 600;
        layout.Controls.Add(_hallStatus);
        layout.Controls.Add(_hideAssistant);
        _hotkeyStatus.Width = 600;
        layout.Controls.Add(_hotkeyStatus);
        layout.Controls.Add(Label("设置保存在项目 config 文件夹；截图默认不留存。"));
        tab.Controls.Add(layout);
        return tab;
    }

    private static TabPage MakeHelp()
    {
        var tab = new TabPage("帮助") { AccessibleName = "帮助" };
        var help = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            AccessibleName = "键盘帮助和当前限制",
            Text = "当前版本支持少数已验证导航按钮的键盘点击。\r\n" +
                "所有助手功能可用键盘操作；用户不需移动或点击鼠标。选中已核对的游戏目标按回车后，助手才会在内部发送一次游戏点击。\r\n" +
                "Tab 和 Shift+Tab 在控件间移动；Ctrl+Tab 切换选项卡；方向键浏览文字与事件；Enter 或 Space 激活按钮。\r\n" +
                "在当前局面选项卡选择 QQ 游戏大厅、欢乐斗地主或 QQ 登录窗口，按连接游戏和重新扫描。Ctrl+F 可按文字筛选；左右或上下方向键浏览文字及中心坐标；可点击项按回车。\r\n" +
                "选择场次后默认焦点在取消；可按 Escape 取消，或移动到确认按钮按回车。Alt+Y 确认、Alt+N 取消是快捷键。场次等候页和结算页可在文字列表选择“退出当前场次”并按回车。\r\n" +
                "点击前重新扫描，目标或窗口变化时拒绝；点击后回读。手牌页先完整扫描，再可在一分钟内快速刷新两帧手牌，跳过整屏 OCR。候选牌仍可能未知或错误。\r\n" +
                "聚焦手牌列表后，左右键按点数浏览，Home/End 到首尾。上下键默认只在本地预览；勾选实验模式后可在游戏中逐张拿放，Ctrl+上拿当前点数整组，Ctrl+下逐张放下所有已抬牌。\r\n" +
                "仅当本人按钮、钟面、手牌和选中状态通过复核时，回车提交出牌，Ctrl+回车提交过牌；用户自己决定是否提交。任何不确定结果都停止，不自动重试。\r\n" +
                "本人叫地主、不叫、抢地主和不抢可在双帧确认后由用户选择。多张选牌及出牌仍在现场验证，不能据此宣称可以独立完成整局。\r\n" +
                "设置页可隐藏或显示 QQ 大厅窗口；隐藏后大厅进程保留，斗地主窗口继续运行。\r\n" +
                "Ctrl+Alt+D 可显示或隐藏助手面板；助手不在 Alt+Tab 中，斗地主是唯一游戏窗口。\r\n" +
                "游戏画面由按窗口捕获组件取得，OCR 仅处理用户可见内容。不读取游戏内存、网络或隐藏牌。\r\n" +
                "启动时默认使用电脑上已登录的 QQ 对官方账号头像尝试一次快捷登录；失败不重试。QQ 登录窗口仅对双帧核验的非敏感按钮开放单次点击；密码和验证码在官方窗口输入。争渡真人读屏和独立完成整局均未验证。"
        };
        tab.Controls.Add(help);
        return tab;
    }

    private void Connect()
    {
        if (_busy || _groupActionActive)
        {
            _detail.Text = "正在扫描，结束后可以重新连接。 ";
            return;
        }
        ClearRoomConfirmation();
        var gameAlreadyRunning = GameWindow.Find(WindowKind.Game) is not null;
        if (SelectedWindowKind == WindowKind.Hall ||
            SelectedWindowKind == WindowKind.Auto && !gameAlreadyRunning &&
            GameWindow.Find(WindowKind.Login) is null)
        {
            try { _hallStatus.Text = HallWindowVisibility.SetVisible(true); }
            catch { /* The hall may not be running yet; normal connection feedback follows. */ }
        }
        _game = GameWindow.Find(SelectedWindowKind);
        _lastScan = null;
        _allEntries = [];
        _ocrLines.Items.Clear();
        _hand.Items.Clear();
        _hand.Items.Add("目标窗口已切换；请重新扫描手牌候选。牌局动作保持禁用。");
        _handSelection.Load([]);
        _handStatus.Text = "目标窗口已切换，请完整扫描。";
        _filter.Clear();
        _activate.Enabled = false;
        if (_game is null)
        {
            _connection.Text = "未找到选定的游戏窗口";
            _scene.Text = "未连接";
            _detail.Text = "请确认 QQ 游戏大厅、QQ 登录窗口或欢乐斗地主正在运行，然后重试。 ";
            AddEvent("未找到游戏窗口");
            return;
        }
        if (_game.Kind == WindowKind.Game)
        {
            try { _hallStatus.Text = HallWindowVisibility.SetVisible(false); }
            catch (Exception ex) { _hallStatus.Text = $"QQ 大厅窗口未能自动隐藏：{ex.Message}"; }
        }
        var targetName = _game.Kind switch
        {
            WindowKind.Hall => "QQ 游戏大厅",
            WindowKind.Login => "QQ 登录窗口",
            _ => "欢乐斗地主"
        };
        _connection.Text = $"已连接：{targetName}，{_game.Title}";
        _scene.Text = "尚未扫描";
        _pass.Enabled = false;
        _play.Enabled = false;
        _detail.Text = "可以重新扫描。游戏版本或布局变化时，点击会停止。 ";
        AddEvent($"已连接游戏进程 {_game.ProcessId}");
    }

    private async Task<ScanResult> ScanLoginWithStartupRetryAsync(
        GameWindow login, AppSettings settings, string phase)
    {
        const int maxAttempts = 12;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await _scanner.ScanAsync(login, settings, CancellationToken.None);
            }
            catch (Exception ex)
            {
                if (attempt == maxAttempts)
                    throw new InvalidOperationException($"{phase}捕获连续失败；没有发送点击。", ex);
                AddEvent($"{phase}捕获暂时不可用，准备第 {attempt + 1} 次只读检查");
                await Task.Delay(1000);
                var current = GameWindow.Find(WindowKind.Login);
                if (current is null || current.ProcessId != login.ProcessId || current.Handle != login.Handle)
                    throw new InvalidOperationException("QQ 登录窗口在只读检查期间发生变化；没有发送点击。", ex);
            }
        }
        throw new InvalidOperationException($"{phase}捕获连续失败；没有发送点击。");
    }

    private void AttachHallAfterQqLogin(GameWindow hall, string detail)
    {
        _game = hall;
        _lastScan = null;
        _allEntries = [];
        _ocrLines.Items.Clear();
        _connection.Text = "已连接：QQ 游戏大厅，QQ游戏";
        _scene.Text = "尚未扫描";
        _detail.Text = detail;
        AddEvent(detail);
    }

    private async Task<ScanResult> ScanForStartupAsync(
        GameWindow target, AppSettings settings, string phase, int attempts = 8)
    {
        Exception? last = null;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                return await _scanner.ScanAsync(target, settings, CancellationToken.None);
            }
            catch (Exception ex)
            {
                last = ex;
                if (attempt == attempts) break;
                AddEvent($"{phase}暂时无法捕获，准备只读重试第 {attempt + 1} 次");
                await Task.Delay(1000);
                var current = GameWindow.Find(target.Kind);
                if (current is null || current.ProcessId != target.ProcessId || current.Handle != target.Handle)
                    break;
            }
        }
        throw new InvalidOperationException($"{phase}连续捕获失败；没有发送点击。", last);
    }

    private async Task ClickStartupEntryAsync(
        GameWindow hall, ScanResult previous, OcrEntry selected, AppSettings settings)
    {
        var fresh = await ScanForStartupAsync(hall, settings, "QQ 游戏大厅启动检查");
        if (fresh.Scene != previous.Scene ||
            fresh.ClientWidth != previous.ClientWidth ||
            fresh.ClientHeight != previous.ClientHeight)
            throw new InvalidOperationException("QQ 游戏大厅场景或窗口尺寸已变化；没有发送点击。");

        var match = OcrNavigation.Build(fresh)
            .Where(entry => entry.CanClick && entry.Kind == "游戏入口" &&
                entry.Line.Text.StartsWith("欢乐斗地主", StringComparison.Ordinal) &&
                Math.Abs(entry.CenterX - selected.CenterX) <= 30 &&
                Math.Abs(entry.CenterY - selected.CenterY) <= 30)
            .OrderBy(entry => Math.Abs(entry.CenterX - selected.CenterX) +
                Math.Abs(entry.CenterY - selected.CenterY))
            .FirstOrDefault();
        if (match is null)
            throw new InvalidOperationException("未能再次核对欢乐斗地主大厅入口；没有发送点击。");

        await GameInput.ClickOnceAsync(hall, match.CenterX, match.CenterY,
            fresh.ClientWidth, fresh.ClientHeight, Handle,
            async () =>
            {
                var final = await ScanForStartupAsync(hall, settings, "QQ 游戏大厅最终检查", 4);
                var stillVerified = OcrNavigation.Build(final).Any(entry =>
                    entry.CanClick && entry.Kind == "游戏入口" &&
                    entry.Line.Text.StartsWith("欢乐斗地主", StringComparison.Ordinal) &&
                    Math.Abs(entry.CenterX - match.CenterX) <= 30 &&
                    Math.Abs(entry.CenterY - match.CenterY) <= 30);
                if (!stillVerified)
                    throw new InvalidOperationException("欢乐斗地主大厅入口在最终检查时已变化；没有发送点击。");
            });
        AddEvent("已从 QQ 游戏大厅单次启动欢乐斗地主");
    }

    private async Task LaunchGameFromHallAsync(GameWindow hall, AppSettings settings)
    {
        _game = hall;
        _source.SelectedIndex = 0;
        var hallScan = await ScanForStartupAsync(hall, settings, "QQ 游戏大厅启动扫描");
        DisplayScan(hallScan);

        // This first-run guide is a non-consuming acknowledgement. Close it so
        // the verified game entry can be reached without asking the user to use
        // the mouse. Rewards and room-selection entries are never auto-activated.
        if (hallScan.Scene == "大厅引导弹窗")
        {
            var guide = OcrNavigation.Build(hallScan).FirstOrDefault(entry =>
                entry.CanClick && entry.Line.Text.Trim() == "知道啦");
            if (guide is null)
                throw new InvalidOperationException("大厅引导弹窗没有出现经核对的关闭入口；请用键盘阅读当前页面。");
            await ClickStartupEntryAsync(hall, hallScan, guide, settings);
            await Task.Delay(500);
            hallScan = await ScanForStartupAsync(hall, settings, "大厅引导关闭后扫描");
            DisplayScan(hallScan);
        }

        var entry = OcrNavigation.Build(hallScan).FirstOrDefault(item =>
            item.CanClick && item.Kind == "游戏入口" &&
            item.Line.Text.StartsWith("欢乐斗地主", StringComparison.Ordinal));
        if (entry is null)
        {
            _detail.Text = "已连接 QQ 游戏大厅，但没有找到经双帧核对的欢乐斗地主入口；没有发送点击。";
            AddEvent(_detail.Text);
            return;
        }

        _detail.Text = "正在从 QQ 游戏大厅单次启动欢乐斗地主；不会自动进入收费或消耗豆子的场次。";
        await ClickStartupEntryAsync(hall, hallScan, entry, settings);

        GameWindow? launched = null;
        for (var attempt = 0; attempt < 24; attempt++)
        {
            await Task.Delay(500);
            launched = GameWindow.Find(WindowKind.Game);
            if (launched is not null) break;
        }
        if (launched is null)
        {
            _detail.Text = "已启动欢乐斗地主入口，但暂未找到游戏窗口；没有重复点击。请稍后重新扫描。";
            AddEvent(_detail.Text);
            return;
        }

        _game = launched;
        try { _hallStatus.Text = HallWindowVisibility.SetVisible(false); }
        catch (Exception ex) { _hallStatus.Text = $"QQ 游戏大厅窗口未能自动隐藏：{ex.Message}"; }
        var gameScan = await ScanForStartupAsync(launched, settings, "欢乐斗地主启动扫描", 10);
        DisplayScan(gameScan);
        _detail.Text = $"已打开欢乐斗地主游戏界面，当前场景：{gameScan.Scene}。现在可以用 Ctrl+Tab、Tab、方向键和回车继续操作。";
        AddEvent(_detail.Text);
        StartPlayMode(launched, gameScan);
    }

    private void StartPlayMode(GameWindow game, ScanResult scan)
    {
        _game = game;
        _playModeActive = true;
        _lastPlayStateAnnouncement = null;
        try
        {
            _globalKeyboard.Start();
            _playModeTimer.Start();
            AnnouncePlayState(scan, true);
            BeginInvoke(() =>
            {
                if (!IsDisposed && IsHandleCreated)
                {
                    Hide();
                    SetForegroundWindow(game.Handle);
                }
            });
        }
        catch (Exception ex)
        {
            _playModeActive = false;
            _playModeTimer.Stop();
            _detail.Text = $"游戏已打开，但键盘游戏模式未启动：{ex.Message}。助手面板保持可见。";
            AddEvent(_detail.Text);
        }
    }

    private void StopPlayMode(string reason, bool showAssistant)
    {
        _playModeActive = false;
        _playModeScanPending = false;
        _playModeTimer.Stop();
        _globalKeyboard.Stop();
        if (showAssistant && !IsDisposed)
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }
        _detail.Text = reason;
        AddEvent(reason);
    }

    private async Task PollPlayModeAsync()
    {
        if (!_playModeActive || _playModeScanPending || _busy || _groupActionActive || _game is null)
            return;
        var game = GameWindow.Find(WindowKind.Game);
        if (game is null || game.ProcessId != _game.ProcessId || game.Handle != _game.Handle)
        {
            StopPlayMode("斗地主窗口已关闭或发生变化，键盘游戏模式已停止。按 Ctrl+Alt+D 可查看助手状态。", true);
            return;
        }
        _playModeScanPending = true;
        try
        {
            var settings = new AppSettings { OcrBackend = _settings.OcrBackend, KeepScreenshots = false };
            var result = await _scanner.ScanAsync(game, settings, CancellationToken.None);
            _suppressPlayModeScanSpeech = true;
            try { DisplayScan(result); }
            finally { _suppressPlayModeScanSpeech = false; }
            AnnouncePlayState(result, false);
        }
        catch (Exception ex)
        {
            AddEvent($"键盘游戏模式后台扫描暂时失败：{ex.Message}");
        }
        finally { _playModeScanPending = false; }
    }

    private void AnnouncePlayState(ScanResult scan, bool force)
    {
        var handSignature = scan.Hand is null ? "" :
            string.Join("|", scan.Hand.Cards.Select(card => $"{card.Rank}/{card.Suit}"));
        var boardText = scan.Lines
            .Where(line => line.Text.Contains("底牌", StringComparison.Ordinal) ||
                line.Text.Contains("上家", StringComparison.Ordinal) ||
                line.Text.Contains("下家", StringComparison.Ordinal) ||
                line.Text.Contains("轮到", StringComparison.Ordinal) ||
                line.Text.Contains("剩余", StringComparison.Ordinal))
            .Select(line => line.Text.Trim())
            .Where(text => text.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Take(4)
            .ToArray();
        var state = $"{scan.Scene}|{handSignature}|{scan.OwnPlayConfirmed}|{scan.OwnPassConfirmed}|" +
            $"{scan.OwnBidButtonsConfirmed}|{scan.OwnRobButtonsConfirmed}|{scan.OwnTurnSeconds}|" +
            string.Join("/", boardText);
        if (!force && state == _lastPlayStateAnnouncement) return;
        _lastPlayStateAnnouncement = state;

        var message = scan.Scene switch
        {
            "牌桌" => "已进入牌桌。",
            "叫地主阶段" => "当前是叫地主或抢地主阶段。",
            _ => $"当前场景：{scan.Scene}。"
        };
        if (scan.Hand is { ObservedCount: > 0 } hand)
            message += $"当前识别到 {hand.ObservedCount} 张手牌" +
                (hand.Cards.Count == hand.ObservedCount ? "，左右方向键浏览手牌。" : "，部分牌未知，动作会保持安全停止。");
        if (scan.OwnPlayConfirmed)
            message += scan.OwnTurnSeconds is { } playSeconds
                ? $"轮到你出牌，剩余约 {playSeconds} 秒；已选牌后按回车出牌。"
                : "轮到你出牌；已选牌后按回车出牌。";
        else if (scan.OwnPassConfirmed)
            message += scan.OwnTurnSeconds is { } passSeconds
                ? $"轮到你决定不出，剩余约 {passSeconds} 秒；按 Control 加回车过牌。"
                : "轮到你决定不出；按 Control 加回车过牌。";
        if (scan.OwnBidButtonsConfirmed || scan.OwnRobButtonsConfirmed)
            message += "本人叫地主或抢地主按钮已核对，请在助手面板中选择，不由程序代替决定。";
        if (boardText.Length > 0)
            message += $"可见牌桌状态：{string.Join("，", boardText)}。";
        SpeakResult(message);
    }

    private bool HandleGlobalKeyboard(GlobalKeyEvent key)
    {
        if (!_playModeActive || _game is null || GetForegroundWindow() != _game.Handle || key.Alt || key.Shift)
            return false;
        if (key.Key is not (Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Enter or Keys.F2))
            return false;
        if (IsDisposed || !IsHandleCreated) return false;
        BeginInvoke(async () => await HandlePlayModeKeyAsync(key));
        return true;
    }

    private async Task HandlePlayModeKeyAsync(GlobalKeyEvent key)
    {
        if (!_playModeActive || _busy || _groupActionActive) return;
        if (key.Key == Keys.F2)
        {
            if (_lastScan is not null) AnnouncePlayState(_lastScan, true);
            return;
        }
        if (_lastScan is null || _lastScan.Scene is not ("牌桌" or "叫地主阶段") ||
            _handSelection.Cards.Count == 0)
        {
            SpeakResult("当前还没有稳定的实时手牌，方向键和出牌动作暂不接管；请等待牌桌扫描完成。");
            return;
        }
        if (key.Key == Keys.Enter)
        {
            if (key.Control) await SubmitPassAsync();
            else await SubmitPlayAsync();
            return;
        }
        if (key.Key is Keys.Left or Keys.Right)
        {
            RunHandAction(key.Key, false);
            SpeakCurrentCard();
            return;
        }
        if (key.Control)
        {
            await ChangeGameGroupAsync(key.Key == Keys.Down);
            return;
        }
        var current = CurrentHandPosition();
        if (key.Key == Keys.Down && _raisedPositions.Count == 0)
        {
            SpeakResult("当前没有已确认抬起的游戏手牌；下键不能放牌。");
            return;
        }
        if (key.Key == Keys.Up && _raisedPositions.Contains(current))
            current = Enumerable.Range(current + 1, Math.Max(0, _handSelection.Cards.Count - current))
                .Concat(Enumerable.Range(1, current))
                .FirstOrDefault(position => !_raisedPositions.Contains(position));
        if (key.Key == Keys.Down && !_raisedPositions.Contains(current))
            current = _raisedPositions.Min;
        if (current <= 0)
        {
            SpeakResult("没有可执行的游戏手牌位置。");
            return;
        }
        await ToggleGameCardAsync(current);
    }

    private int CurrentHandPosition() => _playModeActive
        ? _handSelection.CurrentIndex + 1
        : _hand.SelectedIndex + 1;

    private void SpeakCurrentCard()
    {
        var index = _handSelection.CurrentIndex;
        if (index < 0 || index >= _handSelection.Cards.Count) return;
        var card = _handSelection.Cards[index];
        SpeakResult($"第 {card.Position} 张，点数 {card.Rank}，花色 {card.Suit}。");
    }

    private async Task TryAutoQqLoginAsync()
    {
        if (_autoLoginAttempted || !_settings.AutoLoginWithQq || _busy)
            return;
        _autoLoginAttempted = true;
        _busy = true;
        _connect.Enabled = false;
        _scan.Enabled = false;
        _activate.Enabled = false;
        _detail.Text = "正在检查电脑上已登录的 QQ，并启动 QQ 游戏大厅；只尝试一次。";
        AddEvent("开始一次 QQ 官方快捷登录检查");
        var settings = new AppSettings
        {
            OcrBackend = _settings.OcrBackend,
            KeepScreenshots = false,
            AutoLoginWithQq = true
        };
        try
        {
            if (GameWindow.Find(WindowKind.Game) is { } runningGame)
            {
                _game = runningGame;
                _source.SelectedIndex = 0;
                var runningScan = await ScanForStartupAsync(runningGame, settings, "已有斗地主窗口扫描");
                DisplayScan(runningScan);
                _detail.Text = $"已连接正在运行的欢乐斗地主，当前场景：{runningScan.Scene}。";
                AddEvent(_detail.Text);
                StartPlayMode(runningGame, runningScan);
                return;
            }

            var login = GameWindow.Find(WindowKind.Login);
            if (login is null)
            {
                if (GameWindow.Find(WindowKind.Hall) is { } existingHall)
                {
                    AttachHallAfterQqLogin(existingHall,
                        "已找到正在运行的 QQ 游戏大厅，准备自动打开欢乐斗地主。");
                    await LaunchGameFromHallAsync(existingHall, settings);
                    return;
                }
                var hallExe = QqHallInstallation.FindLauncher();
                Process.Start(new ProcessStartInfo(hallExe)
                {
                    WorkingDirectory = Path.GetDirectoryName(hallExe)!,
                    UseShellExecute = true
                });
                for (var attempt = 0; attempt < 20; attempt++)
                {
                    await Task.Delay(250);
                    if (GameWindow.Find(WindowKind.Hall) is { } hall)
                    {
                        AttachHallAfterQqLogin(hall,
                            "QQ 游戏大厅已使用电脑上登录的 QQ 进入，准备自动打开欢乐斗地主。");
                        await LaunchGameFromHallAsync(hall, settings);
                        return;
                    }
                    login = GameWindow.Find(WindowKind.Login);
                    if (login is not null) break;
                }
            }
            if (login is null)
                throw new InvalidOperationException("未出现 QQ 游戏大厅或官方登录窗口；没有发送点击。");

            await Task.Delay(2500);
            var stableLogin = GameWindow.Find(WindowKind.Login);
            if (stableLogin is null || stableLogin.ProcessId != login.ProcessId || stableLogin.Handle != login.Handle)
                throw new InvalidOperationException("QQ 登录窗口在启动等待期间发生变化；没有发送点击。");
            var first = await ScanLoginWithStartupRetryAsync(login, settings, "QQ 快捷登录首次");
            if (!first.LoginQqQuickConfirmed)
            {
                _game = login;
                DisplayScan(first);
                _detail.Text = "未确认已登录 QQ 的官方快捷入口；没有发送点击。请阅读当前登录提示。";
                AddEvent(_detail.Text);
                return;
            }

            var sent = false;
            await GameInput.ClickOnceAsync(login, 158, 175, 318, 432, Handle, async () =>
            {
                var final = await ScanLoginWithStartupRetryAsync(login, settings, "QQ 快捷登录最终");
                if (!final.LoginQqQuickConfirmed)
                    throw new InvalidOperationException("QQ 快捷登录入口在最终核对时变化；没有发送点击。");
            }, () =>
            {
                sent = true;
                AddEvent("已向双帧核验的 QQ 官方头像发送一次快捷登录点击");
            });
            if (!sent) throw new InvalidOperationException("QQ 快捷登录点击未发送。");

            GameWindow? connectedHall = null;
            for (var attempt = 0; attempt < 30; attempt++)
            {
                await Task.Delay(300);
                connectedHall = GameWindow.Find(WindowKind.Hall);
                if (connectedHall is not null) break;
            }
            if (connectedHall is null)
                throw new InvalidOperationException("已单次点击 QQ 头像，但未确认大厅登录完成；不会自动重试。");
            AttachHallAfterQqLogin(connectedHall,
                "已通过电脑上登录的 QQ 进入游戏大厅，准备自动打开欢乐斗地主。");
            await LaunchGameFromHallAsync(connectedHall, settings);
        }
        catch (Exception ex)
        {
            _detail.Text = $"QQ 自动登录停止：{ex.Message}";
            AddEvent(_detail.Text);
        }
        finally
        {
            _busy = false;
            _connect.Enabled = true;
            _scan.Enabled = true;
            _activate.Enabled = _ocrLines.SelectedItem is OcrEntry { CanClick: true };
        }
    }

    private async Task ScanAsync()
    {
        if (_busy || _groupActionActive)
        {
            _detail.Text = "扫描正在进行，请等待本次结果。 ";
            return;
        }
        ClearRoomConfirmation();
        ConnectIfNeeded();
        if (_game is null) return;
        _busy = true;
        _activate.Enabled = false;
        _pass.Enabled = false;
        _play.Enabled = false;
        _lastScan = null;
        var scanSettings = new AppSettings
        {
            OcrBackend = _settings.OcrBackend,
            KeepScreenshots = _settings.KeepScreenshots
        };
        _scan.Text = "扫描中";
        _scan.AccessibleName = "正在扫描";
        _scene.Text = "正在识别";
        _detail.Text = "正在捕获窗口并读取可见文字，请稍候。 ";
        _hand.Items.Clear();
        _hand.Items.Add("正在识别，旧手牌候选已清除。");
        _handSelection.Load([]);
        _handStatus.Text = "正在完整扫描，旧手牌候选已清除。";
        AddEvent("开始只读扫描");
        try
        {
            var result = await _scanner.ScanAsync(_game, scanSettings, CancellationToken.None);
            DisplayScan(result);
            AddEvent($"扫描完成：{result.Scene}；文字 {result.Lines.Count} 行");
        }
        catch (Exception ex)
        {
            _lastScan = null;
            _allEntries = [];
            _scene.Text = "识别暂停";
            _detail.Text = ex.Message;
            _ocrLines.Items.Clear();
            _ocrLines.Items.Add("本次扫描失败，不能使用旧结果。 ");
            _hand.Items.Clear();
            _hand.Items.Add("本次扫描失败，不能使用旧手牌候选。");
            _handSelection.Load([]);
            _handStatus.Text = "扫描失败，不能使用旧手牌候选。";
            AddEvent($"扫描失败：{ex.Message}");
        }
        finally
        {
            _busy = false;
            _scan.Text = "重新扫描";
            _scan.AccessibleName = "重新扫描";
            _activate.Enabled = _ocrLines.SelectedItem is OcrEntry { CanClick: true };
            if (_closeRequested) BeginInvoke(Close);
        }
    }

    private void DisplayScan(ScanResult result)
    {
        var savedPlan = _raisedPlan;
        var savedPositions = _raisedPositions.ToArray();
        var savedCurrentIndex = _handSelection.CurrentIndex;
        var retainSelection = savedPlan is not null && result.Hand is not null &&
            result.Scene == "牌桌" && DateTimeOffset.Now - _raisedAt < TimeSpan.FromSeconds(30) &&
            HandClickPlanner.SameRaisedHand(savedPlan, result.Hand, savedPositions);
        _raisedPlan = null;
        _raisedPositions.Clear();
        if (_lastScan?.Scene != result.Scene) _filter.Clear();
        _lastScan = result;
        _pass.Enabled = result.OwnPassConfirmed &&
            result.Hand is { ObservedCount: > 0, RaisedPositions.Count: 0 };
        _play.Enabled = false;
        _scene.Text = result.Scene;
        _detail.Text = $"{result.Detail} 截图时间 {result.CapturedAt:HH:mm:ss}；OCR {result.OcrMs:0} 毫秒。";
        _allEntries = OcrNavigation.Build(result);
        ApplyFilter();
        _hand.Items.Clear();
        if (result.Hand is null)
        {
            _handSelection.Load([]);
            _handStatus.Text = "当前场景没有可用的手牌候选。";
            _hand.Items.Add("当前场景没有可用的手牌候选。牌局动作保持禁用。");
        }
        else
        {
            _handStatus.Text = result.Hand.Summary;
            foreach (var position in result.Hand.RaisedPositions)
                _raisedPositions.Add(position);
            if (retainSelection)
            {
                _raisedPlan = savedPlan;
                _play.Enabled = result.OwnPlayConfirmed;
                _handStatus.Text = "已重新核对游戏中抬起的牌，保留刚才的选牌；" +
                    (result.OwnPlayConfirmed ? "本人出牌按钮已确认，可按回车请求出牌。" : "当前未确认本人可出牌，请继续扫描。");
            }
            var cards = retainSelection ? savedPlan!.Cards : result.Hand.Cards.Count > 0 ? result.Hand.Cards :
                result.Hand.RaisedPositions.Count > 0 &&
                (result.Hand.ObservedCount is >= 1 and <= 17 or 20)
                    ? Enumerable.Range(1, result.Hand.ObservedCount)
                        .Select(position => new HandCard(position, "未知", "未知")).ToArray()
                    : [];
            _handSelection.Load(cards);
            if (_playModeActive && cards.Count > 0)
                _handSelection.SetCurrent(Math.Clamp(savedCurrentIndex, 0, cards.Count - 1));
            RefreshHandItems();
        }
    }

    private void RefreshHandItems()
    {
        _hand.BeginUpdate();
        try
        {
            _hand.Items.Clear();
            for (var i = 0; i < _handSelection.Cards.Count; i++)
                _hand.Items.Add(_handSelection.ItemText(i) +
                    (_raisedPositions.Contains(i + 1) ? "；游戏中已抬起" : ""));
            if (_hand.Items.Count == 0) _hand.Items.Add("没有稳定的手牌候选，请完整扫描。");
            else _hand.SelectedIndex = Math.Clamp(_handSelection.CurrentIndex, 0, _hand.Items.Count - 1);
        }
        finally { _hand.EndUpdate(); }
    }

    private async Task HandleHandKeyAsync(KeyEventArgs e)
    {
        if (e.KeyCode is not (Keys.Left or Keys.Right or Keys.Home or Keys.End or
            Keys.Up or Keys.Down or Keys.Enter)) return;
        e.Handled = true;
        e.SuppressKeyPress = true;
        if (_busy || _groupActionActive) return;
        if (e.Shift || e.Alt) return;
        if (e.KeyCode is Keys.Up or Keys.Down or Keys.Enter && !_heldHandKeys.Add(e.KeyCode)) return;
        if (e.KeyCode == Keys.Enter && e.Control)
        {
            await SubmitPassAsync();
            return;
        }
        if (e.KeyCode == Keys.Enter)
        {
            await SubmitPlayAsync();
            return;
        }
        if (_liveHandKeys.Checked && e.KeyCode is Keys.Up or Keys.Down)
        {
            if (e.Control)
            {
                await ChangeGameGroupAsync(e.KeyCode == Keys.Down);
                return;
            }
            var current = _hand.SelectedIndex + 1;
            if (e.KeyCode == Keys.Down && _raisedPositions.Count == 0)
            {
                _handStatus.Text = "当前没有经助手确认抬起的游戏手牌；没有发送点击。";
                return;
            }
            if (e.KeyCode == Keys.Up && _raisedPositions.Contains(current))
                current = Enumerable.Range(current + 1, Math.Max(0, _handSelection.Cards.Count - current))
                    .Concat(Enumerable.Range(1, current))
                    .FirstOrDefault(position => !_raisedPositions.Contains(position));
            if (e.KeyCode == Keys.Down && !_raisedPositions.Contains(current))
                current = _raisedPositions.Min;
            if (current == 0)
            {
                _handStatus.Text = "没有可拿起的游戏手牌；没有发送点击。";
                return;
            }
            await ToggleGameCardAsync(current);
            return;
        }
        RunHandAction(e.KeyCode, e.Control);
    }

    private void RunHandAction(Keys key, bool control)
    {
        if (_busy || _groupActionActive) return;
            if (!_playModeActive) _handSelection.SetCurrent(_hand.SelectedIndex);
        string message;
        switch (key)
        {
            case Keys.Left:
                message = _handSelection.MoveGroup(-1);
                break;
            case Keys.Right:
                message = _handSelection.MoveGroup(1);
                break;
            case Keys.Home:
                message = _handSelection.MoveEnd(false);
                break;
            case Keys.End:
                message = _handSelection.MoveEnd(true);
                break;
            case Keys.Up:
                message = control ? _handSelection.PickGroup() : _handSelection.PickOne();
                break;
            case Keys.Down:
                message = control ? _handSelection.DropAll() : _handSelection.DropOne();
                break;
            default:
                message = control ? "过牌尚未通过回合和按钮校验，没有向游戏提交。" :
                    "出牌尚未通过牌面、回合和按钮校验，没有向游戏提交。";
                break;
        }
        _handStatus.Text = message;
        RefreshHandItems();
        if (_playModeActive) SpeakCurrentCard();
    }

    private async Task ScanHandAsync()
    {
        if (_busy || _groupActionActive) return;
        _pass.Enabled = false;
        _play.Enabled = false;
        _raisedPlan = null;
        _raisedPositions.Clear();
        var previous = _lastScan;
        var game = GameWindow.Find(WindowKind.Game);
        if (previous is null || _game is null || game is null || game.ProcessId != _game.ProcessId ||
            game.Handle != _game.Handle || _game.Kind != WindowKind.Game)
        {
            _handStatus.Text = "游戏窗口未连接或已经变化，请连接并完整扫描。";
            return;
        }
        _busy = true;
        _activate.Enabled = false;
        _handSelection.Load([]);
        _hand.Items.Clear();
        _hand.Items.Add("正在快速刷新，旧候选已经清除。");
        _handStatus.Text = "正在采集两帧手牌；本次跳过整屏 OCR。";
        _scanHand.Enabled = false;
        try
        {
            var settings = new AppSettings { OcrBackend = _settings.OcrBackend,
                KeepScreenshots = _settings.KeepScreenshots };
            var fresh = await _scanner.ScanHandAsync(game, settings, previous, CancellationToken.None);
            foreach (var position in fresh.Hand.RaisedPositions) _raisedPositions.Add(position);
            _handSelection.Load(fresh.Hand.Cards.Count > 0 ? fresh.Hand.Cards :
                fresh.Hand.RaisedPositions.Count > 0 && (fresh.Hand.ObservedCount is >= 1 and <= 17 or 20)
                    ? Enumerable.Range(1, fresh.Hand.ObservedCount)
                        .Select(position => new HandCard(position, "未知", "未知")).ToArray()
                    : []);
            _handStatus.Text = $"{fresh.Hand.Summary} 快速刷新 {fresh.ElapsedMs:0} 毫秒，{fresh.CapturedAt:HH:mm:ss}。场景仍以最近完整扫描为准。";
            RefreshHandItems();
            AddEvent($"快速刷新手牌：{fresh.Hand.Cards.Count} 张候选，{fresh.ElapsedMs:0} 毫秒；本次没有发送游戏动作。");
        }
        catch (Exception ex)
        {
            _handStatus.Text = $"快速刷新失败：{ex.Message}";
            _hand.Items.Clear();
            _hand.Items.Add("快速刷新失败，请完整扫描。旧候选已经清除。");
            AddEvent(_handStatus.Text);
        }
        finally
        {
            _busy = false;
            _scanHand.Enabled = true;
            _activate.Enabled = _ocrLines.SelectedItem is OcrEntry { CanClick: true };
            if (_closeRequested) BeginInvoke(Close);
        }
    }

    private async Task SubmitPlayAsync()
    {
        if (_busy || _groupActionActive) return;
        var game = GameWindow.Find(WindowKind.Game);
        var previous = _lastScan;
        var plan = _raisedPlan;
        var selected = _raisedPositions.ToArray();
        if (!_play.Enabled || _game is null || game is null ||
            game.ProcessId != _game.ProcessId || game.Handle != _game.Handle ||
            previous is null || previous.Scene != "牌桌" || !previous.OwnPlayConfirmed ||
            plan is null || selected.Length == 0 ||
            DateTimeOffset.Now - _raisedAt > TimeSpan.FromSeconds(30))
        {
            _handStatus.Text = "出牌条件已过期或没有经确认抬起的牌；没有发送点击。请重新扫描。";
            return;
        }
        _busy = true;
        _play.Enabled = false;
        _pass.Enabled = false;
        _activate.Enabled = false;
        _toggleGameCard.Enabled = false;
        var clickAttempted = false;
        var settings = new AppSettings { OcrBackend = _settings.OcrBackend,
            KeepScreenshots = _settings.KeepScreenshots };
        _handStatus.Text = "正在重新核对本人出牌按钮、钟面及已抬起的牌；尚未点击。";
        try
        {
            var fresh = await _scanner.ScanAsync(game, settings, CancellationToken.None);
            if (fresh.Scene != "牌桌" || !fresh.OwnPlayConfirmed || fresh.Hand is null ||
                fresh.Hand.LayoutLeft != plan.LayoutLeft ||
                fresh.Hand.ObservedCount != plan.Cards.Count ||
                !fresh.Hand.RaisedPositions.SequenceEqual(selected))
                throw new InvalidOperationException("本人出牌按钮、牌列或已抬牌集合变化；没有点击。");
            await GameInput.ClickOnceAsync(game, 438, 460, 1280, 720, Handle,
                () => _scanner.ConfirmPlayControlsAsync(game, settings, fresh,
                    selected, plan.Cards.Count, plan.LayoutLeft, CancellationToken.None),
                () => clickAttempted = true, restoreAssistantFocus: !_playModeActive);
            var expected = plan.Cards.Where(card => !selected.Contains(card.Position))
                .Select(card => (card.Rank, card.Suit))
                .OrderBy(card => card.Rank).ThenBy(card => card.Suit).ToArray();
            bool Confirmed(ScanResult scan) => scan.Scene == "牌桌" &&
                scan.Hand is { RaisedPositions.Count: 0 } &&
                scan.Hand.Cards.Select(card => (card.Rank, card.Suit))
                    .OrderBy(card => card.Rank).ThenBy(card => card.Suit)
                    .SequenceEqual(expected);
            await Task.Delay(250);
            var after = await _scanner.ScanAsync(game, settings, CancellationToken.None);
            if (!Confirmed(after))
            {
                await Task.Delay(200);
                after = await _scanner.ScanAsync(game, settings, CancellationToken.None);
            }
            var confirmed = Confirmed(after);
            var unchangedRaised = after.Scene == "牌桌" && after.Hand is not null &&
                after.Hand.ObservedCount == plan.Cards.Count &&
                after.Hand.RaisedPositions.SequenceEqual(selected) && after.OwnPlayConfirmed;
            var unchangedFlat = after.Scene == "牌桌" && after.Hand is not null &&
                after.Hand.Cards.SequenceEqual(plan.Cards) && after.OwnPlayConfirmed;
            DisplayScan(after);
            if (!confirmed) { _play.Enabled = false; _lastScan = null; }
            _handStatus.Text = confirmed
                ? $"出牌单次点击已发送；手牌减少 {selected.Length} 张，剩余牌面与预期一致。"
                : unchangedRaised
                    ? "出牌点击已发送，但两次回读仍为原张数、原抬牌集合且本人按钮仍在；未见出牌生效。牌型可能不符合当前要求；请检查画面，不会自动重试。"
                    : unchangedFlat
                    ? "出牌点击已发送，但两次回读手牌未减少，本人按钮仍在；未见出牌生效。请自行检查牌型并重新扫描；不会自动重试。"
                    : "出牌单次点击已发送，后续手牌未能确认结果；请重新扫描。不会自动重试。";
            AddEvent(_handStatus.Text);
        }
        catch (Exception ex)
        {
            _lastScan = null;
            _raisedPlan = null;
            _raisedPositions.Clear();
            _play.Enabled = false;
            _handStatus.Text = clickAttempted
                ? $"出牌未确认，点击可能已经发送：{ex.Message} 不会自动重试；请重新扫描。"
                : $"出牌已取消：{ex.Message} 没有发送点击；请重新扫描。";
            AddEvent(_handStatus.Text);
        }
        finally
        {
            _busy = false;
            _toggleGameCard.Enabled = true;
            _activate.Enabled = _lastScan is not null &&
                _ocrLines.SelectedItem is OcrEntry { CanClick: true };
            if (!_playModeActive) _hand.Focus();
            if (_closeRequested) BeginInvoke(Close);
        }
    }

    private async Task SubmitPassAsync()
    {
        if (_busy || _groupActionActive) return;
        var game = GameWindow.Find(WindowKind.Game);
        var previous = _lastScan;
        if (!_pass.Enabled || _game is null || game is null || game.ProcessId != _game.ProcessId ||
            game.Handle != _game.Handle || previous is null || previous.Scene != "牌桌" ||
            !previous.OwnPassConfirmed || previous.Hand is null ||
            previous.Hand.RaisedPositions.Count != 0 || previous.Hand.ObservedCount == 0)
        {
            _handStatus.Text = "未确认本人可过牌的实时牌桌；没有发送过牌。请重新完整扫描。";
            return;
        }
        _busy = true;
        _pass.Enabled = false;
        _play.Enabled = false;
        _activate.Enabled = false;
        _toggleGameCard.Enabled = false;
        var clickAttempted = false;
        var settings = new AppSettings { OcrBackend = _settings.OcrBackend,
            KeepScreenshots = _settings.KeepScreenshots };
        _handStatus.Text = "正在重新核对本人不出按钮、钟面和手牌；尚未点击。";
        try
        {
            var fresh = await _scanner.ScanAsync(game, settings, CancellationToken.None);
            if (fresh.Scene != "牌桌" || !fresh.OwnPassConfirmed ||
                fresh.Hand is null || fresh.Hand.RaisedPositions.Count != 0 ||
                fresh.Hand.ObservedCount != previous.Hand.ObservedCount ||
                !fresh.Hand.Cards.SequenceEqual(previous.Hand.Cards))
                throw new InvalidOperationException("本人按钮或手牌已变化；请重新扫描后决定是否过牌。");
            await GameInput.ClickOnceAsync(game, 680, 460, 1280, 720, Handle, async () =>
            {
                var beforeClick = await _scanner.ScanAsync(game, settings, CancellationToken.None);
                if (beforeClick.Scene != "牌桌" || !beforeClick.OwnPassConfirmed ||
                    beforeClick.OwnTurnSeconds is not >= 4 || beforeClick.Hand is null ||
                    beforeClick.Hand.RaisedPositions.Count != 0 ||
                    !beforeClick.Hand.Cards.SequenceEqual(fresh.Hand.Cards))
                    throw new InvalidOperationException("临点击前本人过牌按钮、钟面或手牌变化；没有点击。");
            }, () => clickAttempted = true, restoreAssistantFocus: !_playModeActive);
            var after = await _scanner.ScanAsync(game, settings, CancellationToken.None);
            DisplayScan(after);
            var ownPassLabel = after.Lines.Any(line => line.Text.Trim() == "不出" &&
                line.Confidence >= 0.85 &&
                line.X + line.Width / 2 is >= 580 and <= 655 &&
                line.Y + line.Height / 2 is >= 420 and <= 490);
            var confirmed = after.Scene == "牌桌" && !after.OwnPassConfirmed &&
                after.Hand is not null && after.Hand.RaisedPositions.Count == 0 &&
                after.Hand.Cards.SequenceEqual(fresh.Hand.Cards) && ownPassLabel;
            if (!confirmed) { _pass.Enabled = false; _lastScan = null; }
            _handStatus.Text = confirmed
                ? "过牌单次点击已发送；本人不出字样出现、按钮消失，手牌未减少。"
                : "过牌单次点击已发送，后续画面未能确认结果；请重新扫描。不会自动重试。";
            AddEvent(_handStatus.Text);
        }
        catch (Exception ex)
        {
            _lastScan = null;
            _pass.Enabled = false;
            _handStatus.Text = clickAttempted
                ? $"过牌未确认，点击可能已经发送：{ex.Message} 不会自动重试；请重新扫描。"
                : $"过牌已取消：{ex.Message} 没有发送点击；请重新扫描。";
            AddEvent(_handStatus.Text);
        }
        finally
        {
            _busy = false;
            _toggleGameCard.Enabled = true;
            _activate.Enabled = _lastScan is not null &&
                _ocrLines.SelectedItem is OcrEntry { CanClick: true };
            if (!_playModeActive) _hand.Focus();
            if (_closeRequested) BeginInvoke(Close);
        }
    }

    private void CancelGroupAction()
    {
        if (!_groupActionActive) return;
        _groupCancelRequested = true;
        _cancelGroup.Enabled = false;
        _handStatus.Text = "正在停止整组操作；当前单次动作读回后停止，已抬起的牌保留。";
    }

    private async Task ChangeGameGroupAsync(bool dropAll)
    {
        if (_busy || _groupActionActive) return;
        var cards = _handSelection.Cards.ToArray();
        int[] positions;
        if (dropAll)
        {
            positions = _raisedPositions.ToArray();
            if (positions.Length == 0)
            {
                _handStatus.Text = "游戏中没有经助手确认抬起的牌；没有发送点击。";
                return;
            }
        }
        else
        {
            var current = _playModeActive ? _handSelection.CurrentIndex : _hand.SelectedIndex;
            if (current < 0 || current >= cards.Length ||
                cards[current].Rank == "未知" ||
                cards.Any(card => card.Rank == "未知" || card.Suit == "未知"))
            {
                _handStatus.Text = "整手牌面或当前点数未知，不能确认整组；没有发送点击。";
                return;
            }
            var rank = cards[current].Rank;
            positions = cards.Select((card, index) => (card, index))
                .Where(item => item.card.Rank == rank && !_raisedPositions.Contains(item.index + 1))
                .Select(item => item.index + 1).ToArray();
            if (positions.Length == 0)
            {
                _handStatus.Text = $"{rank} 已全部抬起；没有发送点击。";
                return;
            }
            if (cards.Count(card => card.Rank == rank) > 4)
            {
                _handStatus.Text = "当前点数超过一副牌的四张，牌面候选可能错误；没有发送点击。";
                return;
            }
        }

        _groupActionActive = true;
        _groupCancelRequested = false;
        _cancelGroup.Enabled = true;
        _takeGameGroup.Enabled = false;
        _dropGameCards.Enabled = false;
        var completed = 0;
        try
        {
            foreach (var position in positions)
            {
                if (_groupCancelRequested || _closeRequested) break;
                await ToggleGameCardAsync(position, fromGroup: true);
                if (_groupCancelRequested && _raisedPositions.Contains(position) == dropAll) break;
                if (_lastScan is null ||
                    _raisedPositions.Contains(position) == dropAll)
                {
                    var reason = _handStatus.Text;
                    _handStatus.Text = $"整组操作停在第 {position} 张，已核对 {completed} 张。{reason}";
                    return;
                }
                completed++;
            }
            if (_groupCancelRequested || _closeRequested)
            {
                _handStatus.Text = $"整组操作已停止，已核对 {completed} 张；游戏中仍抬起 {_raisedPositions.Count} 张。";
                AddEvent(_handStatus.Text);
                return;
            }
            _handStatus.Text = dropAll
                ? $"游戏中 {completed} 张已逐张放下并核对；没有出牌。"
                : $"游戏中 {completed} 张同点数牌已逐张抬起并核对；没有出牌。";
            AddEvent(_handStatus.Text);
        }
        finally
        {
            _groupActionActive = false;
            _cancelGroup.Enabled = false;
            _takeGameGroup.Enabled = true;
            _dropGameCards.Enabled = true;
            if (_closeRequested) BeginInvoke(Close);
        }
    }

    private async Task ToggleGameCardAsync(int? requestedPosition = null, bool fromGroup = false)
    {
        if (_busy || _groupActionActive && !fromGroup) return;
        var game = GameWindow.Find(WindowKind.Game);
        var previous = _lastScan;
        if (_game is null || _game.Kind != WindowKind.Game || game is null ||
            game.ProcessId != _game.ProcessId || game.Handle != _game.Handle ||
            previous is null || previous.Scene is not ("牌桌" or "叫地主阶段"))
        {
            _handStatus.Text = "请先连接游戏并完整扫描实时牌桌；没有点击游戏手牌。";
            return;
        }
        if (_handSelection.PickedCount != 0)
        {
            _handStatus.Text = "请先清空本地选牌预览，再使用游戏内单张拿放；没有点击游戏手牌。";
            return;
        }
        var savedCards = _handSelection.Cards.ToArray();
        var position = requestedPosition ?? CurrentHandPosition();
        if (position < 1 || position > savedCards.Length)
        {
            _handStatus.Text = "没有选中可核对的手牌；没有点击游戏手牌。";
            return;
        }

        var dropping = _raisedPositions.Contains(position);
        var plan = _raisedPlan;
        var beforePositions = _raisedPositions.ToArray();
        if (beforePositions.Length > 0 && !dropping &&
            (plan is null || DateTimeOffset.Now - _raisedAt > TimeSpan.FromSeconds(30)))
        {
            _handStatus.Text = "本局原牌面记录已过期；只能放下画面确认抬起的牌，请完整扫描后再拿牌。";
            return;
        }
        var afterPositions = beforePositions.Where(item => item != position)
            .Concat(dropping ? [] : [position]).Order().ToArray();
        var settings = new AppSettings { OcrBackend = _settings.OcrBackend,
            KeepScreenshots = _settings.KeepScreenshots };
        var clickAttempted = false;
        _busy = true;
        _toggleGameCard.Enabled = false;
        _pass.Enabled = false;
        _play.Enabled = false;
        _scanHand.Enabled = false;
        _activate.Enabled = false;
        _handStatus.Text = beforePositions.Length > 0 ?
            "正在重新核对游戏中已抬起的手牌。" : "正在重新核对整手牌与所选序号。";
        try
        {
            var fresh = await _scanner.ScanAsync(game, settings, CancellationToken.None);
            if (beforePositions.Length > 0)
            {
                if (fresh.Scene != previous.Scene || fresh.Hand is null ||
                    !fresh.Hand.RaisedPositions.SequenceEqual(beforePositions) ||
                    fresh.Hand.ObservedCount is not (>= 1 and <= 17 or 20) ||
                    fresh.Hand.ObservedCount != savedCards.Length ||
                    (plan is not null && fresh.Hand.LayoutLeft != plan.LayoutLeft))
                    throw new InvalidOperationException("已抬起的牌或布局不能再次确认；请检查游戏画面。");
                plan ??= new HandClickPlan(position,
                    fresh.Hand.LayoutLeft + (position - 1) * 41 + 20, 560,
                    savedCards, fresh.Hand.LayoutLeft);
            }
            else
            {
                if (fresh.Scene != previous.Scene)
                    throw new InvalidOperationException("牌桌场景已经变化；请重新完整扫描。");
                plan = HandClickPlanner.Create(fresh, position);
                if (!plan.Cards.SequenceEqual(savedCards))
                    throw new InvalidOperationException("重新扫描的手牌与当前列表不同；请刷新后重新选择。");
            }

            var checkedPlan = plan!;
            var recovery = _raisedPlan is null && beforePositions.Length > 0;
            var clickX = checkedPlan.LayoutLeft + (position - 1) * 41 + 20;
            await GameInput.ClickOnceAsync(game, clickX, checkedPlan.ClientY,
                1280, 720, Handle, async () =>
                {
                    var beforeClick = await _scanner.ScanHandAsync(game, settings, fresh,
                        CancellationToken.None);
                    var confirmed = beforePositions.Length > 0
                        ? HandClickPlanner.SameRaisedHand(checkedPlan, beforeClick.Hand, beforePositions)
                        : HandClickPlanner.SameHand(checkedPlan, beforeClick.Hand);
                    if (!confirmed)
                        throw new InvalidOperationException("点击前最后两帧手牌状态已经变化；已取消点击。");
                    if (fromGroup && (_groupCancelRequested || _closeRequested))
                        throw new OperationCanceledException("已停止整组操作，当前点击尚未发送。");
                }, () => clickAttempted = true, restoreAssistantFocus: !_playModeActive);

            var after = await _scanner.ScanHandAsync(game, settings, fresh,
                CancellationToken.None);
            if (afterPositions.Length == 0)
            {
                if (recovery ? after.Hand.LayoutLeft != checkedPlan.LayoutLeft ||
                    after.Hand.ObservedCount != checkedPlan.Cards.Count ||
                    after.Hand.RaisedPositions.Count != 0 ||
                    after.Hand.Cards.Count != checkedPlan.Cards.Count :
                    !HandClickPlanner.SameHand(checkedPlan, after.Hand))
                    throw new InvalidOperationException("点击后未确认整手牌回落到原布局。");
                _raisedPlan = null;
                _raisedPositions.Clear();
                _handSelection.Load(after.Hand.Cards);
                _handSelection.SetCurrent(position - 1);
                RefreshHandItems();
                _handStatus.Text = $"游戏中第 {position} 张已回落，整手牌重新核对成功；没有出牌。";
            }
            else
            {
                if (!HandClickPlanner.SameRaisedHand(checkedPlan, after.Hand, afterPositions))
                    throw new InvalidOperationException("点击后未确认所选手牌的抬起状态。");
                _raisedPlan = recovery ? null : checkedPlan;
                if (beforePositions.Length == 0) _raisedAt = DateTimeOffset.Now;
                _raisedPositions.Clear();
                foreach (var item in afterPositions) _raisedPositions.Add(item);
                _handSelection.SetCurrent(position - 1);
                RefreshHandItems();
                _handStatus.Text = recovery
                    ? $"游戏中仍抬起第 {string.Join("、", afterPositions)} 张；可继续逐张放下。没有出牌。"
                    : $"游戏中已稳定抬起第 {string.Join("、", afterPositions)} 张；上键可继续拿一张，下键放下一张。没有出牌。";
            }
            _lastScan = fresh;
            _play.Enabled = _raisedPlan is not null && _raisedPositions.Count > 0 &&
                fresh.OwnPlayConfirmed && DateTimeOffset.Now - _raisedAt < TimeSpan.FromSeconds(30);
            _allEntries = [];
            _ocrLines.Items.Clear();
            _ocrLines.Items.Add("手牌动作后文字导航已清空；如需浏览画面，请重新完整扫描。");
            _detail.Text = $"游戏手牌动作经两帧及牌桌底栏确认，{after.CapturedAt:HH:mm:ss}。文字导航需重新扫描。";
            AddEvent(_handStatus.Text);
        }
        catch (OperationCanceledException) when (!clickAttempted && fromGroup)
        {
            _handStatus.Text = "整组操作已停止，当前点击未发送；已核对的抬牌状态保留。";
        }
        catch (Exception ex)
        {
            _raisedPlan = null;
            _raisedPositions.Clear();
            _lastScan = null;
            _handSelection.Load([]);
            RefreshHandItems();
            _handStatus.Text = clickAttempted
                ? $"手牌拿放未确认，点击可能已经发送：{ex.Message} 不会自动重试；请重新完整扫描并检查画面。"
                : $"手牌拿放已取消：{ex.Message} 没有发送点击；请重新完整扫描。";
            AddEvent(_handStatus.Text);
        }
        finally
        {
            _busy = false;
            _toggleGameCard.Enabled = true;
            _scanHand.Enabled = true;
            _activate.Enabled = _lastScan is not null &&
                _ocrLines.SelectedItem is OcrEntry { CanClick: true };
            if (!_playModeActive) _hand.Focus();
            if (_closeRequested) BeginInvoke(Close);
        }
    }

    private void ApplyFilter()
    {
        var selected = _ocrLines.SelectedItem as OcrEntry;
        var query = _filter.Text.Trim();
        var visible = (query.Length == 0 ? _allEntries :
            _allEntries.Where(entry => entry.Line.Text.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList()).ToList();
        _ocrLines.BeginUpdate();
        try
        {
            _ocrLines.Items.Clear();
            foreach (var entry in visible) _ocrLines.Items.Add(entry);
            if (_ocrLines.Items.Count == 0) _ocrLines.Items.Add(query.Length == 0
                ? "没有识别到文字或坐标。 " : "没有匹配的识别文字；请修改或清除筛选。 ");
            else _ocrLines.SelectedIndex = selected is not null && visible.Contains(selected)
                ? visible.IndexOf(selected) : 0;
        }
        finally { _ocrLines.EndUpdate(); }
    }

    private void ClearRoomConfirmation()
    {
        _pendingRoomEntry = null;
        _confirmRoom.Visible = false;
        _cancelRoom.Visible = false;
        _activate.Enabled = !_busy && _ocrLines.SelectedItem is OcrEntry { CanClick: true };
    }

    private void CancelRoomConfirmation()
    {
        ClearRoomConfirmation();
        _detail.Text = "已取消进入场次；没有向游戏发送点击。";
        _ocrLines.Focus();
    }

    private async Task ActivateSelectedAsync(bool confirmRoom = false)
    {
        if (_busy || _groupActionActive) return;
        if (_ocrLines.SelectedItem is not OcrEntry { CanClick: true } selected || _lastScan is null || _game is null)
        {
            _detail.Text = "当前文字仅供阅读，或尚未完成扫描；没有向游戏发送点击。 ";
            return;
        }
        var game = GameWindow.Find(SelectedWindowKind);
        if (game is null || game.ProcessId != _game.ProcessId || game.Handle != _game.Handle)
        {
            _lastScan = null;
            _activate.Enabled = false;
            _detail.Text = "游戏窗口已变化，请重新连接和扫描；没有发送点击。 ";
            return;
        }
        if (selected.Room is { } room)
        {
            if (!confirmRoom)
            {
                _pendingRoomEntry = selected;
                _detail.Text = $"准备进入{room.Name}。画面标注底分 {room.BaseScore}，豆子范围 {room.BeanRange}；输赢会改变豆子余额。按 Alt+Y 确认，Alt+N 或 Escape 取消。";
                _confirmRoom.Text = $"确认进入{room.Name}(&Y)";
                _confirmRoom.AccessibleName = $"确认进入{room.Name}，Alt+Y";
                _confirmRoom.AccessibleDescription = "按 Alt 加 Y 确认进入当前场次。";
                _cancelRoom.AccessibleName = "取消进入场次，Alt+N 或 Escape";
                _cancelRoom.AccessibleDescription = "按 Alt 加 N 或 Escape 取消，不进入场次。";
                _confirmRoom.Visible = true;
                _cancelRoom.Visible = true;
                _activate.Enabled = false;
                _cancelRoom.Focus();
                var focusTimer = new System.Windows.Forms.Timer { Interval = 200 };
                focusTimer.Tick += (_, _) =>
                {
                    focusTimer.Stop();
                    focusTimer.Dispose();
                    if (ReferenceEquals(_pendingRoomEntry, selected)) _cancelRoom.Focus();
                };
                focusTimer.Start();
                return;
            }
            if (!ReferenceEquals(_pendingRoomEntry, selected))
            {
                CancelRoomConfirmation();
                return;
            }
            ClearRoomConfirmation();
        }
        else if (confirmRoom)
        {
            CancelRoomConfirmation();
            return;
        }
        var previous = _lastScan!;
        _busy = true;
        _activate.Enabled = false;
        _detail.Text = $"正在重新核对“{selected.Line.Text}”的位置，请稍候。";
        AddEvent($"用户请求点击：{selected.Line.Text}");
        var settings = new AppSettings { OcrBackend = _settings.OcrBackend, KeepScreenshots = _settings.KeepScreenshots };
        var sent = false;
        var focusOfficialPassword = previous.Scene == "QQ登录窗口" &&
            selected.Line.Text == "聚焦官方密码输入框";
        try
        {
            var fresh = await _scanner.ScanAsync(game, settings, CancellationToken.None);
            if (fresh.Scene != previous.Scene || fresh.ClientWidth != previous.ClientWidth || fresh.ClientHeight != previous.ClientHeight)
            {
                DisplayScan(fresh);
                throw new InvalidOperationException("场景或窗口尺寸已变化，请重新选择；没有发送点击。");
            }
            var match = OcrNavigation.Build(fresh)
                .Where(entry => entry.CanClick &&
                    (entry.Line.Text.Trim() == selected.Line.Text.Trim() ||
                     previous.Scene == "QQ游戏大厅" &&
                     entry.Line.Text.StartsWith("欢乐斗地主(", StringComparison.Ordinal) &&
                     selected.Line.Text.StartsWith("欢乐斗地主(", StringComparison.Ordinal) ||
                     previous.Scene == "游戏大厅" &&
                     entry.Line.Text.StartsWith("斗地主", StringComparison.Ordinal) &&
                     selected.Line.Text.StartsWith("斗地主", StringComparison.Ordinal)))
                .OrderBy(entry => Math.Abs(entry.CenterX - selected.CenterX) + Math.Abs(entry.CenterY - selected.CenterY))
                .FirstOrDefault();
            if (match is null || Math.Abs(match.CenterX - selected.CenterX) > 30 ||
                Math.Abs(match.CenterY - selected.CenterY) > 30)
            {
                DisplayScan(fresh);
                throw new InvalidOperationException("目标文字或位置已变化，请重新选择；没有发送点击。");
            }
            Func<Task>? finalBidCheck = null;
            if (fresh.Scene == "叫地主阶段")
            {
                  finalBidCheck = () => _scanner.ConfirmBidControlsAsync(game, settings,
                      fresh, match.Line.Text.Trim(), CancellationToken.None);
            }
            if (fresh.Scene == "QQ登录窗口")
            {
                finalBidCheck = async () =>
                {
                    var current = await _scanner.ScanAsync(game, settings, CancellationToken.None);
                    var stillVerified = current.Scene == "QQ登录窗口" &&
                        OcrNavigation.Build(current).Any(entry => entry.CanClick &&
                            entry.Line.Text == match.Line.Text &&
                            Math.Abs(entry.CenterX - match.CenterX) <= 8 &&
                            Math.Abs(entry.CenterY - match.CenterY) <= 8);
                    if (!stillVerified)
                        throw new InvalidOperationException("官方登录窗口或目标已变化；没有发送点击。");
                };
            }
            await GameInput.ClickOnceAsync(game, match.CenterX, match.CenterY,
                fresh.ClientWidth, fresh.ClientHeight, Handle, finalBidCheck,
                () => sent = true, !focusOfficialPassword);
            AddEvent($"已单次点击：{match.Line.Text}，坐标 {match.CenterX}，{match.CenterY}");
            if (focusOfficialPassword)
            {
                _detail.Text = "已单次点击官方密码框，焦点留在 QQ 登录窗口；请在官方窗口直接输入密码。助手没有接收键盘输入。";
                AddEvent(_detail.Text);
                return;
            }
            if (game.Kind == WindowKind.Hall && selected.Kind == "游戏入口")
            {
                GameWindow? launched = null;
                for (var attempt = 0; attempt < 20; attempt++)
                {
                    launched = GameWindow.Find(WindowKind.Game);
                    if (launched is not null) break;
                    await Task.Delay(500);
                }
                if (launched is not null)
                {
                    _game = launched;
                    _source.SelectedIndex = 0;
                    var gameScreen = await _scanner.ScanAsync(launched, settings, CancellationToken.None);
                    DisplayScan(gameScreen);
                    _detail.Text = $"已连接欢乐斗地主，当前场景：{gameScreen.Scene}。请从当前画面继续浏览。";
                    AddEvent(_detail.Text);
                    StartPlayMode(launched, gameScreen);
                    return;
                }
            }
            await Task.Delay(350);
            var after = await _scanner.ScanAsync(game, settings, CancellationToken.None);
            DisplayScan(after);
            var stillVisible = OcrNavigation.Build(after).Any(entry => entry.CanClick &&
                entry.Line.Text.Trim() == selected.Line.Text.Trim());
            var beforeStep = OcrNavigation.ReplayStep(previous);
            var afterStep = OcrNavigation.ReplayStep(after);
            if (selected.Line.Text is ("回放上一手" or "回放下一手") &&
                beforeStep is { } before && afterStep is { } now && before.Current != now.Current)
                _detail.Text = $"回放已切换到第 {now.Current}/{now.Total} 手；请阅读更新后的手牌候选。";
            else
                _detail.Text = stillVisible && after.Scene == previous.Scene
                    ? "点击已发送，但画面未确认变化。请检查画面或重新扫描；不会自动重试。 "
                    : $"点击已发送，当前场景：{after.Scene}。请核对结果。";
        }
        catch (Exception ex)
        {
            _detail.Text = sent ? $"点击已发送，后续画面未能确认：{ex.Message} 不会自动重试。"
                : $"点击已取消：{ex.Message}";
            AddEvent(_detail.Text);
        }
        finally
        {
            _busy = false;
            _activate.Enabled = _ocrLines.SelectedItem is OcrEntry { CanClick: true };
            if (!focusOfficialPassword || !sent) _ocrLines.Focus();
            if (_closeRequested) BeginInvoke(Close);
        }
    }

    private void ConnectIfNeeded()
    {
        var current = GameWindow.Find(SelectedWindowKind);
        if (current is null || _game?.ProcessId != current.ProcessId || _game.Handle != current.Handle)
            Connect();
    }

    private WindowKind SelectedWindowKind => _source.SelectedIndex switch
    {
        1 => WindowKind.Hall,
        2 => WindowKind.Game,
        3 => WindowKind.Login,
        _ => WindowKind.Auto
    };

    private void SaveSettings()
    {
        _settings.OcrBackend = _backend.SelectedIndex == 1 ? "dml" : "cpu";
        _settings.KeepScreenshots = _keepScreenshots.Checked;
        _settings.AutoLoginWithQq = _autoQqLogin.Checked;
        _settings.SpeakActionResults = _speakResults.Checked;
        try
        {
            _settings.Save();
            AddEvent("设置已保存");
            _settingsStatus.Text = "设置已保存。 ";
        }
        catch (Exception ex)
        {
            AddEvent($"设置保存失败：{ex.Message}");
            _settingsStatus.Text = $"设置保存失败：{ex.Message}";
        }
    }

    private void SetHallVisibility(bool visible)
    {
        try
        {
            _hallStatus.Text = HallWindowVisibility.SetVisible(visible);
            AddEvent(_hallStatus.Text);
        }
        catch (Exception ex)
        {
            _hallStatus.Text = $"QQ 大厅窗口未切换：{ex.Message}";
            AddEvent(_hallStatus.Text);
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _hotkeyRegistered = RegisterHotKey(Handle, ToggleHotkeyId, 0x0003, (uint)Keys.D);
    }

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if (keyData == Keys.F6)
        {
            SpeakResult(_lastAnnouncement ?? _detail.Text);
            return true;
        }
        if (_groupActionActive && keyData == Keys.Escape)
        {
            CancelGroupAction();
            return true;
        }
        if (_pendingRoomEntry is not null && keyData == (Keys.Alt | Keys.Y))
        {
            _ = ActivateSelectedAsync(confirmRoom: true);
            return true;
        }
        if (_pendingRoomEntry is not null && keyData == (Keys.Alt | Keys.N))
        {
            CancelRoomConfirmation();
            return true;
        }
        return base.ProcessCmdKey(ref message, keyData);
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        if (_pendingRoomEntry is not null)
            BeginInvoke(() =>
            {
                if (_pendingRoomEntry is not null) _cancelRoom.Focus();
            });
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (_hotkeyRegistered) UnregisterHotKey(Handle, ToggleHotkeyId);
        _hotkeyRegistered = false;
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message message)
    {
        if (Program.ShowExistingMessage != 0 && message.Msg == Program.ShowExistingMessage)
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            return;
        }
        if (message.Msg == WmHotkey && message.WParam == (IntPtr)ToggleHotkeyId)
        {
            if (Visible && GetForegroundWindow() == Handle && !_busy)
            {
                Hide();
                if (_playModeActive && _game is not null) SetForegroundWindow(_game.Handle);
            }
            else
            {
                Show();
                WindowState = FormWindowState.Normal;
                Activate();
            }
            return;
        }
        base.WndProc(ref message);
    }

    private void ResetSettings()
    {
        _backend.SelectedIndex = 0;
        _keepScreenshots.Checked = false;
        _autoQqLogin.Checked = true;
        _speakResults.Checked = true;
        SaveSettings();
        if (_settingsStatus.Text == "设置已保存。 ")
        {
            _settingsStatus.Text = "已恢复默认设置并保存：CPU，诊断截图留存关闭。 ";
            AddEvent("已恢复默认设置");
        }
    }

    private void ClearScreenshots()
    {
        try
        {
            var folder = Path.Combine(ProjectPaths.Root, "captures");
            var files = Directory.Exists(folder) ? Directory.GetFiles(folder, "capture-*.bmp") : [];
            if (files.Length == 0)
            {
                MessageBox.Show(this, "没有由本程序保存的诊断截图。", "清除诊断截图");
                return;
            }
            if (MessageBox.Show(this, $"要删除本项目中 {files.Length} 张诊断截图吗？此操作无法撤销。",
                "清除诊断截图", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            foreach (var file in files) File.Delete(file);
            AddEvent($"已清除 {files.Length} 张诊断截图");
            MessageBox.Show(this, $"已清除 {files.Length} 张诊断截图。", "清除诊断截图");
        }
        catch (Exception ex)
        {
            AddEvent($"清除截图失败：{ex.Message}");
            MessageBox.Show(this, ex.Message, "清除截图失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void QueueAnnouncement(string text)
    {
        if (!_speechReady || _suppressPlayModeScanSpeech || string.IsNullOrWhiteSpace(text)) return;
        // Coalesce rapidly changing progress text; speak the completed result.
        _pendingAnnouncement = text;
        _speechTimer.Stop();
        _speechTimer.Start();
    }

    private void SpeakResult(string text)
    {
        _speech.TrySpeak(text);
        _speechStatus.Text = _speech.Status;
    }

    private void AddEvent(string value)
    {
        if (value == _handStatus.Text) QueueAnnouncement(value);
        _events.Items.Insert(0, $"{DateTime.Now:HH:mm:ss} {value}");
        while (_events.Items.Count > 300) _events.Items.RemoveAt(_events.Items.Count - 1);
    }
}
