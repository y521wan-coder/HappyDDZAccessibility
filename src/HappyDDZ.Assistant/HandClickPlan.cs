namespace HappyDDZ.Assistant;

/// <summary>
/// One reversible card selection click. Playing and passing are outside this plan.
/// </summary>
internal sealed record HandClickPlan(int Position, int ClientX, int ClientY,
    IReadOnlyList<HandCard> Cards, int LayoutLeft);

internal static class HandClickPlanner
{
    private const int CardStep = 41;

    public static HandClickPlan Create(ScanResult scan, int position)
    {
        if (scan.Scene is not ("牌桌" or "叫地主阶段") ||
            scan.ClientWidth != 1280 || scan.ClientHeight != 720 ||
            DateTimeOffset.Now - scan.CapturedAt > TimeSpan.FromSeconds(5))
            throw new InvalidOperationException("请在已知版本的实时牌桌重新完整扫描；没有点击游戏手牌。");
        var hand = scan.Hand;
        if (hand is null)
            throw new InvalidOperationException("没有连续两帧稳定的手牌；没有点击游戏手牌。");
        if (hand.Cards.Count is not (>= 1 and <= 17) and not 20)
            throw new InvalidOperationException(
                $"完整牌面候选为 {hand.Cards.Count} 张，画面观察到 {hand.ObservedCount} 张；没有点击游戏手牌。");
        if (hand.ObservedCount != hand.Cards.Count)
            throw new InvalidOperationException("手牌总张数与牌面候选数量不同；没有点击游戏手牌。");
        if (hand.LayoutLeft < 200 || hand.LayoutLeft > 650)
            throw new InvalidOperationException($"手牌左边界 {hand.LayoutLeft} 超出已验证范围；没有点击游戏手牌。");
        if (hand.RaisedPositions.Count != 0)
            throw new InvalidOperationException(
                $"完整扫描已观察到抬起位置 {string.Join("、", hand.RaisedPositions)}；请先放下或恢复状态。");
        if (hand.Cards.Any(card => card.Rank == "未知" || card.Suit == "未知"))
            throw new InvalidOperationException("完整牌面仍含未知点数或花色；没有点击游戏手牌。");
        if (!HandIdentity.IsConsistent(hand.Cards))
            throw new InvalidOperationException("整手牌候选互相矛盾；没有点击游戏手牌。");
        if (position < 1 || position > hand.Cards.Count)
            throw new InvalidOperationException("所选牌序号已变化；没有点击游戏手牌。");
        var expectedLeft = hand.Cards.Count == 20 ? 218 :
            (int)Math.Round(631.5 - (109 + (hand.Cards.Count - 1) * CardStep - 1) / 2.0);
        if (Math.Abs(hand.LayoutLeft - expectedLeft) > 4)
            throw new InvalidOperationException("牌列位置不在已识别的居中布局；没有点击游戏手牌。");
        var x = hand.LayoutLeft + (position - 1) * CardStep + 20;
        return new HandClickPlan(position, x, 560, hand.Cards.ToArray(), hand.LayoutLeft);
    }

    public static bool SameHand(HandClickPlan plan, HandRead read) =>
        read.LayoutLeft == plan.LayoutLeft && read.RaisedPosition == 0 && read.RaisedPositions.Count == 0 &&
        read.ObservedCount == plan.Cards.Count &&
        read.Cards.Count == plan.Cards.Count &&
        plan.Cards.Zip(read.Cards).All(pair => pair.First.Rank == pair.Second.Rank &&
            pair.First.Suit == pair.Second.Suit);

    public static bool SameRaisedHand(HandClickPlan plan, HandRead read, IReadOnlyList<int> positions) =>
        read.LayoutLeft == plan.LayoutLeft && read.ObservedCount == plan.Cards.Count &&
        positions.Count > 0 && positions.All(position => position >= 1 && position <= plan.Cards.Count) &&
        read.RaisedPositions.SequenceEqual(positions);
}
