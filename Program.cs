#nullable enable
using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace RazerDpiTool
{
    // 程序入口：打开主窗口
    internal static class Program
    {
        private static int _reporting;   // 防重入：错误框里再抛错会无限套娃

        [STAThread]
        private static void Main()
        {
            // 兜底必须在创建任何窗口之前装好，否则 UI 线程的异常轮不到我们处理。
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => ReportFatal(e.Exception);
            AppDomain.CurrentDomain.UnhandledException +=
                (s, e) => ReportFatal(e.ExceptionObject as Exception);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        /// <summary>
        /// 把异常落到 %LOCALAPPDATA%\RazerDpiTool\crash.log，再弹一个能看懂的框。
        /// 不写程序目录：装在 Program Files 下时那里通常没有写权限。
        /// </summary>
        private static void ReportFatal(Exception? ex)
        {
            if (ex == null) return;

            string? logPath = null;
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "RazerDpiTool");
                Directory.CreateDirectory(dir);
                logPath = Path.Combine(dir, "crash.log");
                File.AppendAllText(logPath,
                    "==== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ====" +
                    Environment.NewLine + ex + Environment.NewLine + Environment.NewLine);
            }
            catch
            {
                logPath = null;   // 连日志都写不了，就只弹框
            }

            if (Interlocked.Exchange(ref _reporting, 1) == 1) return;   // 已经在报了，别套娃
            try
            {
                MessageBox.Show(
                    "程序遇到未处理的错误。" + Environment.NewLine + Environment.NewLine +
                    (logPath != null
                        ? "详细信息已记录到：" + Environment.NewLine + logPath
                        : "日志文件写入失败。") + Environment.NewLine + Environment.NewLine +
                    ex.GetType().Name + "：" + ex.Message,
                    "RazerDpiTool", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { }
            finally { Interlocked.Exchange(ref _reporting, 0); }
        }
    }
}
