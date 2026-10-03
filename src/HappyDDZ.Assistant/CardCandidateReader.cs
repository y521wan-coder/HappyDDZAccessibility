using System.Text.Json;

namespace HappyDDZ.Assistant;

internal sealed record HandCard(int Position, string Rank, string Suit)
{
    public override string ToString() => $"第 {Position} 张，点数候选：{Rank}，花色候选：{Suit}";
}

internal sealed record HandRead(string Summary, IReadOnlyList<HandCard> Cards)
{
    public int LayoutLeft { get; init; } = -1;
    public int ObservedCount { get; init; }
    public int RaisedPosition { get; init; }
    public IReadOnlyList<int> RaisedPositions { get; init; } = [];
}

/// <summary>
/// Experimental visible-card reader trained from one historical replay and
/// one labeled live hand. A result alone never authorizes an action: the
/// single-card pilot also verifies scene, layout, full hand, and fresh frames.
/// Bidding, passing, and playing remain disabled.
/// </summary>
internal sealed class CardCandidateReader
{
    private const int Width = 1280;
    private const int Height = 720;
    private const int CardStep = 41;
    private readonly Template[] _templates;

    private sealed record Template(string Rank, string Suit, byte[] RankMask, byte[] SuitMask);
    private sealed record Layout(int Left, int Count);
    private sealed record Prediction(string? Label, double Score, double Margin);
    private sealed record RaisedCard(bool Present, IReadOnlyList<int> Positions);

    public CardCandidateReader() : this("replay-plus-live-one-hand.json") { }

    internal CardCandidateReader(string modelFile)
    {
        var path = Path.Combine(ProjectPaths.Root, "models", "cards", modelFile);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var model = document.RootElement;
        if (model.GetProperty("game_version").GetString() != "8.057.106.17343" ||
            model.GetProperty("mask_width").GetInt32() != 48 ||
            model.GetProperty("rank_height").GetInt32() != 52 ||
            model.GetProperty("suit_height").GetInt32() != 48)
            throw new InvalidDataException("牌角试验模板格式或游戏版本不匹配。");
        _templates = model.GetProperty("templates").EnumerateArray().Select(item => new Template(
            item.GetProperty("rank").GetString() ?? "",
            item.GetProperty("suit").GetString() ?? "",
            Convert.FromBase64String(item.GetProperty("rank_mask").GetString() ?? ""),
            Convert.FromBase64String(item.GetProperty("suit_mask").GetString() ?? ""))).ToArray();
        if (_templates.Length < 30 || _templates.Any(item => item.RankMask.Length != 48 * 52 ||
            item.SuitMask.Length != 48 * 48))
            throw new InvalidDataException("牌角试验模板不完整。");
    }

    public HandRead Read(byte[] first, byte[] second)
    {
        var firstLayout = FindLayout(first);
        var secondLayout = FindLayout(second);
        if (firstLayout is null || secondLayout is null ||
            firstLayout.Count != secondLayout.Count ||
            Math.Abs(firstLayout.Left - secondLayout.Left) > 2)
            return new HandRead("手牌位置或张数在两帧间不稳定；本次不提供候选牌。", []);
        if (HasDimmedHand(first, firstLayout) || HasDimmedHand(second, secondLayout))
            return new HandRead("手牌区域被托管遮罩或弹窗压暗；候选已清空，请在无遮挡牌桌重新扫描。", []);
        var raisedFirst = FindRaisedCard(first, firstLayout);
        var raisedSecond = FindRaisedCard(second, secondLayout);
        if (raisedFirst.Present || raisedSecond.Present)
        {
            var stable = raisedFirst.Present && raisedSecond.Present &&
                raisedFirst.Positions.Count > 0 &&
                raisedFirst.Positions.SequenceEqual(raisedSecond.Positions);
            var positions = stable ? raisedSecond.Positions : [];
            var summary = stable
                ? $"连续两帧检测到第 {string.Join("、", positions)} 张手牌被抬起；牌面候选已清空，当前不提交游戏动作。"
                : "检测到抬牌或抬牌状态变化，位置尚未稳定；牌面候选已清空。";
            return new HandRead(summary, [])
            {
                LayoutLeft = secondLayout.Left,
                ObservedCount = secondLayout.Count,
                RaisedPosition = positions.Count == 1 ? positions[0] : 0,
                RaisedPositions = positions
            };
        }

        var cards = new List<HandCard>(secondLayout.Count);
        for (var i = 0; i < secondLayout.Count; i++)
        {
            var firstX = firstLayout.Left + i * CardStep;
            var secondX = secondLayout.Left + i * CardStep;
            // The client sorts both jokers above every ordinary rank, so a
            // real joker can only occupy one of the first two positions.
            // Restricting the lower JOKER-strip detector prevents dark table
            // overlays on later cards from becoming false small jokers.
            var joker1 = i < 2 ? ReadJoker(first, firstX) : null;
            var joker2 = i < 2 ? ReadJoker(second, secondX) : null;
            if ((joker1 is null) != (joker2 is null) ||
                (joker1 is not null && joker2 is not null && joker1 != joker2))
                return new HandRead("两帧同一位置的王牌状态不同，可能正在重新发牌；候选已清空。", []);
            if (joker1 is not null && joker1 == joker2)
            {
                cards.Add(new HandCard(i + 1, joker1, "无花色"));
                continue;
            }
            var rank1 = Predict(Normalize(first, firstX + 2, 531, firstX + 39, 574, 48, 52), true);
            var rank2 = Predict(Normalize(second, secondX + 2, 531, secondX + 39, 574, 48, 52), true);
            var suit1 = Predict(Normalize(first, firstX + 7, 574, firstX + 35, 606, 48, 48), false);
            var suit2 = Predict(Normalize(second, secondX + 7, 574, secondX + 35, 606, 48, 48), false);
            if (rank1.Label is not null && rank2.Label is not null && rank1.Label != rank2.Label ||
                suit1.Label is not null && suit2.Label is not null && suit1.Label != suit2.Label)
                return new HandRead("两帧同一位置的牌面不同，可能正在重新发牌；候选已清空。", []);
            var rank = rank1.Label is not null && rank1.Label == rank2.Label ? rank1.Label : "未知";
            var suit = suit1.Label is not null && suit1.Label == suit2.Label ? SuitName(suit1.Label) : "未知";
            cards.Add(new HandCard(i + 1, rank, suit));
        }
        if (!HandIdentity.IsConsistent(cards))
            return new HandRead("候选牌出现重复王牌、同花色同点数重复或同点数超过四张；牌面互相矛盾，本次已清空，请重新扫描。", []);
        var full = cards.Count(card => card.Rank != "未知" && card.Suit != "未知");
        return new HandRead($"实验性牌角候选：{cards.Count} 张中 {full} 张点数和花色均有候选；其余未知。样本仍少，跨局与真人验收未完成，不能据此出牌。", cards)
        { LayoutLeft = secondLayout.Left, ObservedCount = secondLayout.Count };
    }

    private static string SuitName(string suit) => suit switch
    {
        "spade" => "黑桃", "heart" => "红桃", "club" => "梅花", "diamond" => "方块", _ => "未知"
    };

    private static bool ValidBmp(byte[] image) => image.Length >= 54 + Width * Height * 4 &&
        BitConverter.ToInt32(image, 18) == Width && BitConverter.ToInt32(image, 22) == -Height &&
        BitConverter.ToInt16(image, 28) == 32;

    private static int Offset(int x, int y) => 54 + (y * Width + x) * 4;

    private static string? ReadJoker(byte[] image, int cardLeft)
    {
        // On the observed 1280x720 table, the two kings print JOKER vertically
        // well below the rank/suit corner. Ordinary overlapping card corners
        // have no ink in this narrow lower strip.
        var redInk = 0;
        var blackInk = 0;
        for (var y = 610; y < 660; y++)
        for (var x = cardLeft + 3; x < cardLeft + 31; x++)
        {
            var offset = Offset(x, y);
            var blue = image[offset];
            var green = image[offset + 1];
            var red = image[offset + 2];
            if (red > 120 && green < 120 && blue < 120) redInk++;
            else if (Math.Max(blue, Math.Max(green, red)) < 150) blackInk++;
        }
        if (redInk >= 200 && blackInk < 40) return "大王";
        if (blackInk >= 200 && redInk < 40) return "小王";
        return null;
    }

    private static RaisedCard FindRaisedCard(byte[] image, Layout layout)
    {
        // A selected card rises about 15 pixels. Its pale top edge forms a
        // 109-pixel run above the normal row. Disjoint selected cards form
        // separate runs; adjacent cards may merge into one wider run.
        var start = -1;
        var runs = new List<(int Left, int Right)>();
        for (var x = 200; x < 1160; x++)
        {
            var pale = 0;
            for (var y = 516; y < 522; y++)
            {
                var offset = Offset(x, y);
                var blue = image[offset];
                var green = image[offset + 1];
                var red = image[offset + 2];
                if (Math.Min(blue, Math.Min(green, red)) > 175 &&
                    Math.Max(blue, Math.Max(green, red)) - Math.Min(blue, Math.Min(green, red)) < 45)
                    pale++;
            }
            if (pale >= 4)
            {
                if (start < 0) start = x;
            }
            else if (start >= 0)
            {
                if (x - start >= 30) runs.Add((start, x - 1));
                start = -1;
            }
        }
        if (start >= 0 && 1160 - start >= 30) runs.Add((start, 1159));
        if (runs.Count == 0) return new RaisedCard(false, []);
        var positions = new SortedSet<int>();
        foreach (var run in runs)
        {
            var width = run.Right - run.Left + 1;
            var count = (int)Math.Round((width - 109) / (double)CardStep) + 1;
            var position = (int)Math.Round((run.Left - layout.Left) / (double)CardStep) + 1;
            var expectedLeft = layout.Left + (position - 1) * CardStep;
            var expectedWidth = 109 + (count - 1) * CardStep;
            if (count < 1 || position < 1 || position + count - 1 > layout.Count ||
                Math.Abs(run.Left - expectedLeft) > 3 || Math.Abs(width - expectedWidth) > 4)
                return new RaisedCard(true, []);
            for (var i = 0; i < count; i++)
                if (!positions.Add(position + i)) return new RaisedCard(true, []);
        }
        return new RaisedCard(true, positions.ToArray());
    }

    private static bool HasDimmedHand(byte[] image, Layout layout)
    {
        // A modal/auto-play shade can leave the pale top-edge geometry visible
        // while turning ordinary card ink into a false black JOKER. The middle
        // of the card row should remain light on the observed table skin.
        var brightness = new List<int>();
        var right = layout.Left + 109 + (layout.Count - 1) * CardStep;
        for (var x = layout.Left + 10; x < right - 10; x += 8)
        {
            var offset = Offset(x, 625);
            brightness.Add(Math.Min(image[offset],
                Math.Min(image[offset + 1], image[offset + 2])));
        }
        if (brightness.Count == 0) return true;
        brightness.Sort();
        return brightness[brightness.Count / 2] < 175;
    }

    private static Layout? FindLayout(byte[] image)
    {
        if (!ValidBmp(image)) return null;
        var left = -1;
        var right = -1;
        for (var x = 200; x < 1160; x++)
        {
            var pale = 0;
            for (var y = 532; y < 541; y++)
            {
                var offset = Offset(x, y);
                var b = image[offset];
                var g = image[offset + 1];
                var r = image[offset + 2];
                var minimum = Math.Min(b, Math.Min(g, r));
                var maximum = Math.Max(b, Math.Max(g, r));
                if (minimum > 175 && maximum - minimum < 45) pale++;
            }
            if (pale < 5) continue;
            if (left < 0) left = x;
            right = x;
        }
        if (left < 0) return null;
        var width = right - left + 1;
        var count = (int)Math.Round((width - 109) / 41.0) + 1;
        var expected = 109 + (count - 1) * CardStep;
        var center = (left + right) / 2.0;
        // The observed 20-card landlord row is shifted right: x=218..1106,
        // center 662, while the 17-card row is centered around x=632.
        var expectedCenter = count == 20 ? 662.0 : 631.5;
        return count is >= 1 and <= 20 && Math.Abs(width - expected) <= 3 &&
            Math.Abs(center - expectedCenter) <= 4 ? new Layout(left, count) : null;
    }

    private Prediction Predict(byte[] query, bool rank)
    {
        var byLabel = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var item in _templates)
        {
            var label = rank ? item.Rank : item.Suit;
            var template = rank ? item.RankMask : item.SuitMask;
            var score = Similarity(query, template, 48, rank ? 52 : 48);
            if (!byLabel.TryGetValue(label, out var previous) || score > previous)
                byLabel[label] = score;
        }
        var best = byLabel.OrderByDescending(pair => pair.Value).Take(2).ToArray();
        if (best.Length < 2) return new Prediction(null, 0, 0);
        var margin = best[0].Value - best[1].Value;
        var minimum = rank ? 0.60 : 0.85;
        return new Prediction(best[0].Value >= minimum && margin >= 0.03 ? best[0].Key : null,
            best[0].Value, margin);
    }

    private static double Similarity(byte[] left, byte[] right, int width, int height)
    {
        var best = 0.0;
        for (var dy = -1; dy <= 1; dy++)
        for (var dx = -1; dx <= 1; dx++)
        {
            var intersection = 0;
            var union = 0;
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var a = left[y * width + x] != 0;
                var sx = (x - dx + width) % width;
                var sy = (y - dy + height) % height;
                var b = right[sy * width + sx] != 0;
                if (a && b) intersection++;
                if (a || b) union++;
            }
            if (union > 0) best = Math.Max(best, (double)intersection / union);
        }
        return best;
    }

    private static byte[] Normalize(byte[] image, int x1, int y1, int x2, int y2,
        int targetWidth, int targetHeight)
    {
        var result = new byte[targetWidth * targetHeight];
        if (!ValidBmp(image) || x1 < 0 || y1 < 0 || x2 > Width || y2 > Height) return result;
        var sourceWidth = x2 - x1;
        var sourceHeight = y2 - y1;
        var ink = new bool[sourceWidth * sourceHeight];
        for (var y = 0; y < sourceHeight; y++)
        for (var x = 0; x < sourceWidth; x++)
        {
            var offset = Offset(x1 + x, y1 + y);
            ink[y * sourceWidth + x] = Math.Min(image[offset],
                Math.Min(image[offset + 1], image[offset + 2])) < 150;
        }

        var keep = new bool[ink.Length];
        var visited = new bool[ink.Length];
        for (var index = 0; index < ink.Length; index++)
        {
            if (!ink[index] || visited[index]) continue;
            var queue = new Queue<int>();
            var component = new List<int>();
            queue.Enqueue(index);
            visited[index] = true;
            var minX = sourceWidth;
            var minY = sourceHeight;
            var maxX = -1;
            var maxY = -1;
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                component.Add(current);
                var x = current % sourceWidth;
                var y = current / sourceWidth;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
                for (var ny = Math.Max(0, y - 1); ny <= Math.Min(sourceHeight - 1, y + 1); ny++)
                for (var nx = Math.Max(0, x - 1); nx <= Math.Min(sourceWidth - 1, x + 1); nx++)
                {
                    var neighbor = ny * sourceWidth + nx;
                    if (!ink[neighbor] || visited[neighbor]) continue;
                    visited[neighbor] = true;
                    queue.Enqueue(neighbor);
                }
            }
            if (component.Count < 2 || maxX - minX + 1 < 2 || maxY - minY + 1 < 2)
                continue;
            foreach (var pixel in component) keep[pixel] = true;
        }

        var bx1 = sourceWidth;
        var by1 = sourceHeight;
        var bx2 = -1;
        var by2 = -1;
        for (var y = 0; y < sourceHeight; y++)
        for (var x = 0; x < sourceWidth; x++)
        {
            if (!keep[y * sourceWidth + x]) continue;
            bx1 = Math.Min(bx1, x);
            by1 = Math.Min(by1, y);
            bx2 = Math.Max(bx2, x);
            by2 = Math.Max(by2, y);
        }
        if (bx2 < bx1) return result;
        var glyphWidth = bx2 - bx1 + 1;
        var glyphHeight = by2 - by1 + 1;
        var scale = Math.Min((targetWidth - 4.0) / glyphWidth, (targetHeight - 4.0) / glyphHeight);
        var resizedWidth = Math.Max(1, (int)Math.Round(glyphWidth * scale));
        var resizedHeight = Math.Max(1, (int)Math.Round(glyphHeight * scale));
        var originX = (targetWidth - resizedWidth) / 2;
        var originY = (targetHeight - resizedHeight) / 2;
        for (var y = 0; y < resizedHeight; y++)
        for (var x = 0; x < resizedWidth; x++)
        {
            var sx = (x + 0.5) * glyphWidth / resizedWidth - 0.5;
            var sy = (y + 0.5) * glyphHeight / resizedHeight - 0.5;
            var fx = Math.Clamp((int)Math.Floor(sx), 0, glyphWidth - 1);
            var fy = Math.Clamp((int)Math.Floor(sy), 0, glyphHeight - 1);
            var cx = Math.Clamp(fx + 1, 0, glyphWidth - 1);
            var cy = Math.Clamp(fy + 1, 0, glyphHeight - 1);
            var ax = Math.Clamp(sx - fx, 0, 1);
            var ay = Math.Clamp(sy - fy, 0, 1);
            double Pixel(int px, int py) => keep[(by1 + py) * sourceWidth + bx1 + px] ? 255 : 0;
            var top = Pixel(fx, fy) * (1 - ax) + Pixel(cx, fy) * ax;
            var bottom = Pixel(fx, cy) * (1 - ax) + Pixel(cx, cy) * ax;
            result[(originY + y) * targetWidth + originX + x] =
                (byte)(top * (1 - ay) + bottom * ay > 100 ? 1 : 0);
        }
        return result;
    }
}
