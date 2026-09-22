using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using CuteClash;

internal static class ProtocolUiTests
{
    public static int Run(string destination)
    {
        Exception failure = null;
        int passed = 0;
        var worker = new Thread(delegate()
        {
            try
            {
                foreach (string language in new[] { "zh-CN", "en" })
                {
                    string folder = Path.Combine(destination, language);
                    Directory.CreateDirectory(folder);
                    string data = Path.Combine(folder, "data");
                    var store = new ProfileStore(data);
                    store.SaveSettings(new AppSettings { Language = language });
                    string original = File.ReadAllText(Path.Combine(data, "settings.json"));
                    var listener = new TcpListener(IPAddress.Loopback, 0);
                    listener.Start();
                    try
                    {
                        string url = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/subscription?token=PRIVATE-EXAMPLE";
                        using (var controller = new AppController(data, null))
                        using (var form = new MainForm(controller))
                        using (var closeDialog = new System.Windows.Forms.Timer { Interval = 150 })
                        {
                            form.Show(); Application.DoEvents();
                            bool observed = false;
                            closeDialog.Tick += delegate
                            {
                                foreach (Form dialog in Application.OpenForms)
                                {
                                    if (dialog.GetType().Name != "SubscriptionDialog") continue;
                                    closeDialog.Stop();
                                    try
                                    {
                                        string actualUrl = (string)dialog.GetType().GetProperty("SubscriptionUrl").GetValue(dialog, null);
                                        string actualName = (string)dialog.GetType().GetProperty("ProfileName").GetValue(dialog, null);
                                        if (actualUrl != url || actualName != "My provider") throw new Exception("Protocol dialog lost prefilled subscription values.");
                                        if (dialog.Text.Contains("PRIVATE-EXAMPLE")) throw new Exception("Subscription token leaked into the dialog title.");
                                        if (listener.Pending() || controller.IsRunning || controller.Settings.Profiles.Count != 0) throw new Exception("Import performed work before confirmation.");
                                        using (var bitmap = new Bitmap(dialog.Width, dialog.Height))
                                        {
                                            dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                                            bitmap.Save(Path.Combine(folder, "protocol-confirmation.png"), System.Drawing.Imaging.ImageFormat.Png);
                                        }
                                        observed = true;
                                    }
                                    catch (Exception ex) { failure = ex; }
                                    dialog.DialogResult = DialogResult.Cancel;
                                    dialog.Close();
                                    break;
                                }
                            };
                            closeDialog.Start();
                            form.ImportProtocolLink("clash://install-config?url=" + Uri.EscapeDataString(url) + "&name=My%20provider");
                            Application.DoEvents();
                            if (failure != null) throw failure;
                            if (!observed || listener.Pending() || controller.IsRunning || controller.Settings.Profiles.Count != 0) throw new Exception("Cancelling import must leave networking and profiles unchanged.");
                            if (File.ReadAllText(Path.Combine(data, "settings.json")) != original) throw new Exception("Cancelled import changed saved settings.");
                            form.ExitAsync().GetAwaiter().GetResult();
                            passed++;
                        }
                    }
                    finally { listener.Stop(); }
                }
            }
            catch (Exception ex) { failure = ex; }
        });
        worker.SetApartmentState(ApartmentState.STA);
        worker.IsBackground = true;
        worker.Start();
        if (!worker.Join(20000)) throw new TimeoutException("Protocol confirmation UI verification timed out.");
        if (failure != null) throw new Exception("Protocol confirmation UI verification failed.", failure);
        return passed;
    }
}
