using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Rimlight.Platform.Overlay;

// Registers the overlay and helper window classes once per process. Both share one window procedure, which forwards
// to the running OverlayHost; the delegate lives in a static field because the classes outlive any one host. Other
// hidden windows (the system watcher's) register their own class here too.
internal static class NativeWindowClass
{
    public const string HelperClassName = "Rimlight.OverlayHelper";

    private const int ErrorClassAlreadyExists = 1410;

    private static readonly object Gate = new();
    private static WNDPROC? procedure;
    private static HINSTANCE instance;
    private static bool registered;

    public static HINSTANCE Instance => instance;

    public static unsafe void Register(WNDPROC windowProcedure)
    {
        lock (Gate)
        {
            if (registered) return;
            instance = PInvoke.GetModuleHandle(default(PCWSTR));
            procedure ??= windowProcedure; // the first delegate stays alive for the process
            RegisterClass(OverlayWindow.ClassName, procedure);
            RegisterClass(HelperClassName, procedure);
            registered = true; // only once both exist, so a failed attempt is retried
        }
    }

    // Registers one more class. windowProcedure must stay alive for the process (a static field): the class does.
    // Registering the same class again is harmless.
    public static void Register(string name, WNDPROC windowProcedure)
    {
        lock (Gate)
        {
            if (instance.IsNull) instance = PInvoke.GetModuleHandle(default(PCWSTR));
            RegisterClass(name, windowProcedure);
        }
    }

    private static unsafe void RegisterClass(string name, WNDPROC windowProcedure)
    {
        fixed (char* className = name)
        {
            var windowClass = new WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(), // the delegate field makes sizeof() unavailable
                lpfnWndProc = windowProcedure,
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
