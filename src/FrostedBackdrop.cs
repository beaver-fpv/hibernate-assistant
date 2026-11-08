using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace HibernateDesk
{
    // Windows 11's system-drawn Desktop Acrylic supplies the live blur.
    // The WPF Window stays transparent and the content supplies one blue tint.
    public sealed class FrostedBackdrop : IDisposable
    {
        private readonly Window window;
        private HwndSource source;
        private bool isDark;
        private bool highContrast;
        private bool disposed;
        private bool queued;

        public bool IsAvailable { get; private set; }
        public event EventHandler AvailabilityChanged;

        public FrostedBackdrop(Window window)
        {
            if (window == null) throw new ArgumentNullException("window");
            this.window = window;
            window.SourceInitialized += OnSourceInitialized;
            window.Activated += OnWindowChanged;
            window.StateChanged += OnWindowChanged;
            window.Closed += OnClosed;
            AttachSource();
        }

        public void Apply(bool isDark, bool highContrast)
        {
            if (disposed || window.Dispatcher.HasShutdownStarted) return;
            if (!window.Dispatcher.CheckAccess())
            {
                window.Dispatcher.BeginInvoke(new Action(delegate { Apply(isDark, highContrast); }));
                return;
            }
            this.isDark = isDark;
            this.highContrast = highContrast;
            AttachSource();
            if (source == null || source.CompositionTarget == null)
            {
                SetAvailable(false);
                return;
            }

            IntPtr handle = source.Handle;
            bool enabled = false;
            try
            {
                bool composition;
                bool canUseAcrylic = !highContrast && !SystemParameters.HighContrast
                    && TransparencyEnabled()
                    && DwmIsCompositionEnabled(out composition) >= 0 && composition;
                if (canUseAcrylic)
                {
                    int dark = isDark ? 1 : 0;
                    DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
                    source.CompositionTarget.BackgroundColor = Colors.Transparent;
                    Margins glass = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                    int material = 3; // DWMSBT_TRANSIENTWINDOW: Desktop Acrylic.
                    int frameResult = DwmExtendFrameIntoClientArea(handle, ref glass);
                    int backdropResult = DwmSetWindowAttribute(handle, 38, ref material, sizeof(int));
                    enabled = frameResult >= 0 && backdropResult >= 0;
                }
                if (!enabled) DisableNativeBackdrop(handle);
            }
            catch (DllNotFoundException) { enabled = false; }
            catch (EntryPointNotFoundException) { enabled = false; }
            catch (InvalidOperationException) { enabled = false; }

            if (!enabled && source.CompositionTarget != null)
                source.CompositionTarget.BackgroundColor = highContrast
                    ? SystemColors.WindowColor : (isDark ? Color.FromRgb(12, 27, 49) : Color.FromRgb(225, 238, 255));
            SetAvailable(enabled);
        }

        private void SetAvailable(bool available)
        {
            if (IsAvailable == available) return;
            IsAvailable = available;
            EventHandler handler = AvailabilityChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private static bool TransparencyEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", false))
                {
                    object value = key == null ? null : key.GetValue("EnableTransparency", 1);
                    return !(value is int) || (int)value != 0;
                }
            }
            catch (SecurityException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (IOException) { return false; }
        }

        private static void DisableNativeBackdrop(IntPtr handle)
        {
            int none = 1; // DWMSBT_NONE.
            DwmSetWindowAttribute(handle, 38, ref none, sizeof(int));
            Margins margins = new Margins();
            DwmExtendFrameIntoClientArea(handle, ref margins);
        }

        private void AttachSource()
        {
            if (source != null || disposed) return;
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;
            source = HwndSource.FromHwnd(handle);
            if (source != null) source.AddHook(WindowMessage);
        }

        private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam,
            IntPtr lParam, ref bool handled)
        {
            // Reapply after frame/style changes used by fullscreen and after
            // Windows changes transparency, theme, or desktop composition.
            if (message == 0x007D || message == 0x001A || message == 0x031A || message == 0x031E)
                QueueApply();
            return IntPtr.Zero;
        }

        private void QueueApply()
        {
            if (disposed || queued || window.Dispatcher.HasShutdownStarted) return;
            queued = true;
            window.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
            {
                queued = false;
                if (!disposed) Apply(isDark, highContrast);
            }));
        }

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            AttachSource();
            Apply(isDark, highContrast);
        }
        private void OnWindowChanged(object sender, EventArgs e) { QueueApply(); }
        private void OnClosed(object sender, EventArgs e) { Dispose(); }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            window.SourceInitialized -= OnSourceInitialized;
            window.Activated -= OnWindowChanged;
            window.StateChanged -= OnWindowChanged;
            window.Closed -= OnClosed;
            if (source != null)
            {
                source.RemoveHook(WindowMessage);
                source = null;
            }
            AvailabilityChanged = null;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Margins { public int Left, Right, Top, Bottom; }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr window, ref Margins margins);
        [DllImport("dwmapi.dll")]
        private static extern int DwmIsCompositionEnabled([MarshalAs(UnmanagedType.Bool)] out bool enabled);
    }
}
