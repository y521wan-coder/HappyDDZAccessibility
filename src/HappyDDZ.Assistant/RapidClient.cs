using System.Runtime.InteropServices;
using System.Text.Json;

namespace HappyDDZ.Assistant;

internal sealed record OcrLine(string Text, double Confidence, int X, int Y, int Width, int Height);
internal sealed record OcrResult(string Text, IReadOnlyList<OcrLine> Lines, double ElapsedMs);

internal sealed class RapidClient : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Initialize(IntPtr options, out ulong handle, out IntPtr json);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Recognize(ulong handle, IntPtr data, nuint byteCount, IntPtr options, out IntPtr json);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Destroy(ulong handle, out IntPtr json);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void Free(IntPtr json);

    private readonly IntPtr _library;
    private readonly Initialize _initialize;
    private readonly Recognize _recognize;
    private readonly Destroy _destroy;
    private readonly Free _free;
    private ulong _handle;
    private bool _disposed;

    public RapidClient(string dllPath, string backend)
    {
        _library = NativeLibrary.Load(dllPath);
        _initialize = Get<Initialize>("rapid_initialize");
        _recognize = Get<Recognize>("rapid_ocr");
        _destroy = Get<Destroy>("rapid_destroy");
        _free = Get<Free>("rapid_free");
        if (backend is not ("cpu" or "dml")) throw new ArgumentOutOfRangeException(nameof(backend));
        var options = Marshal.StringToCoTaskMemUTF8($"{{\"backend\":\"{backend}\",\"det\":\"PP-OCRv6_det_tiny\",\"rec\":\"PP-OCRv6_rec_tiny\"}}");
        try
        {
            var code = _initialize(options, out _handle, out var json);
            using var response = ReadResponse(code, json);
        }
        catch
        {
            NativeLibrary.Free(_library);
            throw;
        }
        finally { Marshal.FreeCoTaskMem(options); }
    }

    private T Get<T>(string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));

    private JsonDocument ReadResponse(int code, IntPtr json)
    {
        try
        {
            var value = json == IntPtr.Zero ? "{}" : Marshal.PtrToStringUTF8(json) ?? "{}";
            if (code != 0)
            {
                using var error = JsonDocument.Parse(value);
                var message = error.RootElement.TryGetProperty("error", out var property) ? property.GetString() : "未知错误";
                throw new InvalidOperationException($"RapidV6 错误 {code}：{message}");
            }
            return JsonDocument.Parse(value);
        }
        finally { if (json != IntPtr.Zero) _free(json); }
    }

    public OcrResult Read(byte[] image)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var pinned = GCHandle.Alloc(image, GCHandleType.Pinned);
        var options = Marshal.StringToCoTaskMemUTF8("{\"mode\":\"ocr\"}");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var code = _recognize(_handle, pinned.AddrOfPinnedObject(), (nuint)image.Length, options, out var json);
            watch.Stop();
            using var result = ReadResponse(code, json);
            var root = result.RootElement;
            var lines = new List<OcrLine>();
            foreach (var element in root.GetProperty("lines").EnumerateArray())
                lines.Add(new OcrLine(
                    element.GetProperty("text").GetString() ?? "",
                    element.GetProperty("confidence").GetDouble(),
                    element.GetProperty("x").GetInt32(), element.GetProperty("y").GetInt32(),
                    element.GetProperty("width").GetInt32(), element.GetProperty("height").GetInt32()));
            return new OcrResult(root.GetProperty("text").GetString() ?? "", lines, watch.Elapsed.TotalMilliseconds);
        }
        finally
        {
            Marshal.FreeCoTaskMem(options);
            pinned.Free();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_handle != 0)
        {
            var code = _destroy(_handle, out var json);
            using var response = ReadResponse(code, json);
            _handle = 0;
        }
        NativeLibrary.Free(_library);
    }
}
