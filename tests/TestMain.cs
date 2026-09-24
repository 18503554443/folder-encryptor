using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using InstantLock;

class TestMain
{
    static int fail = 0;
    static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + what);
        if (!ok) fail++;
    }

    static Dictionary<string, string> Hashes(string folder)
    {
        Dictionary<string, string> d = new Dictionary<string, string>();
        foreach (string f in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
        {
            string rel = f.Substring(folder.Length + 1);
            if (rel.StartsWith(Vault.ContainerDir)) continue;
            if (string.Equals(Path.GetFileName(f), "desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
            using (SHA256 sha = SHA256.Create())
            using (FileStream fs = File.OpenRead(f))
                d[rel] = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "");
        }
        return d;
    }

    static bool Same(Dictionary<string, string> a, Dictionary<string, string> b)
    {
        if (a.Count != b.Count) return false;
        foreach (KeyValuePair<string, string> kv in a)
        {
            if (!b.ContainsKey(kv.Key)) return false;
            if (b[kv.Key] != kv.Value) return false;
        }
        return true;
    }

    static string MakeSample(string root)
    {
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "子目录"));
        Directory.CreateDirectory(Path.Combine(root, "a", "b"));
        File.WriteAllText(Path.Combine(root, "hello.txt"), "你好，世界 Hello World 1234567890");
        File.WriteAllText(Path.Combine(root, "子目录", "note 中文.txt"), new string('x', 5000));
        File.WriteAllText(Path.Combine(root, "a", "b", "deep.dat"), "deep file content");
        byte[] big = new byte[5 * 1024 * 1024 + 777];
        new Random(42).NextBytes(big);
        File.WriteAllBytes(Path.Combine(root, "big.bin"), big);
        byte[] tiny = new byte[10];
        File.WriteAllBytes(Path.Combine(root, "tiny.bin"), tiny);
        return root;
    }

    static void Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        AppConfig.Load();
        string baseDir = Path.Combine(Path.GetTempPath(), "ilock_test_" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(baseDir);

        // ---- case 1: instant lock / unlock
        Console.WriteLine("== 瞬间加密 ==");
        string f1 = MakeSample(Path.Combine(baseDir, "case1"));
        Dictionary<string, string> h1 = Hashes(f1);
        int fileCount = h1.Count;
        DateTime t0 = DateTime.Now;
        Locker.Lock(f1, "pw123456", Strength.Instant, "提示A", "a@b.com", false, null, null);
        TimeSpan lockSpan = DateTime.Now - t0;
        Check(Vault.IsLocked(f1), "加密后存在 vault.dat");
        Check(Hashes(f1).Count == 0, "原文文件已移出可见区域");
        Check(Directory.GetFiles(Vault.DataPath(f1)).Length == fileCount, "容器内文件数量一致 (" + Directory.GetFiles(Vault.DataPath(f1)).Length + "/" + fileCount + ")");
        bool wrongRejected = false;
        try { Locker.Unlock(f1, "bad-password", null, null); }
        catch (Exception ex) { Console.WriteLine("    [debug] " + ex.GetType().FullName + " :: " + ex.Message); wrongRejected = ex is LockException; }
        Check(wrongRejected, "错误密码被拒绝");
        Locker.Unlock(f1, "pw123456", null, null);
        Dictionary<string, string> h1b = Hashes(f1);
        Check(Same(h1, h1b), "解密后内容与原始完全一致");
        Check(!Vault.IsLocked(f1), "容器已清理");
        Console.WriteLine("  瞬间加密耗时: " + lockSpan.TotalMilliseconds.ToString("F0") + " ms");

        // ---- case 2: full AES
        Console.WriteLine("== 完全加密 (AES-256) ==");
        string f2 = MakeSample(Path.Combine(baseDir, "case2"));
        Dictionary<string, string> h2 = Hashes(f2);
        Locker.Lock(f2, "p@ssw0rd-完整", Strength.Full, "hint2", "", false, null, null);
        Check(Vault.IsLocked(f2), "加密后存在 vault.dat");
        Vault vv = VaultIO.ReadHeaderOnly(f2);
        Check(vv.Strength == Strength.Full, "强度记录为 Full");
        string cipherText = File.ReadAllText(Path.Combine(Vault.DataPath(f2), Directory.GetFiles(Vault.DataPath(f2))[0]));
        bool plaintextLeak = false;
        foreach (string f in Directory.GetFiles(Vault.DataPath(f2)))
        {
            byte[] b = File.ReadAllBytes(f);
            string head = Encoding.UTF8.GetString(b, 0, Math.Min(b.Length, 200));
            if (head.Contains("Hello World") || head.Contains("你好")) plaintextLeak = true;
        }
        Check(!plaintextLeak, "密文头部没有明文残留");
        Locker.Unlock(f2, "p@ssw0rd-完整", null, null);
        Check(Same(h2, Hashes(f2)), "完全加密解密后内容一致");

        // ---- case 3: temp unlock and relock
        Console.WriteLine("== 临时解密 / 恢复加密 ==");
        string f3 = MakeSample(Path.Combine(baseDir, "case3"));
        Dictionary<string, string> h3 = Hashes(f3);
        Locker.Lock(f3, "tempPwd", Strength.Instant, "", "", false, null, null);
        Session s = Locker.TempUnlock(f3, "tempPwd", null, null);
        Check(Same(h3, Hashes(f3)), "临时解密后内容一致");
        Check(Vault.IsLocked(f3), "临时解密期间仍保留加密信息");
        File.WriteAllText(Path.Combine(f3, "新增文件.txt"), "added while unlocked");
        Locker.Relock(s, null, null);
        Check(!File.Exists(Path.Combine(f3, "新增文件.txt")), "恢复加密后新增文件也被收进容器");
        Locker.Unlock(f3, "tempPwd", null, null);
        Check(File.Exists(Path.Combine(f3, "新增文件.txt")), "再次解密后新增文件回来了");

        // ---- case 4: change password
        Console.WriteLine("== 修改密码 ==");
        string f4 = MakeSample(Path.Combine(baseDir, "case4"));
        Dictionary<string, string> h4 = Hashes(f4);
        Locker.Lock(f4, "oldpwd", Strength.Instant, "", "", false, null, null);
        VaultIO.ChangePassword(f4, "oldpwd", "newpwd");
        bool oldRejected = false;
        try { Locker.Unlock(f4, "oldpwd", null, null); }
        catch (Exception ex) { Console.WriteLine("    [debug] " + ex.GetType().FullName + " :: " + ex.Message); oldRejected = ex is LockException; }
        Check(oldRejected, "旧密码失效");
        Locker.Unlock(f4, "newpwd", null, null);
        Check(Same(h4, Hashes(f4)), "新密码解密内容一致");

        // ---- case 5: tamper detection
        Console.WriteLine("== 防篡改 ==");
        string f5 = MakeSample(Path.Combine(baseDir, "case5"));
        Locker.Lock(f5, "tamper", Strength.Instant, "", "", false, null, null);
        string vp = Vault.VaultPath(f5);
        File.SetAttributes(vp, FileAttributes.Normal);
        byte[] vb = File.ReadAllBytes(vp);
        vb[40] ^= 0x5A;
        File.WriteAllBytes(vp, vb);
        bool tamperCaught = false;
        try { Locker.Unlock(f5, "tamper", null, null); } catch (LockException) { tamperCaught = true; }
        Check(tamperCaught, "篡改加密信息被检测到");

        try { Locker.DeleteTree(baseDir); } catch { }
        Console.WriteLine();
        Console.WriteLine(fail == 0 ? "全部通过" : ("失败项: " + fail));
        Environment.Exit(fail == 0 ? 0 : 1);
    }
}


