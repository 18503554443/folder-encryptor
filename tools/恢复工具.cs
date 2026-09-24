using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

class RestoreNames
{
    const uint GENERIC_READ = 0x80000000;
    const uint SHARE = 3;
    const uint OPEN_EXISTING = 3;
    const uint FSCTL_QUERY_USN_JOURNAL = 0x000900F4;
    const uint FSCTL_READ_USN_JOURNAL = 0x000900BB;
    const uint FSCTL_ENUM_USN_DATA = 0x000900B3;
    const uint R_OLD = 0x00001000, R_NEW = 0x00002000;
    const ulong MASK = 0x0000FFFFFFFFFFFF;

    [StructLayout(LayoutKind.Sequential)] struct JD { public ulong Id; public long First, Next, Low, Max; public ulong MaxSize, Alloc; }
    [StructLayout(LayoutKind.Sequential)] struct RD { public long StartUsn; public uint Mask, OnlyClose; public ulong Timeout, Wait, Id; }
    [StructLayout(LayoutKind.Sequential)] struct MD { public ulong StartFrn; public long Low, High; }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr CreateFile(string n, uint a, uint s, IntPtr sec, uint d, uint f, IntPtr t);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DeviceIoControl(IntPtr h, uint c, ref RD i, int isz, byte[] o, int osz, out int br, IntPtr ov);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DeviceIoControl(IntPtr h, uint c, ref MD i, int isz, byte[] o, int osz, out int br, IntPtr ov);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DeviceIoControl(IntPtr h, uint c, IntPtr i, int isz, ref JD o, int osz, out int br, IntPtr ov);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

    static string LogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "恢复日志.txt");
    static void Say(string m)
    {
        Console.WriteLine(m);
        try { File.AppendAllText(LogPath, m + Environment.NewLine, Encoding.UTF8); } catch { }
    }

    static Dictionary<ulong, string[]> nodes = new Dictionary<ulong, string[]>();  // frn -> [name, parentFrn]
    static Dictionary<string, string> nameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // 容器名 -> 原名

    static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        try { File.WriteAllText(LogPath, "恢复 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine, Encoding.UTF8); } catch { }
        bool dry = args.Length > 0 && args[0] == "-dry";
        string root = (args.Length > 1 ? args[1] : @"C:\下载").TrimEnd('\\');
        string container = Path.Combine(root, ".ilock", "data");
        string fallback = Path.Combine(root, "_已恢复文件名");

        IntPtr h = CreateFile(@"\\.\C:", GENERIC_READ, SHARE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (h == (IntPtr)(-1)) { Say("需要管理员权限运行（Win32=" + Marshal.GetLastWin32Error() + "）"); return; }
        try
        {
            JD jd = new JD(); int br;
            if (!DeviceIoControl(h, FSCTL_QUERY_USN_JOURNAL, IntPtr.Zero, 0, ref jd, Marshal.SizeOf(typeof(JD)), out br, IntPtr.Zero))
            { Say("查询日志失败"); return; }

            RD rd = new RD(); rd.StartUsn = jd.First; rd.Mask = 0xFFFFFFFF; rd.Id = jd.Id;
            byte[] buf = new byte[1 << 20];
            Dictionary<ulong, string[]> pending = new Dictionary<ulong, string[]>();
            Regex hex = new Regex("^[0-9a-f]{40}$");
            while (true)
            {
                int ret;
                if (!DeviceIoControl(h, FSCTL_READ_USN_JOURNAL, ref rd, Marshal.SizeOf(typeof(RD)), buf, buf.Length, out ret, IntPtr.Zero)) break;
                if (ret <= 8) break;
                int pos = 8;
                while (pos + 60 <= ret)
                {
                    int len = BitConverter.ToInt32(buf, pos);
                    if (len < 60 || pos + len > ret) break;
                    ushort major = BitConverter.ToUInt16(buf, pos + 4);
                    ulong frn = BitConverter.ToUInt64(buf, pos + 8) & MASK;
                    ulong par = BitConverter.ToUInt64(buf, pos + 16) & MASK;
                    uint reason = BitConverter.ToUInt32(buf, pos + 40);
                    ushort nl = BitConverter.ToUInt16(buf, pos + 56);
                    ushort no = BitConverter.ToUInt16(buf, pos + 58);
                    if ((major == 2 || major == 3) && no + nl <= len)
                    {
                        string nm = Encoding.Unicode.GetString(buf, pos + no, nl);
                        if (!nodes.ContainsKey(frn)) nodes[frn] = new string[] { nm, par.ToString() };
                        bool o = (reason & R_OLD) != 0, n = (reason & R_NEW) != 0;
                        if (o && !n) pending[frn] = new string[] { nm, par.ToString() };
                        else if (n && !o)
                        {
                            string[] old;
                            if (pending.TryGetValue(frn, out old) && hex.IsMatch(nm)) nameMap[nm] = old[0] + "|" + old[1];
                            pending.Remove(frn);
                        }
                    }
                    pos += len;
                }
                long nx = BitConverter.ToInt64(buf, 0);
                if (nx <= rd.StartUsn) break;
                rd.StartUsn = nx;
                if (nx >= jd.Next) break;
            }
            Say("日志:容器文件映射 " + nameMap.Count + " 个");

            MD md = new MD(); md.StartFrn = 0; md.High = jd.Next;
            while (true)
            {
                int ret;
                if (!DeviceIoControl(h, FSCTL_ENUM_USN_DATA, ref md, Marshal.SizeOf(typeof(MD)), buf, buf.Length, out ret, IntPtr.Zero)) break;
                if (ret <= 8) break;
                int pos = 8;
                while (pos + 60 <= ret)
                {
                    int len = BitConverter.ToInt32(buf, pos);
                    if (len < 60 || pos + len > ret) break;
                    ulong frn = BitConverter.ToUInt64(buf, pos + 8) & MASK;
                    ulong par = BitConverter.ToUInt64(buf, pos + 16) & MASK;
                    ushort nl = BitConverter.ToUInt16(buf, pos + 56);
                    ushort no = BitConverter.ToUInt16(buf, pos + 58);
                    if (no + nl <= len && !nodes.ContainsKey(frn))
                        nodes[frn] = new string[] { Encoding.Unicode.GetString(buf, pos + no, nl), par.ToString() };
                    pos += len;
                }
                ulong nx = BitConverter.ToUInt64(buf, 0);
                if (nx == md.StartFrn) break;
                md.StartFrn = nx;
            }
            Say("节点(含日志补全): " + nodes.Count);
        }
        finally { CloseHandle(h); }

        if (!Directory.Exists(container)) { Say("找不到 " + container); return; }
        int restored = 0, fallbackCount = 0, unknown = 0;
        StringBuilder csv = new StringBuilder("容器文件名,原文件名,大小MB,恢复位置\r\n");
        foreach (string f in Directory.GetFiles(container))
        {
            string key = Path.GetFileName(f);
            string mapped;
            if (!nameMap.TryGetValue(key, out mapped)) { unknown++; continue; }
            string[] parts2 = mapped.Split('|');
            string orig = parts2[0];
            ulong pf = 0;
            if (parts2.Length > 1) ulong.TryParse(parts2[1], out pf);
            long size = new FileInfo(f).Length;
            string dir;
            string resolved = FullPath(pf);
            if (resolved != null)
            {
                int ai = resolved.LastIndexOf("\\迅雷下载\\", StringComparison.OrdinalIgnoreCase);
                if (ai >= 0) resolved = root + "\\" + resolved.Substring(ai + 1);
                string anchor = Path.Combine(root, resolved.Substring(root.Length + 1).Split('\\')[0]);
                if (!Directory.Exists(anchor)) resolved = null;
            }
            if (resolved != null) { dir = resolved; } else { dir = fallback; fallbackCount++; }
            if (!Directory.Exists(dir)) { if (!dry) Directory.CreateDirectory(dir); }
            int n = 1;
            string final = Path.Combine(dir, orig);
            while (File.Exists(final))
            {
                final = Path.Combine(dir, Path.GetFileNameWithoutExtension(orig) + "_" + n + Path.GetExtension(orig));
                n++;
            }
            try
            {
                if (!dry) { try { File.SetAttributes(f, FileAttributes.Normal); } catch { } File.Move(f, final); }
                restored++;
                csv.Append(key).Append(',').Append(orig.Replace(",", " ")).Append(',').Append(Math.Round(size / 1048576.0, 1)).Append(',').Append(final).Append("\r\n");
            }
            catch (Exception ex) { Say("  失败 " + key + " : " + ex.Message); }
        }
        try { File.WriteAllText(Path.Combine(root, "恢复清单.csv"), csv.ToString(), Encoding.UTF8); } catch { }
        Say("");
        Say((dry ? "[试运行] " : "") + "按原名恢复: " + restored + "，无法识别: " + unknown);
        Say("清单: " + Path.Combine(root, "恢复清单.csv"));
    }

        static string FullPath(ulong frn)
        {
            if (frn == 0) return null;
            List<string> parts = new List<string>();
            ulong cur = frn;
            int guard = 0;
            while (guard++ < 64)
            {
                if (cur == 5) break;                 // 根目录：先判断再查表
                string[] e;
                if (!nodes.TryGetValue(cur, out e)) return null;
                parts.Add(e[0]);
                ulong p2 = 0;
                ulong.TryParse(e[1], out p2);
                if (p2 == cur) return null;
                cur = p2;
            }
            parts.Reverse();
            return @"C:\" + string.Join("\\", parts.ToArray());
        }
    }



