using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;

namespace HibernateDesk
{
    public static class SelfTest
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr parameter, string value);

        private sealed class FakePower : IPowerService
        {
            public bool Ready = true;
            public bool FailHibernate;
            public int HibernateCalls;
            public PowerStatus ReadStatus()
            {
                return new PowerStatus { CanHibernate = Ready, HiberFileExists = Ready,
                    HiberFileBytes = Ready ? 13649965056 : 0, FreeBytes = 44862574592,
                    Detail = Ready ? "测试状态" : "测试状态：休眠未开启" };
            }
            public void EnableHibernate() { Ready = true; }
            public void Hibernate()
            {
                Interlocked.Increment(ref HibernateCalls);
                if (FailHibernate) throw new InvalidOperationException("测试失败信息");
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
        private static async Task WaitFor(Func<bool> condition)
        {
            DateTime timeout = DateTime.UtcNow.AddSeconds(10);
            while (!condition())
            {
                if (DateTime.UtcNow > timeout) throw new TimeoutException("UI condition timed out");
                await Task.Delay(25);
            }
        }
        private static MainWindow Open(FakePower power, Func<bool> darkModeReader = null)
        {
            MainWindow ui = new MainWindow(power, darkModeReader, true);
            ui.Window.WindowStartupLocation = WindowStartupLocation.Manual;
            ui.Window.Left = -10000; ui.Window.Top = -10000;
            ui.Window.ShowInTaskbar = false; ui.Window.ShowActivated = false;
            ui.Window.Show();
            return ui;
        }

        public static int Run(string output)
        {
            int code = 0;
            List<string> results = new List<string>();
            Application app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Dispatcher.BeginInvoke(new Action(async delegate {
                try
                {
                    DateTime now = DateTime.UtcNow;
                    HibernateCountdown count = new HibernateCountdown();
                    Require(!count.TryComplete(now), "Idle dispatch");
                    count.Start(now);
                    Require(count.Remaining(now) == 5 && !count.TryComplete(now.AddSeconds(4)), "Early dispatch");
                    count.Cancel();
                    Require(!count.TryComplete(now.AddMinutes(1)), "Cancel dispatch");
                    count.Start(now);
                    Require(count.TryComplete(now.AddSeconds(5)) && !count.TryComplete(now.AddSeconds(6)), "Duplicate dispatch");
                    results.Add("PASS countdown never dispatches early, after cancel, or twice");

                    bool dark = false;
                    FakePower themePower = new FakePower();
                    MainWindow themed = Open(themePower, () => dark);
                    await WaitFor(() => ((Button)themed.Window.FindName("HibernateButton")).IsEnabled);
                    Color lightBackground = ((SolidColorBrush)themed.Window.Resources["WindowBackground"]).Color;
                    themed.Capture(Path.Combine(Path.GetDirectoryName(output), "theme-light.png"));
                    dark = true;
                    SendMessage(new WindowInteropHelper(themed.Window).Handle, 0x001A, IntPtr.Zero, "ImmersiveColorSet");
                    await WaitFor(() => themed.Theme.IsDark && ((SolidColorBrush)themed.Window.Resources["WindowBackground"]).Color != lightBackground);
                    Color darkBackground = ((SolidColorBrush)themed.Window.Resources["WindowBackground"]).Color;
                    Color darkText = ((SolidColorBrush)themed.Window.Foreground).Color;
                    Require(darkText.R > darkBackground.R, "Dark mode foreground did not change");
                    themed.Capture(Path.Combine(Path.GetDirectoryName(output), "theme-dark.png"));
                    double originalWidth = themed.Window.Width;
                    double originalHeight = themed.Window.Height;
                    WindowStyle originalStyle = themed.Window.WindowStyle;
                    await themed.BeginHibernate();
                    Require(themed.IsFullscreen && themed.Window.WindowStyle == WindowStyle.None, "Countdown did not enter borderless fullscreen");
                    themed.Capture(Path.Combine(Path.GetDirectoryName(output), "theme-dark-countdown.png"));
                    dark = false;
                    SendMessage(new WindowInteropHelper(themed.Window).Handle, 0x031A, IntPtr.Zero, null);
                    await WaitFor(() => !themed.Theme.IsDark && ((SolidColorBrush)themed.Window.Resources["WindowBackground"]).Color == lightBackground);
                    Require(((Button)themed.Window.FindName("CancelButton")).Visibility == Visibility.Visible, "Theme switch reset countdown");
                    themed.Capture(Path.Combine(Path.GetDirectoryName(output), "fullscreen-light.png"));
                    KeyEventArgs escape = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(themed.Window), 0, Key.Escape);
                    escape.RoutedEvent = Keyboard.PreviewKeyDownEvent;
                    themed.Window.RaiseEvent(escape);
                    Require(!themed.IsFullscreen && themed.Window.Width == originalWidth && themed.Window.Height == originalHeight && themed.Window.WindowStyle == originalStyle, "Escape failed to restore original window");
                    Require(themePower.HibernateCalls == 0, "Theme change dispatched a power operation");
                    themed.Window.Close();
                    themed.Theme.Refresh();
                    results.Add("PASS live light-dark-light switching via native window notifications; countdown preserved and no power action");

                    FakePower power = new FakePower();
                    MainWindow ui = Open(power);
                    Button primary = (Button)ui.Window.FindName("HibernateButton");
                    await WaitFor(() => primary.IsEnabled);
                    Require(power.HibernateCalls == 0, "Launch dispatched hibernate");
                    ui.Capture(Path.Combine(Path.GetDirectoryName(output), "test-ready.png"));
                    await ui.BeginHibernate();
                    Require(ui.IsFullscreen, "Start did not open fullscreen countdown");
                    ui.Capture(Path.Combine(Path.GetDirectoryName(output), "test-countdown.png"));
                    await Task.Delay(1200);
                    Require(((TextBlock)ui.CountdownView.FindName("CountdownNumber")).Text != "5", "Fullscreen countdown number did not update");
                    ((Button)ui.CountdownView.FindName("FullscreenCancelButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Require(!ui.IsFullscreen, "Cancel did not leave fullscreen");
                    await Task.Delay(5300);
                    Require(power.HibernateCalls == 0, "Cancelled UI dispatched");
                    results.Add("PASS opening the UI and cancelling the real button flow invoke no power action");

                    primary.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await WaitFor(() => power.HibernateCalls == 1);
                    Require(!ui.IsFullscreen, "Completed countdown did not restore window");
                    await Task.Delay(500);
                    Require(power.HibernateCalls == 1, "Duplicate UI dispatch");
                    results.Add("PASS start button dispatches exactly once to fake service after countdown");
                    ui.Window.Close();

                    FakePower disabled = new FakePower { Ready = false };
                    MainWindow unavailable = Open(disabled);
                    await WaitFor(() => ((TextBlock)unavailable.Window.FindName("StatusTitle")).Text == "休眠暂不可用");
                    Require(!((Button)unavailable.Window.FindName("HibernateButton")).IsEnabled, "Unavailable button enabled");
                    unavailable.Capture(Path.Combine(Path.GetDirectoryName(output), "test-unavailable.png"));
                    ((Button)unavailable.Window.FindName("EnableButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await WaitFor(() => ((Button)unavailable.Window.FindName("HibernateButton")).IsEnabled);
                    Require(disabled.HibernateCalls == 0, "Enabling feature triggered hibernate");
                    await unavailable.BeginHibernate();
                    unavailable.Window.Close();
                    await Task.Delay(5300);
                    Require(disabled.HibernateCalls == 0, "Closed UI dispatched");
                    results.Add("PASS disabled state, fake enable, and close-during-countdown");

                    FakePower failure = new FakePower { FailHibernate = true };
                    MainWindow failed = Open(failure);
                    await WaitFor(() => ((Button)failed.Window.FindName("HibernateButton")).IsEnabled);
                    await failed.BeginHibernate();
                    await WaitFor(() => ((TextBlock)failed.Window.FindName("ActionMessage")).Text.Contains("测试失败信息"));
                    Require(((Button)failed.Window.FindName("HibernateButton")).IsEnabled, "Failure prevented retry");
                    Require(!failed.IsFullscreen, "Failure left fullscreen stuck");
                    failed.Capture(Path.Combine(Path.GetDirectoryName(output), "test-error.png"));
                    failed.Window.Close();
                    results.Add("PASS failed power request reports error and allows retry");
                    results.Add("All actions used FakePower. No Windows power-changing command was executed.");
                }
                catch (Exception ex) { code = 1; results.Add("FAIL " + ex); }
                finally { File.WriteAllLines(output, results); app.Shutdown(code); }
            }));
            app.Run();
            return code;
        }
    }
}
