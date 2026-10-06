using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Rimlight.Platform.Overlay;

// A borderless, click-through, topmost window covering one monitor (doc 04 §1). DirectComposition supplies its
// content (WS_EX_NOREDIRECTIONBITMAP), so the window itself never paints. Created and used by the overlay thread.
internal sealed class OverlayWindow : IDisposable
{
    public const string ClassName = "Rimlight.Overlay";

    private const WINDOW_EX_STYLE ExStyle =
        WINDOW_EX_STYLE.WS_EX_NOREDIRECTIONBITMAP // DirectComposition provides the content
        | WINDOW_EX_STYLE.WS_EX_LAYERED | WINDOW_EX_STYLE.WS_EX_TRANSPARENT // together: mouse input passes through
        | WINDOW_EX_STYLE.WS_EX_TOPMOST
        | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW // not in the taskbar or Alt-Tab
        | WINDOW_EX_STYLE.WS_EX_NOACTIVATE; // never takes focus

    private OverlayWindow(HWND handle, RECT bounds)
    {
        Handle = handle;
        Bounds = bounds;
    }

    public HWND Handle { get; }
    public RECT Bounds { get; private set; }
    public int Width => Bounds.right - Bounds.left;
    public int Height => Bounds.bottom - Bounds.top;

    // The window class must already be registered on this thread's process (NativeWindowClass.Register).
    public static unsafe OverlayWindow Create(RECT bounds, bool excludeFromCapture)
    {
        HWND hwnd;
        fixed (char* className = ClassName)
        fixed (char* title = "Rimlight overlay")
        {
            // Created directly at the monitor's physical rectangle, so it starts on the right monitor with that
            // monitor's DPI and never receives a WM_DPICHANGED for the initial placement.
            hwnd = PInvoke.CreateWindowEx(ExStyle, className, title, WINDOW_STYLE.WS_POPUP,
                bounds.left, bounds.top, bounds.right - bounds.left, bounds.bottom - bounds.top,
                default, default, NativeWindowClass.Instance, null);
        }
        if (hwnd.IsNull) throw new Win32Exception(Marshal.GetLastPInvokeError(), "CreateWindowEx failed for the overlay window.");

        // A layered window stays invisible until an attribute is set.
        PInvoke.SetLayeredWindowAttributes(hwnd, default, 255, LAYERED_WINDOW_ATTRIBUTES_FLAGS.LWA_ALPHA);
        BOOL excluded = true;
        PInvoke.DwmSetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_EXCLUDED_FROM_PEEK, &excluded, (uint)sizeof(BOOL));
        var window = new OverlayWindow(hwnd, bounds);
        window.SetCaptureExclusion(excludeFromCapture);
        return window;
    }

    // Shows the window topmost at its rectangle without activating it. Called once its surface exists.
    public void Show() =>
        PInvoke.SetWindowPos(Handle, HWND.HWND_TOPMOST, Bounds.left, Bounds.top, Width, Height,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW);

    public void Move(RECT bounds)
    {
        Bounds = bounds;
        Show();
    }

    // Other topmost windows can cover the overlay; this puts it back on top of the topmost band.
    public void ReassertTopmost() =>
        PInvoke.SetWindowPos(Handle, HWND.HWND_TOPMOST, 0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);

    // "Hide from screen capture" (doc 04 §1): Windows 10 2004+ excludes the window from screenshots and recordings.
    public void SetCaptureExclusion(bool exclude) =>
        PInvoke.SetWindowDisplayAffinity(Handle, exclude ? WINDOW_DISPLAY_AFFINITY.WDA_EXCLUDEFROMCAPTURE : WINDOW_DISPLAY_AFFINITY.WDA_NONE);

    public void Dispose() => PInvoke.DestroyWindow(Handle);
}
