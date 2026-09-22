using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace CuteClash
{
    public sealed class ProtocolImportRequest
    {
        public string Url { get; private set; }
        public string Name { get; private set; }
        internal ProtocolImportRequest(string url, string name) { Url = url; Name = name; }
    }

    public static class ProtocolImport
    {
        public const int MaxLinkLength = 32 * 1024;
        public const int MaxSubscriptionLength = 8192;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        // Percent-encoded outer values are decoded once; '+' is always literal.
        // A literal http(s):// URL consumes subsequent unknown &key=value parts as
        // its own query. Outer url/name remain reserved delimiters. To carry an
        // inner query named url/name, percent-encode the complete subscription URL.
        // Unknown outer keys around an encoded URL are rejected, not silently lost.
        public static bool TryParse(string raw, out ProtocolImportRequest request, out string error)
        {
            request = null;
            error = null;
            if (String.IsNullOrEmpty(raw) || raw.Length > MaxLinkLength || HasControl(raw))
                return Fail(out error, "导入链接为空、过长或包含控制字符。", "The import link is empty, too long, or contains control characters.");
            const string prefix = "clash://";
            if (!raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return Fail(out error, "只支持 clash:// 导入链接。", "Only clash:// import links are supported.");
            int queryAt = raw.IndexOf('?', prefix.Length);
            string action = queryAt < 0 ? "" : raw.Substring(prefix.Length, queryAt - prefix.Length);
            if (!String.Equals(action, "install-config", StringComparison.OrdinalIgnoreCase) &&
                !String.Equals(action, "install-config/", StringComparison.OrdinalIgnoreCase))
                return Fail(out error, "导入链接的操作无效。", "The import link action is not supported.");
            if (raw.IndexOf('#') >= 0)
                return Fail(out error, "导入链接格式无效，请使用完整编码的订阅链接。", "The import link is malformed. Use a fully encoded subscription URL.");

            string url = null, name = null;
            bool seenUrl = false, seenName = false, literalUrl = false, appendToUrl = false;
            string[] parts = raw.Substring(queryAt + 1).Split('&');
            try
            {
                foreach (string part in parts)
                {
                    int equals = part.IndexOf('=');
                    string key = Decode(equals < 0 ? part : part.Substring(0, equals));
                    string value = equals < 0 ? "" : part.Substring(equals + 1);
                    if (String.Equals(key, "url", StringComparison.OrdinalIgnoreCase))
                    {
                        if (seenUrl || equals < 0)
                            return Fail(out error, "导入链接的 url 参数重复或无效。", "The import link has a duplicate or invalid url parameter.");
                        seenUrl = true;
                        literalUrl = value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || value.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
                        url = literalUrl ? value : Decode(value);
                        appendToUrl = literalUrl;
                    }
                    else if (String.Equals(key, "name", StringComparison.OrdinalIgnoreCase))
                    {
                        if (seenName || equals < 0)
                            return Fail(out error, "导入链接的 name 参数重复或无效。", "The import link has a duplicate or invalid name parameter.");
                        seenName = true;
                        name = Decode(value);
                        appendToUrl = false;
                    }
                    else if (appendToUrl)
                    {
                        url += "&" + part;
                    }
                    else
                    {
                        return Fail(out error, "导入链接包含未知参数。", "The import link contains an unknown parameter.");
                    }
                }
                if (!seenUrl || String.IsNullOrWhiteSpace(url) || url.Length > MaxSubscriptionLength || HasControl(url) || HasControl(Decode(url)))
                    return Fail(out error, "订阅地址为空、过长或格式无效。", "The subscription URL is empty, too long, or malformed.");
                // Validate UTF-16 even when a literal URL did not pass through Decode.
                StrictUtf8.GetByteCount(url);
                if (name != null && (name.Length > 200 || HasControl(name)))
                    return Fail(out error, "配置名称不能超过 200 个字符或包含控制字符。", "The profile name must not exceed 200 characters or contain control characters.");
                Uri parsed;
                if (!Uri.TryCreate(url, UriKind.Absolute, out parsed) ||
                    (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps) ||
                    String.IsNullOrEmpty(parsed.Host) || !String.IsNullOrEmpty(parsed.UserInfo) || url.IndexOf('\\') >= 0 ||
                    !String.Equals(url, url.Trim(), StringComparison.Ordinal))
                    return Fail(out error, "订阅地址必须是没有用户名或密码的 HTTP / HTTPS URL。", "The subscription URL must use HTTP or HTTPS and must not contain a username or password.");
                request = new ProtocolImportRequest(url, String.IsNullOrWhiteSpace(name) ? null : name);
                return true;
            }
            catch (ArgumentException)
            {
                return Fail(out error, "导入链接包含无效编码。", "The import link contains invalid encoding.");
            }
        }

        private static bool HasControl(string value)
        {
            for (int i = 0; i < value.Length; i++) if (Char.IsControl(value[i])) return true;
            return false;
        }
        private static string Decode(string value)
        {
            var decoded = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length;)
            {
                if (value[i] != '%') { decoded.Append(value[i++]); continue; }
                var bytes = new List<byte>();
                while (i < value.Length && value[i] == '%')
                {
                    if (i + 2 >= value.Length) throw new ArgumentException("Invalid percent encoding");
                    int high = Hex(value[i + 1]), low = Hex(value[i + 2]);
                    if (high < 0 || low < 0) throw new ArgumentException("Invalid percent encoding");
                    bytes.Add((byte)(high * 16 + low)); i += 3;
                }
                decoded.Append(StrictUtf8.GetString(bytes.ToArray()));
            }
            string result = decoded.ToString();
            StrictUtf8.GetByteCount(result);
            return result;
        }
        private static int Hex(char value)
        {
            if (value >= '0' && value <= '9') return value - '0';
            if (value >= 'a' && value <= 'f') return value - 'a' + 10;
            if (value >= 'A' && value <= 'F') return value - 'A' + 10;
            return -1;
        }
        private static bool Fail(out string error, string chinese, string english)
        {
            // Never include input text: subscription URLs frequently contain secrets.
            error = Localization.T(chinese, english);
            return false;
        }
    }

    /// <summary>
    /// Same-user delivery only. The callback runs on a background thread and must
    /// enqueue work (e.g. Control.BeginInvoke), never synchronously wait for the UI.
    /// Delivery merely asks the UI to confirm an import; it does not import or connect.
    /// </summary>
    public sealed class ProtocolInbox : IDisposable
    {
        private const int MaxPayloadBytes = ProtocolImport.MaxLinkLength * 4;
        private const int IoTimeoutMs = 1500;
        private readonly Action<string> callback;
        private readonly string pipeName;
        private readonly object sync = new object();
        private readonly HashSet<Guid> delivered = new HashSet<Guid>();
        private readonly Queue<Guid> deliveredOrder = new Queue<Guid>();
        private volatile bool disposed;
        private Thread worker;
        private NamedPipeServerStream listeningPipe;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        internal static string CurrentUserPipeName
        {
            get { return "cute-clash-import-" + WindowsIdentity.GetCurrent().User.Value; }
        }
        public ProtocolInbox(Action<string> callback) : this(callback, CurrentUserPipeName) { }
        internal ProtocolInbox(Action<string> callback, string pipeName)
        {
            if (callback == null) throw new ArgumentNullException("callback");
            this.callback = callback;
            this.pipeName = pipeName;
        }
        public void Start()
        {
            lock (sync)
            {
                if (disposed) throw new ObjectDisposedException("ProtocolInbox");
                if (worker != null) return;
                listeningPipe = CreateServer();
                worker = new Thread(Serve) { IsBackground = true, Name = "Cute Clash import inbox" };
                worker.Start();
            }
        }
        private NamedPipeServerStream CreateServer()
        {
            var security = new PipeSecurity();
            security.SetAccessRuleProtection(true, false);
            SecurityIdentifier user = WindowsIdentity.GetCurrent().User;
            security.SetOwner(user);
            security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
#if NET6_0
            // CurrentUserOnly compares the token's Owner, which changes to the
            // Administrators group after elevation. Pin the actual User SID so
            // a normal browser can talk to this user's elevated TUN window.
            return NamedPipeServerStreamAcl.Create(pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous, 4096, 4096, security);
#else
            return new NamedPipeServerStream(pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous, 4096, 4096, security);
#endif
        }
        private void Serve()
        {
            while (!disposed)
            {
                NamedPipeServerStream pipe;
                lock (sync) { pipe = listeningPipe; }
                if (pipe == null) return;
                try
                {
                    IAsyncResult connection = pipe.BeginWaitForConnection(null, null);
                    using (WaitHandle wait = connection.AsyncWaitHandle)
                    {
                        while (!disposed && !wait.WaitOne(250)) { }
                        if (disposed) return;
                        pipe.EndWaitForConnection(connection);
                    }
                    var timer = Stopwatch.StartNew();
                    byte[] header = new byte[4];
                    ReadExact(pipe, header, timer, IoTimeoutMs);
                    int length = BitConverter.ToInt32(header, 0);
                    if (length <= 0 || length > MaxPayloadBytes) throw new InvalidDataException("Invalid frame length");
                    byte[] requestBytes = new byte[16];
                    ReadExact(pipe, requestBytes, timer, IoTimeoutMs);
                    Guid requestId = new Guid(requestBytes);
                    byte[] bytes = new byte[length];
                    ReadExact(pipe, bytes, timer, IoTimeoutMs);
                    string raw = StrictUtf8.GetString(bytes);
                    if (raw.Length > ProtocolImport.MaxLinkLength) throw new InvalidDataException("Frame exceeds character limit");
                    if (disposed) return;
                    if (!delivered.Contains(requestId))
                    {
                        callback(raw);
                        delivered.Add(requestId);
                        deliveredOrder.Enqueue(requestId);
                        if (deliveredOrder.Count > 64) delivered.Remove(deliveredOrder.Dequeue());
                    }
                    if (!disposed) WriteExact(pipe, new byte[] { 1 }, Stopwatch.StartNew(), IoTimeoutMs);
                }
                catch (Exception)
                {
                    // Malformed/disconnected clients are discarded. Never log URL payloads.
                }
                finally
                {
                    pipe.Dispose();
                    lock (sync)
                    {
                        if (Object.ReferenceEquals(listeningPipe, pipe)) listeningPipe = null;
                        if (!disposed)
                        {
                            try { listeningPipe = CreateServer(); }
                            catch (IOException) { disposed = true; }
                            catch (UnauthorizedAccessException) { disposed = true; }
                        }
                    }
                }
            }
        }
        public static bool TrySend(string raw, int timeoutMs)
        {
            return TrySend(raw, timeoutMs, CurrentUserPipeName);
        }
        internal static bool TrySend(string raw, int timeoutMs, string pipeName)
        {
            return TrySend(raw, timeoutMs, pipeName, Guid.NewGuid());
        }
        internal static bool TrySend(string raw, int timeoutMs, string pipeName, Guid requestId)
        {
            if (String.IsNullOrEmpty(raw) || raw.Length > ProtocolImport.MaxLinkLength || timeoutMs < 1) return false;
            int timeout = Math.Min(timeoutMs, 10000);
            try
            {
                byte[] bytes = StrictUtf8.GetBytes(raw);
                if (bytes.Length > MaxPayloadBytes) return false;
                byte[] frame = new byte[20 + bytes.Length];
                Buffer.BlockCopy(BitConverter.GetBytes(bytes.Length), 0, frame, 0, 4);
                Buffer.BlockCopy(requestId.ToByteArray(), 0, frame, 4, 16);
                Buffer.BlockCopy(bytes, 0, frame, 20, bytes.Length);
                var timer = Stopwatch.StartNew();
                PipeOptions options = PipeOptions.Asynchronous;
                while (timer.ElapsedMilliseconds < timeout)
                {
                    try
                    {
                        using (var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, options,
                            TokenImpersonationLevel.Identification))
                        {
                            pipe.Connect(Remaining(timer, timeout));
                            // Verify the actual User SID on both target runtimes: a pipe
                            // created first by another user must never receive the URL.
                            var owner = (SecurityIdentifier)pipe.GetAccessControl().GetOwner(typeof(SecurityIdentifier));
                            if (!WindowsIdentity.GetCurrent().User.Equals(owner)) return false;
                            WriteExact(pipe, frame, timer, timeout);
                            byte[] ack = new byte[1];
                            ReadExact(pipe, ack, timer, timeout);
                            return ack[0] == 1;
                        }
                    }
                    catch (IOException)
                    {
                        // A previous pipe instance can be closing when Connect
                        // succeeds. Reuse the request ID so an acknowledgment lost
                        // after UI delivery cannot cause a duplicate confirmation.
                        Thread.Sleep(Math.Min(25, Remaining(timer, timeout)));
                    }
                    catch (ObjectDisposedException)
                    {
                        Thread.Sleep(Math.Min(25, Remaining(timer, timeout)));
                    }
                }
                return false;
            }
            catch (Exception) { return false; }
        }
        private static int Remaining(Stopwatch timer, int timeoutMs)
        {
            long remaining = timeoutMs - timer.ElapsedMilliseconds;
            if (remaining <= 0) throw new TimeoutException("Pipe operation timed out");
            return (int)remaining;
        }
        private static void ReadExact(PipeStream pipe, byte[] buffer, Stopwatch timer, int timeoutMs)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int timeout = Remaining(timer, timeoutMs);
                IAsyncResult read = pipe.BeginRead(buffer, offset, buffer.Length - offset, null, null);
                using (WaitHandle wait = read.AsyncWaitHandle)
                {
                    if (!wait.WaitOne(timeout))
                    {
                        pipe.Dispose();
                        // Let cancellation finish before closing the APM wait
                        // handle; Win7 may complete overlapped I/O asynchronously.
                        if (wait.WaitOne(500)) { try { pipe.EndRead(read); } catch (IOException) { } catch (ObjectDisposedException) { } }
                        throw new TimeoutException("Pipe read timed out");
                    }
                    int count = pipe.EndRead(read);
                    if (count <= 0) throw new EndOfStreamException("Pipe frame ended early");
                    offset += count;
                }
            }
        }
        private static void WriteExact(PipeStream pipe, byte[] buffer, Stopwatch timer, int timeoutMs)
        {
            int timeout = Remaining(timer, timeoutMs);
            IAsyncResult write = pipe.BeginWrite(buffer, 0, buffer.Length, null, null);
            using (WaitHandle wait = write.AsyncWaitHandle)
            {
                if (!wait.WaitOne(timeout))
                {
                    pipe.Dispose();
                    if (wait.WaitOne(500)) { try { pipe.EndWrite(write); } catch (IOException) { } catch (ObjectDisposedException) { } }
                    throw new TimeoutException("Pipe write timed out");
                }
                pipe.EndWrite(write);
            }
        }
        public void Dispose()
        {
            Thread thread;
            lock (sync)
            {
                disposed = true;
                if (listeningPipe != null) listeningPipe.Dispose();
                thread = worker;
            }
            if (thread != null && Thread.CurrentThread != thread) thread.Join(IoTimeoutMs + 500);
        }
    }
}
