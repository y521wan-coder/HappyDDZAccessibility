namespace HappyDDZ.Assistant;

/// <summary>
/// Local keyboard rehearsal only. This model never sends an action to the game.
/// </summary>
internal sealed class HandSelectionModel
{
    private readonly List<int> _pickedOrder = [];
    private IReadOnlyList<HandCard> _cards = [];
    public IReadOnlyList<HandCard> Cards => _cards;
    public int CurrentIndex { get; private set; } = -1;
    public int PickedCount => _pickedOrder.Count;

    public void Load(IReadOnlyList<HandCard> cards)
    {
        _cards = cards;
        _pickedOrder.Clear();
        CurrentIndex = cards.Count == 0 ? -1 : 0;
    }

    public bool IsPicked(int index) => _pickedOrder.Contains(index);

    public void SetCurrent(int index)
    {
        if (index >= 0 && index < _cards.Count) CurrentIndex = index;
    }

    public string ItemText(int index) =>
        $"{_cards[index]}，{(IsPicked(index) ? "已拿起（本地预览）" : "未拿起")}";

    public string MoveGroup(int direction)
    {
        if (_cards.Count == 0) return "没有可浏览的手牌候选。";
        if (CurrentIndex < 0) CurrentIndex = 0;
        var rank = _cards[CurrentIndex].Rank;
        for (var i = CurrentIndex + direction; i >= 0 && i < _cards.Count; i += direction)
        {
            if (IsPicked(i) || _cards[i].Rank == rank) continue;
            CurrentIndex = i;
            return DescribeGroup(i);
        }
        return "已到未拿起手牌的边界。";
    }

    public string MoveEnd(bool last)
    {
        for (var i = last ? _cards.Count - 1 : 0; i >= 0 && i < _cards.Count; i += last ? -1 : 1)
        {
            if (IsPicked(i)) continue;
            CurrentIndex = i;
            return DescribeGroup(i);
        }
        return "没有未拿起的手牌候选。";
    }

    public string PickOne()
    {
        if (CurrentIndex < 0) return "没有可拿起的手牌候选。";
        if (IsPicked(CurrentIndex))
        {
            var next = Enumerable.Range(CurrentIndex + 1, _cards.Count - CurrentIndex - 1)
                .Concat(Enumerable.Range(0, CurrentIndex + 1))
                .FirstOrDefault(index => !IsPicked(index), -1);
            if (next < 0) return "所有候选牌都已拿起。";
            CurrentIndex = next;
        }
        _pickedOrder.Add(CurrentIndex);
        return $"本地预览拿起：{_cards[CurrentIndex].Rank}。已拿起 {_pickedOrder.Count} 张；尚未点击游戏。";
    }

    public string PickGroup()
    {
        if (CurrentIndex < 0) return "没有可拿起的手牌候选。";
        var rank = _cards[CurrentIndex].Rank;
        if (rank == "未知") return "当前点数未知，无法按点数整组拿起。";
        if (!_cards.Select((card, i) => (card, i)).Any(item => item.card.Rank == rank && !IsPicked(item.i)))
        {
            var nextGroup = Enumerable.Range(CurrentIndex + 1, _cards.Count - CurrentIndex - 1)
                .FirstOrDefault(index => !IsPicked(index) && _cards[index].Rank != rank &&
                    _cards[index].Rank != "未知", -1);
            if (nextGroup < 0) return "没有其他未拿起的点数组。";
            CurrentIndex = nextGroup;
            rank = _cards[nextGroup].Rank;
        }
        var count = 0;
        for (var i = 0; i < _cards.Count; i++)
        {
            if (_cards[i].Rank != rank || IsPicked(i)) continue;
            _pickedOrder.Add(i);
            count++;
        }
        return count == 0 ? "这一点数的候选牌已经全部拿起。" :
            $"本地预览拿起：{count} 张 {rank}。已拿起 {_pickedOrder.Count} 张；尚未点击游戏。";
    }

    public string DropOne()
    {
        if (_pickedOrder.Count == 0) return "没有已拿起的候选牌。";
        var index = CurrentIndex >= 0 && IsPicked(CurrentIndex) ? CurrentIndex : _pickedOrder[0];
        _pickedOrder.Remove(index);
        return $"本地预览放下：{_cards[index].Rank}。还拿起 {_pickedOrder.Count} 张；尚未点击游戏。";
    }

    public string DropAll()
    {
        if (_pickedOrder.Count == 0) return "没有已拿起的候选牌。";
        var count = _pickedOrder.Count;
        _pickedOrder.Clear();
        return $"本地预览全部放下：{count} 张；尚未点击游戏。";
    }

    private string DescribeGroup(int index)
    {
        var rank = _cards[index].Rank;
        var count = _cards.Select((card, i) => (card, i))
            .Count(item => !IsPicked(item.i) && item.card.Rank == rank);
        return $"未拿起 {count} 张 {rank}，当前第 {index + 1} 张。牌面只是实验候选。";
    }
}
