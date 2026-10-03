namespace HappyDDZ.Assistant;

internal static class HandIdentity
{
    public static bool IsConsistent(IReadOnlyList<HandCard> cards)
    {
        var known = cards.Where(card => card.Rank != "未知").ToArray();
        if (known.GroupBy(card => card.Rank).Any(group =>
            group.Count() > (group.Key is "大王" or "小王" ? 1 : 4))) return false;
        return !known.Where(card => card.Suit != "未知")
            .GroupBy(card => (card.Rank, card.Suit)).Any(group => group.Count() > 1);
    }
}
