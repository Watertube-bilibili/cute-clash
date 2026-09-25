using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CuteClash.Tests
{
    internal static class SubscriptionDownloadTests
    {
        public static int Run(string scratch)
        {
            Directory.CreateDirectory(scratch);
            RunAsync().GetAwaiter().GetResult();
            return 8;
        }

        private static async Task RunAsync()
        {
            string previousLanguage = Localization.Language;
            Localization.Language = "en";
            try
            {
                await DirectDoesNotDiscoverProxy();
                await CompressionAndLimits();
                await HeaderAndBodyTimeouts();
                await OverallDeadlineAndCancellation();
                await FallbackUsesOnlyActiveCore();
                await HttpErrorsDoNotRetry();
                await RedirectsAndBadContent();
                AddressValidation();
            }
            finally { Localization.Language = previousLanguage; }
        }

        private static SubscriptionDownloadOptions FastOptions()
        {
            return new SubscriptionDownloadOptions { ConnectTimeoutMs = 500, HeadersTimeoutMs = 500, IdleTimeoutMs = 500, AttemptTimeoutMs = 1800, OverallTimeoutMs = 2600 };
        }
        private static Task<string> Download(Fixture server, SubscriptionDownloadOptions options)
        { return SubscriptionDownloader.DownloadAsync(server.Url + "profile?token=private-test-token%2Bvalue", 0, CancellationToken.None, options); }

        private static async Task DirectDoesNotDiscoverProxy()
        {
            var poison = new PoisonProxy();
            IWebProxy previous = WebRequest.DefaultWebProxy;
#if NET6_0
            IWebProxy previousHttp = HttpClient.DefaultProxy;
#endif
            using (var server = new Fixture(async delegate(NetworkStream stream, string request, CancellationToken stop)
            { await Reply(stream, "200 OK", "", Encoding.UTF8.GetBytes("proxies: []\nrules: [MATCH,DIRECT]\n")); }))
            {
                try
                {
                    WebRequest.DefaultWebProxy = poison;
#if NET6_0
                    HttpClient.DefaultProxy = poison;
#endif
                    string text = await Download(server, FastOptions());
                    string request = server.FirstRequest;
                    Check(text.StartsWith("proxies:"), "direct response succeeds with a broken process default proxy");
                    Check(poison.Calls == 0 && server.RequestCount == 1, "no proxy discovery and exactly one subscription request");
                    Check(request.Contains("token=private-test-token%2Bvalue") && request.Contains("User-Agent: Cute-Clash/") && request.Contains("(clash.meta; mihomo)"), "subscription token and honest Clash-compatible UA reach the provider");
                }
                finally
                {
                    WebRequest.DefaultWebProxy = previous;
#if NET6_0
                    HttpClient.DefaultProxy = previousHttp;
#endif
                }
            }
        }

        private static async Task CompressionAndLimits()
        {
            byte[] body = Encoding.UTF8.GetBytes("\uFEFFproxies: []\n# 奶猫\n"), compressed;
            using (var memory = new MemoryStream())
            {
                using (var gzip = new GZipStream(memory, CompressionMode.Compress, true)) gzip.Write(body, 0, body.Length);
                compressed = memory.ToArray();
            }
            using (var server = new Fixture(async delegate(NetworkStream stream, string request, CancellationToken stop)
            { await Reply(stream, "200 OK", "Content-Encoding: gzip\r\n", compressed); }))
            {
                Check((await Download(server, FastOptions())).Contains("奶猫"), "gzip and UTF-8 BOM are decoded");
                var limits = FastOptions(); limits.MaxDecodedBytes = 10;
                await Expect(SubscriptionDownloadFailure.TooLarge, delegate { return Download(server, limits); });
            }
            using (var server = new Fixture(async delegate(NetworkStream stream, string request, CancellationToken stop)
            { await Write(stream, "HTTP/1.1 200 OK\r\nContent-Length: 1000000\r\nConnection: close\r\n\r\n"); await Task.Delay(2000, stop); }))
            {
                var limits = FastOptions(); limits.MaxCompressedBytes = 64;
                await Expect(SubscriptionDownloadFailure.TooLarge, delegate { return Download(server, limits); });
            }
            using (var server = new Fixture(async delegate(NetworkStream stream, string request, CancellationToken stop)
            { await Write(stream, "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\nConnection: close\r\n\r\n80\r\n" + new String('x', 128) + "\r\n0\r\n\r\n"); }))
            {
                var limits = FastOptions(); limits.MaxCompressedBytes = 64;
                await Expect(SubscriptionDownloadFailure.TooLarge, delegate { return Download(server, limits); });
            }
        }

        private static async Task HeaderAndBodyTimeouts()
        {
            using (var server = new Fixture(async delegate(NetworkStream stream, string request, CancellationToken stop) { await Task.Delay(5000, stop); }))
            {
                var watch = Stopwatch.StartNew();
                await Expect(SubscriptionDownloadFailure.Timeout, delegate { return Download(server, FastOptions()); });
                Check(watch.ElapsedMilliseconds < 2000, "DNS/connect/header phase has a short bounded wait");
            }
            using (var server = new Fixture(async delegate(NetworkStream stream, string request, CancellationToken stop)
            { await Write(stream, "HTTP/1.1 200 OK\r\nContent-Length: 100\r\nConnection: close\r\n\r\np"); await Task.Delay(5000, stop); }))
            {
                var watch = Stopwatch.StartNew();
                await Expect(SubscriptionDownloadFailure.Timeout, delegate { return Download(server, FastOptions()); });
                Check(watch.ElapsedMilliseconds < 2000, "stalled response body is interrupted even on Framework streams");
            }
        }

        private static async Task OverallDeadlineAndCancellation()
        {
            using (var server = new Fixture(async delegate(NetworkStream stream, string request, CancellationToken stop)
            {
                await Write(stream, "HTTP/1.1 200 OK\r\nContent-Length: 100\r\nConnection: close\r\n\r\n");
                for (int i = 0; i < 100; i++) { await Write(stream, "x"); await Task.Delay(40, stop); }
            }))
            {
                var limits = FastOptions(); limits.OverallTimeoutMs = 300;
                var watch = Stopwatch.StartNew();
                await Expect(SubscriptionDownloadFailure.Timeout, delegate { return Download(server, limits); });
                Check(watch.ElapsedMilliseconds < 1500, "trickling bodies cannot extend the overall deadline");
            }
            using (var server = new Fixture(async delegate(NetworkStream stream, string request, CancellationToken stop) { await Task.Delay(5000, stop); }))
            using (var proxy = new Fixture(async delegate(NetworkStream stream, string request, CancellationToken stop) { await Reply(stream, "200 OK", "", Encoding.UTF8.GetBytes("proxies: []")); }))
            using (var cancel = new CancellationTokenSource(150))
            {
                bool canceled = false;
                try { await SubscriptionDownloader.DownloadAsync(server.Url, proxy.Port, cancel.Token, FastOptions()); }
                catch (OperationCanceledException) { canceled = true; }
                Check(canceled && proxy.RequestCount == 0, "user cancellation is preserved and does not launch a fallback request");
            }
        }

        private static async Task FallbackUsesOnlyActiveCore()
        {
            // .invalid is reserved and cannot identify a public subscription server. The
            // Framework transport unconditionally bypasses proxies for loopback URLs, so
            // use this unresolvable test name and a real loopback HTTP proxy fixture.
            using (var proxy = new Fixture(async delegate(NetworkStream stream, string request, CancellationToken stop)
            { await Reply(stream, "200 OK", "", Encoding.UTF8.GetBytes("proxies: []\nrules: [MATCH,DIRECT]")); }))
            {
                var progress = new List<string>(); var limits = FastOptions(); limits.Progress = delegate(string message) { progress.Add(message); };
                string address = "http://cute-clash-subscription-test.invalid/subscription?token=private-test-token";
                string text = await SubscriptionDownloader.DownloadAsync(address, proxy.Port, CancellationToken.None, limits);
                Check(text.StartsWith("proxies:") && proxy.RequestCount == 1 && proxy.FirstRequest.StartsWith("GET " + address + " HTTP/"), "network failure retries once through exactly the supplied local mixed port");
                Check(progress.Count == 2 && !String.Join("", progress).Contains("private-test-token"), "route progress never contains the subscription token");
            }
        }

        private static async Task HttpErrorsDoNotRetry()
        {
            foreach (int status in new[] { 401, 403, 404, 429, 503 })
            using (var server = new Fixture(async delegate(NetworkStream stream, string request, CancellationToken stop)
            { await Reply(stream, status + " private-test-token", "", Encoding.UTF8.GetBytes("private-test-token")); }))
            using (var proxy = new Fixture(async delegate(NetworkStream stream, string request, CancellationToken stop) { await Reply(stream, "200 OK", "", new byte[0]); }))
            {
                SubscriptionDownloadException error = await Expect(SubscriptionDownloadFailure.HttpStatus, delegate { return SubscriptionDownloader.DownloadAsync(server.Url + "?token=private-test-token", proxy.Port, CancellationToken.None, FastOptions()); });
                Check(error.StatusCode == status && error.Message.Contains(status.ToString()) && !error.ToString().Contains("private-test-token"), "HTTP error is useful without leaking URL, reason phrase or body");
                Check(server.RequestCount == 1 && proxy.RequestCount == 0, "HTTP status errors do not duplicate subscription requests");
            }
        }

        private static async Task RedirectsAndBadContent()
        {
            using (var server = new Fixture(async delegate(NetworkStream stream, string request, CancellationToken stop)
            {
                if (request.StartsWith("GET /next ")) await Reply(stream, "200 OK", "", Encoding.UTF8.GetBytes("proxies: []"));
                else await Reply(stream, "302 Found", "Location: /next\r\n", new byte[0]);
            }))
            {
                Check((await Download(server, FastOptions())).StartsWith("proxies:") && server.RequestCount == 2, "relative redirects are followed sequentially");
            }
            using (var server = new Fixture(async delegate(NetworkStream stream, string request, CancellationToken stop)
            { await Reply(stream, "302 Found", "Location: /again\r\n", new byte[0]); }))
            {
                var options = FastOptions(); options.MaxRedirects = 2;
                await Expect(SubscriptionDownloadFailure.Redirect, delegate { return Download(server, options); });
                Check(server.RequestCount == 3, "redirect loops stop at the configured hop limit");
            }
            foreach (byte[] content in new[] { new byte[0], Encoding.UTF8.GetBytes("<!DOCTYPE html><title>Login</title>"), new byte[] { 0xff, 0xfe } })
            using (var server = new Fixture(async delegate(NetworkStream stream, string request, CancellationToken stop) { await Reply(stream, "200 OK", "", content); }))
                await Expect(SubscriptionDownloadFailure.Content, delegate { return Download(server, FastOptions()); });
        }

        private static void AddressValidation()
        {
            foreach (string invalid in new[] { "file:///private-test-token", "https://user:private-test-token@example.test", "http://localhost/#private-test-token", "http://localhost/\r\nprivate-test-token", "" })
            {
                var error = Expect(SubscriptionDownloadFailure.Address, delegate { return SubscriptionDownloader.DownloadAsync(invalid, 0, CancellationToken.None); }).GetAwaiter().GetResult();
                Check(!error.ToString().Contains("private-test-token"), "invalid URLs are rejected without exposing credentials");
            }
        }

        private static async Task<SubscriptionDownloadException> Expect(SubscriptionDownloadFailure failure, Func<Task<string>> action)
        {
            try { await action(); }
            catch (SubscriptionDownloadException ex) { Check(ex.Failure == failure, "expected " + failure + ", got " + ex.Failure); return ex; }
            throw new Exception("Expected subscription failure: " + failure);
        }
        private static void Check(bool value, string message) { if (!value) throw new Exception("FAIL subscription downloader: " + message); }
        private static Task Write(NetworkStream stream, string text) { byte[] bytes = Encoding.ASCII.GetBytes(text); return stream.WriteAsync(bytes, 0, bytes.Length); }
        private static async Task Reply(NetworkStream stream, string status, string extraHeaders, byte[] body)
        {
            await Write(stream, "HTTP/1.1 " + status + "\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n" + extraHeaders + "\r\n");
            await stream.WriteAsync(body, 0, body.Length);
        }
        private sealed class PoisonProxy : IWebProxy
        {
            public int Calls; public ICredentials Credentials { get; set; }
            public Uri GetProxy(Uri destination) { Interlocked.Increment(ref Calls); throw new Exception("Default proxy discovery must never run"); }
            public bool IsBypassed(Uri host) { Interlocked.Increment(ref Calls); throw new Exception("Default proxy discovery must never run"); }
        }
        private sealed class Fixture : IDisposable
        {
            private readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            private readonly CancellationTokenSource stop = new CancellationTokenSource();
            private readonly Func<NetworkStream, string, CancellationToken, Task> handler;
            private readonly List<TcpClient> clients = new List<TcpClient>();
            private int count; private string firstRequest;
            internal int Port { get; private set; }
            internal string Url { get { return "http://127.0.0.1:" + Port + "/"; } }
            internal int RequestCount { get { return count; } }
            internal string FirstRequest { get { return firstRequest ?? ""; } }
            internal Fixture(Func<NetworkStream, string, CancellationToken, Task> handler)
            {
                this.handler = handler; listener.Start(); Port = ((IPEndPoint)listener.LocalEndpoint).Port;
                var ignored = AcceptAsync();
            }
            private async Task AcceptAsync()
            {
                try
                {
                    while (!stop.IsCancellationRequested)
                    {
                        TcpClient client = await listener.AcceptTcpClientAsync();
                        lock (clients) clients.Add(client);
                        var ignored = ServeAsync(client);
                    }
                }
                catch (ObjectDisposedException) { }
                catch (SocketException) { }
            }
            private async Task ServeAsync(TcpClient client)
            {
                try
                {
                    using (client)
                    using (var stream = client.GetStream())
                    {
                        var text = new StringBuilder(); byte[] one = new byte[1];
                        while (text.Length < 32768 && !text.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
                        {
                            if (await stream.ReadAsync(one, 0, 1, stop.Token) == 0) return;
                            text.Append((char)one[0]);
                        }
                        Interlocked.CompareExchange(ref firstRequest, text.ToString(), null); Interlocked.Increment(ref count);
                        await handler(stream, text.ToString(), stop.Token);
                    }
                }
                catch (OperationCanceledException) { }
                catch (IOException) { }
                catch (ObjectDisposedException) { }
                catch (SocketException) { }
            }
            public void Dispose()
            {
                stop.Cancel(); listener.Stop();
                lock (clients) foreach (TcpClient client in clients) client.Close();
            }
        }
    }
}
