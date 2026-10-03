namespace HappyDDZ.Assistant;

internal sealed record RoomInfo(string Name, string BaseScore, string BeanRange);

internal sealed record OcrEntry(int Number, OcrLine Line, string Kind, bool CanClick, RoomInfo? Room = null)
{
    public int CenterX => Line.X + Line.Width / 2;
    public int CenterY => Line.Y + Line.Height / 2;

    public override string ToString()
    {
        var quality = Line.Confidence < 0.8 ? "可能识别错误，" : "";
        var action = CanClick ? "可按回车点击" : "仅供阅读";
        var room = Room is null ? "" : $"，底分 {Room.BaseScore}，豆子范围 {Room.BeanRange}";
        return $"第 {Number} 项，{Kind}：{quality}{Line.Text}{room}；中心坐标 {CenterX}，{CenterY}；{action}";
    }
}

internal static class OcrNavigation
{
    public static (int Current, int Total)? ReplayStep(ScanResult result)
    {
        var step = result.Lines.Select(line => System.Text.RegularExpressions.Regex.Match(
            line.Text, @"第\s*(\d+)\s*/\s*(\d+)\s*手"))
            .FirstOrDefault(match => match.Success);
        return step is not null && int.TryParse(step.Groups[1].Value, out var current) &&
            int.TryParse(step.Groups[2].Value, out var total)
            ? (current, total) : null;
    }

    private static readonly IReadOnlyDictionary<string, RoomInfo> Rooms = new Dictionary<string, RoomInfo>
    {
        ["新手场"] = new("新手场", "20", "1000-10万"),
        ["初级场"] = new("初级场", "80", "3000-40万"),
        ["普通场"] = new("普通场", "300", "8000-150万"),
        ["中级场"] = new("中级场", "900", "2.5万以上"),
        ["高级场"] = new("高级场", "2700", "8万以上"),
        ["顶级场"] = new("顶级场", "6000", "30万以上")
    };

    public static IReadOnlyList<OcrEntry> Build(ScanResult result)
    {
        IEnumerable<OcrLine> lines = result.Lines.Where(line =>
            !string.IsNullOrWhiteSpace(line.Text) && line.Width > 0 && line.Height > 0 &&
            line.X >= 0 && line.Y >= 0 &&
            line.X + line.Width <= result.ClientWidth &&
            line.Y + line.Height <= result.ClientHeight);

        if (result.Scene == "大厅引导弹窗")
            lines = lines.Where(line => line.Text.Contains("点击切换到", StringComparison.Ordinal) ||
                line.Text.Contains("经典版", StringComparison.Ordinal) || line.Text.Trim() == "知道啦");
        if (result.Scene == "QQ登录窗口" && result.LoginPasswordFormConfirmed)
            lines = lines.Append(new OcrLine("聚焦官方密码输入框", 1, 128, 195, 60, 30));
        if (result.Scene == "QQ登录窗口" && result.LoginQqQuickConfirmed)
            lines = lines.Append(new OcrLine("使用已登录QQ快速登录", 1, 128, 155, 60, 40));
        if (result.Scene == "超时离房弹窗")
            lines = lines.Where(line => line.X >= 400 && line.X < 850 &&
                line.Y >= 320 && line.Y < 505);

        if (result.Scene is "版本更新说明弹窗" or "回归礼物弹窗")
        {
            var isUpdate = result.Scene == "版本更新说明弹窗";
            var title = result.Lines.FirstOrDefault(line => line.Text.Contains(
                isUpdate ? "新版本更新内容" : "好久不见", StringComparison.Ordinal));
            var button = result.Lines.FirstOrDefault(line => line.Text.Trim() == (isUpdate ? "好的" : "领取"));
            if (title is not null)
            {
                var left = isUpdate ? title.X - 25 : title.X - 150;
                var right = Math.Min(result.ClientWidth, title.X + (isUpdate ? 700 : 550));
                var bottom = button is null ? Math.Min(result.ClientHeight, title.Y + 450) : button.Y + button.Height + 8;
                lines = lines.Where(line => line.X >= left && line.X < right &&
                    line.Y >= title.Y - 5 && line.Y <= bottom);
            }
        }
        if (result.Scene == "领取结果弹窗")
        {
            var title = result.Lines.FirstOrDefault(line => line.Text.Contains("恭喜获得", StringComparison.Ordinal));
            var button = result.Lines.FirstOrDefault(line => line.Text.Trim() == "我知道了");
            if (title is not null && button is not null)
                lines = lines.Where(line => line.X >= title.X - 110 && line.X < title.X + 400 &&
                    (line.Text.Contains("恭喜获得", StringComparison.Ordinal) ||
                     line.Text.Trim() == "我知道了" ||
                     line.Y >= title.Y + 120 && line.Y <= button.Y - 70));
        }
        if (result.Scene == "首次对局奖励弹窗")
        {
            var title = result.Lines.FirstOrDefault(line => line.Text.Contains("首次对局奖励", StringComparison.Ordinal));
            var buttons = result.Lines.Where(line => line.Text.Trim() is "去对局" or "免费对局").ToList();
            if (title is not null && buttons.Count > 0)
            {
                var bottom = buttons.Max(line => line.Y + line.Height) + 8;
                lines = lines.Where(line => line.X >= title.X - 285 && line.X < title.X + 570 &&
                    line.Y >= title.Y - 35 && line.Y <= bottom);
            }
        }
        if (result.Scene == "周年庆红包弹窗")
            lines = lines.Where(line => line.Text.Contains("周年庆惊喜红包", StringComparison.Ordinal) ||
                line.Text.Trim() == "X" && line.X > 1000 && line.Y < 220 ||
                line.X >= 570 && line.X < 730 && line.Y >= 300 && line.Y < 620);
        if (result.Scene == "周年庆领取结果弹窗")
            lines = lines.Where(line => line.X >= 450 && line.X < 850 &&
                (line.Text.Contains("恭喜获得", StringComparison.Ordinal) ||
                line.Text.Contains("限时钻石", StringComparison.Ordinal) ||
                line.Text.Contains("1000", StringComparison.Ordinal) && line.Y < 420 ||
                line.Text.Trim() == "开心收下" ||
                line.Text.Contains("次日起可领取每日红包", StringComparison.Ordinal)));
        if (result.Scene == "欢乐豆补助弹窗")
        {
            lines = lines.Where(line => line.X >= 350 && line.X < 925 &&
                line.Y >= 110 && line.Y < 610);
            if (result.BeanAidCloseXConfirmed)
                lines = lines.Append(new OcrLine("关闭欢乐豆补助提示", 1, 903, 186, 24, 24));
        }
        if ((result.Scene is "场次等候页" or "结算") && result.RoomExitConfirmed)
        {
            lines = lines.Where(line => !(line.Text.Trim() == "退出" &&
                line.X >= 1210 && line.Y >= 55 && line.Y <= 85));
            lines = lines.Append(new OcrLine("退出当前场次", 1, 1235, 59, 24, 24));
        }
        if (result.Scene == "无限金卡礼包弹窗" && result.PackCloseXConfirmed)
            lines = lines.Append(new OcrLine("关闭金卡礼包", 1, 1026, 173, 24, 24));
        if (result.Scene == "储豆罐收费弹窗" && result.PiggyBankCloseXConfirmed)
            lines = lines.Append(new OcrLine("关闭储豆罐弹窗", 1, 1042, 158, 24, 24));
        if (result.Scene == "赛季卡册宣传弹窗" && result.SeasonCloseXConfirmed)
            lines = lines.Append(new OcrLine("关闭赛季卡册宣传窗", 1, 1026, 173, 24, 24));
        if (result.Scene == "赛季上新宣传弹窗")
        {
            lines = lines.Where(line => line.X >= 125 && line.X < 1080 &&
                line.Y >= 100 && line.Y < 625);
            if (result.SeasonRefreshCloseXConfirmed)
                lines = lines.Append(new OcrLine("关闭赛季上新宣传窗", 1, 1026, 173, 24, 24));
        }
        if (result.Scene == "对局宝典弹窗")
        {
            lines = lines.Where(line => line.X >= 150 && line.X < 1095 &&
                line.Y >= 120 && line.Y < 660);
            if (result.BattleGuideCloseXConfirmed)
                lines = lines.Append(new OcrLine("关闭对局宝典", 1, 1062, 200, 24, 24));
        }
        if (result.Scene == "回归礼遇弹窗")
        {
            lines = lines.Where(line => line.X >= 160 && line.X < 1110 &&
                line.Y >= 100 && line.Y < 645);
            if (result.ReturnGiftCloseXConfirmed)
                lines = lines.Append(new OcrLine("关闭回归礼遇", 1, 1061, 134, 24, 24));
        }
        if (result.Scene == "种花活动弹窗")
        {
            lines = lines.Where(line => line.X >= 190 && line.X < 1130 &&
                line.Y >= 105 && line.Y < 645);
            if (result.FlowerEventCloseXConfirmed)
                lines = lines.Append(new OcrLine("关闭种花活动", 1, 1082, 153, 24, 24));
        }
        if (result.Scene == "寻灯见礼弹窗")
        {
            lines = lines.Where(line => line.X >= 170 && line.X < 1080 &&
                line.Y >= 110 && line.Y < 625);
            if (result.LanternEventCloseXConfirmed)
                lines = lines.Append(new OcrLine("关闭寻灯见礼", 1, 1025, 172, 24, 24));
        }
        if (result.Scene == "换玩法建议弹窗")
        {
            lines = lines.Where(line => line.X >= 250 && line.X < 1100 &&
                line.Y >= 110 && line.Y < 640);
            if (result.BadLuckCloseXConfirmed)
                lines = lines.Append(new OcrLine("关闭换玩法建议", 1, 1045, 159, 24, 24));
        }
        if (result.Scene == "战绩分享弹窗")
        {
            lines = lines.Where(line => line.X >= 190 && line.X < 1085 &&
                line.Y >= 130 && line.Y < 575);
            if (result.ShareResultCloseXConfirmed)
                lines = lines.Append(new OcrLine("关闭战绩分享", 1, 1056, 139, 24, 24));
        }
        if (result.Scene == "头衔晋升弹窗")
            lines = lines.Where(line => line.X >= 400 && line.X < 850 &&
                line.Y >= 120 && line.Y < 625);
        if (result.Scene == "对局回放" && result.ReplayControlsConfirmed)
        {
            var step = ReplayStep(result);
            if (step is { } progress)
            {
                if (progress.Current > 0)
                    lines = lines.Append(new OcrLine("回放上一手", 1, 1072, 683, 30, 30));
                if (progress.Current < progress.Total)
                    lines = lines.Append(new OcrLine("回放下一手", 1, 1215, 683, 30, 30));
            }
            lines = lines.Append(new OcrLine("回放播放或暂停", 1, 1142, 684, 32, 28));
        }

        var ordered = lines.OrderBy(line => line.Y).ThenBy(line => line.X).ToList();
        var entries = new List<OcrEntry>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            var line = ordered[i];
            var value = line.Text.Trim();
            RoomInfo? room = null;
            if (result.Scene == "场次选择" && Rooms.TryGetValue(value, out var candidateRoom))
            {
                var hasScore = result.Lines.Any(other => other.Text.Trim() == candidateRoom.BaseScore &&
                    Math.Abs(other.X - line.X) < 85 && other.Y >= line.Y + 25 &&
                    other.Y <= line.Y + 80 && other.Confidence >= 0.9);
                var hasRange = result.Lines.Any(other => other.Text.Contains(candidateRoom.BeanRange, StringComparison.Ordinal) &&
                    other.X >= line.X - 15 && other.X <= line.X + 100 &&
                    other.Y >= line.Y + 90 && other.Y <= line.Y + 135 && other.Confidence >= 0.8);
                if (hasScore && hasRange) room = candidateRoom;
            }
            var ddzLobbyTile = result.Scene == "游戏大厅" &&
                value.StartsWith("斗地主", StringComparison.Ordinal) &&
                line.X >= 650 && line.X + line.Width <= 800 &&
                line.Y >= 375 && line.Y <= 430;
            var action = result.Scene switch
            {
                "版本更新说明弹窗" => value == "好的",
                "大厅引导弹窗" => value == "知道啦",
                "QQ登录窗口" => value == "确定" && result.LoginConfirmConfirmed &&
                    line.X + line.Width / 2 is >= 140 and <= 175 &&
                    line.Y + line.Height / 2 is >= 350 and <= 375 ||
                    result.LoginPasswordFormConfirmed &&
                    (value == "聚焦官方密码输入框" || value == "马上登录" &&
                     line.X + line.Width / 2 is >= 145 and <= 175 &&
                     line.Y + line.Height / 2 is >= 275 and <= 300) ||
                    value == "使用已登录QQ快速登录" && result.LoginQqQuickConfirmed,
                "QQ游戏大厅" => value.StartsWith("欢乐斗地主(", StringComparison.Ordinal) &&
                    line.X < 280 && line.Y < 190,
                "回归礼物弹窗" => value == "领取",
                "领取结果弹窗" => value == "我知道了",
                "首次对局奖励弹窗" => (value is "X" or "×") && line.X > 1000 && line.Y < 230,
                "周年庆红包弹窗" => (value is "X" or "×") && line.X > 1000 && line.Y < 220,
                "周年庆领取结果弹窗" => value == "开心收下",
                "无限金卡礼包弹窗" => value == "关闭金卡礼包" && result.PackCloseXConfirmed,
                "储豆罐收费弹窗" => value == "关闭储豆罐弹窗" && result.PiggyBankCloseXConfirmed,
                "欢乐豆补助弹窗" => value == "关闭欢乐豆补助提示" && result.BeanAidCloseXConfirmed ||
                    value == "确定" && result.BeanAidConfirmConfirmed &&
                    line.X >= 575 && line.X + line.Width <= 675 &&
                    line.Y >= 545 && line.Y + line.Height <= 605,
                "赛季卡册宣传弹窗" => value == "关闭赛季卡册宣传窗" && result.SeasonCloseXConfirmed,
                "赛季上新宣传弹窗" => value == "关闭赛季上新宣传窗" && result.SeasonRefreshCloseXConfirmed,
                "对局宝典弹窗" => value == "关闭对局宝典" && result.BattleGuideCloseXConfirmed,
                "回归礼遇弹窗" => value == "关闭回归礼遇" && result.ReturnGiftCloseXConfirmed,
                "种花活动弹窗" => value == "关闭种花活动" && result.FlowerEventCloseXConfirmed,
                "寻灯见礼弹窗" => value == "关闭寻灯见礼" && result.LanternEventCloseXConfirmed,
                "换玩法建议弹窗" => value == "关闭换玩法建议" && result.BadLuckCloseXConfirmed,
                "战绩分享弹窗" => value == "关闭战绩分享" && result.ShareResultCloseXConfirmed,
                "头衔晋升弹窗" => value == "太棒了" && line.X >= 430 &&
                    line.X + line.Width <= 600 && line.Y >= 550 && line.Y < 620,
                "场次等候页" => value == "开始游戏" && line.X >= 650 && line.X + line.Width <= 855 &&
                    line.Y >= 440 && line.Y < 500 || value == "退出当前场次" && result.RoomExitConfirmed,
                "结算引导弹窗" => value == "点击屏幕继续" && line.X >= 500 && line.X < 650 &&
                    line.Y >= 285 && line.Y < 325,
                "卡包引导弹窗" => value == "点击屏幕继续" && line.X >= 845 && line.X < 1010 &&
                    line.Y >= 155 && line.Y < 200,
                "结算" => value == "退出当前场次" && result.RoomExitConfirmed ||
                    value == "继续游戏" && result.SettlementContinueConfirmed &&
                    line.X >= 660 && line.X + line.Width <= 840 &&
                    line.Y >= 575 && line.Y < 630 &&
                    result.Lines.Any(other => other.Text.Contains("按住查看桌面", StringComparison.Ordinal)),
                "牌桌" => value == "取消托管" && result.CancelTrustConfirmed &&
                    line.X >= 565 && line.X + line.Width <= 705 &&
                    line.Y >= 575 && line.Y + line.Height <= 640,
                "叫地主阶段" => result.OwnBidButtonsConfirmed &&
                    (value == "叫地主" && line.X >= 435 && line.X + line.Width <= 570 ||
                     value == "不叫" && line.X >= 695 && line.X + line.Width <= 820) &&
                    line.Y >= 425 && line.Y + line.Height <= 490 ||
                    result.OwnRobButtonsConfirmed &&
                    (value == "抢地主" && line.X >= 435 && line.X + line.Width <= 570 ||
                     value == "不抢" && line.X >= 695 && line.X + line.Width <= 820) &&
                    line.Y >= 425 && line.Y + line.Height <= 490,
                "超时离房弹窗" => value == "确定" && line.X >= 590 &&
                    line.X + line.Width <= 690 && line.Y >= 425 &&
                    line.Y + line.Height <= 500,
                "游戏大厅" => ddzLobbyTile,
                "模式选择" => value is "经典斗地主" or "返回",
                "场次选择" => room is not null,
                "对局回放" => result.ReplayControlsConfirmed &&
                    value is ("回放上一手" or "回放下一手" or "回放播放或暂停"),
                _ => false
            };
            action &= line.Confidence >= (result.Scene == "周年庆红包弹窗" &&
                (value is "X" or "×") ? 0.5 :
                ddzLobbyTile ? 0.8 :
                result.Scene == "首次对局奖励弹窗" && (value is "X" or "×") ? 0.7 : 0.85);
            var kind = result.Scene switch
            {
                "版本更新说明弹窗" when value == "好的" => "弹窗按钮",
                "大厅引导弹窗" when value == "知道啦" => "弹窗按钮",
                "大厅引导弹窗" => "引导说明",
                "QQ游戏大厅" when action => "游戏入口",
                "游戏大厅" when ddzLobbyTile => "斗地主合集入口，原始识别",
                "版本更新说明弹窗" when value == "新版本更新内容" => "弹窗标题",
                "版本更新说明弹窗" => "弹窗正文",
                "回归礼物弹窗" when value == "领取" => "弹窗按钮",
                "回归礼物弹窗" when value.Contains("好久不见", StringComparison.Ordinal) => "弹窗标题",
                "回归礼物弹窗" => "弹窗正文",
                "领取结果弹窗" when value == "我知道了" => "弹窗按钮",
                "领取结果弹窗" when value.Contains("恭喜获得", StringComparison.Ordinal) => "弹窗标题",
                "领取结果弹窗" => "已领取物品",
                "首次对局奖励弹窗" when value is "X" or "×" => "关闭按钮",
                "首次对局奖励弹窗" when value is "去对局" or "免费对局" or "领1万豆再玩" => "对局入口",
                "首次对局奖励弹窗" => "弹窗正文",
                "周年庆红包弹窗" when value is "X" or "×" => "领取并关闭按钮",
                "周年庆红包弹窗" when value.Contains("周年庆惊喜红包", StringComparison.Ordinal) => "弹窗标题",
                "周年庆红包弹窗" => "弹窗正文",
                "周年庆领取结果弹窗" when value == "开心收下" => "弹窗按钮",
                "周年庆领取结果弹窗" when value.Contains("恭喜获得", StringComparison.Ordinal) => "弹窗标题",
                "周年庆领取结果弹窗" => "领取结果",
                "无限金卡礼包弹窗" when value == "关闭金卡礼包" => "图形关闭按钮",
                "储豆罐收费弹窗" when value == "关闭储豆罐弹窗" => "图形关闭按钮",
                "欢乐豆补助弹窗" when value == "关闭欢乐豆补助提示" => "图形关闭按钮",
                "欢乐豆补助弹窗" when value == "确定" => "免费补助确认按钮",
                "赛季卡册宣传弹窗" when value == "关闭赛季卡册宣传窗" => "图形关闭按钮",
                "赛季上新宣传弹窗" when value == "关闭赛季上新宣传窗" => "图形关闭按钮",
                "对局宝典弹窗" when value == "关闭对局宝典" => "图形关闭按钮",
                "回归礼遇弹窗" when value == "关闭回归礼遇" => "图形关闭按钮",
                "种花活动弹窗" when value == "关闭种花活动" => "图形关闭按钮",
                "寻灯见礼弹窗" when value == "关闭寻灯见礼" => "图形关闭按钮",
                "换玩法建议弹窗" when value == "关闭换玩法建议" => "图形关闭按钮",
                "战绩分享弹窗" when value == "关闭战绩分享" => "图形关闭按钮",
                "头衔晋升弹窗" when value == "太棒了" => "关闭头衔晋升提示",
                "场次等候页" when value == "开始游戏" => "进入普通匹配按钮",
                "场次等候页" or "结算" when value == "退出当前场次" && action => "退出当前场次按钮",
                "结算引导弹窗" when value == "点击屏幕继续" => "关闭结算引导",
                "卡包引导弹窗" when value == "点击屏幕继续" => "关闭卡包引导",
                "结算" when value == "继续游戏" => "开始下一局按钮",
                "牌桌" when value == "取消托管" && action => "结束自动托管按钮",
                "叫地主阶段" when action && (value is "抢地主" or "不抢") => "本人抢地主选择按钮",
                "叫地主阶段" when action => "本人叫地主选择按钮",
                "无限金卡礼包弹窗" => "礼包说明",
                "场次选择" when room is not null => "已核对底分的场次",
                "场次选择" when Rooms.ContainsKey(value) => "场次，花费信息未完全确认",
                "超时离房弹窗" when value == "确定" => "弹窗按钮",
                "对局回放" when action => "已核对的图形回放按钮",
                _ => "画面文字"
            };
            entries.Add(new OcrEntry(i + 1, line, kind, action, room));
        }
        return entries;
    }
}
