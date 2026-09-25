using System;
using System.Collections.Generic;
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
    public interface IConnectionProxyBackend : IProxyBackend
    {
        IList<string> GetConnections();
        ProxySnapshot Read(string connection);
        void Write(string connection, ProxySnapshot snapshot);
    }
    public sealed class ConnectionProxyJournal
    {
        public string Connection { get; set; }
        public ProxySnapshot Original { get; set; }
        public ProxySnapshot Applied { get; set; }
    }
    public sealed class ProxyJournal
    {
        // Keep the old fields readable for recovery after an upgrade from 0.3.0.
        public ProxySnapshot Original { get; set; }
        public ProxySnapshot Applied { get; set; }
        public List<ConnectionProxyJournal> Connections { get; set; }
    }
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
                var multi = backend as IConnectionProxyBackend;
                var journal = new ProxyJournal { Connections = new List<ConnectionProxyJournal>() };
                IList<string> connections = multi == null ? new List<string> { null } : multi.GetConnections();
                foreach (string connection in connections)
                    journal.Connections.Add(new ConnectionProxyJournal { Connection = connection, Original = Read(connection), Applied = applied });
                SaveJournal(journal);
                // Persist every LAN/RAS backup before changing the first connection.
                // A failed write/readback keeps recovery available rather than reporting success.
                foreach (ConnectionProxyJournal item in journal.Connections)
                {
                    Write(item.Connection, applied);
                    if (!Read(item.Connection).SameAs(applied))
                        throw new IOException(Localization.T("Windows 未应用系统代理设置，已保留恢复记录。", "Windows did not apply the proxy settings. Recovery data was preserved."));
                }
            }
        }
        public bool Restore()
        {
            lock (sync)
            {
                if (!HasJournal) return false;
                var journal = new JavaScriptSerializer().Deserialize<ProxyJournal>(File.ReadAllText(journalPath));
                if (journal == null) throw CorruptJournal();
                List<ConnectionProxyJournal> items = journal.Connections;
                if (items == null && journal.Applied != null && journal.Original != null)
                    items = new List<ConnectionProxyJournal> { new ConnectionProxyJournal { Original = journal.Original, Applied = journal.Applied } };
                if (items == null || items.Count == 0 || items.Count > 1024) throw CorruptJournal();
                foreach (ConnectionProxyJournal item in items)
                    if (item == null || item.Original == null || item.Applied == null) throw CorruptJournal();
                bool restored = false;
                Exception failure = null;
                var pending = new List<ConnectionProxyJournal>();
                foreach (ConnectionProxyJournal item in items)
                {
                    try
                    {
                        if (Read(item.Connection).SameAs(item.Applied))
                        {
                            Write(item.Connection, item.Original);
                            if (!Read(item.Connection).SameAs(item.Original)) throw new IOException("Proxy restore readback mismatch.");
                            restored = true;
                        }
                        // Do not overwrite a connection another app/user has changed.
                    }
                    catch (Exception ex) { pending.Add(item); if (failure == null) failure = ex; }
                }
                if (pending.Count > 0)
                {
                    SaveJournal(new ProxyJournal { Connections = pending });
                    throw new IOException(Localization.T("部分连接的系统代理未能恢复，恢复记录已保留。", "Some connection proxy settings could not be restored. Recovery data was preserved."), failure);
                }
                File.Delete(journalPath);
                return restored;
            }
        }
        private static InvalidDataException CorruptJournal() { return new InvalidDataException(Localization.T("代理恢复记录损坏；已保留文件供手动恢复。", "The proxy recovery record is damaged. The file was preserved for manual recovery.")); }
        private ProxySnapshot Read(string connection)
        {
            var multi = backend as IConnectionProxyBackend;
            if (multi != null) return multi.Read(connection);
            if (connection != null) throw new NotSupportedException("Connection-specific proxy backend required.");
            return backend.Read();
        }
        private void Write(string connection, ProxySnapshot snapshot)
        {
            var multi = backend as IConnectionProxyBackend;
            if (multi != null) multi.Write(connection, snapshot); else if (connection == null) backend.Write(snapshot);
            else throw new NotSupportedException("Connection-specific proxy backend required.");
        }
        private void SaveJournal(ProxyJournal journal)
        {
            string temp = journalPath + ".tmp";
            File.WriteAllText(temp, new JavaScriptSerializer().Serialize(journal), new UTF8Encoding(false));
            if (File.Exists(journalPath)) File.Replace(temp, journalPath, null); else File.Move(temp, journalPath);
        }
    }
    // LAN + Unicode RAS enumeration and option application are adapted from
    // clash-verge-rev/sysproxy-rs src/windows.rs @ 44aaf00ec9c6779e5a461a55d882eddff7841c98.
    // Copyright (c) 2022 zzzgydi, MIT; see licenses/sysproxy-rs-MIT.txt.
    internal sealed class WinInetProxyBackend : IConnectionProxyBackend
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
        public ProxySnapshot Read() { return Read(null); }
        public ProxySnapshot Read(string connection)
        {
            int size = Marshal.SizeOf(typeof(Option));
            IntPtr block = Marshal.AllocHGlobal(size * 4);
            IntPtr connectionPointer = connection == null ? IntPtr.Zero : Marshal.StringToHGlobalUni(connection);
            var list = new Options { Size = Marshal.SizeOf(typeof(Options)), Count = 4, Items = block, Connection = connectionPointer };
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
                if (connectionPointer != IntPtr.Zero) Marshal.FreeHGlobal(connectionPointer);
            }
        }
        private static Option ReadOption(IntPtr ptr, int size, int i) { return (Option)Marshal.PtrToStructure(IntPtr.Add(ptr, size * i), typeof(Option)); }
        public void Write(ProxySnapshot snapshot) { Write(null, snapshot); }
        public void Write(string connection, ProxySnapshot snapshot)
        {
            int size = Marshal.SizeOf(typeof(Option));
            IntPtr block = Marshal.AllocHGlobal(size * 4);
            IntPtr[] strings = { Marshal.StringToHGlobalUni(snapshot.Server ?? ""), Marshal.StringToHGlobalUni(snapshot.Bypass ?? ""), Marshal.StringToHGlobalUni(snapshot.Pac ?? "") };
            IntPtr connectionPointer = connection == null ? IntPtr.Zero : Marshal.StringToHGlobalUni(connection);
            try
            {
                Marshal.StructureToPtr(new Option { Id = 1, Value = new Value { Number = snapshot.Flags } }, block, false);
                for (int i = 1; i < 4; i++) Marshal.StructureToPtr(new Option { Id = i + 1, Value = new Value { Text = strings[i - 1] } }, IntPtr.Add(block, size * i), false);
                var list = new Options { Size = Marshal.SizeOf(typeof(Options)), Count = 4, Items = block, Connection = connectionPointer };
                if (!InternetSetOption(IntPtr.Zero, 75, ref list, list.Size)) throw new Win32Exception();
                if (!InternetSetOptionNotify(IntPtr.Zero, 95, IntPtr.Zero, 0))
                {
                    int error = Marshal.GetLastWin32Error();
                    // Old WinINet can reject PROXY_SETTINGS_CHANGED. Fall back only
                    // when that option is unsupported, never on access or I/O errors.
                    if (error != 12009 && error != 87) throw new Win32Exception(error);
                    if (!InternetSetOptionNotify(IntPtr.Zero, 39, IntPtr.Zero, 0)) throw new Win32Exception();
                }
                if (!InternetSetOptionNotify(IntPtr.Zero, 37, IntPtr.Zero, 0)) throw new Win32Exception();
            }
            finally { foreach (IntPtr p in strings) Marshal.FreeHGlobal(p); Marshal.FreeHGlobal(block); if (connectionPointer != IntPtr.Zero) Marshal.FreeHGlobal(connectionPointer); }
        }
        public IList<string> GetConnections()
        {
            var result = new List<string> { null }; // null is the LAN connection.
            int size = Marshal.SizeOf(typeof(RasEntry));
            int bytes = 0, count;
            uint code = RasEnumEntries(null, null, IntPtr.Zero, ref bytes, out count);
            if (code == 0 && count == 0) return result;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (code != 603) throw new Win32Exception((int)code, "Could not enumerate Windows dial-up/VPN connections.");
                if (bytes < size || bytes > size * 1023) throw new IOException("Invalid RAS connection count.");
                IntPtr memory = Marshal.AllocHGlobal(bytes);
                try
                {
                    int capacity = bytes / size;
                    for (int i = 0; i < capacity; i++) Marshal.StructureToPtr(new RasEntry { Size = size, Name = "", Phonebook = "" }, IntPtr.Add(memory, i * size), false);
                    code = RasEnumEntries(null, null, memory, ref bytes, out count);
                    if (code == 603) continue;
                    if (code != 0) throw new Win32Exception((int)code, "Could not enumerate Windows dial-up/VPN connections.");
                    if (count < 0 || count > capacity) throw new IOException("Invalid RAS connection result.");
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < count; i++)
                    {
                        string name = ((RasEntry)Marshal.PtrToStructure(IntPtr.Add(memory, i * size), typeof(RasEntry))).Name;
                        if (!String.IsNullOrEmpty(name) && seen.Add(name)) result.Add(name);
                    }
                    return result;
                }
                finally { Marshal.FreeHGlobal(memory); }
            }
            throw new IOException("Windows connection list changed repeatedly; try again.");
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct RasEntry
        {
            public int Size;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)] public string Name;
            public int Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 261)] public string Phonebook;
        }
        [DllImport("rasapi32.dll", EntryPoint = "RasEnumEntriesW", CharSet = CharSet.Unicode)]
        private static extern uint RasEnumEntries(string reserved, string phonebook, IntPtr entries, ref int bytes, out int count);
        [DllImport("wininet.dll", EntryPoint = "InternetQueryOptionW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool InternetQueryOption(IntPtr internet, int option, ref Options data, ref int size);
        [DllImport("wininet.dll", EntryPoint = "InternetSetOptionW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool InternetSetOption(IntPtr internet, int option, ref Options data, int size);
        [DllImport("wininet.dll", EntryPoint = "InternetSetOptionW", SetLastError = true)] private static extern bool InternetSetOptionNotify(IntPtr internet, int option, IntPtr data, int size);
        [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr memory);
    }
}
