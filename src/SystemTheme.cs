using System;
using System.ComponentModel;
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
    public sealed class SystemTheme : IDisposable
    {
        private readonly Window window;
        private readonly Func<bool> darkModeReader;
        private readonly FrostedBackdrop backdrop;
        private HwndSource source;
        private bool disposed;
        private bool refreshQueued;
        private bool subscribedToSystemEvents;
        private int? lastCaptionMode;

        public bool IsDark { get; private set; }
        public bool IsGlassActive { get { return backdrop.IsAvailable; } }

        public SystemTheme(Window window, Func<bool> darkModeReader = null)
        {
            if (window == null) throw new ArgumentNullException("window");
            this.window = window;
            this.darkModeReader = darkModeReader ?? ReadDarkPreference;
            backdrop = new FrostedBackdrop(window);
            backdrop.AvailabilityChanged += OnBackdropChanged;
            window.SourceInitialized += OnSourceInitialized;
            window.Activated += OnActivated;
            window.Closed += OnClosed;
            SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
            try
            {
                SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
                subscribedToSystemEvents = true;
            }
            catch (InvalidOperationException) { }
            catch (ExternalException) { }
            AttachWindowSource();
            Refresh();
        }

        public void Refresh()
        {
            if (disposed || window.Dispatcher.HasShutdownStarted) return;
            if (!window.Dispatcher.CheckAccess())
            {
                QueueRefresh();
                return;
            }

            bool highContrast = SystemParameters.HighContrast;
            IsDark = !highContrast && darkModeReader();
            backdrop.Apply(IsDark, highContrast);
            if (highContrast) ApplyHighContrast();
            else ApplyPalette(IsDark);
            ApplyCaption();
        }

        private static bool ReadDarkPreference()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", false))
                {
                    object value = key == null ? null : key.GetValue("AppsUseLightTheme", 1);
                    return value is int && (int)value == 0;
                }
            }
            catch (SecurityException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (IOException) { return false; }
        }

        private void ApplyPalette(bool dark)
        {
            SetBrush("WindowBackground", backdrop.IsAvailable ? (dark ? "#400B2B55" : "#60C5E1FF") : (dark ? "#122B49" : "#DDEEFF"));
            SetBrush("CountdownBackground", backdrop.IsAvailable ? (dark ? "#260B2B55" : "#40C5E1FF") : (dark ? "#122B49" : "#DDEEFF"));
            SetBrush("TextPrimary", dark ? "#EEF6FF" : "#102E50");
            SetBrush("TextSecondary", dark ? "#EEF6FF" : "#284F78");
            SetBrush("Surface", dark ? "#183C7ABC" : "#48FFFFFF");
            SetBrush("SolidSurface", dark ? "#193C66" : "#E5F1FF");
            SetBrush("Border", dark ? "#506F9DCB" : "#6083ADDA");
            SetBrush("Divider", dark ? "#306CA1D6" : "#407195BC");
            SetBrush("ButtonBackground", dark ? "#404F87C3" : "#668DBAEF");
            SetBrush("Focus", dark ? "#91CDFF" : "#155AA7");
            SetBrush("IconFill", dark ? "#804D9FE8" : "#90A5D4FF");
            SetBrush("AccentText", dark ? "#EAF5FF" : "#1A528B");
            SetBrush("NoticeBackground", dark ? "#263B87D4" : "#448ABCF4");
            SetBrush("PrimaryBackground", dark ? "#E093C8FF" : "#E01B5DB0");
            SetBrush("PrimaryForeground", dark ? "#071E3B" : "#FFFFFF");
            SetBrush("StatusNeutral", dark ? "#AAC9E6" : "#57799B");
            SetBrush("StatusSuccess", dark ? "#8BE1FF" : "#1A6CB3");
            SetBrush("StatusWarning", dark ? "#FFD58C" : "#865314");
        }

        private void ApplyHighContrast()
        {
            SetResource("WindowBackground", SystemColors.WindowBrush);
            SetResource("CountdownBackground", SystemColors.WindowBrush);
            SetResource("TextPrimary", SystemColors.WindowTextBrush);
            SetResource("TextSecondary", SystemColors.WindowTextBrush);
            SetResource("Surface", SystemColors.WindowBrush);
            SetResource("SolidSurface", SystemColors.WindowBrush);
            SetResource("Border", SystemColors.WindowTextBrush);
            SetResource("Divider", SystemColors.WindowTextBrush);
            SetResource("ButtonBackground", SystemColors.ControlBrush);
            SetResource("Focus", SystemColors.HighlightBrush);
            SetResource("IconFill", SystemColors.HighlightBrush);
            SetResource("AccentText", SystemColors.WindowTextBrush);
            SetResource("NoticeBackground", SystemColors.WindowBrush);
            SetResource("PrimaryBackground", SystemColors.HighlightBrush);
            SetResource("PrimaryForeground", SystemColors.HighlightTextBrush);
            SetResource("StatusNeutral", SystemColors.WindowTextBrush);
            SetResource("StatusSuccess", SystemColors.WindowTextBrush);
            SetResource("StatusWarning", SystemColors.WindowTextBrush);
        }

        private void SetBrush(string name, string color)
        {
            SolidColorBrush brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            brush.Freeze();
            SetResource(name, brush);
        }

        private void SetResource(string name, Brush brush)
        {
            window.Resources[name] = brush;
        }

        private void AttachWindowSource()
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
            // WM_SETTINGCHANGE includes ImmersiveColorSet and high-contrast changes.
            // Defer until WPF has also processed the system preference notification.
            if (message == 0x001A || message == 0x031A) QueueRefresh();
            return IntPtr.Zero;
        }

        private void QueueRefresh()
        {
            if (disposed || window.Dispatcher.HasShutdownStarted) return;
            // SystemEvents can arrive on a worker thread. Coalescing is owned by the UI thread.
            window.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
            {
                if (disposed || refreshQueued) return;
                refreshQueued = true;
                window.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
                {
                    refreshQueued = false;
                    Refresh();
                }));
            }));
        }

        private void ApplyCaption()
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;
            int dark = IsDark ? 1 : 0;
            if (lastCaptionMode == dark) return;
            try
            {
                int result = DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
                if (result != 0) result = DwmSetWindowAttribute(handle, 19, ref dark, sizeof(int));
                if (result == 0) lastCaptionMode = dark;
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute,
            ref int value, int size);

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            AttachWindowSource();
            Refresh();
        }
        private void OnActivated(object sender, EventArgs e) { Refresh(); }
        private void OnBackdropChanged(object sender, EventArgs e) { QueueRefresh(); }
        private void OnClosed(object sender, EventArgs e) { Dispose(); }
        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) { QueueRefresh(); }
        private void OnSystemParametersChanged(object sender, PropertyChangedEventArgs e) { QueueRefresh(); }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            backdrop.AvailabilityChanged -= OnBackdropChanged;
            backdrop.Dispose();
            window.SourceInitialized -= OnSourceInitialized;
            window.Activated -= OnActivated;
            window.Closed -= OnClosed;
            SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
            if (subscribedToSystemEvents) SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            if (source != null)
            {
                source.RemoveHook(WindowMessage);
                source = null;
            }
        }
    }
}
