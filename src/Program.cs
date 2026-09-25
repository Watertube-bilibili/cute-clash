using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("Cute Clash")]
[assembly: AssemblyDescription("Clash / Mihomo client for Windows 7 SP1, Windows 10 and Windows 11")]
[assembly: AssemblyCompany("Cute Clash contributors")]
[assembly: AssemblyProduct("Cute Clash")]
[assembly: AssemblyCopyright("Copyright © 2026 Cute Clash contributors")]
[assembly: AssemblyVersion("0.3.1.0")]
[assembly: AssemblyFileVersion("0.3.1.0")]
#if NET6_0
[assembly: System.Runtime.Versioning.TargetFramework(".NETCoreApp,Version=v6.0")]
#else
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]
#endif
namespace CuteClash
{
    internal static class Program
    {
        // Keep the original mutex so old and renamed clients cannot change the proxy concurrently.
        internal static string MutexName { get { return "Local\\win7-clash-" + WindowsIdentity.GetCurrent().User.Value; } }
        internal static void LoadSavedLanguage(string directory)
        {
            try
            {
                string path = Path.Combine(directory, "settings.json");
                if (!File.Exists(path) || new FileInfo(path).Length > 1024 * 1024) return;
                var parser = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = 1024 * 1024, RecursionLimit = 32 };
                var settings = parser.Deserialize<System.Collections.Generic.Dictionary<string, object>>(File.ReadAllText(path));
                object language;
                if (settings != null && settings.TryGetValue("Language", out language)) Localization.Language = language as string;
            }
            catch { /* The controller reports invalid settings; this only chooses the startup dialog language. */ }
        }
        [STAThread]
        private static void Main(string[] args)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            if (args.Length == 4 && args[0] == "--watchdog")
            {
                int parent; long ticks;
                if (Int32.TryParse(args[1], out parent) && Int64.TryParse(args[2], out ticks)) AppController.RunWatchdog(parent, ticks, args[3]);
                return;
            }
            if (args.Length == 2 && args[0] == "--wait-parent")
            {
                int parent;
                if (Int32.TryParse(args[1], out parent)) { try { using (var process = Process.GetProcessById(parent)) process.WaitForExit(15000); } catch (ArgumentException) { } }
            }
            LoadSavedLanguage(AppController.DefaultDataDirectory());
            string protocolLink = args.Length == 1 && args[0].StartsWith("clash:", StringComparison.OrdinalIgnoreCase) ? args[0] : null;
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs e)
            { MessageBox.Show(AppController.Redact(e.Exception.Message), "Cute Clash", MessageBoxButtons.OK, MessageBoxIcon.Error); };
            using (var mutex = new Mutex(false, MutexName))
            {
                bool owned = false;
                try
                {
                    try { owned = mutex.WaitOne(0); } catch (AbandonedMutexException) { owned = true; }
                    if (!owned)
                    {
                        if (protocolLink != null && ProtocolInbox.TrySend(protocolLink, 5000)) return;
                        MessageBox.Show(protocolLink == null ? Localization.T("Cute Clash 已在运行，请从系统托盘打开。", "Cute Clash is already running. Open it from the system tray.") : Localization.T("未能将订阅链接交给已运行的 Cute Clash。请从托盘退出旧版本后重试，或在配置页手动添加订阅。", "Could not deliver the link to the running Cute Clash. Exit the older version from its tray menu and try again, or add the subscription in Profiles."), "Cute Clash");
                        return;
                    }
                    bool captureEnglish = args.Length == 2 && args[0] == "--capture-en";
                    bool capture = captureEnglish || (args.Length == 2 && args[0] == "--capture");
                    string data = capture ? Path.Combine(Path.GetFullPath(args[1]), "capture-data") : AppController.DefaultDataDirectory();
                    using (var controller = new AppController(data, null))
                    {
                        if (capture) { controller.Settings.Language = captureEnglish ? "en" : "zh-CN"; Localization.Language = controller.Settings.Language; }
                        using (var form = new MainForm(controller))
                        {
                            if (capture)
                            {
                                Directory.CreateDirectory(args[1]); form.Show(); Application.DoEvents();
                                foreach (string page in new[] { "Overview", "Profiles", "Proxies", "Settings", "Logs" })
                                {
                                    form.ShowPageForCapture(page); Application.DoEvents();
                                    using (var bitmap = new Bitmap(form.Width, form.Height))
                                    { form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height)); bitmap.Save(Path.Combine(args[1], page + ".png"), System.Drawing.Imaging.ImageFormat.Png); }
                                }
                                form.ExitAsync().GetAwaiter().GetResult();
                            }
                            else
                            {
                                // Only the existing user's process receives private subscription links.
                                using (var inbox = new ProtocolInbox(delegate(string link)
                                {
                                    try { form.BeginInvoke(new Action(delegate { form.ImportProtocolLink(link); })); }
                                    catch (InvalidOperationException) { }
                                }))
                                {
                                    form.Shown += delegate
                                    {
                                        inbox.Start();
                                        if (protocolLink != null) form.BeginInvoke(new Action(delegate { form.ImportProtocolLink(protocolLink); }));
                                    };
                                    Application.Run(form);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex) { MessageBox.Show(AppController.Redact(ex.Message), Localization.T("Cute Clash 启动失败", "Cute Clash could not start"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
                finally { if (owned) mutex.ReleaseMutex(); }
            }
        }
    }
}
