using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace InstantLock
{
    public class Session
    {
        public string Folder;
        public Strength Strength;
        public byte[] KEnc;
        public byte[] KMac;
        public byte[] DataKey;
        public DateTime Created;
        public string Id
        {
            get
            {
                using (SHA1 sha = SHA1.Create())
                {
                    byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(Folder.ToLowerInvariant()));
                    return Crypto.BytesToHex(h).Substring(0, 16);
                }
            }
        }
    }

    public static class SessionStore
    {
        public static string Root
        {
            get
            {
                string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "InstantLock", "sessions");
                Directory.CreateDirectory(d);
                return d;
            }
        }

        public static string PathFor(string folder)
        {
            using (SHA1 sha = SHA1.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(folder.ToLowerInvariant()));
                return Path.Combine(Root, Crypto.BytesToHex(h).Substring(0, 16) + ".dat");
            }
        }

        public static void Save(Session s)
        {
            string text = s.Folder + "\n" + (int)s.Strength + "\n" + Crypto.BytesToHex(s.KEnc) + "\n" + Crypto.BytesToHex(s.KMac) + "\n" + s.Created.ToString("o", CultureInfo.InvariantCulture) + "\n" + Crypto.BytesToHex(s.DataKey);
            byte[] blob = Security.Protect(Encoding.UTF8.GetBytes(text));
            File.WriteAllBytes(PathFor(s.Folder), blob);
            try { File.SetAttributes(PathFor(s.Folder), FileAttributes.Hidden); } catch { }
        }

        public static Session Load(string folder)
        {
            string p = PathFor(folder);
            if (!File.Exists(p)) return null;
            try
            {
                string text = Encoding.UTF8.GetString(Security.Unprotect(File.ReadAllBytes(p)));
                string[] lines = text.Split('\n');
                Session s = new Session();
                s.Folder = lines[0];
                s.Strength = (Strength)int.Parse(lines[1], CultureInfo.InvariantCulture);
                s.KEnc = Crypto.HexToBytes(lines[2]);
                s.KMac = Crypto.HexToBytes(lines[3]);
                s.Created = DateTime.Parse(lines[4], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                if (lines.Length > 5) s.DataKey = Crypto.HexToBytes(lines[5]);
                return s;
            }
            catch { return null; }
        }

        public static void Remove(string folder)
        {
            string p = PathFor(folder);
            if (File.Exists(p))
            {
                try { File.SetAttributes(p, FileAttributes.Normal); } catch { }
                try { File.Delete(p); } catch { }
            }
        }

        public static List<Session> All()
        {
            List<Session> list = new List<Session>();
            foreach (string f in Directory.GetFiles(Root, "*.dat"))
            {
                try
                {
                    string text = Encoding.UTF8.GetString(Security.Unprotect(File.ReadAllBytes(f)));
                    string[] lines = text.Split('\n');
                    Session s = new Session();
                    s.Folder = lines[0];
                    s.Strength = (Strength)int.Parse(lines[1], CultureInfo.InvariantCulture);
                    s.KEnc = Crypto.HexToBytes(lines[2]);
                    s.KMac = Crypto.HexToBytes(lines[3]);
                    s.Created = DateTime.Parse(lines[4], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                    if (lines.Length > 5) s.DataKey = Crypto.HexToBytes(lines[5]);
                    list.Add(s);
                }
                catch { }
            }
            return list;
        }
    }

    public static class Locker
    {
        public const int HeaderBytes = 4096;
        private const int ChunkSize = 1024 * 1024;

        public static List<string> Lock(string folder, string password, Strength strength, string hint, string email, bool hideFolder, Action<ProgressInfo> report, Func<bool> cancel)
        {
            folder = Path.GetFullPath(folder).TrimEnd('\\');
            if (!Directory.Exists(folder)) throw new LockException("待加密的文件夹不存在。");
            if (Vault.IsLocked(folder)) throw new LockException("该文件夹已经处于加密状态。");
            if (IsAncestorOfSelf(folder)) throw new LockException("不允许加密本程序的上级目录。");

            byte[] salt = Crypto.RandomBytes(16);
            byte[] kEnc, kMac, kVer;
            Crypto.DeriveKeys(password, salt, 60000, out kEnc, out kMac, out kVer);

            string container = Vault.ContainerPath(folder);
            string data = Vault.DataPath(folder);
            EnsureDirectory(data, true);

            // 预检：容器必须可写，否则直接拒绝（常见原因是杀毒软件拦截隐藏容器）
            string probe = Path.Combine(data, ".probe");
            try
            {
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
            }
            catch (Exception ex)
            {
                throw new LockException("无法写入加密容器，可能被杀毒软件拦截：\r\n" + data + "\r\n" + ex.Message, ex);
            }

            List<string> files = EnumerateFiles(folder, container);
            List<string> dirs = EnumerateDirs(folder, container);
            Vault v = new Vault();
            v.DataKey = Crypto.RandomBytes(32);
            v.Salt = salt;
            v.Strength = strength;
            v.Verifier = Crypto.Hmac(kVer, Encoding.ASCII.GetBytes("ilock-verify-v1"));
            v.Hint = hint == null ? "" : hint;
            v.Email = email == null ? "" : email;
            v.HeaderEncrypted = true;
            v.Created = DateTime.Now.ToString("o", CultureInfo.InvariantCulture);

            // 阶段一：先把全部文件的登记表写进容器，保证中途中断也能完整恢复
            foreach (string f in files)
            {
                FileInfo fi = new FileInfo(f);
                Entry e = new Entry();
                e.Rel = f.Substring(folder.Length + 1);
                e.Cipher = Crypto.NewCipherName();
                e.Size = fi.Length;
                e.Nonce = Crypto.RandomBytes(16);
                v.Entries.Add(e);
            }
            VaultIO.Write(folder, v, kEnc, kMac);

            // 阶段二：逐个搬运并加密文件头；单个文件失败只跳过它，不再中断整体
            List<string> failed = new List<string>();
            List<Entry> done = new List<Entry>();
            int idx = 0;
            foreach (string f in files)
            {
                if (cancel != null && cancel()) break;
                Entry e = v.Entries[idx];
                string dst = Path.Combine(data, e.Cipher);
                try
                {
                    try { File.SetAttributes(f, FileAttributes.Normal); } catch { }
                    File.Move(f, dst);
                    ProcessFile(dst, e, v.DataKey, strength, report);
                    done.Add(e);
                    idx++;
                }
                catch (Exception ex)
                {
                    failed.Add(e.Rel + "  ——  " + ex.Message);
                    try { if (File.Exists(dst) && !File.Exists(f)) File.Move(dst, f); } catch { }
                    idx++;
                }
                if (report != null && files.Count > 0)
                {
                    ProgressInfo pi = new ProgressInfo();
                    pi.CurrentFile = e.Rel; pi.FileIndex = idx; pi.FileCount = files.Count;
                    pi.TotalPercent = 100.0 * idx / files.Count;
                    pi.Phase = "加密";
                    report(pi);
                }
            }

            dirs.Sort(delegate(string a, string b) { return b.Length.CompareTo(a.Length); });
            foreach (string d in dirs)
            {
                try
                {
                    if (Directory.Exists(d) && Directory.GetFileSystemEntries(d).Length == 0)
                    {
                        Entry de = new Entry();
                        de.IsDir = true;
                        de.Rel = d.Substring(folder.Length + 1);
                        de.Cipher = "";
                        de.Nonce = new byte[16];
                        v.Entries.Add(de);
                        Directory.Delete(d);
                    }
                }
                catch { }
            }

            // 阶段三：把失败项从登记表移除后重写，保证清单与容器内容一致
            List<Entry> keep = new List<Entry>();
            foreach (Entry x in v.Entries) if (x.IsDir || done.Contains(x)) keep.Add(x);
            v.Entries = keep;
            VaultIO.Write(folder, v, kEnc, kMac);
            ApplyLockLook(folder, hideFolder);
            return failed;
        }

        public static string SelfFile
        {
            get
            {
                try { return Path.GetFullPath(System.Reflection.Assembly.GetExecutingAssembly().Location); }
                catch { return null; }
            }
        }

        public static string SelfDir
        {
            get
            {
                string f = SelfFile;
                return f == null ? null : Path.GetDirectoryName(f);
            }
        }

        // 只禁止整包加密「程序的上级目录」；程序自己所在的目录允许（默认场景）
        private static bool IsAncestorOfSelf(string folder)
        {
            try
            {
                string dir = SelfDir;
                if (string.IsNullOrEmpty(dir)) return false;
                dir = Path.GetFullPath(dir).TrimEnd('\\');
                if (dir.Equals(folder, StringComparison.OrdinalIgnoreCase)) return false;
                return dir.StartsWith(folder + "\\", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private static bool IsSelfFile(string path)
        {
            try
            {
                string self = SelfFile;
                if (string.IsNullOrEmpty(self)) return false;
                return string.Equals(Path.GetFullPath(path), self, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private static void ProcessFile(string path, Entry e, byte[] kEnc, Strength strength, Action<ProgressInfo> report)
        {
            byte[] fileKey = Crypto.FileKey(kEnc, e.Nonce);
            long len = new FileInfo(path).Length;
            e.Size = len;
            if (strength == Strength.Instant)
            {
                int hl = (int)Math.Min(HeaderBytes, len);
                if (hl <= 0) return;
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    byte[] buf = new byte[hl];
                    int got = 0;
                    while (got < hl)
                    {
                        int r = fs.Read(buf, got, hl - got);
                        if (r <= 0) break;
                        got += r;
                    }
                    Crypto.AesCtrXor(fileKey, e.Nonce, 0, buf, 0, got);
                    fs.Position = 0;
                    fs.Write(buf, 0, got);
                    fs.Flush(true);
                }
                e.Done = hl;
                e.Mac = "";
            }
            else
            {
                long pos = 0;
                byte[] buf = new byte[ChunkSize];
                HMACSHA256 mac = new HMACSHA256(Crypto.FileMacKey(kEnc));
                mac.Initialize();
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    while (pos < len)
                    {
                        int want = (int)Math.Min(ChunkSize, len - pos);
                        fs.Position = pos;
                        int r = fs.Read(buf, 0, want);
                        if (r <= 0) break;
                        Crypto.AesCtrXor(fileKey, e.Nonce, pos / 16, buf, 0, r);
                        fs.Position = pos;
                        fs.Write(buf, 0, r);
                        mac.TransformBlock(buf, 0, r, null, 0);
                        pos += r;
                        e.Done = pos;
                        if (report != null && (ChunkSize == 0 || pos % (8 * ChunkSize) < ChunkSize))
                        {
                            ProgressInfo pch = new ProgressInfo();
                            pch.CurrentFile = e.Rel;
                            pch.Phase = "加密";
                            pch.CurrentPercent = len <= 0 ? 100 : (100.0 * pos / len);
                            report(pch);
                        }
                    }
                    fs.Flush(true);
                }
                mac.TransformFinalBlock(new byte[0], 0, 0);
                e.Mac = Crypto.BytesToHex(mac.Hash);
                mac.Dispose();
            }
        }

        public static void Unlock(string folder, string password, Action<ProgressInfo> report, Func<bool> cancel)
        {
            folder = Path.GetFullPath(folder).TrimEnd('\\');
            byte[] kEnc, kMac;
            Vault v = VaultIO.Open(folder, password, out kEnc, out kMac);
            UnlockInternal(folder, v, kEnc, kMac, report, cancel);
            CleanupAfterUnlock(folder, true);
            SessionStore.Remove(folder);
        }

        private static void UnlockInternal(string folder, Vault v, byte[] kEnc, byte[] kMac, Action<ProgressInfo> report, Func<bool> cancel)
        {
            string data = Vault.DataPath(folder);
            List<Entry> files = new List<Entry>();
            List<Entry> dirs = new List<Entry>();
            foreach (Entry e in v.Entries) { if (e.IsDir) dirs.Add(e); else files.Add(e); }

            int idx = 0;
            foreach (Entry e in files)
            {
                if (cancel != null && cancel()) throw new LockException("操作已取消。");
                string src = Path.Combine(data, e.Cipher);
                string dst = Path.Combine(folder, e.Rel);
                if (File.Exists(src))
                {
                    byte[] fileKey = Crypto.FileKey(v.DataKey, e.Nonce);
                    if (v.Strength == Strength.Instant)
                    {
                        using (FileStream fs = new FileStream(src, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                        {
                            int hl = (int)Math.Min(HeaderBytes, fs.Length);
                            if (hl > 0)
                            {
                                byte[] buf = new byte[hl];
                                int got = 0;
                                while (got < hl)
                                {
                                    int r = fs.Read(buf, got, hl - got);
                                    if (r <= 0) break;
                                    got += r;
                                }
                                Crypto.AesCtrXor(fileKey, e.Nonce, 0, buf, 0, got);
                                fs.Position = 0;
                                fs.Write(buf, 0, got);
                                fs.Flush(true);
                            }
                        }
                    }
                    else
                    {
                        byte[] buf = new byte[ChunkSize];
                        HMACSHA256 mac = new HMACSHA256(Crypto.FileMacKey(v.DataKey));
                        long pos = 0;
                        long len = new FileInfo(src).Length;
                        using (FileStream fs = new FileStream(src, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                        {
                            while (pos < len)
                            {
                                int want = (int)Math.Min(ChunkSize, len - pos);
                                fs.Position = pos;
                                int r = fs.Read(buf, 0, want);
                                if (r <= 0) break;
                                mac.TransformBlock(buf, 0, r, null, 0);
                                Crypto.AesCtrXor(fileKey, e.Nonce, pos / 16, buf, 0, r);
                                fs.Position = pos;
                                fs.Write(buf, 0, r);
                                pos += r;
                            }
                            fs.Flush(true);
                        }
                        mac.TransformFinalBlock(new byte[0], 0, 0);
                        string got = Crypto.BytesToHex(mac.Hash);
                        mac.Dispose();
                        if (!string.IsNullOrEmpty(e.Mac) && got != e.Mac)
                            throw new LockException("文件完整性校验失败: " + e.Rel);
                    }
                    string dir = Path.GetDirectoryName(dst);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    if (File.Exists(dst)) { try { File.SetAttributes(dst, FileAttributes.Normal); } catch { } File.Delete(dst); }
                    File.Move(src, dst);
                }
                idx++;
                if (report != null)
                {
                    ProgressInfo pi = new ProgressInfo();
                    pi.CurrentFile = e.Rel; pi.FileIndex = idx; pi.FileCount = files.Count;
                    pi.TotalPercent = files.Count == 0 ? 100 : (100.0 * idx / files.Count);
                    pi.Phase = "解密";
                    report(pi);
                }
            }

            foreach (Entry d in dirs)
            {
                try
                {
                    string p = Path.Combine(folder, d.Rel);
                    if (!Directory.Exists(p)) Directory.CreateDirectory(p);
                }
                catch { }
            }
        }

        public static Session TempUnlock(string folder, string password, Action<ProgressInfo> report, Func<bool> cancel)
        {
            folder = Path.GetFullPath(folder).TrimEnd('\\');
            byte[] kEnc, kMac;
            Vault v = VaultIO.Open(folder, password, out kEnc, out kMac);
            UnlockInternal(folder, v, kEnc, kMac, report, cancel);
            Session s = new Session();
            s.Folder = folder;
            s.Strength = v.Strength;
            s.DataKey = v.DataKey;
            s.KEnc = kEnc;
            s.KMac = kMac;
            s.Created = DateTime.Now;
            SessionStore.Save(s);
            try { File.WriteAllText(Path.Combine(Vault.ContainerPath(folder), "temp"), s.Id); } catch { }
            CleanupAfterUnlock(folder, false);
            return s;
        }

        public static void Relock(Session s, Action<ProgressInfo> report, Func<bool> cancel)
        {
            string folder = s.Folder;
            byte[] iv, cp, mac, body;
            Vault v = VaultIO.ReadHeader(folder, out iv, out cp, out mac, out body);
            v.HeaderEncrypted = true;
            string data = Vault.DataPath(folder);
            EnsureDirectory(data, true);

            List<string> present = EnumerateFiles(folder, Vault.ContainerPath(folder));
            int idx = 0;
            foreach (string f in present)
            {
                if (cancel != null && cancel()) throw new LockException("操作已取消。");
                string rel = f.Substring(folder.Length + 1);
                Entry e = FindByRel(v, rel);
                if (e == null)
                {
                    e = new Entry();
                    e.Rel = rel;
                    e.Cipher = Crypto.NewCipherName();
                    e.Nonce = Crypto.RandomBytes(16);
                    v.Entries.Add(e);
                }
                e.IsDir = false;
                string dst = Path.Combine(data, e.Cipher);
                if (File.Exists(dst)) { try { File.SetAttributes(dst, FileAttributes.Normal); } catch { } File.Delete(dst); }
                File.Move(f, dst);
                ProcessFile(dst, e, s.DataKey, s.Strength, report);
                if (report != null)
                {
                    ProgressInfo pi = new ProgressInfo();
                    pi.CurrentFile = rel; pi.FileIndex = idx + 1; pi.FileCount = present.Count;
                    pi.TotalPercent = present.Count == 0 ? 100 : (100.0 * (idx + 1) / present.Count);
                    pi.Phase = "恢复加密";
                    report(pi);
                }
                idx++;
            }

            List<string> relockDirs = EnumerateDirs(folder, Vault.ContainerPath(folder));
            relockDirs.Sort(delegate(string a, string b) { return b.Length.CompareTo(a.Length); });
            foreach (string d in relockDirs)
            {
                try
                {
                    if (Directory.Exists(d) && Directory.GetFileSystemEntries(d).Length == 0)
                    {
                        if (FindByRel(v, d.Substring(folder.Length + 1)) == null)
                        {
                            Entry de = new Entry();
                            de.IsDir = true;
                            de.Rel = d.Substring(folder.Length + 1);
                            de.Nonce = new byte[16];
                            v.Entries.Add(de);
                        }
                        Directory.Delete(d);
                    }
                }
                catch { }
            }

            VaultIO.Write(folder, v, s.KEnc, s.KMac);
            SessionStore.Remove(folder);
            try { File.Delete(Path.Combine(Vault.ContainerPath(folder), "temp")); } catch { }
            ApplyLockLook(folder, AppConfig.Current.HideFolder);
        }

        private static Entry FindByRel(Vault v, string rel)
        {
            foreach (Entry e in v.Entries) if (!e.IsDir && string.Equals(e.Rel, rel, StringComparison.OrdinalIgnoreCase)) return e;
            return null;
        }

        public static void ApplyLockLook(string folder, bool hide)
        {
            try
            {
                string container = Vault.ContainerPath(folder);
                File.SetAttributes(container, FileAttributes.Hidden | FileAttributes.System);
                string data = Vault.DataPath(folder);
                if (Directory.Exists(data)) File.SetAttributes(data, FileAttributes.Hidden | FileAttributes.System);
                string vault = Vault.VaultPath(folder);
                if (File.Exists(vault)) File.SetAttributes(vault, FileAttributes.Hidden | FileAttributes.System);
            }
            catch { }
            try
            {
                FileAttributes a = File.GetAttributes(folder);
                a |= FileAttributes.System;
                if (hide) a |= FileAttributes.Hidden; else a &= ~FileAttributes.Hidden;
                File.SetAttributes(folder, a);
                WriteDesktopIni(folder, true);
            }
            catch { }
        }

        public static void CleanupAfterUnlock(string folder, bool complete)
        {
            try
            {
                if (complete)
                {
                    string container = Vault.ContainerPath(folder);
                    if (Directory.Exists(container))
                    {
                        try { File.SetAttributes(container, FileAttributes.Normal); } catch { }
                        DeleteTree(container);
                    }
                    // 同时清掉加密时复制进来的“文件夹解密.exe”（只删和我们自己完全一致的那份）
                    try
                    {
                        string unlocker = Path.Combine(folder, "文件夹解密.exe");
                        if (File.Exists(unlocker))
                        {
                            File.SetAttributes(unlocker, FileAttributes.Normal);
                            string self = SelfFile;
                            bool same = false;
                            if (!string.IsNullOrEmpty(self) && File.Exists(self))
                            {
                                using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
                                using (FileStream fa = File.OpenRead(unlocker))
                                using (FileStream fb = File.OpenRead(self))
                                    same = BitConverter.ToString(sha.ComputeHash(fa)) == BitConverter.ToString(sha.ComputeHash(fb));
                            }
                            if (same)
                            {
                                try { File.Delete(unlocker); }
                                catch
                                {
                                    // 正在运行的程序无法自删：交给后台命令等本进程退出后再删
                                    try
                                    {
                                        System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo(
                                            "cmd.exe", "/c ping -n 3 127.0.0.1 >nul & del /f /q \"" + unlocker + "\"");
                                        psi.CreateNoWindow = true;
                                        psi.UseShellExecute = false;
                                        System.Diagnostics.Process.Start(psi);
                                    }
                                    catch { }
                                }
                            }
                        }
                    }
                    catch { }

                    string di = Path.Combine(folder, "desktop.ini");
                    if (File.Exists(di))
                    {
                        try { File.SetAttributes(di, FileAttributes.Normal); } catch { }
                        File.Delete(di);
                    }
                    FileAttributes a = File.GetAttributes(folder);
                    a &= ~(FileAttributes.System | FileAttributes.Hidden);
                    File.SetAttributes(folder, a);
                }
                else
                {
                    string data = Vault.DataPath(folder);
                    if (Directory.Exists(data))
                    {
                        foreach (string f in Directory.GetFiles(data))
                        {
                            try { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); } catch { }
                        }
                    }
                }
            }
            catch { }
        }

        private static void WriteDesktopIni(string folder, bool encrypted)
        {
            try
            {
                string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
                string name = "文件夹解密.exe";
                if (!File.Exists(Path.Combine(folder, name))) name = Path.GetFileName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                string ini = "[.ShellClassInfo]\r\nIconResource=" + name + ",0\r\n";
                if (encrypted) { }
                string path = Path.Combine(folder, "desktop.ini");
                using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    byte[] b = Encoding.Default.GetBytes(ini);
                    fs.Write(b, 0, b.Length);
                }
                File.SetAttributes(path, FileAttributes.Hidden | FileAttributes.System);
            }
            catch { }
        }

        public static void DeleteTree(string dir)
        {
            try
            {
                foreach (string f in Directory.GetFiles(dir))
                {
                    try { File.SetAttributes(f, FileAttributes.Normal); } catch { }
                }
                foreach (string d in Directory.GetDirectories(dir))
                {
                    try { File.SetAttributes(d, FileAttributes.Normal); } catch { }
                    DeleteTree(d);
                }
                Directory.Delete(dir, true);
            }
            catch { }
        }

        public static void EnsureDirectory(string dir, bool hidden)
        {
            Directory.CreateDirectory(dir);
            if (hidden) { try { File.SetAttributes(dir, FileAttributes.Hidden | FileAttributes.System); } catch { } }
        }

        public static List<string> EnumerateFiles(string root, string skipPrefix)
        {
            List<string> list = new List<string>();
            Walk(root, skipPrefix, list, null);
            return list;
        }

        public static List<string> EnumerateDirs(string root, string skipPrefix)
        {
            List<string> list = new List<string>();
            Walk(root, skipPrefix, null, list);
            return list;
        }

        private static void Walk(string dir, string skipPrefix, List<string> files, List<string> dirs)
        {
            string[] sub;
            try { sub = Directory.GetDirectories(dir); }
            catch { return; }
            foreach (string d in sub)
            {
                if (skipPrefix != null && d.StartsWith(skipPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                if (dirs != null) dirs.Add(d);
                Walk(d, skipPrefix, files, dirs);
            }
            string[] fs;
            try { fs = Directory.GetFiles(dir); }
            catch { return; }
            foreach (string f in fs)
            {
                if (skipPrefix != null && f.StartsWith(skipPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                string nm = Path.GetFileName(f);
                if (string.Equals(nm, "desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                if (IsSelfFile(f)) continue;
                if (files != null) files.Add(f);
            }
        }

        public static void CopyUnlockerInto(string folder)
        {
            try
            {
                string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
                string dst = Path.Combine(folder, "文件夹解密.exe");
                if (string.Equals(exe, dst, StringComparison.OrdinalIgnoreCase)) return;
                File.Copy(exe, dst, true);
                File.SetAttributes(dst, FileAttributes.Hidden | FileAttributes.System);
            }
            catch { }
        }
    }
}











