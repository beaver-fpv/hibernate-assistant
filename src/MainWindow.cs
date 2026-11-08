using System;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace HibernateDesk
{
    // Each countdown can dispatch at most once. Closing or cancelling clears it.
    public sealed class HibernateCountdown
    {
        private DateTime? deadline;
        public bool Active { get { return deadline.HasValue; } }
        public void Start(DateTime now) { deadline = now.AddSeconds(5); }
        public void Cancel() { deadline = null; }
        public int Remaining(DateTime now)
        {
            return deadline.HasValue ? Math.Max(0, (int)Math.Ceiling((deadline.Value - now).TotalSeconds)) : 0;
        }
        public bool TryComplete(DateTime now)
        {
            if (!deadline.HasValue || now < deadline.Value) return false;
            deadline = null;
            return true;
        }
    }

    public sealed class MainWindow
    {
        public readonly Window Window;
        public readonly SystemTheme Theme;
        public readonly FrameworkElement CountdownView;
        public bool IsFullscreen { get { return fullscreen.IsActive; } }
        private readonly FullscreenWindowState fullscreen;
        private readonly FrameworkElement normalContent;
        private readonly TextBlock countdownNumber;
        private readonly IPowerService power;
        private readonly HibernateCountdown countdown = new HibernateCountdown();
        private readonly DispatcherTimer timer = new DispatcherTimer();
        private readonly Button hibernateButton, cancelButton, enableButton, refreshButton;
        private readonly TextBlock statusTitle, statusDescription, hiberSize, freeSize, message;
        private readonly TextBox systemDetail;
        private readonly Ellipse statusDot;
        private PowerStatus status;
        private bool busy;
        private bool closing;

        public MainWindow(IPowerService powerService, Func<bool> darkModeReader = null, bool previewMode = false)
        {
            power = powerService;
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MainWindow.xaml"))
                Window = (Window)XamlReader.Load(stream);
            Theme = new SystemTheme(Window, darkModeReader);
            Window.Closed += delegate { Theme.Dispose(); };
            fullscreen = new FullscreenWindowState(Window, previewMode);
            normalContent = (FrameworkElement)Window.Content;
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("CountdownView.xaml"))
                CountdownView = (FrameworkElement)XamlReader.Load(stream);
            countdownNumber = (TextBlock)CountdownView.FindName("CountdownNumber");
            ((Button)CountdownView.FindName("FullscreenCancelButton")).Click += delegate { CancelHibernate(); };
            hibernateButton = Find<Button>("HibernateButton");
            cancelButton = Find<Button>("CancelButton");
            enableButton = Find<Button>("EnableButton");
            refreshButton = Find<Button>("RefreshButton");
            statusTitle = Find<TextBlock>("StatusTitle");
            statusDescription = Find<TextBlock>("StatusDescription");
            hiberSize = Find<TextBlock>("HiberSize");
            freeSize = Find<TextBlock>("FreeSize");
            message = Find<TextBlock>("ActionMessage");
            systemDetail = Find<TextBox>("SystemDetail");
            statusDot = Find<Ellipse>("StatusDot");
            hibernateButton.Click += async delegate { await BeginHibernate(); };
            cancelButton.Click += delegate { CancelHibernate(); };
            refreshButton.Click += async delegate { await Refresh(); };
            enableButton.Click += async delegate { await Enable(); };
            Window.PreviewKeyDown += delegate(object sender, KeyEventArgs e) {
                if (e.Key == Key.Escape && countdown.Active) { CancelHibernate(); e.Handled = true; }
            };
            Window.Closing += delegate { closing = true; countdown.Cancel(); timer.Stop(); };
            timer.Interval = TimeSpan.FromMilliseconds(100);
            timer.Tick += async delegate { await Tick(); };
            Window.Loaded += async delegate { await Refresh(); };
        }

        private T Find<T>(string name) where T : class { return Window.FindName(name) as T; }
        private static string Size(long bytes) { return bytes < 0 ? "暂时无法读取" : (bytes / 1073741824.0).ToString("0.0") + " GB"; }

        private void DisplayStatus(PowerStatus current)
        {
            status = current;
            statusTitle.Text = current.CanHibernate ? "已准备好休眠" : "休眠暂不可用";
            statusDescription.Text = current.CanHibernate ? "Windows 已开启休眠，可以保存当前工作状态。" : "可尝试开启休眠功能，或展开下方系统说明。";
            statusDot.SetResourceReference(Shape.FillProperty, current.CanHibernate ? "StatusSuccess" : "StatusWarning");
            hiberSize.Text = current.HiberFileBytes < 0 ? "暂时无法读取" : (current.HiberFileExists ? Size(current.HiberFileBytes) : "未检测到文件");
            freeSize.Text = Size(current.FreeBytes);
            systemDetail.Text = current.Detail;
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            hibernateButton.IsEnabled = !busy && !countdown.Active && status != null && status.CanHibernate;
            refreshButton.IsEnabled = !busy && !countdown.Active;
            enableButton.IsEnabled = !busy && !countdown.Active && (status == null || !status.CanHibernate);
            enableButton.Content = status != null && status.CanHibernate ? "休眠功能已开启" : "开启休眠功能";
            cancelButton.Visibility = countdown.Active ? Visibility.Visible : Visibility.Collapsed;
            hibernateButton.Content = countdown.Active ? "即将休眠…" : "开始休眠";
        }

        public async Task Refresh()
        {
            if (busy || closing || countdown.Active) return;
            busy = true;
            UpdateButtons();
            try
            {
                PowerStatus result = await Task.Run(() => power.ReadStatus());
                if (!closing) DisplayStatus(result);
            }
            catch (Exception ex)
            {
                if (!closing)
                {
                    status = null;
                    statusTitle.Text = "状态读取失败";
                    statusDescription.Text = "暂时无法确认休眠是否可用，请刷新重试。";
                    statusDot.SetResourceReference(Shape.FillProperty, "StatusWarning");
                    hiberSize.Text = "暂时无法读取";
                    freeSize.Text = "暂时无法读取";
                    message.Text = "请点击刷新状态重试。";
                    systemDetail.Text = ex.Message;
                }
            }
            finally { busy = false; if (!closing) UpdateButtons(); }
        }

        private async Task Enable()
        {
            if (busy || closing) return;
            busy = true;
            UpdateButtons();
            message.Text = "请在 Windows 授权窗口中允许开启休眠。";
            try
            {
                await Task.Run(() => power.EnableHibernate());
                PowerStatus result = await Task.Run(() => power.ReadStatus());
                if (!closing)
                {
                    DisplayStatus(result);
                    message.Text = result.CanHibernate ? "休眠已开启。点击「开始休眠」即可使用。" : "Windows 仍未允许休眠，请查看系统说明。";
                }
            }
            catch (Exception ex) { if (!closing) message.Text = ex.Message; }
            finally { busy = false; if (!closing) UpdateButtons(); }
        }

        public async Task BeginHibernate()
        {
            if (busy || closing || countdown.Active) return;
            busy = true;
            UpdateButtons();
            message.Text = "正在确认休眠状态…";
            try
            {
                PowerStatus result = await Task.Run(() => power.ReadStatus());
                if (closing) return;
                DisplayStatus(result);
                if (!result.CanHibernate) { message.Text = "请先开启休眠功能，再开始休眠。"; return; }
                countdown.Start(DateTime.UtcNow);
                ShowFullscreenCountdown();
                message.Text = "5 秒后进入休眠。按 Esc 或点击取消可以停止。";
                timer.Start();
            }
            catch (Exception ex)
            {
                countdown.Cancel();
                timer.Stop();
                if (!closing) { HideFullscreenCountdown(); message.Text = "无法准备休眠。" + ex.Message; }
            }
            finally { busy = false; if (!closing) UpdateButtons(); }
        }

        public void CancelHibernate()
        {
            if (!countdown.Active) return;
            countdown.Cancel();
            timer.Stop();
            HideFullscreenCountdown();
            message.Text = "已取消，电脑继续运行。";
            UpdateButtons();
        }

        private async Task Tick()
        {
            if (closing || !countdown.Active) return;
            DateTime now = DateTime.UtcNow;
            if (!countdown.TryComplete(now))
            {
                countdownNumber.Text = countdown.Remaining(now).ToString();
                message.Text = countdown.Remaining(now) + " 秒后进入休眠。按 Esc 或点击取消可以停止。";
                return;
            }
            timer.Stop();
            HideFullscreenCountdown();
            busy = true;
            UpdateButtons();
            message.Text = "正在请求 Windows 保存状态并进入休眠…";
            try
            {
                await Task.Run(() => power.Hibernate());
                if (!closing) message.Text = "已向 Windows 发出休眠请求。恢复使用后，可再次点击休眠。";
            }
            catch (Exception ex) { if (!closing) message.Text = "休眠请求未完成。" + ex.Message; }
            finally { busy = false; if (!closing) UpdateButtons(); }
        }

        private void ShowFullscreenCountdown()
        {
            countdownNumber.Text = "5";
            Window.Content = CountdownView;
            fullscreen.Enter();
            Theme.Refresh();
            ((Button)CountdownView.FindName("FullscreenCancelButton")).Focus();
        }

        private void HideFullscreenCountdown()
        {
            if (Window.Content == CountdownView) Window.Content = normalContent;
            fullscreen.Exit();
            Theme.Refresh();
        }

        public void Capture(string path)
        {
            Window.UpdateLayout();
            FrameworkElement content = (FrameworkElement)Window.Content;
            RenderTargetBitmap bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(content);
            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (FileStream file = File.Create(path)) encoder.Save(file);
        }
    }
}
