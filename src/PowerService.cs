using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace HibernateDesk
{
    public class PowerStatus
    {
        public bool CanHibernate;
        public bool HiberFileExists;
        public long HiberFileBytes = -1;
        public long FreeBytes = -1;
        public string Detail = "";
    }

    public interface IPowerService
    {
        PowerStatus ReadStatus();
        void EnableHibernate();
        void Hibernate();
    }

    public sealed class WindowsPowerService : IPowerService
    {
        private const byte HiberFileReduced = 1;
        private const byte HiberFileFull = 2;

        // SYSTEM_POWER_CAPABILITIES is 76 bytes on Windows 10/11, x86 and x64.
        // Only the fields used here are declared. BOOLEAN is one native byte.
        [StructLayout(LayoutKind.Explicit, Size = 76)]
        private struct SystemPowerCapabilities
        {
            [FieldOffset(6)] public byte SystemS4;
            [FieldOffset(8)] public byte HiberFilePresent;
            [FieldOffset(22)] public byte HiberFileType;
        }

        [DllImport("powrprof.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.U1)]
        private static extern bool GetPwrCapabilities(out SystemPowerCapabilities capabilities);

        public PowerStatus ReadStatus()
        {
            PowerStatus status = new PowerStatus();
            try
            {
                SystemPowerCapabilities capabilities = ReadCapabilities();
                status.HiberFileExists = capabilities.HiberFilePresent != 0;
                status.CanHibernate = capabilities.SystemS4 != 0
                    && status.HiberFileExists
                    && capabilities.HiberFileType == HiberFileFull;

                if (status.CanHibernate)
                    status.Detail = "休眠已就绪，可以保存当前工作状态。";
                else if (!status.HiberFileExists)
                    status.Detail = "系统休眠尚未启用。点击开启后，Windows 会创建休眠文件。";
                else if (capabilities.HiberFileType == HiberFileReduced)
                    status.Detail = "当前文件仅用于快速启动。开启完整休眠后即可保存工作状态。";
                else
                    status.Detail = "当前系统条件不支持休眠，可能受到硬件、驱动或系统策略限制。";
            }
            catch (Exception exception)
            {
                status.Detail = "暂时无法读取休眠状态。" + exception.Message;
            }

            string systemRoot = Path.GetPathRoot(Environment.SystemDirectory);
            try
            {
                FileInfo hiberFile = new FileInfo(Path.Combine(systemRoot, "hiberfil.sys"));
                // Length throws on an unavailable file instead of presenting unknown as zero.
                status.HiberFileBytes = hiberFile.Length;
                status.HiberFileExists = true;
            }
            catch (FileNotFoundException)
            {
                status.HiberFileBytes = 0;
                status.HiberFileExists = false;
                status.CanHibernate = false;
            }
            catch (DirectoryNotFoundException)
            {
                status.HiberFileBytes = -1;
            }
            catch (UnauthorizedAccessException)
            {
                status.HiberFileBytes = -1;
            }
            catch (IOException)
            {
                status.HiberFileBytes = -1;
            }

            try
            {
                status.FreeBytes = new DriveInfo(systemRoot).AvailableFreeSpace;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return status;
        }

        public void EnableHibernate()
        {
            if (ReadStatus().CanHibernate)
                return;

            RunElevatedPowerConfiguration("/hibernate on");
            SystemPowerCapabilities capabilities = ReadCapabilities();
            if (capabilities.HiberFileType == HiberFileReduced)
                RunElevatedPowerConfiguration("/hibernate /type full");

            PowerStatus status = ReadStatus();
            if (!status.CanHibernate)
                throw new InvalidOperationException("Windows 已处理开启请求，但休眠仍不可用。" + status.Detail);
        }

        public void Hibernate()
        {
            PowerStatus status = ReadStatus();
            if (!status.CanHibernate)
                throw new InvalidOperationException(status.Detail);

            ProcessStartInfo start = new ProcessStartInfo
            {
                FileName = GetSystemTool("shutdown.exe"),
                Arguments = "/h",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.Default,
                StandardErrorEncoding = Encoding.Default
            };
            using (Process process = Process.Start(start))
            {
                if (process == null)
                    throw new InvalidOperationException("无法启动 Windows 休眠功能。");
                Task<string> output = process.StandardOutput.ReadToEndAsync();
                Task<string> error = process.StandardError.ReadToEndAsync();
                process.WaitForExit();
                Task.WaitAll(output, error);
                if (process.ExitCode != 0)
                {
                    string detail = (error.Result + " " + output.Result).Trim();
                    if (detail.Length == 0)
                        detail = new Win32Exception(process.ExitCode).Message;
                    throw new InvalidOperationException("Windows 未能进入休眠。" + detail);
                }
            }
        }

        private static SystemPowerCapabilities ReadCapabilities()
        {
            SystemPowerCapabilities capabilities;
            if (!GetPwrCapabilities(out capabilities))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return capabilities;
        }

        private static string GetSystemTool(string fileName)
        {
            string path = Path.Combine(Environment.SystemDirectory, fileName);
            if (!File.Exists(path))
                throw new FileNotFoundException("找不到所需的 Windows 系统组件。", path);
            return path;
        }

        private static void RunElevatedPowerConfiguration(string arguments)
        {
            if (arguments != "/hibernate on" && arguments != "/hibernate /type full")
                throw new ArgumentException("不支持的电源操作。", "arguments");

            ProcessStartInfo start = new ProcessStartInfo
            {
                FileName = GetSystemTool("powercfg.exe"),
                Arguments = arguments,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };
            try
            {
                using (Process process = Process.Start(start))
                {
                    if (process == null)
                        throw new InvalidOperationException("无法启动 Windows 电源设置。");
                    process.WaitForExit();
                    if (process.ExitCode != 0)
                        throw new InvalidOperationException("开启休眠失败，Windows 返回错误码 "
                            + process.ExitCode + "。请检查磁盘空间与系统电源限制。");
                }
            }
            catch (Win32Exception exception)
            {
                if (exception.NativeErrorCode == 1223)
                    throw new OperationCanceledException("已取消管理员授权。", exception);
                throw;
            }
        }
    }
}
