using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace HibernateDesk
{
    // Uses the monitor's full bounds, including the taskbar area. The window's
    // native placement preserves restored bounds even when it began maximized.
    public sealed class FullscreenWindowState
    {
        private readonly Window window;
        private readonly bool previewMode;
        private bool closed;
        private WindowStyle savedStyle;
        private ResizeMode savedResizeMode;
        private WindowState savedState;
        private SizeToContent savedSizeToContent;
        private WindowStartupLocation savedStartupLocation;
        private bool savedTopmost;
        private bool savedShowActivated;
        private Rect savedBounds;
        private double savedMinWidth, savedMinHeight, savedMaxWidth, savedMaxHeight;
        private WindowPlacement savedPlacement;
        private bool hasPlacement;

        public bool IsActive { get; private set; }

        public FullscreenWindowState(Window window, bool previewMode = false)
        {
            if (window == null) throw new ArgumentNullException("window");
            this.window = window;
            this.previewMode = previewMode;
            window.Closed += delegate { closed = true; IsActive = false; };
        }

        public void Enter()
        {
            window.Dispatcher.VerifyAccess();
            if (closed || IsActive) return;
            IntPtr handle = new WindowInteropHelper(window).EnsureHandle();
            MonitorInfo monitor = new MonitorInfo();
            monitor.Size = Marshal.SizeOf(typeof(MonitorInfo));
            if (!previewMode && !GetMonitorInfo(MonitorFromWindow(handle, 2), ref monitor))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取显示器范围。");

            savedStyle = window.WindowStyle;
            savedResizeMode = window.ResizeMode;
            savedState = window.WindowState;
            savedSizeToContent = window.SizeToContent;
            savedStartupLocation = window.WindowStartupLocation;
            savedTopmost = window.Topmost;
            savedShowActivated = window.ShowActivated;
            savedBounds = window.RestoreBounds;
            if (savedBounds.IsEmpty)
                savedBounds = new Rect(window.Left, window.Top, window.ActualWidth, window.ActualHeight);
            savedMinWidth = window.MinWidth;
            savedMinHeight = window.MinHeight;
            savedMaxWidth = window.MaxWidth;
            savedMaxHeight = window.MaxHeight;
            savedPlacement = new WindowPlacement();
            savedPlacement.Length = Marshal.SizeOf(typeof(WindowPlacement));
            hasPlacement = GetWindowPlacement(handle, ref savedPlacement);
            IsActive = true;

            try
            {
                window.WindowState = WindowState.Normal;
                window.WindowStyle = WindowStyle.None;
                window.ResizeMode = ResizeMode.NoResize;
                window.SizeToContent = SizeToContent.Manual;
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.MinWidth = 0;
                window.MinHeight = 0;
                window.MaxWidth = Double.PositiveInfinity;
                window.MaxHeight = Double.PositiveInfinity;
                window.Topmost = !previewMode;
                if (previewMode)
                {
                    window.ShowActivated = false;
                    window.Left = -10000;
                    window.Top = -10000;
                    window.Width = 1280;
                    window.Height = 720;
                    return;
                }

                NativeRect bounds = monitor.Monitor;
                HwndSource source = HwndSource.FromHwnd(handle);
                Matrix fromDevice = source != null && source.CompositionTarget != null
                    ? source.CompositionTarget.TransformFromDevice : Matrix.Identity;
                Point origin = fromDevice.Transform(new Point(bounds.Left, bounds.Top));
                Vector size = fromDevice.Transform(new Vector(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top));
                window.Left = origin.X;
                window.Top = origin.Y;
                window.Width = size.X;
                window.Height = size.Y;
                // Native coordinates avoid accumulating DIP rounding at mixed-DPI
                // monitor edges, including displays to the left of the primary one.
                if (!SetWindowPos(handle, new IntPtr(-1), bounds.Left, bounds.Top,
                    bounds.Right - bounds.Left, bounds.Bottom - bounds.Top, 0x0020 | 0x0040))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "无法显示全屏倒计时。");
            }
            catch
            {
                try { Exit(); } catch { }
                throw;
            }
        }

        public void Exit()
        {
            window.Dispatcher.VerifyAccess();
            if (!IsActive) return;
            IsActive = false;
            if (closed) return;
            window.WindowState = WindowState.Normal;
            window.WindowStyle = savedStyle;
            window.ResizeMode = savedResizeMode;
            window.Topmost = savedTopmost;
            window.ShowActivated = savedShowActivated;
            window.MinWidth = savedMinWidth;
            window.MinHeight = savedMinHeight;
            window.MaxWidth = savedMaxWidth;
            window.MaxHeight = savedMaxHeight;
            window.Left = savedBounds.Left;
            window.Top = savedBounds.Top;
            window.Width = savedBounds.Width;
            window.Height = savedBounds.Height;
            window.SizeToContent = savedSizeToContent;
            window.WindowStartupLocation = savedStartupLocation;
            window.WindowState = savedState;

            IntPtr handle = new WindowInteropHelper(window).Handle;
            // SetWindowPlacement intentionally moves entirely off-screen windows
            // back onto a monitor, so previews restore only their WPF bounds.
            if (!previewMode && hasPlacement && handle != IntPtr.Zero && IsWindow(handle))
            {
                WindowPlacement placement = savedPlacement;
                if (!SetWindowPlacement(handle, ref placement))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "无法恢复原窗口位置。");
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public int Size;
            public NativeRect Monitor;
            public NativeRect Work;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowPlacement
        {
            public int Length;
            public int Flags;
            public int ShowCommand;
            public NativePoint MinPosition;
            public NativePoint MaxPosition;
            public NativeRect NormalPosition;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowPlacement(IntPtr window, ref WindowPlacement placement);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPlacement(IntPtr window, ref WindowPlacement placement);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindow(IntPtr window);
    }
}
