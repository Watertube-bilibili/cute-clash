// SPDX-License-Identifier: GPL-3.0-or-later
// Independent C# implementation. Explicit direct/local-core routing and sequential
// fallback follow the approach reviewed in these GPL-3.0 open-source clients:
// Clash Party: https://github.com/mihomo-party-org/clash-party/blob/364578f21007cdea0a5b4304acebfa7cf5655b1f/src/main/config/profile.ts
// Clash Verge Rev: https://github.com/clash-verge-rev/clash-verge-rev/blob/6a752994cf5f33c85a21ce3a590ff250b80e9846/src-tauri/src/feat/profile.rs
// No upstream source text is copied; certificate bypass and automatic system proxy
// discovery are deliberately not exposed by this downloader.
using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CuteClash
{
    internal enum SubscriptionDownloadFailure { Address, Network, Timeout, Certificate, HttpStatus, TooLarge, Content, Redirect }

    internal sealed class SubscriptionDownloadException : InvalidOperationException
    {
        public SubscriptionDownloadFailure Failure { get; private set; }
        public int StatusCode { get; private set; }
        public bool UsedCoreProxy { get; private set; }
        internal SubscriptionDownloadException(SubscriptionDownloadFailure failure, string message, bool usedCoreProxy, int statusCode)
            : base(message) { Failure = failure; UsedCoreProxy = usedCoreProxy; StatusCode = statusCode; }
    }

    internal sealed class SubscriptionDownloadOptions
    {
        public int ConnectTimeoutMs = 5000;
        public int HeadersTimeoutMs = 6000;
        public int IdleTimeoutMs = 8000;
        public int AttemptTimeoutMs = 20000;
        public int OverallTimeoutMs = 35000;
        public int MaxCompressedBytes = 8 * 1024 * 1024;
        public int MaxDecodedBytes = 8 * 1024 * 1024;
        public int MaxRedirects = 5;
        // This callback receives only fixed route descriptions, never a URL or server text.
        public Action<string> Progress;
    }

    internal static class SubscriptionDownloader
    {
        internal static Task<string> DownloadAsync(string url, int activeMixedPort, CancellationToken cancellationToken)
        { return DownloadAsync(url, activeMixedPort, cancellationToken, new SubscriptionDownloadOptions()); }

        internal static async Task<string> DownloadAsync(string url, int activeMixedPort, CancellationToken cancellationToken, SubscriptionDownloadOptions options)
        {
            Uri uri = ValidateAddress(url);
            if (options == null || options.ConnectTimeoutMs <= 0 || options.HeadersTimeoutMs <= 0 || options.IdleTimeoutMs <= 0 ||
                options.AttemptTimeoutMs <= 0 || options.OverallTimeoutMs <= 0 || options.MaxCompressedBytes <= 0 || options.MaxDecodedBytes <= 0 || options.MaxRedirects < 0)
                throw new ArgumentException("Invalid subscription download limits.");
            if (activeMixedPort < 0 || activeMixedPort > 65535) throw new ArgumentOutOfRangeException("activeMixedPort");
            cancellationToken.ThrowIfCancellationRequested();
            using (var overall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                bool usedCoreProxy = false;
                overall.CancelAfter(options.OverallTimeoutMs);
                try
                {
                    if (options.Progress != null) options.Progress(Localization.T("正在直连下载订阅…", "Downloading the subscription directly…"));
                    try { return await DownloadRouteAsync(uri, 0, overall.Token, options).ConfigureAwait(false); }
                    catch (SubscriptionDownloadException ex)
                    {
                        // A HTTP response, TLS rejection or invalid payload is not a routing failure.
                        // Do not issue duplicate authenticated requests or work around certificate errors.
                        if (activeMixedPort == 0 || (ex.Failure != SubscriptionDownloadFailure.Network && ex.Failure != SubscriptionDownloadFailure.Timeout)) throw;
                    }
                    overall.Token.ThrowIfCancellationRequested();
                    usedCoreProxy = true;
                    if (options.Progress != null) options.Progress(Localization.T("直连失败，正在通过当前 Cute Clash 连接下载订阅…", "Direct download failed; trying the active Cute Clash connection…"));
                    return await DownloadRouteAsync(uri, activeMixedPort, overall.Token, options).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    throw Error(SubscriptionDownloadFailure.Timeout, usedCoreProxy,
                        "订阅下载已达到总时限，请检查网络后重试。", "The subscription download reached its overall time limit. Check your connection and retry.");
                }
            }
        }

        private static Uri ValidateAddress(string url)
        {
            Uri uri;
            if (String.IsNullOrWhiteSpace(url) || url.Length > 8192 || !Uri.TryCreate(url, UriKind.Absolute, out uri) ||
                (uri.Scheme != "https" && uri.Scheme != "http") || !String.IsNullOrEmpty(uri.UserInfo) || !String.IsNullOrEmpty(uri.Fragment))
                throw Error(SubscriptionDownloadFailure.Address, false, "订阅地址必须是有效的 HTTPS 或 HTTP URL，不能包含用户名、密码或片段。", "The subscription must be a valid HTTPS or HTTP URL without embedded credentials or a fragment.");
            foreach (char c in url) if (Char.IsControl(c)) throw Error(SubscriptionDownloadFailure.Address, false,
                "订阅地址包含无效控制字符。", "The subscription address contains invalid control characters.");
            return uri;
        }

        private static HttpMessageHandler CreateHandler(int proxyPort, SubscriptionDownloadOptions options)
        {
            IWebProxy proxy = proxyPort == 0 ? null : new CoreProxy(proxyPort);
#if NET6_0
            return new SocketsHttpHandler
            {
                UseProxy = proxyPort != 0, Proxy = proxy, UseCookies = false, AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.None, ConnectTimeout = TimeSpan.FromMilliseconds(options.ConnectTimeoutMs),
                MaxResponseHeadersLength = 32, SslOptions = new System.Net.Security.SslClientAuthenticationOptions { EnabledSslProtocols = SslProtocols.Tls12 }
            };
#else
            // Framework negotiates TLS through Windows Schannel. Program also sets this at startup;
            // set it here for callers that use the downloader outside the GUI.
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            return new HttpClientHandler
            {
                UseProxy = proxyPort != 0, Proxy = proxy, UseCookies = false, AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.None, MaxResponseHeadersLength = 32
            };
#endif
        }

        private static async Task<string> DownloadRouteAsync(Uri uri, int proxyPort, CancellationToken overall, SubscriptionDownloadOptions options)
        {
            bool viaProxy = proxyPort != 0;
            using (var attempt = CancellationTokenSource.CreateLinkedTokenSource(overall))
            using (var client = new HttpClient(CreateHandler(proxyPort, options)))
            {
                attempt.CancelAfter(options.AttemptTimeoutMs);
                client.Timeout = Timeout.InfiniteTimeSpan;
                Version version = typeof(SubscriptionDownloader).Assembly.GetName().Version;
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Cute-Clash/" + version.ToString(3) + " (clash.meta; mihomo)");
                client.DefaultRequestHeaders.Accept.ParseAdd("application/yaml, text/yaml, text/plain, */*;q=0.5");
                client.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
                try
                {
                    for (int redirect = 0; ; redirect++)
                    {
                        attempt.Token.ThrowIfCancellationRequested();
                        using (var request = new HttpRequestMessage(HttpMethod.Get, uri))
                        using (var headers = CancellationTokenSource.CreateLinkedTokenSource(attempt.Token))
                        {
                            // The Framework transport lacks a separate ConnectTimeout. Its
                            // combined DNS/connect/TLS/header phase has the tighter deadline.
#if NET6_0
                            headers.CancelAfter(options.HeadersTimeoutMs);
#else
                            headers.CancelAfter(Math.Min(options.ConnectTimeoutMs, options.HeadersTimeoutMs));
#endif
                            using (var response = await BoundedAsync(client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headers.Token), headers.Token).ConfigureAwait(false))
                            {
                                headers.CancelAfter(Timeout.Infinite);
                                int status = (int)response.StatusCode;
                                if (status == 301 || status == 302 || status == 303 || status == 307 || status == 308)
                                {
                                    if (redirect >= options.MaxRedirects || response.Headers.Location == null)
                                        throw Error(SubscriptionDownloadFailure.Redirect, viaProxy, "订阅重定向过多或缺少目标地址。", "The subscription has too many redirects or no redirect destination.");
                                    Uri next = ValidateAddress(new Uri(uri, response.Headers.Location).AbsoluteUri);
                                    if (uri.Scheme == "https" && next.Scheme != "https")
                                        throw Error(SubscriptionDownloadFailure.Redirect, viaProxy, "订阅服务器试图从 HTTPS 降级到 HTTP，已停止下载。", "The subscription server tried to redirect HTTPS to HTTP; the download was stopped.");
                                    uri = next;
                                    continue;
                                }
                                if (status != 200)
                                    throw new SubscriptionDownloadException(SubscriptionDownloadFailure.HttpStatus,
                                        Localization.Format("订阅服务器返回 HTTP {0}。请检查订阅是否有效、过期或被限流。", "The subscription server returned HTTP {0}. Check whether the subscription is valid, expired or rate limited.", status), viaProxy, status);
                                if (response.Content.Headers.ContentLength > options.MaxCompressedBytes)
                                    throw TooLarge(viaProxy);
                                return await ReadBodyAsync(response, attempt.Token, viaProxy, options).ConfigureAwait(false);
                            }
                        }
                    }
                }
                catch (SubscriptionDownloadException) { throw; }
                catch (OperationCanceledException)
                {
                    overall.ThrowIfCancellationRequested();
                    throw Error(SubscriptionDownloadFailure.Timeout, viaProxy,
                        viaProxy ? "通过当前连接下载订阅超时，请检查所选节点或稍后重试。" : "直连订阅超时，请检查网络或先连接可用节点。",
                        viaProxy ? "The subscription timed out through the active connection. Check the selected node or retry later." : "The direct subscription download timed out. Check your network or connect to a working node first.");
                }
                catch (Exception ex)
                {
                    overall.ThrowIfCancellationRequested();
                    if (attempt.IsCancellationRequested) throw Error(SubscriptionDownloadFailure.Timeout, viaProxy, "订阅下载超时。", "The subscription download timed out.");
                    if (IsTlsFailure(ex)) throw Error(SubscriptionDownloadFailure.Certificate, viaProxy,
                        "订阅的 TLS 连接或证书校验失败。请检查系统时间、Win7 TLS 1.2 和根证书更新；不会跳过证书校验。",
                        "Subscription TLS or certificate validation failed. Check the clock, Windows 7 TLS 1.2 and root certificate updates. Certificate validation remains enabled.");
                    if (ex is InvalidDataException || ex is DecoderFallbackException)
                        throw Error(SubscriptionDownloadFailure.Content, viaProxy, "订阅压缩数据或 UTF-8 内容无效。", "The subscription contains invalid compression data or UTF-8 content.");
                    if (!(ex is HttpRequestException) && !(ex is IOException) && !(ex is WebException)) throw;
                    throw Error(SubscriptionDownloadFailure.Network, viaProxy,
                        viaProxy ? "当前 Cute Clash 连接无法下载订阅，请检查所选节点。" : "无法直连订阅服务器，请检查网络、DNS 或订阅地址。",
                        viaProxy ? "The active Cute Clash connection could not download the subscription. Check the selected node." : "Could not reach the subscription server directly. Check the network, DNS or subscription address.");
                }
            }
        }

        private static async Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken, bool viaProxy, SubscriptionDownloadOptions options)
        {
            using (var raw = await BoundedAsync(response.Content.ReadAsStreamAsync(), cancellationToken).ConfigureAwait(false))
            using (var limited = new LimitedReadStream(raw, options.MaxCompressedBytes, viaProxy))
            using (var memory = new MemoryStream())
            {
                Stream decoded = limited;
                string encoding = String.Join(",", response.Content.Headers.ContentEncoding);
                if (encoding.Equals("gzip", StringComparison.OrdinalIgnoreCase)) decoded = new GZipStream(limited, CompressionMode.Decompress, true);
                else if (encoding.Length != 0 && !encoding.Equals("identity", StringComparison.OrdinalIgnoreCase))
                    throw Error(SubscriptionDownloadFailure.Content, viaProxy, "订阅使用不支持的压缩格式。", "The subscription uses an unsupported content encoding.");
                try
                {
                    byte[] buffer = new byte[16384];
                    using (cancellationToken.Register(delegate { raw.Dispose(); }))
                    {
                        for (;;)
                        {
                            int count;
                            using (var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                            {
                                idle.CancelAfter(options.IdleTimeoutMs);
                                using (idle.Token.Register(delegate { raw.Dispose(); }))
                                    count = await BoundedAsync(decoded.ReadAsync(buffer, 0, buffer.Length, idle.Token), idle.Token).ConfigureAwait(false);
                            }
                            if (count == 0) break;
                            if (memory.Length + count > options.MaxDecodedBytes) throw TooLarge(viaProxy);
                            memory.Write(buffer, 0, count);
                        }
                    }
                    string text = new UTF8Encoding(false, true).GetString(memory.ToArray()).TrimStart('\uFEFF');
                    string beginning = text.TrimStart();
                    if (beginning.Length == 0 || beginning.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase) || beginning.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
                        throw Error(SubscriptionDownloadFailure.Content, viaProxy, "服务器返回空内容或网页，请使用 Clash / Mihomo YAML 订阅链接。", "The server returned an empty response or a web page. Use a Clash / Mihomo YAML subscription link.");
                    return text;
                }
                finally { if (!Object.ReferenceEquals(decoded, limited)) decoded.Dispose(); }
            }
        }

        private static async Task<T> BoundedAsync<T>(Task<T> operation, CancellationToken cancellationToken)
        {
            // Some Framework stream implementations ignore ReadAsync cancellation. The
            // caller closes the transport; this also bounds the await if close is delayed.
            var canceled = new TaskCompletionSource<bool>();
            using (cancellationToken.Register(delegate { canceled.TrySetResult(true); }))
            {
                if (await Task.WhenAny(operation, canceled.Task).ConfigureAwait(false) != operation)
                {
                    var ignored = operation.ContinueWith(delegate(Task<T> task)
                    {
                        if (task.IsFaulted) { var observed = task.Exception; }
                        else if (task.Status == TaskStatus.RanToCompletion) { var value = (object)task.Result as IDisposable; if (value != null) value.Dispose(); }
                    }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                    cancellationToken.ThrowIfCancellationRequested();
                }
                try { return await operation.ConfigureAwait(false); }
                catch { cancellationToken.ThrowIfCancellationRequested(); throw; }
            }
        }

        private static bool IsTlsFailure(Exception exception)
        {
            for (Exception current = exception; current != null; current = current.InnerException)
            {
                if (current is AuthenticationException) return true;
                var web = current as WebException;
                if (web != null && (web.Status == WebExceptionStatus.TrustFailure || web.Status == WebExceptionStatus.SecureChannelFailure)) return true;
            }
            return false;
        }
        private static SubscriptionDownloadException Error(SubscriptionDownloadFailure failure, bool proxy, string chinese, string english)
        { return new SubscriptionDownloadException(failure, Localization.T(chinese, english), proxy, 0); }
        private static SubscriptionDownloadException TooLarge(bool proxy)
        { return Error(SubscriptionDownloadFailure.TooLarge, proxy, "订阅下载或解压后的内容超过大小限制（默认 8 MiB）。", "The downloaded or decompressed subscription exceeds the size limit (8 MiB by default)."); }

        private sealed class CoreProxy : IWebProxy
        {
            private readonly Uri address;
            internal CoreProxy(int port) { address = new Uri("http://127.0.0.1:" + port); }
            public Uri GetProxy(Uri destination) { return address; }
            public bool IsBypassed(Uri destination) { return false; }
            public ICredentials Credentials { get; set; }
        }

        private sealed class LimitedReadStream : Stream
        {
            private readonly Stream inner; private readonly long maximum; private readonly bool proxy; private long consumed;
            internal LimitedReadStream(Stream inner, long maximum, bool proxy) { this.inner = inner; this.maximum = maximum; this.proxy = proxy; }
            private int Count(int count) { consumed += count; if (consumed > maximum) throw TooLarge(proxy); return count; }
            public override int Read(byte[] buffer, int offset, int count) { return Count(inner.Read(buffer, offset, count)); }
            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            { return Count(await inner.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false)); }
            public override bool CanRead { get { return true; } } public override bool CanWrite { get { return false; } } public override bool CanSeek { get { return false; } }
            public override long Length { get { throw new NotSupportedException(); } }
            public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
            public override void Flush() { } public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
            public override void SetLength(long value) { throw new NotSupportedException(); } public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
        }
    }
}
