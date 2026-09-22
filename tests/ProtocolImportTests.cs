using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace CuteClash.Tests
{
    internal static class ProtocolImportTests
    {
        public static int Run(string scratch)
        {
            Directory.CreateDirectory(scratch);
            string language = Localization.Language;
            try
            {
                string subscription = "https://example.test/订阅?token=a%26b%2Bc+d&signature=q%2526r";
                CheckParsed("clash://install-config?url=" + Uri.EscapeDataString(subscription) + "&name=" + Uri.EscapeDataString("机场 · 我的配置"), subscription, "机场 · 我的配置");
                CheckParsed("CLASH://INSTALL-CONFIG/?name=" + Uri.EscapeDataString("中文名称") + "&url=" + Uri.EscapeDataString(subscription), subscription, "中文名称");
                CheckParsed("clash://install-config?url=https%3A%2F%2Fexample.test%2Fa%3Ftoken%3Dabc%2526def%2Bghi", "https://example.test/a?token=abc%26def+ghi", null);
                CheckParsed("clash://install-config?url=https://example.test/a?token=abc%26def+ghi&foo=bar%2Bbaz&limit=7", "https://example.test/a?token=abc%26def+ghi&foo=bar%2Bbaz&limit=7", null);
                CheckParsed("clash://install-config/?name=My+Profile&url=http://example.test/a?token=abc&foo=bar", "http://example.test/a?token=abc&foo=bar", "My+Profile");
                CheckParsed("clash://install-config?url=https://example.test/a?token=abc&name=Profile", "https://example.test/a?token=abc", "Profile");
                CheckParsed("clash://install-config?url=" + Uri.EscapeDataString("https://example.test/?name=inner&url=inside") + "&name=outer", "https://example.test/?name=inner&url=inside", "outer");

                foreach (string invalid in new[] {
                    null, "", "http://example.test", "clash://evil?url=https://example.test", "clash://install-config", "clash://install-config?name=only",
                    "clash://install-config?url=file%3A%2F%2FC%3A%2Fsecret", "clash://install-config?url=javascript%3Aalert(1)",
                    "clash://install-config?url=https://user:secret@example.test/private", "clash://install-config?url=https://example.test&url=https://evil.test",
                    "clash://install-config?url=https://example.test&name=a&name=b", "clash://install-config?url=https://example.test&%75rl=https://evil.test",
                    "clash://install-config?url=https%3A%2F%2Fexample.test&unknown=x", "clash://install-config?unknown=x&url=https://example.test",
                    "clash://install-config?url=https://example.test/#fragment", "clash://install-config?url=https://example.test/\r\nsecret",
                    "clash://install-config?url=https://example.test/%0d%0aSecret", "clash://install-config?url=https%3A%2F%2Fexample.test%2F%2500",
                    "clash://install-config?url=https://example.test&name=%00", "clash://install-config?url=https://example.test&name=%C3%28",
                    "clash://install-config?url=https://example.test/%GG", "clash://install-config?url=https://example.test&name=" + new string('n', 201),
                    "clash://install-config?url=https://example.test/" + new string('a', ProtocolImport.MaxLinkLength),
                    "clash://install-config?url=https://example.test/" + new string('a', ProtocolImport.MaxSubscriptionLength),
                    "clash://install-config?url=https://example.test&name=\uD800", "clash://install-config?url=https:\\example.test/"
                }) Reject(invalid);

                Localization.Language = "en";
                ProtocolImportRequest request; string error;
                Check(!ProtocolImport.TryParse("clash://install-config?url=https://secret:private-password-123@example.test/token", out request, out error) &&
                    error.Contains("HTTP") && !error.Contains("private-password-123") && !error.Contains("example.test"), "English errors explain validation without leaking credentials");
                Localization.Language = "zh-CN";
                Check(!ProtocolImport.TryParse("clash://invalid?url=https://secret.test", out request, out error) &&
                    error.Contains("导入") && !error.Contains("secret"), "Chinese errors do not echo input");

                TestPipe();
                return 5;
            }
            finally { Localization.Language = language; }
        }
        private static void CheckParsed(string raw, string expectedUrl, string expectedName)
        {
            ProtocolImportRequest request; string error;
            Check(ProtocolImport.TryParse(raw, out request, out error) && request != null &&
                request.Url == expectedUrl && request.Name == expectedName && error == null, "Protocol URL/name decoding preserves subscription bytes");
        }
        private static void Reject(string raw)
        {
            ProtocolImportRequest request; string error;
            Check(!ProtocolImport.TryParse(raw, out request, out error) && request == null && !String.IsNullOrEmpty(error), "Invalid protocol input is rejected with a safe error");
        }
        private static void TestPipe()
        {
            string pipeName = ProtocolInbox.CurrentUserPipeName + "-test-" + Guid.NewGuid().ToString("N");
            string payload = "clash://install-config?url=https%3A%2F%2Fexample.test%2F%3Ftoken%3Dsecret%2526x&name=" + Uri.EscapeDataString("测试订阅");
            string received = null;
            int deliveries = 0;
            using (var ready = new ManualResetEvent(false))
            using (var inbox = new ProtocolInbox(delegate(string value) { received = value; Interlocked.Increment(ref deliveries); ready.Set(); }, pipeName))
            {
                inbox.Start(); inbox.Start();
                Check(ProtocolInbox.TrySend(payload, 3000, pipeName) && ready.WaitOne(1000) && received == payload && deliveries == 1,
                    "Same-user named pipe delivers exact UTF-8 payload and acknowledgment");
                var timer = Stopwatch.StartNew();
                Check(!ProtocolInbox.TrySend(new string('x', ProtocolImport.MaxLinkLength + 1), 200, pipeName) && timer.ElapsedMilliseconds < 1000,
                    "Oversized IPC message is rejected without connecting");
                // A client that opens the pipe but withholds its frame must not
                // permanently prevent the next browser import from being delivered.
                using (var stalled = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification))
                {
                    stalled.Connect(3000);
                    stalled.WriteByte(1); stalled.Flush();
                    timer.Restart();
                    Check(ProtocolInbox.TrySend(payload, 5000, pipeName) && timer.ElapsedMilliseconds < 5000 && deliveries == 2,
                        "Incomplete client is timed out and the inbox accepts another import");
                }
                using (var malformed = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification))
                {
                    malformed.Connect(3000);
                    byte[] header = BitConverter.GetBytes(Int32.MaxValue);
                    malformed.Write(header, 0, header.Length); malformed.Flush();
                }
                bool afterMalformed = ProtocolInbox.TrySend(payload, 3000, pipeName);
                Check(afterMalformed && deliveries == 3, "Oversized incoming frame is rejected before allocation and recovery continues (ack=" + afterMalformed + ", deliveries=" + deliveries + ")");
                Guid duplicateId = Guid.NewGuid();
                Check(ProtocolInbox.TrySend(payload, 3000, pipeName, duplicateId) && ProtocolInbox.TrySend(payload, 3000, pipeName, duplicateId) && deliveries == 4,
                    "Retrying the same delivery ID acknowledges without asking for confirmation twice");
                using (var pending = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification))
                {
                    pending.Connect(3000);
                    timer.Restart(); inbox.Dispose(); inbox.Dispose();
                    Check(timer.ElapsedMilliseconds < 3000, "Stopping an inbox with a stalled client is bounded and idempotent");
                }
            }
            var absentTimer = Stopwatch.StartNew();
            Check(!ProtocolInbox.TrySend(payload, 100, pipeName) && absentTimer.ElapsedMilliseconds < 1500, "Unavailable inbox connect has a bounded timeout");
        }
        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("FAIL " + message);
        }
    }
}
