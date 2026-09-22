using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;

namespace CuteClash
{
    public sealed class ProxySnapshot
    {
        public int Flags { get; set; }
        public string Server { get; set; }
        public string Bypass { get; set; }
        public string Pac { get; set; }
        public bool SameAs(ProxySnapshot other)
        {
            return other != null && Flags == other.Flags && (Server ?? "") == (other.Server ?? "") &&
                (Bypass ?? "") == (other.Bypass ?? "") && (Pac ?? "") == (other.Pac ?? "");
        }
    }
    public interface IProxyBackend { ProxySnapshot Read(); void Write(ProxySnapshot snapshot); }
    public sealed class ProxyJournal { public ProxySnapshot Original { get; set; } public ProxySnapshot Applied { get; set; } }
    public sealed class SystemProxy
    {
        private readonly string journalPath;
        private readonly IProxyBackend backend;
        private readonly object sync = new object();
        public SystemProxy(string directory) : this(directory, new WinInetProxyBackend()) { }
        public SystemProxy(string directory, IProxyBackend backend)
        {
            Directory.CreateDirectory(directory);
            journalPath = Path.Combine(directory, "proxy-recovery.json"); this.backend = backend;
        }
        public bool HasJournal { get { return File.Exists(journalPath); } }
        public void Enable(int port)
        {
            lock (sync)
            {
                if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException("port");
                if (HasJournal) Restore();
                var applied = new ProxySnapshot { Flags = 3, Server = "127.0.0.1:" + port, Bypass = "<local>;localhost;127.*;[::1]", Pac = "" };
                var journal = new ProxyJournal { Original = backend.Read(), Applied = applied };
                string temp = journalPath + ".tmp";
                File.WriteAllText(temp, new JavaScriptSerializer().Serialize(journal), new UTF8Encoding(false));
                if (File.Exists(journalPath)) File.Replace(temp, journalPath, null); else File.Move(temp, journalPath);
                // Persist recovery BEFORE changing WinINet (including PAC and autodetect flags).
                backend.Write(applied);
            }
        }
        public bool Restore()
        {
            lock (sync)
            {
                if (!HasJournal) return false;
                var journal = new JavaScriptSerializer().Deserialize<ProxyJournal>(File.ReadAllText(journalPath));
                if (journal == null || journal.Applied == null || journal.Original == null) throw new InvalidDataException(Localization.T("代理恢复记录损坏；已保留文件供手动恢复。", "The proxy recovery record is damaged. The file was preserved for manual recovery."));
                bool ours = backend.Read().SameAs(journal.Applied);
                if (ours) backend.Write(journal.Original);
                // Never overwrite settings another application/user has changed since enable.
                File.Delete(journalPath);
                return ours;
            }
        }
    }
    internal sealed class WinInetProxyBackend : IProxyBackend
    {
        [StructLayout(LayoutKind.Explicit)] private struct Value
        {
            [FieldOffset(0)] public int Number;
            [FieldOffset(0)] public IntPtr Text;
            [FieldOffset(0)] public System.Runtime.InteropServices.ComTypes.FILETIME FileTime;
        }
        [StructLayout(LayoutKind.Sequential)] private struct Option { public int Id; public Value Value; }
        [StructLayout(LayoutKind.Sequential)] private struct Options
        {
            public int Size; public IntPtr Connection; public int Count; public int Error; public IntPtr Items;
        }
        public ProxySnapshot Read()
        {
            int size = Marshal.SizeOf(typeof(Option));
            IntPtr block = Marshal.AllocHGlobal(size * 4);
            var list = new Options { Size = Marshal.SizeOf(typeof(Options)), Count = 4, Items = block };
            bool queried = false;
            try
            {
                for (int i = 0; i < 4; i++) Marshal.StructureToPtr(new Option { Id = i + 1 }, IntPtr.Add(block, size * i), false);
                Marshal.StructureToPtr(new Option { Id = 10 }, block, false); // FLAGS_UI preserves user's auto-detect setting.
                int bytes = list.Size;
                if (!InternetQueryOption(IntPtr.Zero, 75, ref list, ref bytes))
                {
                    for (int i = 1; i < 4; i++) { IntPtr p = ReadOption(block, size, i).Value.Text; if (p != IntPtr.Zero) GlobalFree(p); }
                    for (int i = 0; i < 4; i++) Marshal.StructureToPtr(new Option { Id = i + 1 }, IntPtr.Add(block, size * i), false);
                    bytes = list.Size;
                    if (!InternetQueryOption(IntPtr.Zero, 75, ref list, ref bytes)) throw new Win32Exception();
                }
                queried = true;
                return new ProxySnapshot { Flags = ReadOption(block, size, 0).Value.Number,
                    Server = Marshal.PtrToStringUni(ReadOption(block, size, 1).Value.Text) ?? "",
                    Bypass = Marshal.PtrToStringUni(ReadOption(block, size, 2).Value.Text) ?? "",
                    Pac = Marshal.PtrToStringUni(ReadOption(block, size, 3).Value.Text) ?? "" };
            }
            finally
            {
                if (queried) for (int i = 1; i < 4; i++) { IntPtr p = ReadOption(block, size, i).Value.Text; if (p != IntPtr.Zero) GlobalFree(p); }
                Marshal.FreeHGlobal(block);
            }
        }
        private static Option ReadOption(IntPtr ptr, int size, int i) { return (Option)Marshal.PtrToStructure(IntPtr.Add(ptr, size * i), typeof(Option)); }
        public void Write(ProxySnapshot snapshot)
        {
            int size = Marshal.SizeOf(typeof(Option));
            IntPtr block = Marshal.AllocHGlobal(size * 4);
            IntPtr[] strings = { Marshal.StringToHGlobalUni(snapshot.Server ?? ""), Marshal.StringToHGlobalUni(snapshot.Bypass ?? ""), Marshal.StringToHGlobalUni(snapshot.Pac ?? "") };
            try
            {
                Marshal.StructureToPtr(new Option { Id = 1, Value = new Value { Number = snapshot.Flags } }, block, false);
                for (int i = 1; i < 4; i++) Marshal.StructureToPtr(new Option { Id = i + 1, Value = new Value { Text = strings[i - 1] } }, IntPtr.Add(block, size * i), false);
                var list = new Options { Size = Marshal.SizeOf(typeof(Options)), Count = 4, Items = block };
                if (!InternetSetOption(IntPtr.Zero, 75, ref list, list.Size)) throw new Win32Exception();
                InternetSetOptionNotify(IntPtr.Zero, 39, IntPtr.Zero, 0);
                InternetSetOptionNotify(IntPtr.Zero, 37, IntPtr.Zero, 0);
            }
            finally { foreach (IntPtr p in strings) Marshal.FreeHGlobal(p); Marshal.FreeHGlobal(block); }
        }
        [DllImport("wininet.dll", EntryPoint = "InternetQueryOptionW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool InternetQueryOption(IntPtr internet, int option, ref Options data, ref int size);
        [DllImport("wininet.dll", EntryPoint = "InternetSetOptionW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool InternetSetOption(IntPtr internet, int option, ref Options data, int size);
        [DllImport("wininet.dll", EntryPoint = "InternetSetOptionW", SetLastError = true)] private static extern bool InternetSetOptionNotify(IntPtr internet, int option, IntPtr data, int size);
        [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr memory);
    }
}
