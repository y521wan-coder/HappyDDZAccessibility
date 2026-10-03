using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace HappyDDZ.Assistant;

internal static class Program
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    internal static readonly uint ShowExistingMessage = RegisterWindowMessage("HappyDDZAccessibility.ShowAssistant.v1");
    [STAThread]
    private static void Main()
    {
        using var instance = new Mutex(true, @"Local\HappyDDZAccessibility.Assistant.v1", out var firstInstance);
        if (!firstInstance)
        {
            PostMessage(new IntPtr(0xffff), ShowExistingMessage, IntPtr.Zero, IntPtr.Zero);
            return;
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
