using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HappyDDZ.Assistant;

// Signatures follow the ZDSRAPI.txt and C# sample in the installed reader.
// Load the user's current API DLL; never bundle a stale reader DLL.
internal sealed class ZdsrSpeech : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private delegate int Initialize(int channel, [MarshalAs(UnmanagedType.LPWStr)] string? name,
        [MarshalAs(UnmanagedType.Bool)] bool keyInterrupt);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private delegate int SpeakText([MarshalAs(UnmanagedType.LPWStr)] string text,
        [MarshalAs(UnmanagedType.Bool)] bool interrupt);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SpeakState();
    private IntPtr _library;
    private SpeakText? _speak;
    private SpeakState? _state;
    public string Status { get; private set; } = "争渡语音接口尚未连接。";

    private static string? FindApi()
    {
        foreach (var name in new[] { "ZDSRMain_x64", "ZDSRMain" })
        foreach (var process in Process.GetProcessesByName(name))
        {
            using (process)
            {
                try
                {
                    var executable = process.MainModule?.FileName;
                    if (executable is null) continue;
                    var path = Path.Combine(Path.GetDirectoryName(executable)!, "ZDSRAPI_x64.dll");
                    if (File.Exists(path)) return path;
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
            }
        }
        return null;
    }

    public int TrySpeak(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        try
        {
            if (_library == IntPtr.Zero)
            {
                var api = FindApi();
                if (api is null) { Status = "未找到运行中的争渡，可用读屏浏览状态框。"; return -1; }
                _library = NativeLibrary.Load(api);
                var initialize = Marshal.GetDelegateForFunctionPointer<Initialize>(NativeLibrary.GetExport(_library, "InitTTS"));
                _speak = Marshal.GetDelegateForFunctionPointer<SpeakText>(NativeLibrary.GetExport(_library, "Speak"));
                _state = Marshal.GetDelegateForFunctionPointer<SpeakState>(NativeLibrary.GetExport(_library, "GetSpeakState"));
                var initialized = initialize(0, null, true);
                if (initialized != 0) { Dispose(); Status = $"争渡接口初始化失败，代码 {initialized}。"; return initialized; }
            }
            var code = _speak!(text, true);
            Status = code == 0 ? "争渡已接受操作结果朗读。" : $"争渡未接受朗读，代码 {code}；可用读屏浏览状态框。";
            return code;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or System.ComponentModel.Win32Exception)
        {
            Dispose();
            Status = "争渡接口暂不可用，可用读屏浏览状态框。";
            return -1;
        }
    }

    public int GetState() => _state?.Invoke() ?? -1;
    public void Dispose()
    {
        _speak = null;
        _state = null;
        if (_library != IntPtr.Zero) NativeLibrary.Free(_library);
        _library = IntPtr.Zero;
    }
}
