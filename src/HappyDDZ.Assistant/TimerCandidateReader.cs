using System.Text.Json;

namespace HappyDDZ.Assistant;

/// <summary>Visible own-turn clock candidate. A stable bid clock also gates manual bid navigation.</summary>
internal sealed class TimerCandidateReader
{
    private const int Width = 1280;
    private const int Height = 720;
    private const int MaskWidth = 39;
    private const int MaskHeight = 36;
    private readonly IReadOnlyList<(int Seconds, byte[] Mask)> _templates;

    public TimerCandidateReader()
    {
        var path = Path.Combine(ProjectPaths.Root, "models", "timer", "clock-v1.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (root.GetProperty("game_version").GetString() != "8.057.106.17343" ||
            root.GetProperty("mask_width").GetInt32() != MaskWidth ||
            root.GetProperty("mask_height").GetInt32() != MaskHeight)
            throw new InvalidDataException("倒计时模板版本或尺寸不匹配。");
        _templates = root.GetProperty("templates").EnumerateArray()
            .Select(item => (item.GetProperty("seconds").GetInt32(),
                Convert.FromBase64String(item.GetProperty("mask").GetString() ?? "")))
            .ToArray();
        if (_templates.Count != 17 || _templates.Any(item =>
                item.Item1 < 4 || item.Item1 > 20 || item.Item2.Length != MaskWidth * MaskHeight))
            throw new InvalidDataException("倒计时模板不完整。");
    }

    public int? Read(byte[] first, byte[] second)
        => ReadAt(first, second, 0, 0);

    public int? ReadOwnCountdown(byte[] first, byte[] second)
    {
        var a = PredictAt(first, 0);
        var b = PredictAt(second, 0);
        return a is not null && b is not null && b >= 4 &&
            (a == b || a == b + 1) ? b : null;
    }

    public int? ReadAt(byte[] first, byte[] second, int offsetX, int offsetY)
    {
        if ((offsetX, offsetY) is not ((0, 0) or (75, -4))) return null;
        var a = PredictAt(first, offsetX);
        var b = PredictAt(second, offsetX);
        return a is not null && a == b ? a : null;
    }

    public int? ReadBidCountdown(byte[] first, byte[] second)
    {
        var (a, b) = ReadBidCountdownFrames(first, second);
        return a is not null && b is not null && b >= 4 &&
            (a == b || a == b + 1) ? b : null;
    }

    internal (int? First, int? Second) ReadBidCountdownFrames(byte[] first, byte[] second)
        => (PredictAt(first, 75), PredictAt(second, 75));

    private int? PredictAt(byte[] image, int offsetX)
    {
        if (offsetX == 0) return Predict(image, 0, 0);
        foreach (var y in new[] { -4, -3, -2, -1 })
        {
            var value = Predict(image, 75, y);
            if (value is not null) return value;
        }
        return null;
    }

    private int? Predict(byte[] bmp, int offsetX, int offsetY)
    {
        if (!HasOwnClock(bmp, offsetX, offsetY)) return null;
        var pixels = new byte[MaskWidth * MaskHeight];
        var index = 0;
        for (var y = 438 + offsetY; y < 474 + offsetY; y++)
        for (var x = 539 + offsetX; x < 578 + offsetX; x++)
        {
            var (red, green, blue) = Color(bmp, x, y);
            var min = Math.Min(red, Math.Min(green, blue));
            var max = Math.Max(red, Math.Max(green, blue));
            pixels[index++] = (byte)(min >= 210 && max - min <= 55 ? 1 : 0);
        }
        var scored = _templates.Select(item => (item.Seconds,
                Distance(pixels, item.Mask)))
            .OrderBy(item => item.Item2).ToArray();
        if (scored[0].Item2 > 0.05 || scored[1].Item2 - scored[0].Item2 < 0.018)
            return null;
        return scored[0].Seconds;
    }

    internal static bool HasOwnClock(byte[] bmp)
        => HasOwnClock(bmp, 0, 0);

    private static bool HasOwnClock(byte[] bmp, int offsetX, int offsetY)
    {
        if (bmp.Length < 54 + Width * Height * 4 ||
            BitConverter.ToInt32(bmp, 18) != Width ||
            BitConverter.ToInt32(bmp, 22) != -Height) return false;
        var left = Color(bmp, 530 + offsetX, 455 + offsetY);
        var right = Color(bmp, 580 + offsetX, 455 + offsetY);
        var bottom = Color(bmp, 555 + offsetX, 482 + offsetY);
        return left.Red > 200 && left.Green > 175 && left.Blue > 120 &&
            right.Red > 180 && right.Green > 100 && right.Blue < 130 &&
            bottom.Red > 190 && bottom.Green > 115 && bottom.Blue < 75;
    }

    private static double Distance(byte[] a, byte[] b)
    {
        var different = 0;
        for (var i = 0; i < a.Length; i++)
            if (a[i] != b[i]) different++;
        return (double)different / a.Length;
    }

    private static (int Red, int Green, int Blue) Color(byte[] bmp, int x, int y)
    {
        var offset = 54 + (y * Width + x) * 4;
        return (bmp[offset + 2], bmp[offset + 1], bmp[offset]);
    }
}
