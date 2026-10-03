using System.Diagnostics;

namespace HappyDDZ.Assistant;

internal sealed record ScanResult(string Scene, string Detail, IReadOnlyList<OcrLine> Lines,
    double OcrMs, DateTimeOffset CapturedAt, string WindowTitle, int ClientWidth, int ClientHeight,
    bool PackCloseXConfirmed, bool ReplayControlsConfirmed, bool SeasonCloseXConfirmed,
    bool SeasonRefreshCloseXConfirmed, bool BattleGuideCloseXConfirmed, HandRead? Hand)
{
    public bool CancelTrustConfirmed { get; init; }
    public bool SettlementContinueConfirmed { get; init; }
    public bool ReturnGiftCloseXConfirmed { get; init; }
    public bool FlowerEventCloseXConfirmed { get; init; }
    public bool LanternEventCloseXConfirmed { get; init; }
    public bool OwnBidButtonsConfirmed { get; init; }
    public bool OwnRobButtonsConfirmed { get; init; }
    public bool BadLuckCloseXConfirmed { get; init; }
    public bool ShareResultCloseXConfirmed { get; init; }
    public bool PiggyBankCloseXConfirmed { get; init; }
    public bool BeanAidConfirmConfirmed { get; init; }
    public bool BeanAidCloseXConfirmed { get; init; }
    public bool RoomExitConfirmed { get; init; }
    public bool OwnPassConfirmed { get; init; }
    public bool OwnPlayConfirmed { get; init; }
    public bool LoginConfirmConfirmed { get; init; }
    public bool LoginPasswordFormConfirmed { get; init; }
    public bool LoginQqQuickConfirmed { get; init; }
    public int? OwnTurnSeconds { get; init; }
}

internal sealed record HandScanResult(HandRead Hand, DateTimeOffset CapturedAt, double ElapsedMs);

internal sealed class ReadOnlyScanner : IDisposable
{
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private RapidClient? _engine;
    private string? _engineBackend;
    private CardCandidateReader? _cardReader;
    private TimerCandidateReader? _timerReader;
    private BidPhaseReader? _bidPhaseReader;

    public async Task<ScanResult> ScanAsync(GameWindow game, AppSettings settings, CancellationToken cancellationToken)
    {
        await _scanGate.WaitAsync(cancellationToken);
        try { return await ScanCoreAsync(game, settings, cancellationToken); }
        finally { _scanGate.Release(); }
    }

    public async Task<HandScanResult> ScanHandAsync(GameWindow game, AppSettings settings,
        ScanResult previous, CancellationToken cancellationToken)
    {
        if (game.Kind != WindowKind.Game || previous.Scene is not ("牌桌" or "叫地主阶段" or "对局回放") ||
            previous.WindowTitle != game.Title || previous.ClientWidth != 1280 || previous.ClientHeight != 720 ||
            DateTimeOffset.Now - previous.CapturedAt > TimeSpan.FromSeconds(60))
            throw new InvalidOperationException("快速刷新需要最近一分钟内确认过的 1280×720 牌桌或回放；请先完整扫描。");
        await _scanGate.WaitAsync(cancellationToken);
        try
        {
            var watch = Stopwatch.StartNew();
            var first = await CaptureAsync(game, settings, cancellationToken);
            await Task.Delay(180, cancellationToken);
            var second = await CaptureAsync(game, settings, cancellationToken);
            if (BitConverter.ToInt32(first.Image, 18) != 1280 || BitConverter.ToInt32(first.Image, 22) != -720 ||
                BitConverter.ToInt32(second.Image, 18) != 1280 || BitConverter.ToInt32(second.Image, 22) != -720)
                throw new InvalidOperationException("游戏窗口尺寸改变；请完整扫描。");
            var footerVisible = previous.Scene == "对局回放"
                ? HasReplayControls(first.Image) && HasReplayControls(second.Image)
                : HasLiveTableFooter(first.Image) && HasLiveTableFooter(second.Image);
            if (!footerVisible)
                throw new InvalidOperationException("牌桌底栏或回放控制已变化，可能出现遮挡弹窗；请完整扫描。");
            _cardReader ??= new CardCandidateReader();
            var hand = await Task.Run(() => _cardReader.Read(first.Image, second.Image), cancellationToken);
            watch.Stop();
            return new HandScanResult(hand, second.CapturedAt, watch.Elapsed.TotalMilliseconds);
        }
        finally { _scanGate.Release(); }
    }

    public async Task ConfirmBidControlsAsync(GameWindow game, AppSettings settings,
        ScanResult previous, string actionText, CancellationToken cancellationToken)
    {
        var rob = actionText is "抢地主" or "不抢";
        if (actionText is not ("叫地主" or "不叫" or "抢地主" or "不抢"))
            throw new InvalidOperationException("不是已核对的叫/抢地主按钮；没有发送点击。");
        if (game.Kind != WindowKind.Game || previous.Scene != "叫地主阶段" ||
            !(rob ? previous.OwnRobButtonsConfirmed : previous.OwnBidButtonsConfirmed) ||
            previous.WindowTitle != game.Title ||
            previous.ClientWidth != 1280 || previous.ClientHeight != 720 ||
            DateTimeOffset.Now - previous.CapturedAt > TimeSpan.FromSeconds(3))
            throw new InvalidOperationException("叫/抢地主完整扫描已过期或场景变化；没有发送点击。");
        await _scanGate.WaitAsync(cancellationToken);
        try
        {
            var first = await CaptureAsync(game, settings, cancellationToken);
            await Task.Delay(120, cancellationToken);
            var second = await CaptureAsync(game, settings, cancellationToken);
            bool Stable(byte[] image) =>
                BitConverter.ToInt32(image, 18) == 1280 &&
                BitConverter.ToInt32(image, 22) == -720 &&
                HasLiveTableFooter(image) && HasBidButtonPixels(image);
            _timerReader ??= new TimerCandidateReader();
            _bidPhaseReader ??= new BidPhaseReader();
            if (!Stable(first.Image) || !Stable(second.Image))
                throw new InvalidOperationException("叫/抢地主按钮或牌桌底栏在临点击前变化；没有发送点击。");
            if (!_bidPhaseReader.IsPhase(first.Image, rob) ||
                !_bidPhaseReader.IsPhase(second.Image, rob))
                throw new InvalidOperationException("叫/抢地主按钮文字阶段在临点击前变化；没有发送点击。");
            if (_timerReader.ReadBidCountdown(first.Image, second.Image) is not >= 4)
            {
                var (a, b) = _timerReader.ReadBidCountdownFrames(first.Image, second.Image);
                throw new InvalidOperationException($"叫/抢地主钟面在临点击前不稳定或时间不足（两帧候选：{a?.ToString() ?? "未知"}、{b?.ToString() ?? "未知"}）；没有发送点击。");
            }
        }
        finally { _scanGate.Release(); }
    }

    public async Task ConfirmPlayControlsAsync(GameWindow game, AppSettings settings,
        ScanResult previous, IReadOnlyList<int> selected, int expectedCount, int layoutLeft,
        CancellationToken cancellationToken)
    {
        if (game.Kind != WindowKind.Game || previous.Scene != "牌桌" ||
            !previous.OwnPlayConfirmed || previous.WindowTitle != game.Title ||
            previous.ClientWidth != 1280 || previous.ClientHeight != 720 ||
            DateTimeOffset.Now - previous.CapturedAt > TimeSpan.FromSeconds(3) ||
            selected.Count == 0 || expectedCount is not (>= 1 and <= 20))
            throw new InvalidOperationException("出牌完整扫描已过期或按钮变化；没有点击。");
        await _scanGate.WaitAsync(cancellationToken);
        try
        {
            var first = await CaptureAsync(game, settings, cancellationToken);
            await Task.Delay(120, cancellationToken);
            var second = await CaptureAsync(game, settings, cancellationToken);
            if (!HasLiveTableFooter(first.Image) || !HasLiveTableFooter(second.Image) ||
                !HasOwnPlayButtonPixels(first.Image) || !HasOwnPlayButtonPixels(second.Image))
                throw new InvalidOperationException("临点击前出牌按钮或牌桌底栏变化；没有点击。");
            _timerReader ??= new TimerCandidateReader();
            if (_timerReader.ReadOwnCountdown(first.Image, second.Image) is not >= 4)
                throw new InvalidOperationException("临点击前本人钟面不稳定或时间不足；没有点击。");
            _cardReader ??= new CardCandidateReader();
            var hand = await Task.Run(() => _cardReader.Read(first.Image, second.Image),
                cancellationToken);
            if (hand.LayoutLeft != layoutLeft || hand.ObservedCount != expectedCount ||
                !hand.RaisedPositions.SequenceEqual(selected))
                throw new InvalidOperationException("临点击前已抬牌集合或牌列变化；没有点击。");
        }
        finally { _scanGate.Release(); }
    }

    private async Task<ScanResult> ScanCoreAsync(GameWindow game, AppSettings settings, CancellationToken cancellationToken)
    {
        if (!File.Exists(ProjectPaths.CaptureExe)) throw new FileNotFoundException("按窗口捕获组件缺失。", ProjectPaths.CaptureExe);
        if (!File.Exists(ProjectPaths.RapidDll)) throw new FileNotFoundException("RapidV6 组件缺失。", ProjectPaths.RapidDll);
        var firstImage = await CaptureAsync(game, settings, cancellationToken);
        await Task.Delay(180, cancellationToken);
        var secondImage = await CaptureAsync(game, settings, cancellationToken);
        var (first, second) = await Task.Run(() =>
        {
            if (_engine is null || _engineBackend != settings.OcrBackend)
            {
                _engine?.Dispose();
                _engine = new RapidClient(ProjectPaths.RapidDll, settings.OcrBackend);
                _engineBackend = settings.OcrBackend;
            }
            return (_engine.Read(firstImage.Image), _engine.Read(secondImage.Image));
        }, cancellationToken);
        var (firstScene, _) = IdentifyScene(first.Text, game.Kind);
        var (secondScene, detail) = IdentifyScene(second.Text, game.Kind);
        var scene = secondScene;
        if (firstScene != secondScene)
        {
            scene = "状态不确定";
            detail = "连续两帧识别到不同场景，可能正在切换或动画中。请稍后重新扫描。";
        }
        // The game's Win32 window title is only “欢乐斗地主”; the visible title bar
        // renders its version inside the capture, so use OCR for the read-only gate.
        if (game.Kind == WindowKind.Game &&
            (!first.Text.Contains("8.057.106.17343", StringComparison.Ordinal) ||
             !second.Text.Contains("8.057.106.17343", StringComparison.Ordinal)))
        {
            scene = "版本未确认，识别暂停";
            detail = "连续两帧未能确认采样版本 8.057.106.17343。公开文字可回看，但布局和游戏输入不可用。";
        }
        if (scene is "牌桌" or "叫地主阶段")
            detail += DescribeVisibleControls(first.Lines, second.Lines);
        var loginConfirm = scene == "QQ登录窗口" &&
            HasLoginErrorConfirm(first.Lines, firstImage.Image) &&
            HasLoginErrorConfirm(second.Lines, secondImage.Image);
        var loginPasswordForm = scene == "QQ登录窗口" &&
            HasLoginPasswordForm(first.Lines, firstImage.Image) &&
            HasLoginPasswordForm(second.Lines, secondImage.Image);
        var loginQqQuick = scene == "QQ登录窗口" &&
            HasQqQuickLogin(first.Lines, firstImage.Image) &&
            HasQqQuickLogin(second.Lines, secondImage.Image);
        if (loginConfirm)
            detail += " 登录失败提示的“确定”按钮经双帧确认，可由用户选择后单次点击；密码与验证仍在官方窗口完成。";
        if (loginPasswordForm)
            detail += " 官方密码框和“马上登录”按钮经双帧确认；可用键盘选择聚焦密码框，然后直接在官方窗口键入密码，回助手选择登录。助手不接收键盘输入，登录窗截图不留存。";
        if (loginQqQuick)
            detail += " 检测到 QQ 游戏官方快捷登录入口；只会对当前可见账号头像发送一次点击，不读取密码。";
        var passButtonsVisible = scene == "牌桌" &&
            HasOwnPassButtons(first.Lines, firstImage.Image) &&
            HasOwnPassButtons(second.Lines, secondImage.Image);
        var playButtonsVisible = scene == "牌桌" &&
            HasOwnPlayButtons(first.Lines, firstImage.Image) &&
            HasOwnPlayButtons(second.Lines, secondImage.Image);
        int? ownTurnSeconds = null;
        if (passButtonsVisible || playButtonsVisible)
        {
            try
            {
                _timerReader ??= new TimerCandidateReader();
                ownTurnSeconds = _timerReader.ReadOwnCountdown(firstImage.Image,
                    secondImage.Image);
            }
            catch (Exception ex) { detail += $" 本人过牌钟面不可用：{ex.Message}"; }
        }
        if (passButtonsVisible)
            detail += ownTurnSeconds >= 8
                ? $" 本人不出按钮与钟面已双帧确认，剩余 {ownTurnSeconds} 秒；用户提交前还会复核手牌和画面。"
                : " 本人不出按钮可见，但钟面不足或不稳定；本次不提交过牌。";
        if (playButtonsVisible)
            detail += ownTurnSeconds >= 8
                ? $" 本人出牌按钮与钟面已双帧确认，剩余 {ownTurnSeconds} 秒；仍须核对用户已选中的手牌。"
                : " 本人出牌按钮可见，但钟面不足或不稳定；本次不提交出牌。";
        var ownCallVisible = false;
        var ownRobVisible = false;
        if (scene == "叫地主阶段")
        {
            try
            {
                _bidPhaseReader ??= new BidPhaseReader();
                ownCallVisible = HasOwnBidButtons(first.Lines, firstImage.Image) &&
                    HasOwnBidButtons(second.Lines, secondImage.Image) &&
                    _bidPhaseReader.IsPhase(firstImage.Image, false) &&
                    _bidPhaseReader.IsPhase(secondImage.Image, false);
                ownRobVisible = HasOwnRobButtons(first.Lines, firstImage.Image) &&
                    HasOwnRobButtons(second.Lines, secondImage.Image) &&
                    _bidPhaseReader.IsPhase(firstImage.Image, true) &&
                    _bidPhaseReader.IsPhase(secondImage.Image, true);
            }
            catch (Exception ex) { detail += $" 叫/抢地主按钮模板不可用：{ex.Message}"; }
        }
        var bidButtonsVisible = ownCallVisible != ownRobVisible;
        int? bidClock = null;
        if (bidButtonsVisible)
        {
            try
            {
                _timerReader ??= new TimerCandidateReader();
                bidClock = _timerReader.ReadAt(firstImage.Image, secondImage.Image, 75, -4);
                if (bidClock is not null)
                    detail += $" 本人叫/抢地主钟面候选：{bidClock} 秒。";
            }
            catch (Exception ex) { detail += $" 叫/抢地主钟面不可用：{ex.Message}"; }
        }
        var bidButtons = bidButtonsVisible && bidClock >= 4;
        if (scene == "叫地主阶段")
            detail += bidButtons
                ? $" 本人{(ownRobVisible ? "抢地主与不抢" : "叫地主与不叫")}按钮经双帧文字、位置、底色、阶段图形和钟面核对；仅在用户选中对应列表项后单次点击。"
                : " 叫/抢地主入口尚未通过按钮及剩余时间门槛；不发送点击。";
        var packClose = scene == "无限金卡礼包弹窗" &&
            HasWhiteX(firstImage.Image, 1038, 185) && HasWhiteX(secondImage.Image, 1038, 185);
        var seasonClose = scene == "赛季卡册宣传弹窗" &&
            HasWhiteX(firstImage.Image, 1038, 185) && HasWhiteX(secondImage.Image, 1038, 185);
        var seasonRefreshClose = scene == "赛季上新宣传弹窗" &&
            HasWhiteX(firstImage.Image, 1038, 185) && HasWhiteX(secondImage.Image, 1038, 185);
        var battleGuideClose = scene == "对局宝典弹窗" &&
            HasWhiteX(firstImage.Image, 1074, 212) && HasWhiteX(secondImage.Image, 1074, 212);
        var returnGiftClose = scene == "回归礼遇弹窗" &&
            HasWhiteX(firstImage.Image, 1073, 146) && HasWhiteX(secondImage.Image, 1073, 146);
        var flowerEventClose = scene == "种花活动弹窗" &&
            HasWhiteX(firstImage.Image, 1094, 165) && HasWhiteX(secondImage.Image, 1094, 165);
        var lanternEventClose = scene == "寻灯见礼弹窗" &&
            HasWhiteX(firstImage.Image, 1037, 184) && HasWhiteX(secondImage.Image, 1037, 184);
        var badLuckClose = scene == "换玩法建议弹窗" &&
            HasWhiteX(firstImage.Image, 1057, 171) && HasWhiteX(secondImage.Image, 1057, 171);
        var shareResultClose = scene == "战绩分享弹窗" &&
            HasWhiteX(firstImage.Image, 1068, 151) && HasWhiteX(secondImage.Image, 1068, 151);
        var piggyBankClose = scene == "储豆罐收费弹窗" &&
            HasWhiteX(firstImage.Image, 1054, 170) && HasWhiteX(secondImage.Image, 1054, 170);
        var beanAidConfirm = scene == "欢乐豆补助弹窗" &&
            HasBeanAidConfirm(first.Lines, firstImage.Image) &&
            HasBeanAidConfirm(second.Lines, secondImage.Image);
        var beanAidClose = scene == "欢乐豆补助弹窗" &&
            HasWhiteX(firstImage.Image, 915, 198) && HasWhiteX(secondImage.Image, 915, 198);
        var roomExit = (scene is "场次等候页" or "结算") &&
            HasRoomExitPixels(firstImage.Image) && HasRoomExitPixels(secondImage.Image);
        var replayControls = scene == "对局回放" &&
            HasReplayControls(firstImage.Image) && HasReplayControls(secondImage.Image);
        var cancelTrust = scene == "牌桌" &&
            HasCancelTrust(first.Lines) && HasCancelTrust(second.Lines);
        var settlementContinue = scene == "结算" &&
            HasSettlementContinue(firstImage.Image) &&
            HasSettlementContinue(secondImage.Image);
        HandRead? hand = null;
        if (game.Kind == WindowKind.Game && scene is ("牌桌" or "叫地主阶段" or "对局回放"))
        {
            try
            {
                _cardReader ??= new CardCandidateReader();
                hand = _cardReader.Read(firstImage.Image, secondImage.Image);
            }
            catch (Exception ex)
            {
                hand = new HandRead($"牌角候选不可用：{ex.Message}；所有牌局动作仍禁用。", []);
            }
        }
        if (game.Kind == WindowKind.Game && scene == "牌桌")
        {
            try
            {
                _timerReader ??= new TimerCandidateReader();
                var seconds = _timerReader.Read(firstImage.Image, secondImage.Image);
                if (seconds is not null)
                    detail += $" 本人按钮区钟面候选：{seconds} 秒；单独的钟面候选不能授权牌局动作。";
            }
            catch (Exception ex)
            {
                detail += $" 钟面候选不可用：{ex.Message}";
            }
        }
        return new ScanResult(scene, detail, second.Lines,
            first.ElapsedMs + second.ElapsedMs, secondImage.CapturedAt, game.Title,
            BitConverter.ToInt32(secondImage.Image, 18), Math.Abs(BitConverter.ToInt32(secondImage.Image, 22)),
            packClose, replayControls, seasonClose, seasonRefreshClose, battleGuideClose, hand)
        { CancelTrustConfirmed = cancelTrust,
            LoginConfirmConfirmed = loginConfirm,
            LoginPasswordFormConfirmed = loginPasswordForm,
            LoginQqQuickConfirmed = loginQqQuick,
            SettlementContinueConfirmed = settlementContinue,
            ReturnGiftCloseXConfirmed = returnGiftClose,
            FlowerEventCloseXConfirmed = flowerEventClose,
            LanternEventCloseXConfirmed = lanternEventClose,
            OwnBidButtonsConfirmed = bidButtons && ownCallVisible,
            OwnRobButtonsConfirmed = bidButtons && ownRobVisible,
            BadLuckCloseXConfirmed = badLuckClose,
            ShareResultCloseXConfirmed = shareResultClose,
            PiggyBankCloseXConfirmed = piggyBankClose,
            BeanAidConfirmConfirmed = beanAidConfirm,
              BeanAidCloseXConfirmed = beanAidClose,
              RoomExitConfirmed = roomExit,
              OwnPassConfirmed = passButtonsVisible && ownTurnSeconds >= 8,
              OwnPlayConfirmed = playButtonsVisible && ownTurnSeconds >= 8,
              OwnTurnSeconds = ownTurnSeconds };
    }

    public void Dispose()
    {
        _scanGate.Wait();
        try { _engine?.Dispose(); }
        finally
        {
            _engine = null;
            _scanGate.Release();
            _scanGate.Dispose();
        }
    }

    private static async Task<(byte[] Image, DateTimeOffset CapturedAt)> CaptureAsync(
        GameWindow game, AppSettings settings, CancellationToken cancellationToken)
    {
        var cache = Path.Combine(ProjectPaths.Root, "cache");
        Directory.CreateDirectory(cache);
        var keepScreenshot = settings.KeepScreenshots && game.Kind != WindowKind.Login;
        var file = keepScreenshot
            ? Path.Combine(ProjectPaths.Root, "captures", $"capture-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.bmp")
            : Path.Combine(cache, $"capture-{Guid.NewGuid():N}.bmp");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        try
        {
            var start = new ProcessStartInfo(ProjectPaths.CaptureExe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            start.ArgumentList.Add(game.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            start.ArgumentList.Add(game.Handle.ToInt64().ToString(System.Globalization.CultureInfo.InvariantCulture));
            start.ArgumentList.Add(file);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("按窗口捕获组件启动失败。 ");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill();
                throw new TimeoutException("按窗口捕获超时，扫描已停止。 ");
            }
            var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
            if (process.ExitCode != 0) throw new InvalidOperationException($"按窗口捕获失败：{stderr.Trim()}");
            var capturedAt = DateTimeOffset.Now;
            return (await File.ReadAllBytesAsync(file, cancellationToken), capturedAt);
        }
        finally { if (!keepScreenshot && File.Exists(file)) File.Delete(file); }
    }

    internal static (string Scene, string Detail) IdentifyScene(string text, WindowKind kind)
    {
        if (kind == WindowKind.Login)
            return ("QQ登录窗口", "QQ 官方登录窗口的可见文字可朗读。密码与验证码请在官方窗口输入；助手不保存凭据。仅经双帧核验的非敏感按钮可供用户选择。");
        if (kind == WindowKind.Hall)
        {
            if (text.Contains("点击切换到", StringComparison.Ordinal) &&
                text.Contains("知道啦", StringComparison.Ordinal))
                return ("大厅引导弹窗", "QQ 游戏大厅提示切换到经典版。当前可用“知道啦”关闭引导；背景内容暂不操作。");
            if (text.Contains("我的游戏", StringComparison.Ordinal) &&
                text.Contains("游戏库", StringComparison.Ordinal))
                return ("QQ游戏大厅", "大厅公开文字和坐标可浏览。已验证的欢乐斗地主入口可由用户单次点击；其他图文入口继续只读。");
            return ("状态不确定", "未识别出已验证的 QQ 游戏大厅界面；只提供文字阅读，不发送点击。");
        }
        if (text.Contains("太久没操作") || text.Contains("离开房间"))
        {
            var button = text.Contains("确定") ? "可见按钮：确定。" : "弹窗按钮未确认。";
            return ("超时离房弹窗", $"游戏提示长时间未操作，将离开房间。{button}背景画面被遮挡；除已核对的确定按钮外，游戏动作暂停。");
        }
        if (text.Contains("送你欢乐豆补助", StringComparison.Ordinal) &&
            text.Contains("基础送豆", StringComparison.Ordinal) &&
            text.Contains("确定", StringComparison.Ordinal))
            return ("欢乐豆补助弹窗", "游戏显示免费欢乐豆补助。可阅读数量；经双帧核对后，用方向键选择“确定”领取或关闭图形 X，再按回车。背景结算按钮暂停操作。");
        if (text.Contains("头衔晋升", StringComparison.Ordinal) &&
            text.Contains("解锁等级奖励", StringComparison.Ordinal) &&
            text.Contains("太棒了", StringComparison.Ordinal))
            return ("头衔晋升弹窗", "本局后出现头衔晋升提示。可阅读公开奖励文字，并选择“太棒了”关闭；分享入口仅供阅读，背景结算页暂停操作。");
        if (text.Contains("猪猪储豆罐", StringComparison.Ordinal) &&
            text.Contains("31000豆", StringComparison.Ordinal) &&
            text.Contains("开启", StringComparison.Ordinal))
            return ("储豆罐收费弹窗", "储豆罐弹窗含真实货币入口。充值、购买和开启均只读；仅右上角 X 经双帧核对后可关闭。");
        if (text.Contains("新版本更新内容", StringComparison.Ordinal))
        {
            var button = text.Contains("好的", StringComparison.Ordinal) ? "可见按钮：好的。" : "弹窗按钮未确认。";
            return ("版本更新说明弹窗", $"游戏显示新版本的公告内容。{button}先阅读弹窗正文；背景大厅不可操作。");
        }
        if (text.Contains("运气不好", StringComparison.Ordinal) &&
            text.Contains("洗个手再来", StringComparison.Ordinal) &&
            text.Contains("继续对局", StringComparison.Ordinal))
            return ("换玩法建议弹窗", "失败后游戏建议换玩法。‘继续对局’和‘去换场’可能入场，暂仅供阅读；右上角 X 经双帧核对后可关闭遮挡。");
        if (text.Contains("无敌是多么", StringComparison.Ordinal) &&
            text.Contains("经典新手场", StringComparison.Ordinal) &&
            text.Contains("明牌开始", StringComparison.Ordinal))
            return ("战绩分享弹窗", "战绩分享图遮挡结算。可能含头像、昵称和二维码；仅供本人阅读，右上角 X 经双帧核对后可关闭，不使用保存或分享入口。");
        if (text.Contains("回归礼遇", StringComparison.Ordinal) &&
            text.Contains("回归礼赠", StringComparison.Ordinal) &&
            text.Contains("累登奖励", StringComparison.Ordinal))
            return ("回归礼遇弹窗", "回归活动遮挡大厅。任务奖励仅供阅读；右上角图形 X 经双帧核对后可关闭，不点击领奖或特惠入口。");
        if (text.Contains("活动时间9.23-10.10", StringComparison.Ordinal) &&
            text.Contains("对局领取花袄种子", StringComparison.Ordinal) &&
            text.Contains("去种植", StringComparison.Ordinal))
            return ("种花活动弹窗", "种花活动遮挡大厅。活动文字仅供阅读；右上角图形 X 经双帧核对后可关闭，奖励及种植入口仅供阅读。");
        if (text.Contains("中秋点花灯", StringComparison.Ordinal) &&
            text.Contains("祈愿赢好礼", StringComparison.Ordinal) &&
            text.Contains("立即前往", StringComparison.Ordinal))
            return ("寻灯见礼弹窗", "中秋寻灯活动遮挡大厅。奖励文字仅供阅读；右上角图形 X 经双帧核对后可关闭，不点击活动入口。");
        if (text.Contains("赛季卡册", StringComparison.Ordinal) &&
            text.Contains("蔬菜精灵", StringComparison.Ordinal) &&
            text.Contains("立即前往", StringComparison.Ordinal))
            return ("赛季卡册宣传弹窗", "活动宣传遮挡大厅。可先阅读公开文字，再选择经双帧核对的右上角图形 X 关闭；不点击‘立即前往’。");
        if (text.Contains("赛季上新领好礼", StringComparison.Ordinal) &&
            text.Contains("立即前往", StringComparison.Ordinal) &&
            text.Contains("牌面", StringComparison.Ordinal))
            return ("赛季上新宣传弹窗", "赛季外观宣传遮挡大厅。可阅读活动文字，并选择经双帧核对的右上角图形 X 关闭；不点击‘立即前往’。");
        if (text.Contains("对局宝典", StringComparison.Ordinal) &&
            text.Contains("奖励一览", StringComparison.Ordinal) &&
            text.Contains("解锁奖励", StringComparison.Ordinal))
            return ("对局宝典弹窗", "对局奖励活动遮挡场次页。奖励和解锁内容可阅读；经双帧核对后可选择右上角 X 关闭。");
        if (text.Contains("卡包信息移动到此处", StringComparison.Ordinal) &&
            text.Contains("点击屏幕继续", StringComparison.Ordinal))
            return ("卡包引导弹窗", "游戏介绍结算页卡包位置。可选择提示文字继续查看结果；不会开始下一局。");
        if (text.Contains("全新结算界面", StringComparison.Ordinal) &&
            text.Contains("点击屏幕继续", StringComparison.Ordinal))
            return ("结算引导弹窗", "游戏介绍新的结算界面。可选择提示文字继续查看本局结果；不会开始下一局。");
        if (text.Contains("开始游戏", StringComparison.Ordinal) &&
            text.Contains("明牌开始", StringComparison.Ordinal) &&
            text.Contains("对局门票", StringComparison.Ordinal))
            return ("场次等候页", "当前位于经典场次等候页。可阅读门票与输赢上限；选择“开始游戏”会进入普通匹配并消耗欢乐豆。");
        if (text.Contains("好久不见", StringComparison.Ordinal) &&
            text.Contains("欢迎回来", StringComparison.Ordinal) &&
            text.Contains("领取", StringComparison.Ordinal))
            return ("回归礼物弹窗", "游戏显示回归礼物说明和“领取”按钮。领取会改变账号物品；只在用户选择该按钮后单次点击。背景大厅不可操作。");
        if (text.Contains("恭喜获得", StringComparison.Ordinal) &&
            text.Contains("我知道了", StringComparison.Ordinal))
            return ("领取结果弹窗", "游戏显示已领取的物品及“我知道了”按钮。物品名称由 OCR 逐项呈现；点击该按钮可关闭结果页。");
        if (text.Contains("首次对局奖励", StringComparison.Ordinal) &&
            text.Contains("去对局", StringComparison.Ordinal) &&
            text.Contains("免费对局", StringComparison.Ordinal))
            return ("首次对局奖励弹窗", "游戏列出多种对局入口。“去对局”是否直接入场未确认，“免费对局”实测会消耗生命值，当前均只读。可选择右上角 X 关闭。");
        if (text.Contains("周年庆惊喜红包", StringComparison.Ordinal) &&
            text.Contains("点击领取红包", StringComparison.Ordinal))
            return ("周年庆红包弹窗", "游戏显示领取红包活动。本机实测点击右上角 X 会领取限时钻石并进入结果页；请自行决定是否按回车。背景大厅不可操作。");
        if (text.Contains("无限金卡礼包", StringComparison.Ordinal) &&
            text.Contains("免费领取", StringComparison.Ordinal))
            return ("无限金卡礼包弹窗", "礼包包含免费领取项和标价 30 钻石的项目，领取按钮当前均只读。右上角图形 X 经双帧确认后可由用户关闭弹窗。");
        if (text.Contains("恭喜获得", StringComparison.Ordinal) &&
            text.Contains("限时钻石", StringComparison.Ordinal) &&
            text.Contains("开心收下", StringComparison.Ordinal))
            return ("周年庆领取结果弹窗", "游戏显示已获得的限时钻石及“开心收下”按钮。数量文字由 OCR 读取，用户可按回车关闭结果页。");
        if (System.Text.RegularExpressions.Regex.IsMatch(text, @"第\s*\d+\s*/\s*\d+\s*手") &&
            text.Contains("收藏", StringComparison.Ordinal) &&
            text.Contains("举报", StringComparison.Ordinal))
            return ("对局回放", "这是已结束对局的回放，不是实时牌局。可只读试验本人手牌候选；模板尚未跨对局验证，不能据此出牌。");
        if (text.Contains("去充值") || text.Contains("购买金币") || text.Contains("购买钻石")) return ("收费页面", "检测到收费内容。所有游戏动作暂停。 ");
        if (text.Contains("继续游戏", StringComparison.Ordinal) &&
            (text.Contains("回放", StringComparison.Ordinal) ||
             text.Contains("明牌开始", StringComparison.Ordinal)) &&
            text.Contains("按住查看桌面", StringComparison.Ordinal))
            return ("结算", "当前对局已结束。可阅读可见的豆子变化；选择“继续游戏”将开始下一局普通匹配。");
        if ((text.Contains("胜利") || text.Contains("失败")) && text.Contains("总分")) return ("结算", "结算文字仅供复核；未验证金额识别。 ");
        if (text.Contains("出牌") && (text.Contains("不出") || text.Contains("提示"))) return ("牌桌", "发现本人牌桌按钮；出牌和过牌须经按钮、钟面、手牌及临点击前的额外校验。 ");
        if (text.Contains("叫地主") && text.Contains("不叫") ||
            text.Contains("抢地主") || text.Contains("不抢"))
            return ("叫地主阶段", "发现叫地主或抢地主相关文字；只有本人叫地主与不叫按钮通过双帧核对后才可选择，抢地主仍只读。 ");
        if (text.Contains("排序", StringComparison.Ordinal) &&
            text.Contains("倍", StringComparison.Ordinal) &&
            (text.Contains("托管", StringComparison.Ordinal) ||
             text.Contains("不叫", StringComparison.Ordinal) ||
             text.Contains("不出", StringComparison.Ordinal)))
            return ("牌桌", "牌桌仍在进行，可只读查看本人可见手牌；轮次和按钮未可靠确认，游戏输入禁用。");
        if (text.Contains("新手场") && text.Contains("普通场")) return ("场次选择", "场次可能消耗豆子。名称、底分和豆子范围经核对后才可由用户确认进入；其余文字只读。 ");
        if (text.Contains("经典斗地主") && text.Contains("不洗牌斗地主")) return ("模式选择", "可查看模式。已核对的经典斗地主入口可由用户单次点击；其他模式只读。 ");
        if (text.Contains("五人斗地主") && text.Contains("棋牌合集")) return ("游戏大厅", "已找到斗地主合集入口，用户可在列表中选择后单次点击。 ");
        return ("状态不确定", "未识别出已验证场景。请保留当前局面，勿提交游戏动作。 ");
    }

    internal static string DescribeVisibleControls(IReadOnlyList<OcrLine> first,
        IReadOnlyList<OcrLine> second)
    {
        static bool Button(IReadOnlyList<OcrLine> lines, string name, int minX, int maxX) =>
            lines.Any(line => line.Text.Trim() == name && line.Confidence >= 0.85 &&
                line.X >= minX && line.X + line.Width <= maxX &&
                line.Y >= 420 && line.Y + line.Height <= 505);
        static bool Pair(IReadOnlyList<OcrLine> lines, string left, string right) =>
            Button(lines, left, 350, 610) && Button(lines, right, 680, 900);

        if (Pair(first, "叫地主", "不叫") && Pair(second, "叫地主", "不叫"))
            return " 连续两帧可见本人叫地主与不叫按钮；仍需独立核对按钮底色和钟面。";
        if (Pair(first, "抢地主", "不抢") && Pair(second, "抢地主", "不抢"))
            return " 连续两帧可见本人抢地主与不抢按钮；仍需独立核对按钮文字阶段、底色和钟面。";
        if (Pair(first, "出牌", "提示") && Pair(second, "出牌", "提示"))
            return " 连续两帧可见本人出牌与提示按钮；仍须核对底色、钟面和已选中的牌。";
        if (Button(first, "不出", 610, 745) && Button(first, "提示", 770, 900) &&
            Button(second, "不出", 610, 745) && Button(second, "提示", 770, 900))
            return " 连续两帧可见本人不出与提示按钮；仍须核对底色、钟面和点击后反馈。";
        return " 当前未连续确认本人操作按钮；可能是对手回合或画面正在变化。";
    }

    private static bool HasLoginErrorConfirm(IReadOnlyList<OcrLine> lines, byte[] bmp)
    {
        if (bmp.Length < 54 + 318 * 432 * 4 ||
            BitConverter.ToInt32(bmp, 18) != 318 ||
            BitConverter.ToInt32(bmp, 22) != -432 ||
            !lines.Any(line => line.Text.Contains("QQ登录失败", StringComparison.Ordinal)) ||
            !lines.Any(line => line.Text.Contains("重新输入密码", StringComparison.Ordinal)) ||
            !lines.Any(line => line.Text.Trim() == "确定" && line.Confidence >= 0.85 &&
                line.X + line.Width / 2 is >= 140 and <= 175 &&
                line.Y + line.Height / 2 is >= 350 and <= 375))
            return false;
        foreach (var (x, y) in new[] { (80, 360), (100, 360), (220, 360) })
        {
            var offset = 54 + (y * 318 + x) * 4;
            var red = bmp[offset + 2];
            var green = bmp[offset + 1];
            var blue = bmp[offset];
            if (red < 235 || green < 235 || blue < 235 ||
                Math.Max(red, Math.Max(green, blue)) - Math.Min(red, Math.Min(green, blue)) > 12)
                return false;
        }
        return true;
    }

    private static bool HasLoginPasswordForm(IReadOnlyList<OcrLine> lines, byte[] bmp)
    {
        if (bmp.Length < 54 + 318 * 432 * 4 ||
            BitConverter.ToInt32(bmp, 18) != 318 ||
            BitConverter.ToInt32(bmp, 22) != -432 ||
            !lines.Any(line => line.Text.Trim() == "马上登录" && line.Confidence >= 0.9 &&
                line.X + line.Width / 2 is >= 145 and <= 175 &&
                line.Y + line.Height / 2 is >= 275 and <= 300) ||
            !lines.Any(line => line.Text.Contains("记住密码", StringComparison.Ordinal) &&
                line.Y + line.Height / 2 is >= 230 and <= 255))
            return false;
        foreach (var (x, y, white) in new[]
            { (100, 210, true), (210, 210, true), (100, 286, false), (210, 286, false) })
        {
            var offset = 54 + (y * 318 + x) * 4;
            var red = bmp[offset + 2];
            var green = bmp[offset + 1];
            var blue = bmp[offset];
            if (white ? red < 245 || green < 245 || blue < 245 :
                red is < 95 or > 160 || green is < 90 or > 155 || blue < 180 ||
                blue < red + 55)
                return false;
        }
        return true;
    }

    private static bool HasQqQuickLogin(IReadOnlyList<OcrLine> lines, byte[] bmp)
    {
        if (bmp.Length < 54 + 318 * 432 * 4 ||
            BitConverter.ToInt32(bmp, 18) != 318 ||
            BitConverter.ToInt32(bmp, 22) != -432 ||
            !lines.Any(line => line.Text.Contains("检测到您的账号", StringComparison.Ordinal) &&
                line.Text.Contains("点击头像登录", StringComparison.Ordinal) &&
                line.Confidence >= 0.9 && line.Y is >= 265 and <= 295) ||
            !lines.Any(line => line.Text.Contains("其他QQ账号登录", StringComparison.Ordinal) &&
                line.Confidence >= 0.85 && line.Y is >= 310 and <= 345) ||
            !lines.Any(line => line.Text.Contains("切换微信登录", StringComparison.Ordinal) &&
                line.Confidence >= 0.85 && line.Y is >= 360 and <= 400))
            return false;
        static (int Red, int Green, int Blue) Pixel(byte[] data, int x, int y)
        {
            var offset = 54 + (y * 318 + x) * 4;
            return (data[offset + 2], data[offset + 1], data[offset]);
        }
        var ring = Pixel(bmp, 158, 138);
        var background = Pixel(bmp, 140, 138);
        return ring.Red >= 235 && ring.Green >= 235 && ring.Blue >= 235 &&
            background.Blue >= 220 && background.Red is >= 110 and <= 205 &&
            background.Blue >= background.Red + 35;
    }

    private static bool HasCancelTrust(IReadOnlyList<OcrLine> lines) =>
        lines.Any(line => line.Text.Trim() == "取消托管" && line.Confidence >= 0.9 &&
            line.X >= 565 && line.X + line.Width <= 705 &&
            line.Y >= 575 && line.Y + line.Height <= 640);

    private static bool HasOwnPassButtons(IReadOnlyList<OcrLine> lines, byte[] bmp)
    {
        static bool TextAt(IReadOnlyList<OcrLine> items, string name, int left, int right) =>
            items.Any(line => line.Text.Trim() == name && line.Confidence >= 0.90 &&
                line.X >= left && line.X + line.Width <= right &&
                line.Y >= 425 && line.Y + line.Height <= 490);
        if (!TextAt(lines, "不出", 625, 745) || !TextAt(lines, "提示", 775, 900) ||
            bmp.Length < 54 + 1280 * 720 * 4 || BitConverter.ToInt32(bmp, 18) != 1280 ||
            BitConverter.ToInt32(bmp, 22) != -720 || !HasLiveTableFooter(bmp)) return false;
        foreach (var (x, y) in new[] { (640, 440), (715, 440), (640, 475), (715, 475) })
        {
            var color = PixelColor(bmp, x, y);
            if (color.Blue < 215 || color.Blue < color.Red + 80 ||
                color.Blue < color.Green + 35 || color.Red is < 70 or > 160)
                return false;
        }
        return true;
    }

    private static bool HasOwnPlayButtons(IReadOnlyList<OcrLine> lines, byte[] bmp)
    {
        static bool TextAt(IReadOnlyList<OcrLine> items, string name, int left, int right) =>
            items.Any(line => line.Text.Trim() == name && line.Confidence >= 0.90 &&
                line.X >= left && line.X + line.Width <= right &&
                line.Y >= 425 && line.Y + line.Height <= 490);
        return TextAt(lines, "出牌", 385, 500) &&
            TextAt(lines, "提示", 775, 900) && HasLiveTableFooter(bmp) &&
            HasOwnPlayButtonPixels(bmp);
    }

    private static bool HasOwnPlayButtonPixels(byte[] bmp)
    {
        if (bmp.Length < 54 + 1280 * 720 * 4 || BitConverter.ToInt32(bmp, 18) != 1280 ||
            BitConverter.ToInt32(bmp, 22) != -720) return false;
        foreach (var (x, y) in new[] { (390, 440), (470, 440), (390, 475), (470, 475) })
        {
            var color = PixelColor(bmp, x, y);
            if (color.Red < 230 || color.Green is < 135 or > 235 ||
                color.Blue is < 45 or > 115 || color.Red < color.Green + 30)
                return false;
        }
        return true;
    }

    private static bool HasOwnBidButtons(IReadOnlyList<OcrLine> lines, byte[] bmp)
    {
        static bool TextAt(IReadOnlyList<OcrLine> items, string name, int left, int right) =>
            items.Any(line => line.Text.Trim() == name && line.Confidence >= 0.90 &&
                line.X >= left && line.X + line.Width <= right &&
                line.Y >= 425 && line.Y + line.Height <= 490);
        return TextAt(lines, "叫地主", 435, 570) &&
            TextAt(lines, "不叫", 695, 820) && HasBidButtonPixels(bmp);
    }

    private static bool HasOwnRobButtons(IReadOnlyList<OcrLine> lines, byte[] bmp)
    {
        static bool TextAt(IReadOnlyList<OcrLine> items, string name, int left, int right) =>
            items.Any(line => line.Text.Trim() == name && line.Confidence >= 0.90 &&
                line.X >= left && line.X + line.Width <= right &&
                line.Y >= 425 && line.Y + line.Height <= 490);
        return TextAt(lines, "抢地主", 435, 570) &&
            TextAt(lines, "不抢", 695, 820) && HasBidButtonPixels(bmp);
    }

    private static bool HasBidButtonPixels(byte[] bmp)
    {
        if (
            bmp.Length < 54 + 1280 * 720 * 4 || BitConverter.ToInt32(bmp, 18) != 1280 ||
            BitConverter.ToInt32(bmp, 22) != -720) return false;
        static (int Red, int Green, int Blue) Color(byte[] data, int x, int y)
        {
            var offset = 54 + (y * 1280 + x) * 4;
            return (data[offset + 2], data[offset + 1], data[offset]);
        }
        var orange = Color(bmp, 500, 475);
        var blue = Color(bmp, 760, 475);
        return orange.Red >= 220 && orange.Green >= 125 && orange.Green <= 205 &&
            orange.Blue <= 110 && orange.Red >= orange.Green + 60 &&
            blue.Blue >= 220 && blue.Red >= 75 && blue.Red <= 165 &&
            blue.Green >= 100 && blue.Blue >= blue.Green + 45;
    }

    private static bool HasSettlementContinue(byte[] bmp)
    {
        if (bmp.Length < 54 + 1280 * 720 * 4 || BitConverter.ToInt32(bmp, 18) != 1280 ||
            BitConverter.ToInt32(bmp, 22) != -720) return false;
        foreach (var (x, y) in new[] { (680, 590), (700, 590), (800, 590),
                     (680, 620), (740, 620), (800, 620) })
        {
            var offset = 54 + (y * 1280 + x) * 4;
            var blue = bmp[offset];
            var green = bmp[offset + 1];
            var red = bmp[offset + 2];
            if (red < 220 || green < 160 || blue > 120 ||
                red < green + 10 || green < blue + 70) return false;
        }
        return true;
    }

    private static bool HasBeanAidConfirm(IReadOnlyList<OcrLine> lines, byte[] bmp)
    {
        if (bmp.Length < 54 + 1280 * 720 * 4 || BitConverter.ToInt32(bmp, 18) != 1280 ||
            BitConverter.ToInt32(bmp, 22) != -720 ||
            !lines.Any(line => line.Text.Trim() == "确定" && line.Confidence >= 0.9 &&
                line.X >= 575 && line.X + line.Width <= 675 &&
                line.Y >= 545 && line.Y + line.Height <= 605)) return false;
        foreach (var (x, y) in new[] { (550, 560), (700, 560), (550, 585), (700, 585) })
        {
            var color = PixelColor(bmp, x, y);
            if (color.Red < 220 || color.Green < 155 || color.Green > 240 ||
                color.Blue > 100 || color.Red < color.Green + 10) return false;
        }
        return true;
    }

    private static bool HasRoomExitPixels(byte[] bmp)
    {
        if (bmp.Length < 54 + 1280 * 720 * 4 || BitConverter.ToInt32(bmp, 18) != 1280 ||
            BitConverter.ToInt32(bmp, 22) != -720) return false;
        var bright = 0;
        for (var y = 38; y < 81; y += 2)
            for (var x = 1230; x < 1266; x += 2)
            {
                var color = PixelColor(bmp, x, y);
                if ((color.Red + color.Green + color.Blue) / 3 >= 175) bright++;
            }
        return bright is >= 125 and <= 180;
    }

    private static (int Red, int Green, int Blue) PixelColor(byte[] data, int x, int y)
    {
        var offset = 54 + (y * 1280 + x) * 4;
        return (data[offset + 2], data[offset + 1], data[offset]);
    }

    private static bool HasWhiteX(byte[] bmp, int x, int y)
    {
        if (bmp.Length < 54 || BitConverter.ToInt32(bmp, 18) != 1280 ||
            BitConverter.ToInt32(bmp, 22) != -720) return false;
        static int Brightness(byte[] data, int px, int py)
        {
            var offset = 54 + (py * 1280 + px) * 4;
            return (data[offset] + data[offset + 1] + data[offset + 2]) / 3;
        }
        if (bmp.Length < 54 + 1280 * 720 * 4) return false;
        var white = new[] { (0, 0), (-10, -10), (10, 10), (-10, 10), (10, -10) };
        var dark = new[] { (-10, 0), (10, 0), (0, -10), (0, 10) };
        return white.All(point => Brightness(bmp, x + point.Item1, y + point.Item2) >= 210) &&
            dark.All(point => Brightness(bmp, x + point.Item1, y + point.Item2) <= 160);
    }

    private static bool HasReplayControls(byte[] bmp)
    {
        if (bmp.Length < 54 + 1280 * 720 * 4 || BitConverter.ToInt32(bmp, 18) != 1280 ||
            BitConverter.ToInt32(bmp, 22) != -720) return false;
        foreach (var x in new[] { 1140, 1175 })
        {
            var offset = 54 + (699 * 1280 + x) * 4;
            var blue = bmp[offset];
            var green = bmp[offset + 1];
            var red = bmp[offset + 2];
            if (green < 170 || green <= red + 40 || green <= blue + 35) return false;
        }
        return true;
    }

    private static bool HasLiveTableFooter(byte[] bmp)
    {
        if (bmp.Length < 54 + 1280 * 720 * 4 || BitConverter.ToInt32(bmp, 18) != 1280 ||
            BitConverter.ToInt32(bmp, 22) != -720) return false;
        foreach (var x in new[] { 1175, 1200 })
        {
            var offset = 54 + (699 * 1280 + x) * 4;
            var blue = bmp[offset];
            var green = bmp[offset + 1];
            var red = bmp[offset + 2];
            if (green < 170 || green <= red + 60 || green <= blue + 40) return false;
        }
        return true;
    }
}
