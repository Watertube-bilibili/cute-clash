using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace CuteClash.Tests
{
    internal static class ConnectionProbeTests
    {
        public static int Run(string scratch)
        {
            TestHandshakes().GetAwaiter().GetResult();
            TestDeadlines().GetAwaiter().GetResult();
            TestAdapterSnapshots();
            TestRouteSnapshots();
            TestNativeRouteQuery();
            return 8;
        }

        private static async Task TestHandshakes()
        {
            using (var server = new ProbeServer(delegate(NetworkStream stream)
            {
                byte[] greeting = ReadExactly(stream, 3);
                Check(greeting[0] == 5 && greeting[1] == 1 && greeting[2] == 0, "Only the unauthenticated SOCKS5 method is offered");
                stream.WriteByte(5); Thread.Sleep(25); stream.WriteByte(0);
                Check(stream.ReadByte() == -1, "Probe closes after greeting without sending a SOCKS CONNECT or external request");
            }))
            {
                ConnectionProbeResult result = await ConnectionProbe.ProbeMixedPortAsync(server.Port, 2000, CancellationToken.None);
                Check(result.Ready && result.Code == "ready", "Fragmented SOCKS5 response is assembled");
                server.Wait();
            }
            foreach (byte[] reply in new[] { new byte[] { 72, 84, 84, 80 }, new byte[] { 4, 0 }, new byte[] { 5, 255 }, new byte[] { 5, 2 }, new byte[] { 5 } })
            {
                using (var server = new ProbeServer(delegate(NetworkStream stream)
                {
                    ReadExactly(stream, 3); stream.Write(reply, 0, reply.Length);
                }))
                {
                    ConnectionProbeResult result = await ConnectionProbe.ProbeMixedPortAsync(server.Port, 1000, CancellationToken.None);
                    Check(!result.Ready && !String.IsNullOrEmpty(result.Reason), "HTTP, SOCKS4, authentication rejection and truncated greetings are not ready");
                    server.Wait();
                }
            }
            int closedPort;
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); closedPort = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            var failed = await ConnectionProbe.ProbeMixedPortAsync(closedPort, 500, CancellationToken.None);
            Check(!failed.Ready && (failed.Code == "socket-connect" || failed.Code == "timeout-connect"), "A closed listener is not ready");
        }

        private static async Task TestDeadlines()
        {
            foreach (bool partial in new[] { false, true })
            {
                using (var server = new ProbeServer(delegate(NetworkStream stream)
                {
                    ReadExactly(stream, 3);
                    if (partial) stream.WriteByte(5);
                    Check(stream.ReadByte() == -1, "Timeout closes stalled socket");
                }))
                {
                    var clock = Stopwatch.StartNew();
                    var result = await ConnectionProbe.ProbeMixedPortAsync(server.Port, 150, CancellationToken.None);
                    Check(!result.Ready && result.Code == "timeout-response" && clock.ElapsedMilliseconds < 1500, "Silent and partial responses share a bounded total deadline");
                    server.Wait();
                }
            }
            using (var greeted = new ManualResetEvent(false))
            using (var cancellation = new CancellationTokenSource())
            using (var server = new ProbeServer(delegate(NetworkStream stream)
            {
                ReadExactly(stream, 3); greeted.Set();
                Check(stream.ReadByte() == -1, "Cancellation closes pending socket I/O");
            }))
            {
                Task<ConnectionProbeResult> pending = ConnectionProbe.ProbeMixedPortAsync(server.Port, 5000, cancellation.Token);
                Check(greeted.WaitOne(1500), "The cancellation test reaches the pending read");
                var clock = Stopwatch.StartNew(); cancellation.Cancel();
                bool cancelled = false;
                try { await pending; } catch (OperationCanceledException) { cancelled = true; }
                Check(cancelled && clock.ElapsedMilliseconds < 1000, "Cancellation interrupts pending read promptly");
                server.Wait();
            }
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel(); bool cancelled = false;
                try { await ConnectionProbe.ProbeMixedPortAsync(1, 1000, cancellation.Token); } catch (OperationCanceledException) { cancelled = true; }
                Check(cancelled, "Pre-cancelled probe never opens a socket");
            }
        }

        private static void TestAdapterSnapshots()
        {
            CheckTun("adapter-missing", new TunAdapterSnapshot("cute-clash 2", OperationalStatus.Up, IPAddress.Parse("198.18.0.1")));
            CheckTun("adapter-down", new TunAdapterSnapshot("cute-clash", OperationalStatus.Down, IPAddress.Parse("198.18.0.1")));
            CheckTun("adapter-address", new TunAdapterSnapshot("cute-clash", OperationalStatus.Up));
            foreach (string address in new[] { "::1", "2001:db8::1", "0.0.0.0", "127.0.0.1", "169.254.1.2", "224.0.0.1", "255.255.255.255" })
                CheckTun("adapter-address", new TunAdapterSnapshot("cute-clash", OperationalStatus.Up, IPAddress.Parse(address)));
            foreach (string address in new[] { "198.18.0.1", "198.19.0.1", "10.20.0.1", "172.19.0.1" })
            {
                var result = ConnectionProbe.EvaluateTunAdapters(new[] { new TunAdapterSnapshot("Cute-Clash", OperationalStatus.Up, IPAddress.Parse(address)) }, "cute-clash");
                Check(result.Ready && result.Address.Equals(IPAddress.Parse(address)), "Exact Windows adapter name and an assigned unicast IPv4 address are required, without a fixed subnet");
            }
            var snapshots = new[] {
                new TunAdapterSnapshot("unrelated", OperationalStatus.Up, IPAddress.Parse("198.18.0.1")),
                new TunAdapterSnapshot("cute-clash", OperationalStatus.Down, IPAddress.Parse("198.18.0.1")),
                new TunAdapterSnapshot("cute-clash", OperationalStatus.Up, IPAddress.Parse("198.19.0.1")) };
            Check(ConnectionProbe.EvaluateTunAdapters(snapshots, "cute-clash").Ready, "A stale down snapshot does not hide a matching ready adapter");
            Check(!ConnectionProbe.EvaluateTunAdapters(new TunAdapterSnapshot[0], "cute-clash").Ready, "Missing adapters are not ready");
        }

        private static void CheckTun(string expectedCode, params TunAdapterSnapshot[] adapters)
        {
            ConnectionProbeResult result = ConnectionProbe.EvaluateTunAdapters(adapters, "cute-clash");
            Check(!result.Ready && result.Code == expectedCode && !String.IsNullOrEmpty(result.Reason), "TUN failure reason: " + expectedCode);
        }

        private static void TestRouteSnapshots()
        {
            var adapters = new[] { new TunAdapterSnapshot("cute-clash", OperationalStatus.Up, 42, IPAddress.Parse("198.18.0.1")) };
            int reads = 0; bool lowerHalf = false, upperHalf = false;
            var result = ConnectionProbe.EvaluateTunReadiness(adapters, "cute-clash", delegate(IPAddress destination)
            {
                reads++;
                if (destination.GetAddressBytes()[0] < 128) lowerHalf = true; else upperHalf = true;
                return new IPv4RouteSnapshot(0, 42);
            });
            Check(result.Ready && reads == 3 && lowerHalf && upperHalf && result.InterfaceIndex == 42, "Best route checks cover both IPv4 halves and compare the current adapter index");
            result = ConnectionProbe.EvaluateTunReadiness(adapters, "cute-clash", delegate(IPAddress destination) { return new IPv4RouteSnapshot(0, 7); });
            Check(!result.Ready && result.Code == "route-conflict", "An up adapter is not ready while another VPN has the preferred route");
            result = ConnectionProbe.EvaluateTunReadiness(adapters, "cute-clash", delegate(IPAddress destination)
            { return new IPv4RouteSnapshot(0, destination.GetAddressBytes()[0] < 128 ? 7u : 42u); });
            Check(!result.Ready && result.Code == "route-conflict", "A competing lower-half route cannot be hidden by successful upper-half lookups");
            result = ConnectionProbe.EvaluateTunReadiness(adapters, "cute-clash", delegate(IPAddress destination) { return new IPv4RouteSnapshot(1231, 0); });
            Check(!result.Ready && result.Code == "route-query", "No route / native API failure never counts as readiness");
            result = ConnectionProbe.EvaluateTunReadiness(adapters, "cute-clash", delegate(IPAddress destination) { return null; });
            Check(!result.Ready && result.Code == "route-query", "Missing route data fails closed");
            result = ConnectionProbe.EvaluateTunReadiness(new[] { new TunAdapterSnapshot("cute-clash", OperationalStatus.Up, IPAddress.Parse("198.18.0.1")) }, "cute-clash",
                delegate(IPAddress destination) { throw new Exception("A missing interface index must not query routes"); });
            Check(!result.Ready && result.Code == "adapter-index", "A missing interface index is rejected before native lookup");
            result = ConnectionProbe.EvaluateTunReadiness(new[] { new TunAdapterSnapshot("cute-clash", OperationalStatus.Down, 42, IPAddress.Parse("198.18.0.1")) }, "cute-clash",
                delegate(IPAddress destination) { throw new Exception("A down adapter must not query routes"); });
            Check(!result.Ready && result.Code == "adapter-down", "Adapter state remains a prerequisite for route checks");
        }

        private static void TestNativeRouteQuery()
        {
            Check(Marshal.SizeOf(typeof(NativeIPv4Route)) == 56 && Marshal.OffsetOf(typeof(NativeIPv4Route), "InterfaceIndex").ToInt32() == 16 &&
                Marshal.OffsetOf(typeof(NativeIPv4Route), "Metric5").ToInt32() == 52, "MIB_IPFORWARDROW matches the Win32 ABI on both x86 and x64");
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) return;
            IPv4RouteSnapshot route = ConnectionProbe.ReadBestIPv4Route(IPAddress.Loopback);
            Check(route.ErrorCode == 0 && route.InterfaceIndex != 0, "Read-only native GetBestRoute can resolve the loopback route without a network request");
            bool matches = false;
            foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback || !adapter.Supports(NetworkInterfaceComponent.IPv4)) continue;
                IPv4InterfaceProperties ipv4 = adapter.GetIPProperties().GetIPv4Properties();
                if (ipv4 != null && (uint)ipv4.Index == route.InterfaceIndex) matches = true;
            }
            Check(matches, "The native route interface index agrees with managed adapter enumeration (including byte order)");
        }

        private static byte[] ReadExactly(NetworkStream stream, int count)
        {
            byte[] result = new byte[count]; int offset = 0;
            while (offset < count) { int read = stream.Read(result, offset, count - offset); if (read == 0) throw new IOException("Unexpected EOF"); offset += read; }
            return result;
        }

        private sealed class ProbeServer : IDisposable
        {
            private readonly TcpListener listener;
            private readonly Thread worker;
            private Exception failure;
            private TcpClient accepted;
            public int Port { get; private set; }

            public ProbeServer(Action<NetworkStream> serve)
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
                            { stream.ReadTimeout = 3000; stream.WriteTimeout = 3000; serve(stream); }
                        }
                    }
                    catch (Exception ex) { failure = ex; }
                });
                worker.IsBackground = true; worker.Start();
            }
            public void Wait()
            { Check(worker.Join(3500), "Fake proxy server stops within its I/O deadline"); if (failure != null) throw new Exception("Fake proxy failed", failure); }
            public void Dispose()
            { listener.Stop(); if (accepted != null) accepted.Close(); worker.Join(3500); }
        }

        private static void Check(bool condition, string message)
        { if (!condition) throw new Exception("FAIL " + message); }
    }
}
