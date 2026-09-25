using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CuteClash.Tests
{
    internal static class StartupRegressionTests
    {
        public static int Run(string project, string scratch)
        {
            Directory.CreateDirectory(scratch);
            string corePath = Path.Combine(project, "dependencies", "mihomo", IntPtr.Size == 8 ? "x64" : "x86", "mihomo.exe");
            TestUdpCollision(project, Path.Combine(scratch, "udp-collision"), corePath).GetAwaiter().GetResult();
            TestLocalAuthentication(project, Path.Combine(scratch, "local-authentication"), corePath).GetAwaiter().GetResult();
            return 2;
        }

        private static async Task TestUdpCollision(string project, string scratch, string corePath)
        {
            using (var controller = new AppController(scratch, corePath))
            using (var occupied = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                occupied.ExclusiveAddressUse = true;
                occupied.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                int port = ((IPEndPoint)occupied.LocalEndPoint).Port;
                Configure(controller, port);
                Check(CanBindTcp(port), "UDP fixture leaves the same TCP port free to reproduce the original false-ready condition");
                await controller.ImportFileAsync(Path.Combine(project, "examples", "direct-test.yaml")).ConfigureAwait(false);
                var clock = Stopwatch.StartNew();
                bool rejected = false;
                try { await controller.StartAsync().ConfigureAwait(false); }
                catch (InvalidOperationException) { rejected = true; }
                Check(rejected && !controller.IsRunning, "An occupied UDP mixed port prevents a successful connection even if the controller API could start");
                Check(clock.ElapsedMilliseconds < 15000, "UDP collision fails promptly instead of waiting for the startup readiness deadline");
                Check(!String.IsNullOrEmpty(controller.LastError) && controller.LastError.Contains(port.ToString()), "UDP failure names the conflicting local port");
                CheckNoCore(controller);
                Check(CanBindTcp(port) && CanBindTcp(controller.Settings.ControllerPort), "Failed startup leaves no mixed TCP or controller API listener");
                occupied.Close();

                await controller.StartAsync().ConfigureAwait(false);
                Check(controller.IsRunning, "The same controller can connect after the UDP conflict is removed");
                CheckHttpForwarding(port);
                CheckSocksForwarding(port);
                await controller.StopAsync().ConfigureAwait(false);
                CheckNoCore(controller);
                Check(CanBindTcp(port) && CanBindTcp(controller.Settings.ControllerPort) && CanBindUdp(port), "Stopping the retry releases TCP, UDP and controller listeners");
            }
        }

        private static async Task TestLocalAuthentication(string project, string scratch, string corePath)
        {
            Directory.CreateDirectory(scratch);
            string fixture = Path.Combine(scratch, "provider-with-local-auth.yaml");
            File.WriteAllText(fixture, File.ReadAllText(Path.Combine(project, "examples", "direct-test.yaml")) +
                "\nauthentication: ['fixture-user:fixture-password']\nskip-auth-prefixes: []\n", new UTF8Encoding(false));
            using (var controller = new AppController(scratch, corePath))
            {
                Configure(controller, FreeMixedPort());
                await controller.ImportFileAsync(fixture).ConfigureAwait(false);
                string sourcePath = new ProfileStore(scratch).GetProfilePath(controller.Settings.SelectedProfileId);
                Check(File.ReadAllText(sourcePath).Contains("fixture-user:fixture-password"), "Import preserves the original subscription authentication field on disk");
                await controller.StartAsync().ConfigureAwait(false);
                Check(controller.IsRunning, "Subscription local-inbound authentication does not prevent the application's own loopback listener from becoming ready");
                // No Proxy-Authorization header and no SOCKS username/password method are sent.
                CheckHttpForwarding(controller.Settings.MixedPort);
                CheckSocksForwarding(controller.Settings.MixedPort);
                Check(File.ReadAllText(sourcePath).Contains("fixture-user:fixture-password"), "Runtime normalization does not rewrite the subscription source");
                await controller.StopAsync().ConfigureAwait(false);
                CheckNoCore(controller);
            }
        }

        private static void Configure(AppController controller, int mixedPort)
        {
            controller.Settings.TunEnabled = false;
            controller.Settings.SystemProxyEnabled = false;
            controller.Settings.MixedPort = mixedPort;
            do { controller.Settings.ControllerPort = FreeTcpPort(); } while (controller.Settings.ControllerPort == mixedPort);
        }

        private static void CheckNoCore(AppController controller)
        {
            Check(!controller.IsRunning && typeof(AppController).GetField("core", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller) == null &&
                typeof(AppController).GetField("job", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller) == null,
                "No core process or process-job remains attached after failed or stopped startup");
        }

        private static void CheckHttpForwarding(int mixedPort)
        {
            using (var target = new LocalHttpTarget())
            using (TcpClient client = ConnectLoopback(mixedPort))
            using (NetworkStream stream = client.GetStream())
            {
                SetTimeouts(stream);
                string request = "GET http://127.0.0.1:" + target.Port + "/ HTTP/1.1\r\nHost: 127.0.0.1:" + target.Port + "\r\nConnection: close\r\n\r\n";
                Write(stream, Encoding.ASCII.GetBytes(request));
                string response = new StreamReader(stream).ReadToEnd();
                Check(response.StartsWith("HTTP/1.1 200", StringComparison.Ordinal) && response.Contains(LocalHttpTarget.Body), "Real core forwards unauthenticated HTTP to the local fixture");
                target.Wait();
            }
        }

        private static void CheckSocksForwarding(int mixedPort)
        {
            using (var target = new LocalHttpTarget())
            using (TcpClient client = ConnectLoopback(mixedPort))
            using (NetworkStream stream = client.GetStream())
            {
                SetTimeouts(stream);
                Write(stream, new byte[] { 5, 1, 0 });
                byte[] greeting = ReadExact(stream, 2);
                Check(greeting[0] == 5 && greeting[1] == 0, "Real core accepts SOCKS5 without subscription-specified credentials");
                Write(stream, new byte[] { 5, 1, 0, 1, 127, 0, 0, 1, (byte)(target.Port >> 8), (byte)(target.Port & 255) });
                byte[] reply = ReadExact(stream, 4);
                Check(reply[0] == 5 && reply[1] == 0, "Real core SOCKS5 CONNECT reaches the local fixture");
                if (reply[3] == 1) ReadExact(stream, 6);
                else if (reply[3] == 4) ReadExact(stream, 18);
                else if (reply[3] == 3) ReadExact(stream, ReadExact(stream, 1)[0] + 2);
                else throw new Exception("Unexpected SOCKS5 address type");
                Write(stream, Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: close\r\n\r\n"));
                string response = new StreamReader(stream).ReadToEnd();
                Check(response.StartsWith("HTTP/1.1 200", StringComparison.Ordinal) && response.Contains(LocalHttpTarget.Body), "Real core forwards the HTTP payload through SOCKS5");
                target.Wait();
            }
        }

        private static TcpClient ConnectLoopback(int port)
        {
            var client = new TcpClient(AddressFamily.InterNetwork);
            try
            {
                Task connecting = client.ConnectAsync(IPAddress.Loopback, port);
                if (!connecting.Wait(3000)) throw new TimeoutException("Local test connection timed out");
                connecting.GetAwaiter().GetResult();
                return client;
            }
            catch { client.Close(); throw; }
        }
        private static void SetTimeouts(NetworkStream stream) { stream.ReadTimeout = 5000; stream.WriteTimeout = 5000; }
        private static void Write(NetworkStream stream, byte[] bytes) { stream.Write(bytes, 0, bytes.Length); }
        private static byte[] ReadExact(NetworkStream stream, int count)
        {
            byte[] bytes = new byte[count]; int offset = 0;
            while (offset < count) { int read = stream.Read(bytes, offset, count - offset); if (read == 0) throw new EndOfStreamException(); offset += read; }
            return bytes;
        }
        private static int FreeTcpPort()
        { var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port; }
        private static int FreeMixedPort()
        {
            for (int attempt = 0; attempt < 20; attempt++) { int port = FreeTcpPort(); if (CanBindUdp(port)) return port; }
            throw new IOException("Could not reserve a local mixed-port test candidate");
        }
        private static bool CanBindTcp(int port)
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            try { listener.Server.ExclusiveAddressUse = true; listener.Start(); return true; }
            catch (SocketException) { return false; }
            finally { listener.Stop(); }
        }
        private static bool CanBindUdp(int port)
        {
            using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                try { socket.ExclusiveAddressUse = true; socket.Bind(new IPEndPoint(IPAddress.Loopback, port)); return true; }
                catch (SocketException) { return false; }
            }
        }

        private sealed class LocalHttpTarget : IDisposable
        {
            public const string Body = "cute-clash-local-startup-regression";
            private readonly TcpListener listener;
            private readonly Thread worker;
            private TcpClient accepted;
            private Exception failure;
            public int Port { get; private set; }

            public LocalHttpTarget()
            {
                listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
                Port = ((IPEndPoint)listener.LocalEndpoint).Port;
                worker = new Thread(delegate()
                {
                    try
                    {
                        using (TcpClient client = listener.AcceptTcpClient())
                        {
                            accepted = client;
                            using (NetworkStream stream = client.GetStream())
                            {
                                SetTimeouts(stream);
                                var request = new StringBuilder();
                                while (request.Length < 16384)
                                {
                                    int next = stream.ReadByte(); if (next < 0) throw new EndOfStreamException();
                                    request.Append((char)next); if (request.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal)) break;
                                }
                                Check(request.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal), "Local fixture receives a bounded HTTP request");
                                Write(stream, Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: " + Body.Length + "\r\nConnection: close\r\n\r\n" + Body));
                            }
                        }
                    }
                    catch (Exception ex) { failure = ex; }
                });
                worker.IsBackground = true; worker.Start();
            }
            public void Wait()
            { Check(worker.Join(5500), "Local fixture finishes within its I/O deadline"); if (failure != null) throw new Exception("Local fixture failed", failure); }
            public void Dispose()
            { listener.Stop(); if (accepted != null) accepted.Close(); worker.Join(5500); }
        }

        private static void Check(bool condition, string name)
        { if (!condition) throw new Exception("FAIL " + name); }
    }
}
