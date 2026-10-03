using System.Collections;
using System.Reflection;
using System.Text.Json;

var source = args.Length > 0 ? args[0] : Path.Combine("samples", "private", "replay-step0.bmp");
var comparison = args.Length > 1 ? args[1] : source;
var assembly = Assembly.Load("HappyDDZ.Assistant");
var type = assembly.GetType("HappyDDZ.Assistant.CardCandidateReader")
    ?? throw new InvalidOperationException("CardCandidateReader unavailable.");
var testModel = Environment.GetEnvironmentVariable("CARDPROBE_MODEL");
var reader = testModel is null
    ? Activator.CreateInstance(type, nonPublic: true)
    : Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
        null, [testModel], null);
if (reader is null) throw new InvalidOperationException("Could not initialize card reader.");
var method = type.GetMethod("Read", BindingFlags.Instance | BindingFlags.Public)
    ?? throw new InvalidOperationException("Read method unavailable.");
if (args.Length == 1 && args[0] == "--speech-check")
{
    var speechType = assembly.GetType("HappyDDZ.Assistant.ZdsrSpeech")!;
    using var speech = (IDisposable)Activator.CreateInstance(speechType, nonPublic: true)!;
    var resultCode = (int)speechType.GetMethod("TrySpeak")!.Invoke(speech,
        ["欢乐斗地主助手语音测试。操作结果朗读已接入争渡。"])!;
    var stateCode = (int)speechType.GetMethod("GetState")!.Invoke(speech, null)!;
    Console.WriteLine($"Zhengdu API Speak={resultCode}; state={stateCode}; " + speechType.GetProperty("Status")!.GetValue(speech));
    if (resultCode != 0 || stateCode is not (3 or 4)) throw new Exception("Zhengdu speech API check failed.");
    return;
}
if (args.Length == 1 && args[0] == "--selection-refresh-check")
{
    Exception? failure = null;
    var uiThread = new Thread(() =>
    {
        try
        {
            var formType = assembly.GetType("HappyDDZ.Assistant.MainForm")!;
            using var form = (System.Windows.Forms.Form)Activator.CreateInstance(formType, nonPublic: true)!;
            var scanType = assembly.GetType("HappyDDZ.Assistant.ScanResult")!;
            var lineType = assembly.GetType("HappyDDZ.Assistant.OcrLine")!;
            var planner = assembly.GetType("HappyDDZ.Assistant.HandClickPlanner")!;
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var display = formType.GetMethod("DisplayScan", flags)!;
            FieldInfo Field(string name) => formType.GetField(name, flags)!;
            object ReadHand(string file)
            {
                var data = File.ReadAllBytes(Path.Combine("samples", "private", file));
                return method.Invoke(reader, [data, data])!;
            }
            object Scan(object hand)
            {
                var scan = Activator.CreateInstance(scanType, "牌桌", "", Array.CreateInstance(lineType, 0),
                    0d, DateTimeOffset.Now, "欢乐斗地主", 1280, 720, false, false, false, false, false, hand)!;
                scanType.GetProperty("OwnPlayConfirmed")!.SetValue(scan, true);
                return scan;
            }
            var original = ReadHand("two-raised-20261003-123641-baseline-a.bmp");
            var raised = ReadHand("two-raised-20261003-123641-after-two-a.bmp");
            var plan = planner.GetMethod("Create")!.Invoke(null, [Scan(original), 2])!;
            void Seed(DateTimeOffset at)
            {
                Field("_raisedPlan").SetValue(form, plan);
                Field("_raisedAt").SetValue(form, at);
                var positions = (SortedSet<int>)Field("_raisedPositions").GetValue(form)!;
                positions.Clear(); positions.UnionWith([2, 8]);
            }
            bool CanPlay() => ((System.Windows.Forms.Button)Field("_play").GetValue(form)!).Enabled;
            Seed(DateTimeOffset.Now);
            display.Invoke(form, [Scan(raised)]);
            if (Field("_raisedPlan").GetValue(form) != plan || !CanPlay())
                throw new Exception("Fresh matching selection was lost on turn refresh.");
            Seed(DateTimeOffset.Now.AddSeconds(-31));
            display.Invoke(form, [Scan(raised)]);
            if (Field("_raisedPlan").GetValue(form) is not null || CanPlay())
                throw new Exception("Expired selection enabled play.");
            Seed(DateTimeOffset.Now);
            raised.GetType().GetProperty("ObservedCount")!.SetValue(raised, 16);
            display.Invoke(form, [Scan(raised)]);
            if (Field("_raisedPlan").GetValue(form) is not null || CanPlay())
                throw new Exception("Changed hand count retained selection.");
            Console.WriteLine("Turn refresh: fresh matching selection retained; expired and changed-count selections disable play.");
        }
        catch (Exception ex) { failure = ex; }
    });
    uiThread.SetApartmentState(ApartmentState.STA);
    uiThread.Start(); uiThread.Join();
    if (failure is not null) throw new Exception("Selection refresh check failed.", failure);
    return;
}
if (args.Length == 1 && args[0] == "--installation-check")
{
    var installType = assembly.GetType("HappyDDZ.Assistant.QqHallInstallation")!;
    var parse = installType.GetMethod("ParseIconPath", BindingFlags.Static | BindingFlags.NonPublic)!;
    string? Parse(string input) => (string?)parse.Invoke(null, [input]);
    var expected = @"C:\Program Files (x86)\Tencent\QQGameTempest\QQGame.exe";
    if (Parse('"' + expected + "\",0") != expected || Parse(expected) != expected ||
        Parse("QQGame.exe") is not null || Parse(expected + " --run") is not null ||
        Parse(@"C:\somewhere\Other.exe") is not null)
        throw new Exception("Installed QQ launcher path parsing failed.");
    var installed = (string)installType.GetMethod("FindLauncher")!.Invoke(null, null)!;
    if (!File.Exists(installed) || Path.GetFileName(installed) != "QQGame.exe")
        throw new Exception("Installed QQ launcher is unavailable.");
    Console.WriteLine("Installed QQ launcher found: " + installed);
    Console.WriteLine("Quoted registry icon paths accepted; relative paths, other executables and arguments rejected.");
    return;
}
if (args.Length == 1 && args[0] == "--identity-check")
{
    var cardType = assembly.GetType("HappyDDZ.Assistant.HandCard")!;
    var gate = assembly.GetType("HappyDDZ.Assistant.HandIdentity")!.GetMethod("IsConsistent")!;
    bool Valid(params (string Rank, string Suit)[] input)
    {
        var list = Array.CreateInstance(cardType, input.Length);
        for (var i = 0; i < input.Length; i++)
            list.SetValue(Activator.CreateInstance(cardType, i + 1, input[i].Rank, input[i].Suit), i);
        return (bool)gate.Invoke(null, [list])!;
    }
    if (!Valid(("大王", "无花色"), ("小王", "无花色"), ("A", "黑桃"), ("A", "红桃")) ||
        !Valid(("未知", "未知"), ("未知", "未知")) ||
        Valid(("小王", "无花色"), ("小王", "无花色")) ||
        Valid(("A", "黑桃"), ("A", "黑桃")) ||
        Valid(("A", "黑桃"), ("A", "红桃"), ("A", "梅花"), ("A", "方块"), ("A", "未知")))
        throw new Exception("Deck consistency gate failed.");
    Console.WriteLine("Hand identity: duplicate jokers, duplicate physical cards, and five equal ranks rejected; partial unknowns retained.");
    return;
}
if (args.Length == 1 && args[0] == "--login-check")
{
    var lineType = assembly.GetType("HappyDDZ.Assistant.OcrLine")!;
    var scanner = assembly.GetType("HappyDDZ.Assistant.ReadOnlyScanner")!;
    var gate = scanner.GetMethod("HasLoginErrorConfirm", BindingFlags.Static | BindingFlags.NonPublic)!;
    var scanType = assembly.GetType("HappyDDZ.Assistant.ScanResult")!;
    var navigation = assembly.GetType("HappyDDZ.Assistant.OcrNavigation")!;
    object Line(string text, int x, int y, int width, int height) =>
        Activator.CreateInstance(lineType, text, 0.99, x, y, width, height)!;
    Array Lines(params object[] values)
    {
        var array = Array.CreateInstance(lineType, values.Length);
        for (var i = 0; i < values.Length; i++) array.SetValue(values[i], i);
        return array;
    }
    var lines = Lines(Line("QQ登录失败", 20, 95, 120, 20),
        Line("重新输入密码", 20, 112, 140, 20), Line("确定", 142, 350, 32, 22));
    var bmp = File.ReadAllBytes(Path.Combine("samples", "private", "qq-login-error.bmp"));
    bool Allowed(Array entries, byte[] image) => (bool)gate.Invoke(null, [entries, image])!;
    var damaged = (byte[])bmp.Clone();
    var offset = 54 + (360 * 318 + 80) * 4;
    damaged[offset] = damaged[offset + 1] = damaged[offset + 2] = 0;
    if (!Allowed(lines, bmp) || Allowed(lines, damaged) ||
        Allowed(Lines(Line("确定", 142, 350, 32, 22)), bmp))
        throw new Exception("Login confirmation text and pixel gate failed.");
    object Scan(bool confirmed)
    {
        var result = Activator.CreateInstance(scanType, "QQ登录窗口", "", lines, 0d,
            DateTimeOffset.Now, "主账号登录窗口", 318, 432,
            false, false, false, false, false, null)!;
        scanType.GetProperty("LoginConfirmConfirmed")!.SetValue(result, confirmed);
        return result;
    }
    int Clickable(object scan) => ((IEnumerable)navigation.GetMethod("Build")!.Invoke(null, [scan])!)
        .Cast<object>().Count(entry => (bool)entry.GetType().GetProperty("CanClick")!.GetValue(entry)!);
    if (Clickable(Scan(true)) != 1 || Clickable(Scan(false)) != 0)
        throw new Exception("Login navigation gate failed.");
    var passwordGate = scanner.GetMethod("HasLoginPasswordForm",
        BindingFlags.Static | BindingFlags.NonPublic)!;
    var passwordLines = Lines(Line("记住密码", 70, 232, 70, 20),
        Line("马上登录", 130, 275, 60, 22));
    var passwordBmp = File.ReadAllBytes(Path.Combine("samples", "private",
        "qq-login-after-assistant-confirm.bmp"));
    bool PasswordAllowed(Array entries, byte[] image) =>
        (bool)passwordGate.Invoke(null, [entries, image])!;
    var brokenPasswordBmp = (byte[])passwordBmp.Clone();
    var loginButtonOffset = 54 + (286 * 318 + 100) * 4;
    brokenPasswordBmp[loginButtonOffset] = 0;
    if (!PasswordAllowed(passwordLines, passwordBmp) ||
        PasswordAllowed(passwordLines, bmp) ||
        PasswordAllowed(passwordLines, brokenPasswordBmp))
        throw new Exception("Password field and login button gate failed.");
    var passwordScan = Activator.CreateInstance(scanType, "QQ登录窗口", "", passwordLines,
        0d, DateTimeOffset.Now, "主账号登录窗口", 318, 432,
        false, false, false, false, false, null)!;
    scanType.GetProperty("LoginPasswordFormConfirmed")!.SetValue(passwordScan, true);
    if (Clickable(passwordScan) != 2)
        throw new Exception("Keyboard password focus/login entries failed.");
    var quickGate = scanner.GetMethod("HasQqQuickLogin",
        BindingFlags.Static | BindingFlags.NonPublic)!;
    var quickLines = Lines(Line("检测到您的账号，请点击头像登录", 67, 277, 184, 15),
        Line("其他QQ账号登录>", 121, 321, 102, 16),
        Line("切换微信登录", 130, 373, 96, 18));
    var quickBmp = File.ReadAllBytes(Path.Combine("samples", "private",
        "qq-login-chooser-after-qq.bmp"));
    bool QuickAllowed(Array entries, byte[] image) =>
        (bool)quickGate.Invoke(null, [entries, image])!;
    var brokenQuick = (byte[])quickBmp.Clone();
    var quickOffset = 54 + (138 * 318 + 158) * 4;
    brokenQuick[quickOffset] = brokenQuick[quickOffset + 1] = brokenQuick[quickOffset + 2] = 0;
    if (!QuickAllowed(quickLines, quickBmp) || QuickAllowed(quickLines, brokenQuick) ||
        QuickAllowed(Lines(Line("其他QQ账号登录>", 121, 321, 102, 16)), quickBmp))
        throw new Exception("QQ quick-login text and image gate failed.");
    var quickScan = Activator.CreateInstance(scanType, "QQ登录窗口", "", quickLines,
        0d, DateTimeOffset.Now, "主账号登录窗口", 318, 432,
        false, false, false, false, false, null)!;
    scanType.GetProperty("LoginQqQuickConfirmed")!.SetValue(quickScan, true);
    if (Clickable(quickScan) != 1)
        throw new Exception("QQ quick-login navigation gate failed.");
    Console.WriteLine("QQ login: quick QQ, confirm, password focus, and login gates PASS; changed pixels/text rejected.");
    return;
}
if (args.Length == 1 && args[0] == "--joker-check")
{
    var path = Path.Combine("samples", "private", "match-01-start.bmp");
    var original = File.ReadAllBytes(path);
    var baseline = method.Invoke(reader, [original, original])!;
    var baselineCards = ((IEnumerable)baseline.GetType().GetProperty("Cards")!
        .GetValue(baseline)!).Cast<object>().ToArray();
    var layoutLeft = (int)baseline.GetType().GetProperty("LayoutLeft")!.GetValue(baseline)!;
    if (baselineCards.Length != 17 || layoutLeft < 200)
        throw new Exception("Joker test baseline hand unavailable.");
    var damaged = (byte[])original.Clone();
    const int width = 1280;
    var laterCardLeft = layoutLeft + 7 * 41;
    for (var y = 610; y < 660; y++)
    for (var x = laterCardLeft + 3; x < laterCardLeft + 31; x++)
    {
        var offset = 54 + (y * width + x) * 4;
        damaged[offset] = damaged[offset + 1] = damaged[offset + 2] = 0;
    }
    var probed = method.Invoke(reader, [damaged, damaged])!;
    var probedCards = ((IEnumerable)probed.GetType().GetProperty("Cards")!
        .GetValue(probed)!).Cast<object>().ToArray();
    if (probedCards.Length != 17)
        throw new Exception("Dark-strip negative changed the hand count.");
    var laterRank = (string)probedCards[7].GetType().GetProperty("Rank")!.GetValue(probedCards[7])!;
    if (laterRank is "大王" or "小王")
        throw new Exception("A dark strip on card 8 became a false joker.");
    Console.WriteLine($"Joker position gate: real leading jokers retained; dark card-8 strip read as {laterRank}, not a joker.");
    return;
}
if (args.Length == 1 && args[0] == "--control-check")
{
    var scanner = assembly.GetType("HappyDDZ.Assistant.ReadOnlyScanner")!;
    var lineType = assembly.GetType("HappyDDZ.Assistant.OcrLine")!;
    var describe = scanner.GetMethod("DescribeVisibleControls", BindingFlags.Static | BindingFlags.NonPublic)!;
    object Line(string value, int x, int y, int width = 90) =>
        Activator.CreateInstance(lineType, value, 0.99, x, y, width, 45)!;
    Array Lines(params object[] values)
    {
        var array = Array.CreateInstance(lineType, values.Length);
        for (var i = 0; i < values.Length; i++) array.SetValue(values[i], i);
        return array;
    }
    var bid = Lines(Line("叫地主", 454, 437), Line("不叫", 719, 434));
    var rob = Lines(Line("抢地主", 453, 437, 104), Line("不抢", 722, 438, 79));
    var play = Lines(Line("出牌", 398, 437), Line("提示", 788, 437));
    var pass = Lines(Line("不出", 645, 437, 80), Line("提示", 794, 437, 83));
    var opponent = Lines(Line("叫地主", 792, 320), Line("不叫", 319, 323));
    string Read(Array a, Array b) => (string)describe.Invoke(null, [a, b])!;
    if (!Read(bid, bid).Contains("叫地主与不叫") ||
        !Read(rob, rob).Contains("抢地主与不抢") ||
        !Read(play, play).Contains("出牌与提示") ||
        !Read(pass, pass).Contains("不出与提示") ||
        !Read(bid, opponent).Contains("未连续确认") ||
        !Read(opponent, opponent).Contains("未连续确认"))
        throw new Exception("Visible control gate failed.");
    var bidGate = scanner.GetMethod("HasOwnBidButtons", BindingFlags.Static | BindingFlags.NonPublic)!;
    var passGate = scanner.GetMethod("HasOwnPassButtons", BindingFlags.Static | BindingFlags.NonPublic)!;
    var playGate = scanner.GetMethod("HasOwnPlayButtons", BindingFlags.Static | BindingFlags.NonPublic)!;
    var passImage = File.ReadAllBytes(Path.Combine("samples", "private", "card-pilot-second.bmp"));
    if (!(bool)passGate.Invoke(null, [pass, passImage])! ||
        (bool)passGate.Invoke(null, [opponent, passImage])! ||
        (bool)passGate.Invoke(null, [pass, File.ReadAllBytes(Path.Combine("samples", "private", "match-03-current.bmp"))])!)
        throw new Exception("Own pass button text and pixel gate failed.");
    if (!(bool)playGate.Invoke(null, [play, passImage])! ||
        (bool)playGate.Invoke(null, [opponent, passImage])! ||
        (bool)playGate.Invoke(null, [play, File.ReadAllBytes(Path.Combine("samples", "private", "match-03-current.bmp"))])!)
        throw new Exception("Own play button text and pixel gate failed.");
    var timerType = assembly.GetType("HappyDDZ.Assistant.TimerCandidateReader")!;
    var timer = Activator.CreateInstance(timerType, nonPublic: true)!;
    var ownCountdown = timerType.GetMethod("ReadOwnCountdown")!;
    var livePassImage = File.ReadAllBytes(Path.Combine("samples", "private", "pass-gate-current.bmp"));
    if (!(bool)passGate.Invoke(null, [pass, livePassImage])! ||
        (int?)ownCountdown.Invoke(timer, [livePassImage, livePassImage]) != 8)
        throw new Exception("Real own-pass clock and button frame failed.");
    var robGate = scanner.GetMethod("HasOwnRobButtons", BindingFlags.Static | BindingFlags.NonPublic)!;
    var bidPixels = scanner.GetMethod("HasBidButtonPixels", BindingFlags.Static | BindingFlags.NonPublic)!;
    var liveFooter = scanner.GetMethod("HasLiveTableFooter", BindingFlags.Static | BindingFlags.NonPublic)!;
    var bidImage = File.ReadAllBytes(Path.Combine("samples", "private", "bid-probe-found-12.bmp"));
    var robImage = File.ReadAllBytes(Path.Combine("samples", "private", "bid-next-20261003-12.bmp"));
    var robExpired = File.ReadAllBytes(Path.Combine("samples", "private", "bid-next-20261003-16.bmp"));
    var expiredBidImage = File.ReadAllBytes(Path.Combine("samples", "private", "bid-probe-found-21.bmp"));
    if (!(bool)bidGate.Invoke(null, [bid, bidImage])! ||
        !(bool)robGate.Invoke(null, [rob, robImage])! ||
        !(bool)bidPixels.Invoke(null, [robImage])! ||
        (bool)robGate.Invoke(null, [rob, robExpired])! ||
        !(bool)bidPixels.Invoke(null, [bidImage])! ||
        !(bool)liveFooter.Invoke(null, [bidImage])! ||
        (bool)bidPixels.Invoke(null, [expiredBidImage])! ||
        (bool)bidGate.Invoke(null, [bid, expiredBidImage])! ||
        (bool)bidGate.Invoke(null, [opponent, bidImage])!)
        throw new Exception($"Own bid button gate failed: call={(bool)bidGate.Invoke(null, [bid, bidImage])!}, rob={(bool)robGate.Invoke(null, [rob, robImage])!}, robPixels={(bool)bidPixels.Invoke(null, [robImage])!}, expiredRob={(bool)robGate.Invoke(null, [rob, robExpired])!}, callAsRob={(bool)robGate.Invoke(null, [rob, bidImage])!}");
    var phaseType = assembly.GetType("HappyDDZ.Assistant.BidPhaseReader")!;
    var phase = Activator.CreateInstance(phaseType, nonPublic: true)!;
    var isPhase = phaseType.GetMethod("IsPhase")!;
    bool Phase(byte[] image, bool isRob) =>
        (bool)isPhase.Invoke(phase, [image, isRob])!;
    if (!Phase(bidImage, false) || Phase(bidImage, true) ||
        !Phase(robImage, true) || Phase(robImage, false) ||
        Phase(robExpired, false) || Phase(robExpired, true))
        throw new Exception("Bid/rob visual phase gate failed.");
    var scannerResult = assembly.GetType("HappyDDZ.Assistant.ScanResult")!;
    var nav = assembly.GetType("HappyDDZ.Assistant.OcrNavigation")!;
    var buildNav = nav.GetMethod("Build", BindingFlags.Static | BindingFlags.Public)!;
    var scan = Activator.CreateInstance(scannerResult, "叫地主阶段", "", bid, 0d,
        DateTimeOffset.Now, "欢乐斗地主", 1280, 720,
        false, false, false, false, false, null)!;
    int ClickableBidCount(object value) => ((IEnumerable)buildNav.Invoke(null, [value])!)
        .Cast<object>().Count(entry =>
            (bool)entry.GetType().GetProperty("CanClick")!.GetValue(entry)!);
    if (ClickableBidCount(scan) != 0)
        throw new Exception("Unconfirmed bid buttons became clickable.");
    scannerResult.GetProperty("OwnBidButtonsConfirmed")!.SetValue(scan, true);
    if (ClickableBidCount(scan) != 2)
        throw new Exception("Confirmed bid buttons were not both clickable.");
    var robScan = Activator.CreateInstance(scannerResult, "叫地主阶段", "", rob, 0d,
        DateTimeOffset.Now, "欢乐斗地主", 1280, 720,
        false, false, false, false, false, null)!;
    if (ClickableBidCount(robScan) != 0)
        throw new Exception("Unconfirmed rob buttons became clickable.");
    scannerResult.GetProperty("OwnRobButtonsConfirmed")!.SetValue(robScan, true);
    if (ClickableBidCount(robScan) != 2)
        throw new Exception("Confirmed rob buttons were not both clickable.");
    var aidImage = File.ReadAllBytes(Path.Combine("samples", "private", "current-20261003-0726.bmp"));
    var aidConfirm = scanner.GetMethod("HasBeanAidConfirm", BindingFlags.Static | BindingFlags.NonPublic)!;
    var aidText = Lines(Line("确定", 593, 554, 67));
    if (!(bool)aidConfirm.Invoke(null, [aidText, aidImage])! ||
        (bool)aidConfirm.Invoke(null, [aidText, bidImage])! ||
        (bool)aidConfirm.Invoke(null, [Lines(Line("确定", 608, 445, 65)), aidImage])!)
        throw new Exception("Bean aid confirm button gate failed.");
    var aidScan = Activator.CreateInstance(scannerResult, "欢乐豆补助弹窗", "", aidText, 0d,
        DateTimeOffset.Now, "欢乐斗地主", 1280, 720,
        false, false, false, false, false, null)!;
    if (ClickableBidCount(aidScan) != 0)
        throw new Exception("Unconfirmed aid button became clickable.");
    scannerResult.GetProperty("BeanAidConfirmConfirmed")!.SetValue(aidScan, true);
    scannerResult.GetProperty("BeanAidCloseXConfirmed")!.SetValue(aidScan, true);
    if (ClickableBidCount(aidScan) != 2)
        throw new Exception("Confirmed aid button and close X were not keyboard selectable.");
    var exitPixels = scanner.GetMethod("HasRoomExitPixels", BindingFlags.Static | BindingFlags.NonPublic)!;
    var waitingImage = File.ReadAllBytes(Path.Combine("samples", "private", "session-after-enter-room.bmp"));
    var settledImage = File.ReadAllBytes(Path.Combine("samples", "private", "third-settlement.bmp"));
    var roomImage = File.ReadAllBytes(Path.Combine("samples", "private", "session-rooms.bmp"));
    if (!(bool)exitPixels.Invoke(null, [waitingImage])! ||
        !(bool)exitPixels.Invoke(null, [settledImage])! ||
        (bool)exitPixels.Invoke(null, [aidImage])! ||
        (bool)exitPixels.Invoke(null, [roomImage])!)
        throw new Exception("Room exit icon pixels failed.");
    var exitScan = Activator.CreateInstance(scannerResult, "场次等候页", "", Lines(), 0d,
        DateTimeOffset.Now, "欢乐斗地主", 1280, 720,
        false, false, false, false, false, null)!;
    if (ClickableBidCount(exitScan) != 0)
        throw new Exception("Unconfirmed room exit became clickable.");
    scannerResult.GetProperty("RoomExitConfirmed")!.SetValue(exitScan, true);
    if (ClickableBidCount(exitScan) != 1)
        throw new Exception("Confirmed room exit was not keyboard selectable.");
    var timeoutScan = Activator.CreateInstance(scannerResult, "超时离房弹窗", "",
        Lines(Line("确定", 608, 445, 65), Line("确定", 593, 554, 67)), 0d,
        DateTimeOffset.Now, "欢乐斗地主", 1280, 720,
        false, false, false, false, false, null)!;
    if (ClickableBidCount(timeoutScan) != 1)
        throw new Exception("Covered bean aid button was selectable through timeout popup.");
    var piggyX = scanner.GetMethod("HasWhiteX", BindingFlags.Static | BindingFlags.NonPublic)!;
    var piggy = File.ReadAllBytes(Path.Combine("samples", "private", "hall-hidden-test-20261003.bmp"));
    if (!(bool)piggyX.Invoke(null, [piggy, 1054, 170])! ||
        (bool)piggyX.Invoke(null, [bidImage, 1054, 170])!)
        throw new Exception("Piggy bank close X gate failed.");
    var windowKind = assembly.GetType("HappyDDZ.Assistant.WindowKind")!;
    var game = Enum.Parse(windowKind, "Game");
    var identify = scanner.GetMethod("IdentifyScene", BindingFlags.Static | BindingFlags.NonPublic)!;
    string Scene(string text)
    {
        var result = ((string Name, string Detail))identify.Invoke(null, [text, game])!;
        return result.Name;
    }
    if (Scene("头衔晋升 解锁等级奖励 太棒了 继续游戏") != "头衔晋升弹窗" ||
        Scene("太久没操作 离开房间 确定 头衔晋升 解锁等级奖励 太棒了") != "超时离房弹窗" ||
        Scene("继续游戏 明牌开始×5 按住查看桌面") != "结算" ||
        Scene("猪猪储豆罐 开罐即得31000豆 ¥1开启") != "储豆罐收费弹窗" ||
        Scene("送你欢乐豆补助 基础送豆4500+回归特权10500 确定 继续游戏") != "欢乐豆补助弹窗" ||
        Scene("回归礼遇 回归礼赠 累登奖励 领奖") != "回归礼遇弹窗" ||
        Scene("活动时间9.23-10.10 对局领取花袄种子 去种植") != "种花活动弹窗" ||
        Scene("中秋点花灯,祈愿赢好礼 立即前往") != "寻灯见礼弹窗" ||
        Scene("运气不好 洗个手再来 继续对局 去换场") != "换玩法建议弹窗" ||
        Scene("无敌是多么~多么~寂寞 经典新手场 明牌开始×5 继续游戏") != "战绩分享弹窗" ||
        Scene("继续游戏 明牌开始×5") == "结算")
        throw new Exception("Popup priority gate failed.");
    var settlementImage = File.ReadAllBytes(Path.Combine("samples", "private",
        "after-promotion-current.bmp"));
    var settlementButton = scanner.GetMethod("HasSettlementContinue",
        BindingFlags.Static | BindingFlags.NonPublic)!;
    if (!(bool)settlementButton.Invoke(null, [settlementImage])!)
        throw new Exception("Settlement button pixels failed.");
    var coveredSettlement = File.ReadAllBytes(Path.Combine("samples", "private",
        "current-before-card-pilot.bmp"));
    if ((bool)settlementButton.Invoke(null, [coveredSettlement])!)
        throw new Exception("Covered settlement button was accepted.");
    var returnGiftImage = File.ReadAllBytes(Path.Combine("samples", "private",
        "current-relaunch-2026-10-03.bmp"));
    var whiteX = scanner.GetMethod("HasWhiteX", BindingFlags.Static | BindingFlags.NonPublic)!;
    if (!(bool)whiteX.Invoke(null, [returnGiftImage, 1073, 146])! ||
        (bool)whiteX.Invoke(null, [coveredSettlement, 1073, 146])!)
        throw new Exception("Return gift close X pixels failed.");
    var flowerEventImage = File.ReadAllBytes(Path.Combine("samples", "private",
        "after-return-gift-close-2026-10-03.bmp"));
    if (!(bool)whiteX.Invoke(null, [flowerEventImage, 1094, 165])! ||
        (bool)whiteX.Invoke(null, [returnGiftImage, 1094, 165])!)
        throw new Exception("Flower event close X pixels failed.");
    var lanternEventImage = File.ReadAllBytes(Path.Combine("samples", "private",
        "after-flower-close-2026-10-03.bmp"));
    if (!(bool)whiteX.Invoke(null, [lanternEventImage, 1037, 184])! ||
        (bool)whiteX.Invoke(null, [flowerEventImage, 1037, 184])!)
        throw new Exception("Lantern event close X pixels failed.");
    var badLuckImage = File.ReadAllBytes(Path.Combine("samples", "private",
        "after-landlord-current.bmp"));
    if (!(bool)whiteX.Invoke(null, [badLuckImage, 1057, 171])! ||
        (bool)whiteX.Invoke(null, [lanternEventImage, 1057, 171])!)
        throw new Exception("Bad luck popup close X pixels failed.");
    var shareResultImage = File.ReadAllBytes(Path.Combine("samples", "private",
        "current-settlement-no-entry.bmp"));
    if (!(bool)whiteX.Invoke(null, [shareResultImage, 1068, 151])! ||
        (bool)whiteX.Invoke(null, [badLuckImage, 1068, 151])!)
        throw new Exception("Share result popup close X pixels failed.");
    if (!(bool)whiteX.Invoke(null, [aidImage, 915, 198])! ||
        (bool)whiteX.Invoke(null, [settledImage, 915, 198])!)
        throw new Exception("Bean aid close X pixels failed.");
    Console.WriteLine("Visible control gate: two-frame own buttons PASS; opponent and mixed frames rejected.");
    Console.WriteLine("Own bid buttons: live pixels and text accepted; expired and opponent rejected; navigation gate PASS.");
    Console.WriteLine("Popup priority gate: title promotion and timeout overlay PASS.");
    Console.WriteLine("Settlement gate: text without replay label and yellow button pixels PASS.");
    Console.WriteLine("Return gift popup: scene and close X pixels PASS.");
    Console.WriteLine("Flower event popup: scene and close X pixels PASS.");
    Console.WriteLine("Lantern event popup: scene and close X pixels PASS.");
    Console.WriteLine("Bad luck popup: scene and close X pixels PASS.");
    Console.WriteLine("Share result popup: scene and close X pixels PASS.");
    Console.WriteLine("Bean aid: overlay priority, yellow confirm, close X, and keyboard navigation PASS.");
    Console.WriteLine("Room exit: waiting/settlement icon and keyboard navigation PASS; covered and room scenes rejected.");
    Console.WriteLine("Timeout popup: only foreground confirm selectable when covered reward also says confirm PASS.");
    return;
}
if (args.Length == 1 && args[0] == "--layout-check")
{
    object ReadSample(string file)
    {
        var data = File.ReadAllBytes(Path.Combine("samples", "private", file));
        return method.Invoke(reader, [data, data])!;
    }
    int Property(object result, string name) => (int)result.GetType().GetProperty(name)!.GetValue(result)!;
    var normal = ReadSample("match-05-after-drop-click.bmp");
    var lifted = ReadSample("match-05-after-lift-click.bmp");
    var landlord = ReadSample("live-pair-a.bmp");
    if (Property(normal, "LayoutLeft") != 249 || Property(normal, "RaisedPosition") != 0 ||
        Property(lifted, "LayoutLeft") != 249 || Property(lifted, "RaisedPosition") != 9 ||
        Property(landlord, "LayoutLeft") != 218 || Property(landlord, "RaisedPosition") != 0)
        throw new Exception("Hand layout or raised position gate failed.");
    Console.WriteLine("Hand layout gate: normal 17, raised ninth, landlord 20 PASS.");
    return;
}
if (args.Length == 1 && args[0] == "--raised-set-check")
{
    object Read(string first, string second)
    {
        byte[] Image(string file) => File.ReadAllBytes(Path.Combine("samples", "private", file));
        return method.Invoke(reader, [Image(first), Image(second)])!;
    }
    int[] Raised(object result) => ((IEnumerable<int>)result.GetType()
        .GetProperty("RaisedPositions")!.GetValue(result)!).ToArray();
    int CardCount(object result) => ((System.Collections.ICollection)result.GetType()
        .GetProperty("Cards")!.GetValue(result)!).Count;
    const string prefix = "two-raised-20261003-123641-";
    var both = Read(prefix + "after-two-a.bmp", prefix + "after-two-b.bmp");
    var one = Read(prefix + "after-one.bmp", prefix + "after-one.bmp");
    var mixed = Read(prefix + "after-one.bmp", prefix + "after-two-a.bmp");
    var dropped = Read(prefix + "after-drop.bmp", prefix + "after-drop.bmp");
    if (!Raised(both).SequenceEqual([2, 8]) || CardCount(both) != 0 ||
        !Raised(one).SequenceEqual([2]) || Raised(mixed).Length != 0 ||
        Raised(dropped).Length != 0 || CardCount(dropped) != 17)
        throw new Exception("Real two-raised selection state check failed.");
    Console.WriteLine("Real hand: raised {2,8}, single {2}, mixed frames rejected, and 17-card drop PASS.");
    return;
}
if (args.Length == 1 && args[0] == "--click-plan-check")
{
    var scannerType = assembly.GetType("HappyDDZ.Assistant.ScanResult")!;
    var lineType = assembly.GetType("HappyDDZ.Assistant.OcrLine")!;
    var planner = assembly.GetType("HappyDDZ.Assistant.HandClickPlanner")!;
    var create = planner.GetMethod("Create")!;
    var same = planner.GetMethod("SameHand")!;
    object Hand(string file)
    {
        var data = File.ReadAllBytes(Path.Combine("samples", "private", file));
        return method.Invoke(reader, [data, data])!;
    }
    object Scan(string scene, object hand, DateTimeOffset capturedAt)
    {
        var lines = Array.CreateInstance(lineType, 0);
        return Activator.CreateInstance(scannerType, scene, "", lines, 0d, capturedAt,
            "欢乐斗地主", 1280, 720, false, false, false, false, false, hand)!;
    }
    object Plan(object scan, int position) => create.Invoke(null, [scan, position])!;
    int Coordinate(object plan, string property) =>
        (int)plan.GetType().GetProperty(property)!.GetValue(plan)!;
    void Reject(object scan, int position)
    {
        try { Plan(scan, position); throw new Exception("Unsafe click plan accepted."); }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException) { }
    }
    var normal = Hand("match-05-after-drop-click.bmp");
    var landlord = Hand("live-pair-a.bmp");
    var raised = Hand("match-05-after-lift-click.bmp");
    var replayPartial = Hand(Path.Combine("replay-frames", "step-18.bmp"));
    var first = Plan(Scan("牌桌", normal, DateTimeOffset.Now), 1);
    var last = Plan(Scan("牌桌", normal, DateTimeOffset.Now), 17);
    var landlordLast = Plan(Scan("牌桌", landlord, DateTimeOffset.Now), 20);
    var partialFirst = Plan(Scan("牌桌", replayPartial, DateTimeOffset.Now), 1);
    if (Coordinate(first, "ClientX") != 269 || Coordinate(last, "ClientX") != 925 ||
        Coordinate(landlordLast, "ClientX") != 1017 ||
        Coordinate(partialFirst, "ClientX") is < 470 or > 480 ||
        Coordinate(first, "ClientY") != 560 ||
        !(bool)same.Invoke(null, [first, normal])!)
        throw new Exception("Click coordinates or hand identity failed.");
    Reject(Scan("对局回放", normal, DateTimeOffset.Now), 1);
    Reject(Scan("牌桌", normal, DateTimeOffset.Now.AddSeconds(-10)), 1);
    Reject(Scan("牌桌", raised, DateTimeOffset.Now), 9);
    Reject(Scan("牌桌", normal, DateTimeOffset.Now), 18);
    var raisedSet = Hand("two-raised-20261003-123641-after-two-a.bmp");
    var raisedPlan = Plan(Scan("牌桌", Hand("two-raised-20261003-123641-baseline-a.bmp"), DateTimeOffset.Now), 2);
    var sameRaised = planner.GetMethod("SameRaisedHand")!;
    bool MatchesRaised() => (bool)sameRaised.Invoke(null, [raisedPlan, raisedSet, new int[] { 2, 8 }])!;
    if (!MatchesRaised()) throw new Exception("Real raised set failed.");
    raisedSet.GetType().GetProperty("ObservedCount")!.SetValue(raisedSet, 16);
    if (MatchesRaised()) throw new Exception("Changed count with same raised positions was accepted.");
    normal.GetType().GetProperty("ObservedCount")!.SetValue(normal, 16);
    Reject(Scan("牌桌", normal, DateTimeOffset.Now), 2);
    if ((bool)same.Invoke(null, [first, normal])!) throw new Exception("Changed observed count was accepted.");
    Console.WriteLine("Raised and unraised hand count changes rejected even when layout and selected positions are unchanged.");
    Console.WriteLine("Single-card click plan: 7/17/20 coordinates PASS; replay scene, stale, raised, and invalid position rejected.");
    return;
}
if (args.Length == 1 && args[0] == "--timer-check")
{
    var timerType = assembly.GetType("HappyDDZ.Assistant.TimerCandidateReader")!;
    var timer = Activator.CreateInstance(timerType, nonPublic: true)!;
    var read = timerType.GetMethod("Read")!;
    var readAt = timerType.GetMethod("ReadAt")!;
    var readBid = timerType.GetMethod("ReadBidCountdown")!;
    int? Seconds(string first, string? second = null)
    {
        var a = File.ReadAllBytes(Path.Combine("samples", "private", first));
        var b = File.ReadAllBytes(Path.Combine("samples", "private", second ?? first));
        return (int?)read.Invoke(timer, [a, b]);
    }
    if (Seconds("card-pilot-second.bmp") != 19 ||
        Seconds("live-pair-a.bmp") != 6 ||
        Seconds("live-pair-b.bmp") != 6 ||
        Seconds("match-05-after-drop-click.bmp") != 11 ||
        Seconds("card-pilot-second.bmp", "match-05-after-drop-click.bmp") is not null ||
        Seconds("after-promotion-current.bmp") is not null ||
        Seconds("current-before-card-pilot.bmp") is not null)
        throw new Exception("Visible timer gate failed.");
    var bidSeven = File.ReadAllBytes(Path.Combine("samples", "private", "bid-probe-found-12.bmp"));
    var bidAt75 = (int?)readAt.Invoke(timer, [bidSeven, bidSeven, 75, -4]);
    var bidAt0 = (int?)readAt.Invoke(timer, [bidSeven, bidSeven, 0, 0]);
    if (bidAt75 != 7 || bidAt0 is not null)
        throw new Exception("Bid clock offset gate failed.");
    var bidSequence = Enumerable.Range(8, 13).Select(index =>
    {
        var data = File.ReadAllBytes(Path.Combine("samples", "private",
            $"bid-probe-found-{index}.bmp"));
        return (index, seconds: (int?)readAt.Invoke(timer, [data, data, 75, -4]));
    }).ToArray();
    var expectedBid = new int?[] { 10, 9, 9, 8, 7, 6, 5, 5, 4,
        null, null, null, null };
    if (!bidSequence.Select(item => item.seconds).SequenceEqual(expectedBid))
        throw new Exception("Bid clock sequence gate failed.");
    var robSequence = Enumerable.Range(12, 5).Select(index =>
    {
        var data = File.ReadAllBytes(Path.Combine("samples", "private",
            $"bid-next-20261003-{index}.bmp"));
        return (index, seconds: (int?)readAt.Invoke(timer, [data, data, 75, -4]));
    }).ToArray();
    if (!robSequence.Select(item => item.seconds)
        .SequenceEqual(new int?[] { 7, 6, 4, null, null }))
        throw new Exception("Rob clock sequence gate failed.");
    byte[] BidFrame(int index) => File.ReadAllBytes(Path.Combine("samples", "private",
        $"bid-probe-found-{index}.bmp"));
    if ((int?)readBid.Invoke(timer, [BidFrame(8), BidFrame(9)]) != 9 ||
        (int?)readBid.Invoke(timer, [BidFrame(9), BidFrame(8)]) is not null ||
        (int?)readBid.Invoke(timer, [BidFrame(16), BidFrame(17)]) is not null)
        throw new Exception("Bid clock descending-frame gate failed.");
    Console.WriteLine("Read-only clock: independent 19/6/11 frames PASS; mixed digits and covered/settlement scenes rejected.");
    Console.WriteLine("Bid clock: shifted own clock reads 7 seconds; play clock region rejected.");
    Console.WriteLine("Bid clock sample sequence: " + string.Join(", ",
        bidSequence.Select(item => $"{item.index}:{item.seconds?.ToString() ?? "?"}")));
    Console.WriteLine("Rob clock sample sequence: " + string.Join(", ",
        robSequence.Select(item => $"{item.index}:{item.seconds?.ToString() ?? "?"}")));
    return;
}
if (args.Length is 2 or 4 && args[0] == "--evaluate")
{
    var excluded = args.Length == 4 && args[2] == "--exclude" ? args[3] : null;
    using var labels = JsonDocument.Parse(File.ReadAllText(args[1]));
    var frameCount = 0;
    var totalCards = 0;
    var fullyRecognized = 0;
    var wrongRank = 0;
    var wrongSuit = 0;
    foreach (var frame in labels.RootElement.GetProperty("hands").EnumerateArray())
    {
        var file = frame.GetProperty("file").GetString()!;
        if (file == excluded) continue;
        var path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(args[1])!, "..", "private", file));
        var image = File.ReadAllBytes(path);
        var read = method.Invoke(reader, [image, image])!;
        var predicted = ((IEnumerable)read.GetType().GetProperty("Cards")!.GetValue(read)!).Cast<object>().ToArray();
        var expected = frame.GetProperty("cards").EnumerateArray().ToArray();
        if (predicted.Length != expected.Length) throw new Exception($"{file}: card count mismatch.");
        var frameFull = 0;
        var frameErrors = 0;
        for (var i = 0; i < expected.Length; i++)
        {
            var rank = (string)predicted[i].GetType().GetProperty("Rank")!.GetValue(predicted[i])!;
            var suit = (string)predicted[i].GetType().GetProperty("Suit")!.GetValue(predicted[i])!;
            var actualRank = expected[i][0].GetString()!;
            var actualSuit = expected[i][1].GetString()!;
            if (rank != "未知" && rank != actualRank) { wrongRank++; frameErrors++; }
            if (suit != "未知" && suit != actualSuit) { wrongSuit++; frameErrors++; }
            if (rank != "未知" && suit != "未知") { fullyRecognized++; frameFull++; }
        }
        frameCount++;
        totalCards += expected.Length;
        Console.WriteLine($"{file}: complete candidates {frameFull}/{expected.Length}, incorrect known fields {frameErrors}");
    }
    Console.WriteLine($"Total: {frameCount} hands, {totalCards} cards, complete candidates {fullyRecognized}, incorrect ranks {wrongRank}, incorrect suits {wrongSuit}.");
    if (wrongRank + wrongSuit > 0) Environment.ExitCode = 1;
    return;
}
var result = method.Invoke(reader, [File.ReadAllBytes(source), File.ReadAllBytes(comparison)])
    ?? throw new InvalidOperationException("Card read returned null.");
var firstFrame = File.ReadAllBytes(source);
var secondFrame = File.ReadAllBytes(comparison);
var durations = new List<double>();
for (var trial = 0; trial < 5; trial++)
{
    var watch = System.Diagnostics.Stopwatch.StartNew();
    method.Invoke(reader, [firstFrame, secondFrame]);
    durations.Add(watch.Elapsed.TotalMilliseconds);
}
durations.Sort();
Console.WriteLine($"Two-frame card candidate median: {durations[2]:0.0} ms (capture excluded)");
var resultType = result.GetType();
Console.WriteLine(resultType.GetProperty("Summary")?.GetValue(result));
var cards = resultType.GetProperty("Cards")?.GetValue(result) as IEnumerable;
if (cards is not null)
    foreach (var card in cards) Console.WriteLine(card);

if (source == comparison && Path.GetFileName(source) == "replay-step0.bmp" &&
    resultType.GetProperty("Cards")?.GetValue(result) is { } handCards)
{
    var modelType = assembly.GetType("HappyDDZ.Assistant.HandSelectionModel")
        ?? throw new InvalidOperationException("HandSelectionModel unavailable.");
    var model = Activator.CreateInstance(modelType, nonPublic: true)!;
    object? Call(string name, params object[] parameters) =>
        modelType.GetMethod(name)!.Invoke(model, parameters);
    int Picked() => (int)modelType.GetProperty("PickedCount")!.GetValue(model)!;
    Call("Load", handCards);
    Call("MoveEnd", false);
    Call("PickOne");
    if (Picked() != 1) throw new Exception("Single card pick failed.");
    Call("MoveGroup", 1);
    Call("PickGroup");
    if (Picked() < 2) throw new Exception("Rank group pick failed.");
    Call("DropOne");
    if (Picked() < 1) throw new Exception("Single card drop failed.");
    Call("DropAll");
    if (Picked() != 0) throw new Exception("Drop all failed.");
    Call("Load", handCards);
    if (Picked() != 0) throw new Exception("Refresh did not clear selection.");
    Call("PickOne");
    Call("PickOne");
    if (Picked() != 2) throw new Exception("Repeated up did not advance to another card.");
    Call("SetCurrent", 2);
    Call("DropOne");
    if ((bool)Call("IsPicked", 0)!) throw new Exception("Down did not drop the earliest picked card.");
    Console.WriteLine("Hand gesture preview: pick one/group, repeated up, drop one/all, FIFO, reset: PASS");
}
