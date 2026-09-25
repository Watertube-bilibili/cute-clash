using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace CuteClash
{
    internal sealed class ConnectionProbeResult
    {
        public bool Ready { get; private set; }
        public string Code { get; private set; }
        public string Reason { get; private set; }
        public IPAddress Address { get; private set; }
        public int InterfaceIndex { get; private set; }

        internal ConnectionProbeResult(bool ready, string code, string reason, IPAddress address, int interfaceIndex = 0)
        { Ready = ready; Code = code; Reason = reason; Address = address; InterfaceIndex = interfaceIndex; }
    }

    internal sealed class TunAdapterSnapshot
    {
        public string Name { get; private set; }
        public OperationalStatus Status { get; private set; }
        public IPAddress[] UnicastAddresses { get; private set; }
        public int IPv4InterfaceIndex { get; private set; }

        public TunAdapterSnapshot(string name, OperationalStatus status, params IPAddress[] unicastAddresses)
            : this(name, status, 0, unicastAddresses) { }

        public TunAdapterSnapshot(string name, OperationalStatus status, int ipv4InterfaceIndex, params IPAddress[] unicastAddresses)
        {
            Name = name; Status = status; IPv4InterfaceIndex = ipv4InterfaceIndex;
            UnicastAddresses = unicastAddresses == null ? new IPAddress[0] : (IPAddress[])unicastAddresses.Clone();
        }
    }

    internal sealed class IPv4RouteSnapshot
    {
        public uint ErrorCode { get; private set; }
        public uint InterfaceIndex { get; private set; }

        public IPv4RouteSnapshot(uint errorCode, uint interfaceIndex)
        { ErrorCode = errorCode; InterfaceIndex = interfaceIndex; }
    }

    // MIB_IPFORWARDROW contains fourteen 32-bit DWORDs on both x86 and x64.
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct NativeIPv4Route
    {
        public uint Destination, Mask, Policy, NextHop, InterfaceIndex, Type, Protocol, Age, NextHopAs;
        public uint Metric1, Metric2, Metric3, Metric4, Metric5;
    }

    /// <summary>Local handshake, adapter and route readiness; these do not prove remote connectivity or packet forwarding.</summary>
    internal static class ConnectionProbe
    {
        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        private static extern uint GetBestRoute(uint destination, uint source, out NativeIPv4Route route);

        public static async Task<ConnectionProbeResult> ProbeMixedPortAsync(int port, int timeoutMilliseconds, CancellationToken cancellationToken)
        {
            if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException("port");
            if (timeoutMilliseconds < 1) throw new ArgumentOutOfRangeException("timeoutMilliseconds");
            cancellationToken.ThrowIfCancellationRequested();
            var elapsed = Stopwatch.StartNew();
            string stage = "connect";
            // Use a literal loopback address: DNS and external requests must never be part of this check.
            using (var client = new TcpClient(AddressFamily.InterNetwork))
            {
                try
                {
                    await BoundedAsync(client.ConnectAsync(IPAddress.Loopback, port), elapsed, timeoutMilliseconds, cancellationToken).ConfigureAwait(false);
                    stage = "greeting";
                    using (NetworkStream stream = client.GetStream())
                    {
                        // RFC 1928 method negotiation only; never issue a SOCKS CONNECT command.
                        byte[] greeting = { 5, 1, 0 };
                        await BoundedAsync(stream.WriteAsync(greeting, 0, greeting.Length), elapsed, timeoutMilliseconds, cancellationToken).ConfigureAwait(false);
                        stage = "response";
                        byte[] reply = new byte[2];
                        int offset = 0;
                        while (offset < reply.Length)
                        {
                            Task<int> reading = stream.ReadAsync(reply, offset, reply.Length - offset);
                            await BoundedAsync(reading, elapsed, timeoutMilliseconds, cancellationToken).ConfigureAwait(false);
                            int count = await reading.ConfigureAwait(false);
                            if (count == 0) return Failure("closed", "本地代理在 SOCKS5 握手完成前关闭了连接。", "The local proxy closed before completing the SOCKS5 handshake.");
                            offset += count;
                            if (reply[0] != 5) return Failure("protocol", "本地端口没有返回有效的 SOCKS5 握手，可能被其他程序占用。", "The local port did not return a SOCKS5 handshake; another application may own it.");
                        }
                        cancellationToken.ThrowIfCancellationRequested();
                        if (reply[1] != 0) return Failure("authentication", "本地 SOCKS5 代理没有接受免认证连接，请检查核心的本地认证配置。", "The local SOCKS5 proxy did not accept an unauthenticated connection. Check the core's local authentication settings.");
                        return new ConnectionProbeResult(true, "ready", Localization.T("本地 SOCKS5 握手成功。", "Local SOCKS5 handshake succeeded."), IPAddress.Loopback);
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (TimeoutException)
                {
                    return Failure("timeout-" + stage, "本地代理握手超时，监听端口尚未就绪。", "The local proxy handshake timed out; its listener is not ready.");
                }
                catch (SocketException)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return Failure("socket-" + stage, "无法连接本地代理端口，或核心提前关闭了连接。", "The local proxy port could not be reached, or the core closed the connection.");
                }
                catch (IOException)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return Failure("io-" + stage, "本地代理握手过程中连接中断。", "The local proxy connection was interrupted during the handshake.");
                }
            }
        }

        private static async Task BoundedAsync(Task operation, Stopwatch elapsed, int timeoutMilliseconds, CancellationToken cancellationToken)
        {
            // Observe any fault from an I/O operation abandoned after timeout/cancellation.
            ObserveFailure(operation);
            cancellationToken.ThrowIfCancellationRequested();
            long remaining = (long)timeoutMilliseconds - elapsed.ElapsedMilliseconds;
            if (remaining <= 0) throw new TimeoutException();
            using (var delayCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                Task timeout = Task.Delay((int)remaining, delayCancellation.Token);
                Task completed = await Task.WhenAny(operation, timeout).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (completed != operation || elapsed.ElapsedMilliseconds >= timeoutMilliseconds) throw new TimeoutException();
                delayCancellation.Cancel();
                await operation.ConfigureAwait(false);
            }
            // On failure the caller disposes the socket, aborting the pending native operation.
        }

        private static void ObserveFailure(Task operation)
        {
            operation.ContinueWith(delegate(Task failed) { var ignored = failed.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        public static ConnectionProbeResult ProbeTunAdapter()
        { return ProbeTunAdapter("cute-clash"); }

        public static ConnectionProbeResult ProbeTunAdapter(string expectedName)
        {
            ValidateAdapterName(expectedName);
            try
            {
                var snapshots = new List<TunAdapterSnapshot>();
                foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (!String.Equals(adapter.Name, expectedName, StringComparison.OrdinalIgnoreCase)) continue;
                    var addresses = new List<IPAddress>();
                    IPInterfaceProperties properties = adapter.GetIPProperties();
                    foreach (UnicastIPAddressInformation address in properties.UnicastAddresses)
                        addresses.Add(address.Address);
                    IPv4InterfaceProperties ipv4 = adapter.Supports(NetworkInterfaceComponent.IPv4) ? properties.GetIPv4Properties() : null;
                    snapshots.Add(new TunAdapterSnapshot(adapter.Name, adapter.OperationalStatus, ipv4 == null ? 0 : ipv4.Index, addresses.ToArray()));
                }
                return EvaluateTunReadiness(snapshots, expectedName, ReadBestIPv4Route);
            }
            catch (NetworkInformationException)
            { return Failure("adapter-query", "无法读取 Windows 网卡状态，尚未确认 TUN 就绪。", "Windows adapter status could not be read; TUN readiness is unconfirmed."); }
            catch (System.Security.SecurityException)
            { return Failure("adapter-permission", "没有权限读取 Windows 网卡状态，尚未确认 TUN 就绪。", "Access to Windows adapter status was denied; TUN readiness is unconfirmed."); }
            catch (EntryPointNotFoundException)
            { return Failure("route-api", "Windows 路由查询接口不可用，尚未确认 TUN 路由就绪。", "The Windows route query API is unavailable; TUN route readiness is unconfirmed."); }
            catch (DllNotFoundException)
            { return Failure("route-api", "Windows 路由查询组件不可用，尚未确认 TUN 路由就绪。", "The Windows route query component is unavailable; TUN route readiness is unconfirmed."); }
        }

        // This pure check accepts a snapshot rather than consulting or changing the host network.
        public static ConnectionProbeResult EvaluateTunAdapters(IEnumerable<TunAdapterSnapshot> adapters, string expectedName)
        {
            if (adapters == null) throw new ArgumentNullException("adapters");
            ValidateAdapterName(expectedName);
            bool found = false, up = false;
            foreach (TunAdapterSnapshot adapter in adapters)
            {
                if (adapter == null || !String.Equals(adapter.Name, expectedName, StringComparison.OrdinalIgnoreCase)) continue;
                found = true;
                if (adapter.Status != OperationalStatus.Up) continue;
                up = true;
                foreach (IPAddress address in adapter.UnicastAddresses)
                    if (IsUsableIPv4(address))
                        return new ConnectionProbeResult(true, "ready", Localization.T("TUN 网卡已启动并获得 IPv4 地址。", "The TUN adapter is up and has an IPv4 address."), address, adapter.IPv4InterfaceIndex);
            }
            if (!found) return Failure("adapter-missing", "尚未找到 Cute Clash 的 TUN 网卡，请检查 Wintun 初始化日志。", "The Cute Clash TUN adapter was not found. Check the Wintun initialization log.");
            if (!up) return Failure("adapter-down", "Cute Clash 的 TUN 网卡尚未启动。", "The Cute Clash TUN adapter is not up yet.");
            return Failure("adapter-address", "Cute Clash 的 TUN 网卡尚未获得可用 IPv4 地址。", "The Cute Clash TUN adapter does not have a usable IPv4 address yet.");
        }

        public static ConnectionProbeResult EvaluateTunReadiness(IEnumerable<TunAdapterSnapshot> adapters, string expectedName,
            Func<IPAddress, IPv4RouteSnapshot> readBestRoute)
        {
            if (readBestRoute == null) throw new ArgumentNullException("readBestRoute");
            ConnectionProbeResult adapter = EvaluateTunAdapters(adapters, expectedName);
            if (!adapter.Ready) return adapter;
            if (adapter.InterfaceIndex <= 0)
                return Failure("adapter-index", "无法读取 TUN 网卡的 IPv4 接口编号，尚未确认路由就绪。", "The TUN IPv4 interface index could not be read; route readiness is unconfirmed.");

            // Mihomo v1.19.31's sing-tun Windows auto-route installs 0.0.0.0/0 and sets
            // the TUN interface metric to zero. Cute Clash does not inherit route
            // exclusions from subscriptions. Check both halves of IPv4 so a competing
            // VPN's split default routes cannot leave an apparently ready adapter.
            // GetBestRoute only consults the local routing table; it sends no packets.
            foreach (string destination in new[] { "203.0.113.1", "198.51.100.1", "64.0.0.1" })
            {
                IPv4RouteSnapshot route = readBestRoute(IPAddress.Parse(destination));
                if (route == null || route.ErrorCode != 0)
                    return Failure("route-query", "无法查询 IPv4 最佳路由，TUN 路由尚未就绪。", "The best IPv4 route could not be queried; TUN routing is not ready.");
                if (route.InterfaceIndex != (uint)adapter.InterfaceIndex)
                    return new ConnectionProbeResult(false, "route-conflict", Localization.Format(
                        "TUN 网卡已启动，但 {0} 的路由仍指向其他网卡；可能有其他 VPN 或手动路由优先接管。",
                        "The TUN adapter is up, but the route for {0} still selects another adapter. Another VPN or a manual route may take priority.", destination), adapter.Address, adapter.InterfaceIndex);
            }
            return new ConnectionProbeResult(true, "ready", Localization.T("TUN 网卡及 IPv4 路由已就绪。", "The TUN adapter and IPv4 routes are ready."), adapter.Address, adapter.InterfaceIndex);
        }

        internal static IPv4RouteSnapshot ReadBestIPv4Route(IPAddress destination)
        {
            if (destination == null || destination.AddressFamily != AddressFamily.InterNetwork)
                throw new ArgumentException("An IPv4 route destination is required.", "destination");
            NativeIPv4Route route;
            // The DWORD is an in_addr: its bytes, not its printed integer value, are
            // in network order. BitConverter preserves the address bytes on Windows.
            uint error = GetBestRoute(BitConverter.ToUInt32(destination.GetAddressBytes(), 0), 0, out route);
            return new IPv4RouteSnapshot(error, route.InterfaceIndex);
        }

        private static bool IsUsableIPv4(IPAddress address)
        {
            if (address == null || address.AddressFamily != AddressFamily.InterNetwork) return false;
            byte[] bytes = address.GetAddressBytes();
            // Do not pin 198.18.0.0/16: a valid profile may use a different fake-IP range.
            return bytes[0] != 0 && bytes[0] != 127 && bytes[0] < 224 && !(bytes[0] == 169 && bytes[1] == 254);
        }

        private static void ValidateAdapterName(string expectedName)
        { if (String.IsNullOrWhiteSpace(expectedName)) throw new ArgumentException("A TUN adapter name is required.", "expectedName"); }

        private static ConnectionProbeResult Failure(string code, string chinese, string english)
        { return new ConnectionProbeResult(false, code, Localization.T(chinese, english), null); }
    }
}
