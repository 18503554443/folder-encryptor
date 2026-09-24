using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace InstantLock
{
    public enum Strength { Instant = 0, Full = 1 }

    public class Entry
    {
        public string Rel;
        public string Cipher;
        public long Size;
        public byte[] Nonce;
        public long Done;
        public string Mac;
        public bool IsDir;
    }

    public class Vault
    {
        public const string ContainerDir = ".ilock";
        public const string VaultFile = "vault.dat";
        public const string PayloadMagic = "ILOCK1";

        public int Version = 1;
        public Strength Strength;
        public byte[] Salt = new byte[16];
        public int Iterations = 60000;
        public byte[] Verifier = new byte[32];
        public string Hint = "";
        public string Email = "";
        public byte[] DataKey = new byte[32];
        public bool HeaderEncrypted;
        public string Created = "";
        public List<Entry> Entries = new List<Entry>();

        public static string ContainerPath(string folder) { return Path.Combine(folder, ContainerDir); }
        public static string VaultPath(string folder) { return Path.Combine(ContainerPath(folder), VaultFile); }
        public static string DataPath(string folder) { return Path.Combine(ContainerPath(folder), "data"); }

        public static bool IsLocked(string folder)
        {
            try { return File.Exists(VaultPath(folder)); }
            catch { return false; }
        }
    }

    public class ProgressInfo
    {
        public string CurrentFile = "";
        public int FileIndex;
        public int FileCount;
        public double TotalPercent;
        public double CurrentPercent;
        public string Phase = "";
    }

    public class LockException : Exception
    {
        public LockException(string msg) : base(msg) { }
        public LockException(string msg, Exception inner) : base(msg, inner) { }
    }

    public static class Crypto
    {
        public static void DeriveKeys(string password, byte[] salt, int iterations, out byte[] kEnc, out byte[] kMac, out byte[] kVer)
        {
            using (Rfc2898DeriveBytes d = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256))
            {
                kEnc = d.GetBytes(32);
                kMac = d.GetBytes(32);
                kVer = d.GetBytes(32);
            }
        }

        public static byte[] Hmac(byte[] key, byte[] data)
        {
            using (HMACSHA256 h = new HMACSHA256(key)) { return h.ComputeHash(data); }
        }

        public static byte[] Hmac(byte[] key, params byte[][] parts)
        {
            using (HMACSHA256 h = new HMACSHA256(key))
            {
                h.Initialize();
                foreach (byte[] p in parts) { if (p != null && p.Length > 0) h.TransformBlock(p, 0, p.Length, null, 0); }
                h.TransformFinalBlock(new byte[0], 0, 0);
                return h.Hash;
            }
        }

        public static bool FixedEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        public static byte[] RandomBytes(int n)
        {
            byte[] b = new byte[n];
            using (RNGCryptoServiceProvider r = new RNGCryptoServiceProvider()) { r.GetBytes(b); }
            return b;
        }

        public static string NewCipherName()
        {
            byte[] b = RandomBytes(20);
            StringBuilder sb = new StringBuilder(40);
            for (int i = 0; i < b.Length; i++) sb.Append(b[i].ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        public static byte[] HexToBytes(string s)
        {
            if (s == null) return new byte[0];
            byte[] r = new byte[s.Length / 2];
            for (int i = 0; i < r.Length; i++) r[i] = byte.Parse(s.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return r;
        }

        public static string BytesToHex(byte[] b)
        {
            StringBuilder sb = new StringBuilder(b.Length * 2);
            for (int i = 0; i < b.Length; i++) sb.Append(b[i].ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        public static byte[] FileKey(byte[] dataKey, byte[] nonce)
        {
            return Hmac(dataKey, Encoding.ASCII.GetBytes("ilock-file-key"), nonce);
        }

        public static byte[] FileMacKey(byte[] dataKey)
        {
            return Hmac(dataKey, Encoding.ASCII.GetBytes("ilock-file-mac"));
        }

        public static void AesCtrXor(byte[] key, byte[] nonce, long blockIndex, byte[] buffer, int offset, int count)
        {
            using (AesCryptoServiceProvider aes = new AesCryptoServiceProvider())
            {
                aes.Mode = CipherMode.ECB;
                aes.Padding = PaddingMode.None;
                aes.KeySize = 256;
                aes.Key = key;
                using (ICryptoTransform enc = aes.CreateEncryptor())
                {
                    byte[] ctr = new byte[16];
                    Array.Copy(nonce, 0, ctr, 0, 8);
                    byte[] ks = new byte[16];
                    long block = blockIndex;
                    int done = 0;
                    while (done < count)
                    {
                        ulong c = (ulong)block;
                        for (int i = 0; i < 8; i++) ctr[15 - i] = (byte)(c >> (8 * i));
                        enc.TransformBlock(ctr, 0, 16, ks, 0);
                        int n = Math.Min(16, count - done);
                        for (int i = 0; i < n; i++) buffer[offset + done + i] ^= ks[i];
                        done += n;
                        block++;
                    }
                }
            }
        }

        public static void EncryptRange(FileStream fs, byte[] key, byte[] nonce, long start, long length)
        {
            if (length <= 0) return;
            fs.Position = start;
            byte[] buf = new byte[65536];
            long remaining = length;
            long pos = start;
            while (remaining > 0)
            {
                int want = (int)Math.Min(buf.Length, remaining);
                int read = fs.Read(buf, 0, want);
                if (read <= 0) break;
                AesCtrXor(key, nonce, pos / 16, buf, 0, read);
                fs.Position = pos;
                fs.Write(buf, 0, read);
                pos += read;
                remaining -= read;
                if (read % 16 != 0) break;
            }
            fs.Flush(true);
        }
    }

    public static class Security
    {
        [DllImport("crypt32.dll", SetLastError = true)]
        private static extern bool CryptProtectData(ref DATA_BLOB pDataIn, string szDataDescr, IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, out DATA_BLOB pDataOut);

        [DllImport("crypt32.dll", SetLastError = true)]
        private static extern bool CryptUnprotectData(ref DATA_BLOB pDataIn, IntPtr ppszDataDescr, IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, out DATA_BLOB pDataOut);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr hMem);

        [StructLayout(LayoutKind.Sequential)]
        private struct DATA_BLOB { public int cbData; public IntPtr pbData; }

        public static byte[] Protect(byte[] data)
        {
            DATA_BLOB inb = new DATA_BLOB();
            inb.pbData = Marshal.AllocHGlobal(data.Length);
            Marshal.Copy(data, 0, inb.pbData, data.Length);
            inb.cbData = data.Length;
            DATA_BLOB outb;
            try
            {
                if (!CryptProtectData(ref inb, "InstantLock", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out outb))
                    throw new LockException("DPAPI 保护失败");
                byte[] r = new byte[outb.cbData];
                Marshal.Copy(outb.pbData, r, 0, outb.cbData);
                LocalFree(outb.pbData);
                return r;
            }
            finally { Marshal.FreeHGlobal(inb.pbData); }
        }

        public static byte[] Unprotect(byte[] data)
        {
            DATA_BLOB inb = new DATA_BLOB();
            inb.pbData = Marshal.AllocHGlobal(data.Length);
            Marshal.Copy(data, 0, inb.pbData, data.Length);
            inb.cbData = data.Length;
            DATA_BLOB outb;
            try
            {
                if (!CryptUnprotectData(ref inb, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out outb))
                    throw new LockException("DPAPI 解密失败");
                byte[] r = new byte[outb.cbData];
                Marshal.Copy(outb.pbData, r, 0, outb.cbData);
                LocalFree(outb.pbData);
                return r;
            }
            finally { Marshal.FreeHGlobal(inb.pbData); }
        }
    }

    public static class VaultIO
    {
        public static void Write(string folder, Vault v, byte[] kEnc, byte[] kMac)
        {
            string payload = Serialize(v);
            byte[] plain = Encoding.UTF8.GetBytes(payload);
            byte[] iv = Crypto.RandomBytes(16);
            byte[] kFile = Crypto.FileKey(kEnc, iv);
            Crypto.AesCtrXor(kFile, iv, 0, plain, 0, plain.Length);

            MemoryStream ms = new MemoryStream();
            BinaryWriter w = new BinaryWriter(ms);
            w.Write(Encoding.ASCII.GetBytes(Vault.PayloadMagic), 0, 6);
            w.Write(v.Version);
            w.Write((int)v.Strength);
            w.Write(v.Iterations);
            w.Write(v.Salt, 0, 16);
            w.Write(v.Verifier, 0, 32);
            byte[] hint = Encoding.UTF8.GetBytes(v.Hint == null ? "" : v.Hint);
            byte[] mail = Encoding.UTF8.GetBytes(v.Email == null ? "" : v.Email);
            w.Write(hint.Length); w.Write(hint, 0, hint.Length);
            w.Write(mail.Length); w.Write(mail, 0, mail.Length);
            byte[] crt = Encoding.UTF8.GetBytes(v.Created == null ? "" : v.Created);
            w.Write(crt.Length); w.Write(crt, 0, crt.Length);
            w.Write(iv, 0, 16);
            w.Write(plain.Length);
            w.Write(plain, 0, plain.Length);
            byte[] body = ms.ToArray();
            byte[] mac = Crypto.Hmac(kMac, body);
            string dir = Vault.ContainerPath(folder);
            Directory.CreateDirectory(dir);
            string tmp = Path.Combine(dir, Vault.VaultFile + ".new");
            using (FileStream fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(body, 0, body.Length);
                fs.Write(mac, 0, mac.Length);
                fs.Flush(true);
            }
            string target = Vault.VaultPath(folder);
            if (File.Exists(target))
            {
                try { File.SetAttributes(target, FileAttributes.Normal); } catch { }
                File.Delete(target);
            }
            File.Move(tmp, target);
            try { File.SetAttributes(target, FileAttributes.Hidden | FileAttributes.System); } catch { }
        }

        public static Vault ReadHeader(string folder, out byte[] iv, out byte[] cipherPayload, out byte[] mac, out byte[] body)
        {
            iv = null; cipherPayload = null; mac = null; body = null;
            string path = Vault.VaultPath(folder);
            if (!File.Exists(path)) throw new LockException("未找到加密信息文件。");
            byte[] all = File.ReadAllBytes(path);
            if (all.Length < 6 + 4 + 4 + 4 + 16 + 32 + 4 + 4 + 16 + 4 + 32)
                throw new LockException("加密信息文件已损坏。");
            mac = new byte[32];
            Array.Copy(all, all.Length - 32, mac, 0, 32);
            body = new byte[all.Length - 32];
            Array.Copy(all, 0, body, 0, body.Length);

            MemoryStream ms = new MemoryStream(body);
            BinaryReader r = new BinaryReader(ms);
            byte[] magic = r.ReadBytes(6);
            if (Encoding.ASCII.GetString(magic, 0, 6) != Vault.PayloadMagic) throw new LockException("加密信息文件格式不正确。");
            Vault v = new Vault();
            v.Version = r.ReadInt32();
            v.Strength = (Strength)r.ReadInt32();
            v.Iterations = r.ReadInt32();
            v.Salt = r.ReadBytes(16);
            v.Verifier = r.ReadBytes(32);
            int hl = r.ReadInt32(); v.Hint = Encoding.UTF8.GetString(r.ReadBytes(hl));
            int ml = r.ReadInt32(); v.Email = Encoding.UTF8.GetString(r.ReadBytes(ml));
            if (ms.Position < ms.Length) { int cl = r.ReadInt32(); v.Created = Encoding.UTF8.GetString(r.ReadBytes(cl)); }
            iv = r.ReadBytes(16);
            int pl = r.ReadInt32();
            cipherPayload = r.ReadBytes(pl);
            return v;
        }

        public static Vault ReadHeaderOnly(string folder)
        {
            byte[] iv, cp, mac, body;
            return ReadHeader(folder, out iv, out cp, out mac, out body);
        }

        public static Vault Open(string folder, string password, out byte[] kEnc, out byte[] kMac)
        {
            byte[] iv, cipherPayload, mac, body;
            Vault v = ReadHeader(folder, out iv, out cipherPayload, out mac, out body);
            byte[] kE, kM, kV;
            Crypto.DeriveKeys(password, v.Salt, v.Iterations, out kE, out kM, out kV);
            byte[] expect = Crypto.Hmac(kV, Encoding.ASCII.GetBytes("ilock-verify-v1"));
            if (!Crypto.FixedEquals(expect, v.Verifier)) throw new LockException("密码错误，请重新输入！");
            byte[] got = Crypto.Hmac(kM, body);
            if (!Crypto.FixedEquals(got, mac)) throw new LockException("加密信息校验失败，文件可能已被修改。");
            byte[] kFile = Crypto.FileKey(kE, iv);
            Crypto.AesCtrXor(kFile, iv, 0, cipherPayload, 0, cipherPayload.Length);
            Parse(v, Encoding.UTF8.GetString(cipherPayload));
            kEnc = kE; kMac = kM;
            return v;
        }

        public static void ChangePassword(string folder, string oldPwd, string newPwd)
        {
            byte[] kEnc, kMac;
            Vault v = Open(folder, oldPwd, out kEnc, out kMac);
            byte[] salt = Crypto.RandomBytes(16);
            byte[] kE, kM, kV;
            Crypto.DeriveKeys(newPwd, salt, v.Iterations, out kE, out kM, out kV);
            v.Salt = salt;
            v.Verifier = Crypto.Hmac(kV, Encoding.ASCII.GetBytes("ilock-verify-v1"));
            VaultIO.Write(folder, v, kE, kM);
            byte[] endVerify;
            Vault check = Open(folder, newPwd, out endVerify, out endVerify);
            if (check.Entries.Count != v.Entries.Count) throw new LockException("密码修改后校验失败。");
        }

        private static string Serialize(Vault v)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("V\t").Append(v.Version).Append('\n');
            sb.Append("S\t").Append((int)v.Strength).Append('\n');
            sb.Append("H\t").Append(v.HeaderEncrypted ? "1" : "0").Append('\n');
            sb.Append("T\t").Append(v.Created).Append('\n');
            sb.Append("K\t").Append(Convert.ToBase64String(v.DataKey)).Append('\n');
            foreach (Entry e in v.Entries)
            {
                sb.Append(e.IsDir ? "D\t" : "E\t");
                sb.Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(e.Rel))).Append('\t');
                sb.Append(e.Cipher).Append('\t');
                sb.Append(e.Size.ToString(CultureInfo.InvariantCulture)).Append('\t');
                sb.Append(Crypto.BytesToHex(e.Nonce)).Append('\t');
                sb.Append(e.Done.ToString(CultureInfo.InvariantCulture)).Append('\t');
                sb.Append(e.Mac == null ? "" : e.Mac).Append('\n');
            }
            return sb.ToString();
        }

        private static void Parse(Vault v, string text)
        {
            v.Entries = new List<Entry>();
            string[] lines = text.Split('\n');
            foreach (string raw in lines)
            {
                string line = raw.TrimEnd('\r');
                if (line.Length == 0) continue;
                string[] p = line.Split('\t');
                if (p[0] == "V" && p.Length > 1) v.Version = int.Parse(p[1], CultureInfo.InvariantCulture);
                else if (p[0] == "S" && p.Length > 1) v.Strength = (Strength)int.Parse(p[1], CultureInfo.InvariantCulture);
                else if (p[0] == "H" && p.Length > 1) v.HeaderEncrypted = p[1] == "1";
                else if (p[0] == "T" && p.Length > 1) v.Created = p[1];
                else if (p[0] == "K" && p.Length > 1) v.DataKey = Convert.FromBase64String(p[1]);
                else if ((p[0] == "E" || p[0] == "D") && p.Length >= 7)
                {
                    Entry e = new Entry();
                    e.IsDir = p[0] == "D";
                    e.Rel = Encoding.UTF8.GetString(Convert.FromBase64String(p[1]));
                    e.Cipher = p[2];
                    e.Size = long.Parse(p[3], CultureInfo.InvariantCulture);
                    e.Nonce = Crypto.HexToBytes(p[4]);
                    e.Done = p[5].Length == 0 ? 0 : long.Parse(p[5], CultureInfo.InvariantCulture);
                    e.Mac = p[6];
                    v.Entries.Add(e);
                }
            }
        }
    }
}





