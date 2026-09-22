using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using CuteClash;

public static class LocalizationTests
{
    public static int Run(string scratch)
    {
        Directory.CreateDirectory(scratch);
        string previous = Localization.Language;
        int passed = 0;
        try
        {
            Localization.Language = "en";
            Check(Localization.T("中文", "English") == "English", "English text selection");
            Check(Localization.Format("{0} 个", "{0} items", 3) == "3 items", "Translated formatting");
            Localization.Language = "EN";
            Check(Localization.Language == "en", "Case-insensitive language selection");
            Localization.Language = "unsupported";
            Check(Localization.Language == "zh-CN" && Localization.T("中文", "English") == "中文", "Unsupported locale fallback");
            Localization.Language = null;
            Check(Localization.Language == "zh-CN", "Missing locale fallback");
            passed++;

            var store = new ProfileStore(Path.Combine(scratch, "preferences"));
            var settings = new AppSettings();
            Check(settings.Language == "zh-CN", "New users default to Simplified Chinese");
            settings.Language = "en"; store.SaveSettings(settings);
            Check(store.LoadSettings().Language == "en", "English preference survives reload");
            settings.Language = "zh-CN"; store.SaveSettings(settings);
            Check(store.LoadSettings().Language == "zh-CN", "Chinese preference survives reload");
            settings.Language = "unknown";
            bool invalidRejected = false;
            try { store.SaveSettings(settings); } catch (InvalidDataException) { invalidRejected = true; }
            Check(invalidRejected && store.LoadSettings().Language == "zh-CN", "Unknown locale cannot replace the saved preference");
            File.WriteAllText(Path.Combine(scratch, "preferences", "settings.json"), "{\"MixedPort\":7890,\"ControllerPort\":19090,\"Mode\":\"rule\",\"Profiles\":[]}");
            Check(store.LoadSettings().Language == "zh-CN", "Version 0.1 settings without Language still load");
            passed++;
            return passed;
        }
        finally { Localization.Language = previous; }
    }

    // Call separately from the runtime suite: all UI work runs on an STA thread,
    // with a fresh app-data folder and no core, driver or system-proxy activation.
    public static int CaptureUi(string destination)
    {
        Exception failure = null;
        int passed = 0;
        Thread thread = new Thread(delegate()
        {
            try
            {
                Application.EnableVisualStyles();
                foreach (string language in new[] { "zh-CN", "en" })
                {
                    string output = Path.Combine(destination, language);
                    string data = Path.Combine(output, "app-data");
                    Directory.CreateDirectory(output);
                    var settings = new AppSettings { Language = language };
                    var store = new ProfileStore(data);
                    var profile = store.ImportText("用户配置 · My profile", null, "proxies: []\nproxy-groups: []\nrules: ['MATCH,DIRECT']\n");
                    settings.Profiles.Add(profile); settings.SelectedProfileId = profile.Id; store.SaveSettings(settings);
                    string saved = File.ReadAllText(Path.Combine(data, "settings.json"));
                    using (var controller = new AppController(data, null))
                    using (var form = new MainForm(controller))
                    {
                        form.Show(); Application.DoEvents();
                        Check(form.Text == "Cute Clash", "Application brand");
                        Check(!controller.IsRunning && !controller.Settings.SystemProxyEnabled && !controller.Settings.TunEnabled, "UI launch leaves networking inactive");
                        Check(File.ReadAllText(Path.Combine(data, "settings.json")) == saved, "Building and syncing UI does not save settings");
                        foreach (string page in new[] { "Overview", "Profiles", "Proxies", "Settings", "Logs" })
                        {
                            form.ShowPageForCapture(page); Application.DoEvents();
                            using (var bitmap = new Bitmap(form.Width, form.Height))
                            {
                                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                                bitmap.Save(Path.Combine(output, page + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                            }
                            if (language == "en") CheckEnglish(form);
                            passed++;
                        }
                        Type subscriptionType = typeof(MainForm).GetNestedType("SubscriptionDialog", BindingFlags.NonPublic);
                        using (var subscription = (Form)Activator.CreateInstance(subscriptionType, new object[] { form.Font }))
                        {
                            subscription.Show(form); Application.DoEvents();
                            if (language == "en") CheckEnglish(subscription);
                            using (var bitmap = new Bitmap(subscription.Width, subscription.Height))
                            {
                                subscription.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                                bitmap.Save(Path.Combine(output, "Subscription-dialog.png"), System.Drawing.Imaging.ImageFormat.Png);
                            }
                            subscription.Close();
                        }
                        passed++;
                        var languageChoice = (ComboBox)typeof(MainForm).GetField("languageChoice", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
                        Check(languageChoice.SelectedIndex == (language == "en" ? 1 : 0), "Saved language selected in Settings");
                        languageChoice.SelectedIndex = language == "en" ? 0 : 1;
                        typeof(MainForm).GetMethod("SyncState", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(form, null);
                        Check(File.ReadAllText(Path.Combine(data, "settings.json")) == saved, "Changing selection or syncing does not apply language before Apply");
                        languageChoice.SelectedIndex = language == "en" ? 1 : 0;
                        form.Size = form.MinimumSize; // Respect the DPI-scaled native outer-window minimum.
                        form.ShowPageForCapture("Settings"); Application.DoEvents();
                        using (var bitmap = new Bitmap(form.Width, form.Height))
                        {
                            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                            bitmap.Save(Path.Combine(output, "Settings-minimum.png"), System.Drawing.Imaging.ImageFormat.Png);
                        }
                        form.ExitAsync().GetAwaiter().GetResult();
                        passed++;
                    }
                    using (var controller = new AppController(data, null))
                    {
                        Check(controller.Settings.Language == language, "Saved language retained after closing and reopening");
                        Check(Localization.Language == language, "Reload restores application language");
                    }
                    passed++;
                }
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(45000)) throw new TimeoutException("Localization UI verification exceeded 45 seconds.");
        if (failure != null) throw new Exception("Localization UI verification failed.", failure);
        return passed;
    }

    private static void CheckEnglish(Control control)
    {
        if (control.Visible)
        {
            CheckEnglishText(control.Text);
            var list = control as ListView;
            if (list != null)
            {
                foreach (ColumnHeader column in list.Columns) CheckEnglishText(column.Text);
                foreach (ListViewItem item in list.Items)
                    foreach (ListViewItem.ListViewSubItem cell in item.SubItems) CheckEnglishText(cell.Text);
            }
            var combo = control as ComboBox;
            if (combo != null) foreach (object item in combo.Items) CheckEnglishText(Convert.ToString(item));
            var strip = control as ToolStrip;
            if (strip != null) foreach (ToolStripItem item in strip.Items) CheckEnglishText(item.Text);
        }
        foreach (Control child in control.Controls) CheckEnglish(child);
    }

    private static void CheckEnglishText(string text)
    {
        if (String.IsNullOrEmpty(text) || text == "语言 / Language" || text == "简体中文" || text.Contains("用户配置 · My profile")) return;
        Check(!Regex.IsMatch(text, "[\\u3400-\\u9fff]"), "Untranslated application text: " + text);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
