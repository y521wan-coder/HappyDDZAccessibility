using System.Text.Json;

namespace HappyDDZ.Assistant;

/// <summary>Checks only the visible lettering inside both own bid buttons.</summary>
internal sealed class BidPhaseReader
{
    private sealed record Box(int X, int Y, int Width, int Height,
        byte[] Call, byte[] Rob);

    private readonly Box[] _boxes;

    public BidPhaseReader()
    {
        var path = Path.Combine(ProjectPaths.Root, "models", "controls", "bid-phase-v1.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (root.GetProperty("game_version").GetString() != "8.057.106.17343")
            throw new InvalidDataException("叫/抢地主按钮模板版本不匹配。");
        _boxes = root.GetProperty("boxes").EnumerateObject().Select(entry =>
        {
            var value = entry.Value;
            var x = value.GetProperty("x").GetInt32();
            var y = value.GetProperty("y").GetInt32();
            var width = value.GetProperty("width").GetInt32();
            var height = value.GetProperty("height").GetInt32();
            var call = Convert.FromBase64String(value.GetProperty("call").GetString() ?? "");
            var rob = Convert.FromBase64String(value.GetProperty("rob").GetString() ?? "");
            if (width <= 0 || height <= 0 || x < 0 || y < 0 ||
                x + width > 1280 || y + height > 720 ||
                call.Length != width * height || rob.Length != width * height ||
                call.Any(pixel => pixel > 1) || rob.Any(pixel => pixel > 1))
                throw new InvalidDataException("叫/抢地主按钮模板尺寸或像素不合法。");
            return new Box(x, y, width, height, call, rob);
        }).ToArray();
        if (_boxes.Length != 2)
            throw new InvalidDataException("叫/抢地主按钮模板不完整。");
    }

    public bool IsPhase(byte[] bmp, bool rob)
    {
        if (bmp.Length < 54 + 1280 * 720 * 4 ||
            BitConverter.ToInt32(bmp, 18) != 1280 ||
            BitConverter.ToInt32(bmp, 22) != -720) return false;
        foreach (var box in _boxes)
        {
            var expected = rob ? box.Rob : box.Call;
            var different = 0;
            var index = 0;
            for (var y = box.Y; y < box.Y + box.Height; y++)
            for (var x = box.X; x < box.X + box.Width; x++)
            {
                var offset = 54 + (y * 1280 + x) * 4;
                var white = bmp[offset + 2] >= 225 &&
                    bmp[offset + 1] >= 220 && bmp[offset] >= 175;
                if ((white ? 1 : 0) != expected[index++]) different++;
            }
            if (different > 40) return false;
        }
        return true;
    }
}
