using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace HibernateDesk
{
    // Preview has a read-only service: even an accidental click cannot change power state.
    public sealed class ReadOnlyPowerService : IPowerService
    {
        private readonly WindowsPowerService source = new WindowsPowerService();
        public PowerStatus ReadStatus() { return source.ReadStatus(); }
        public void EnableHibernate() { throw new InvalidOperationException("界面预览模式，操作已禁用。"); }
        public void Hibernate() { throw new InvalidOperationException("界面预览模式，操作已禁用。"); }
    }

    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                if (args.Length == 2 && args[0] == "/self-test") return SelfTest.Run(args[1]);
                bool preview = args.Length == 2 && args[0] == "/preview";
                if (args.Length != 0 && !preview) return 2;
                Application app = new Application();
                MainWindow ui = new MainWindow(preview ? (IPowerService)new ReadOnlyPowerService() : new WindowsPowerService(), null, preview);
                if (preview)
                {
                    ui.Window.WindowStartupLocation = WindowStartupLocation.Manual;
                    ui.Window.Left = -10000;
                    ui.Window.Top = -10000;
                    ui.Window.ShowActivated = false;
                    ui.Window.ShowInTaskbar = false;
                    ui.Window.ContentRendered += async delegate {
                        await Task.Delay(1800);
                        ui.Capture(Path.GetFullPath(args[1]));
                        ui.Window.Close();
                    };
                }
                app.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e) {
                    MessageBox.Show("操作未完成，请重新打开休眠助手。\n" + e.Exception.Message, "休眠助手", MessageBoxButton.OK, MessageBoxImage.Information);
                    e.Handled = true;
                };
                return app.Run(ui.Window);
            }
            catch (Exception ex)
            {
                if (args.Length > 0) File.WriteAllText(Path.Combine(Path.GetTempPath(), "HibernateDesk-error.txt"), ex.ToString());
                else MessageBox.Show(ex.Message, "休眠助手", MessageBoxButton.OK, MessageBoxImage.Information);
                return 1;
            }
        }
    }
}
