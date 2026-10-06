using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Rimlight.Platform.Overlay;

// Registers the overlay and helper window classes once per process. Both share one window procedure, which forwards
// to the running OverlayHost; the delegate lives in a static field because the classes outlive any one host.
internal static class NativeWindowClass
{
    public const string HelperClassName = "Rimlight.OverlayHelper";

    private const int ErrorClassAlreadyExists = 1410;

    private static readonly object Gate = new();
    private static WNDPROC? procedure;
    private static HINSTANCE instance;

    public static HINSTANCE Instance => instance;

    public static unsafe void Register(WNDPROC windowProcedure)
    {
        lock (Gate)
        {
            if (procedure is not null) return;
            instance = PInvoke.GetModuleHandle(default(PCWSTR));
            procedure = windowProcedure;
            RegisterClass(OverlayWindow.ClassName);
            RegisterClass(HelperClassName);
        }
    }

    private static unsafe void RegisterClass(string name)
    {
        fixed (char* className = name)
        {
            var windowClass = new WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(), // the delegate field makes sizeof() unavailable
                lpfnWndProc = procedure!,
                hInstance = instance,
                lpszClassName = className,
            };
            if (PInvoke.RegisterClassEx(in windowClass) == 0)
            {
                int error = Marshal.GetLastPInvokeError();
                if (error != ErrorClassAlreadyExists) throw new Win32Exception(error, $"RegisterClassEx failed for {name}.");
            }
        }
    }
}
