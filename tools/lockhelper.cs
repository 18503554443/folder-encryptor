using System;
using InstantLock;
class LockHelper
{
    static void Main(string[] a)
    {
        if (a.Length < 3) { Console.WriteLine("usage: lockhelper lock|full|unlock <folder> <pwd>"); return; }
        string f = a[1], p = a[2];
        try
        {
            if (a[0] == "lock") { var bad = Locker.Lock(f, p, Strength.Instant, "演示密码提示", "", false, null, null); foreach (var x in bad) Console.WriteLine("  未加密: " + x); }
            else if (a[0] == "full") Locker.Lock(f, p, Strength.Full, "演示密码提示", "", false, null, null);
            else if (a[0] == "unlock") Locker.Unlock(f, p, null, null);
            Console.WriteLine("ok " + a[0]);
        }
        catch (Exception ex) { Console.WriteLine("fail: " + ex.Message); Environment.Exit(1); }
    }
}

