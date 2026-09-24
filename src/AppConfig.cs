using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace InstantLock
{
    public class AppConfig
    {
        public static AppConfig Current = new AppConfig();
        public static string Dir
        {
            get
            {
                string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "InstantLock");
                Directory.CreateDirectory(d);
                return d;
            }
        }
        public static string FilePath { get { return Path.Combine(Dir, "config.ini"); } }

        public bool HideFolder = false;
        public bool ShowLockIcon = true;
        public bool CopyUnlocker = true;
        public string Language = "zh-CN";
        public int DefaultStrength = 0;
        public bool LoginPwdEnabled = false;
        public string LoginPwdHash = "";
        public bool RightMenuEnabled = false;
        public string RightMenuText = "文件夹加密";
        public string LastEmail = "";
        public bool SaveEmail = false;
        public bool StayOnTop = true;
        public bool AutoToTray = false;
        public bool AutoRelock = false;
        public int AutoRelockMinutes = 10;
        public bool SeenHelp = false;

        public static void Load()
        {
            AppConfig c = new AppConfig();
            try
            {
                if (File.Exists(FilePath))
                {
                    foreach (string raw in File.ReadAllLines(FilePath, Encoding.UTF8))
                    {
                        int i = raw.IndexOf('=');
                        if (i <= 0) continue;
                        string k = raw.Substring(0, i).Trim();
                        string v = raw.Substring(i + 1);
                        switch (k)
                        {
                            case "HideFolder": c.HideFolder = v == "1"; break;
                            case "ShowLockIcon": c.ShowLockIcon = v == "1"; break;
                            case "CopyUnlocker": c.CopyUnlocker = v == "1"; break;
                            case "Language": c.Language = v; break;
                            case "DefaultStrength": c.DefaultStrength = int.Parse(v, CultureInfo.InvariantCulture); break;
                            case "LoginPwdEnabled": c.LoginPwdEnabled = v == "1"; break;
                            case "LoginPwdHash": c.LoginPwdHash = v; break;
                            case "RightMenuEnabled": c.RightMenuEnabled = v == "1"; break;
                            case "RightMenuText": c.RightMenuText = v; break;
                            case "LastEmail": c.LastEmail = v; break;
                            case "SaveEmail": c.SaveEmail = v == "1"; break;
                            case "StayOnTop": c.StayOnTop = v == "1"; break;
                            case "AutoToTray": c.AutoToTray = v == "1"; break;
                            case "AutoRelock": c.AutoRelock = v == "1"; break;
                            case "AutoRelockMinutes": c.AutoRelockMinutes = int.Parse(v, CultureInfo.InvariantCulture); break;
                            case "SeenHelp": c.SeenHelp = v == "1"; break;
                        }
                    }
                }
            }
            catch { }
            Current = c;
        }

        public void Save()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("HideFolder=").Append(HideFolder ? "1" : "0").Append('\n');
            sb.Append("ShowLockIcon=").Append(ShowLockIcon ? "1" : "0").Append('\n');
            sb.Append("CopyUnlocker=").Append(CopyUnlocker ? "1" : "0").Append('\n');
            sb.Append("Language=").Append(Language).Append('\n');
            sb.Append("DefaultStrength=").Append(DefaultStrength).Append('\n');
            sb.Append("LoginPwdEnabled=").Append(LoginPwdEnabled ? "1" : "0").Append('\n');
            sb.Append("LoginPwdHash=").Append(LoginPwdHash).Append('\n');
            sb.Append("RightMenuEnabled=").Append(RightMenuEnabled ? "1" : "0").Append('\n');
            sb.Append("RightMenuText=").Append(RightMenuText).Append('\n');
            sb.Append("LastEmail=").Append(LastEmail).Append('\n');
            sb.Append("SaveEmail=").Append(SaveEmail ? "1" : "0").Append('\n');
            sb.Append("StayOnTop=").Append(StayOnTop ? "1" : "0").Append('\n');
            sb.Append("AutoToTray=").Append(AutoToTray ? "1" : "0").Append('\n');
            sb.Append("AutoRelock=").Append(AutoRelock ? "1" : "0").Append('\n');
            sb.Append("AutoRelockMinutes=").Append(AutoRelockMinutes).Append('\n');
            sb.Append("SeenHelp=").Append(SeenHelp ? "1" : "0").Append('\n');
            try { File.WriteAllText(FilePath, sb.ToString(), Encoding.UTF8); } catch { }
        }

        public static string HashPassword(string pwd)
        {
            using (SHA256 sha = SHA256.Create())
                return Crypto.BytesToHex(sha.ComputeHash(Encoding.UTF8.GetBytes("ilock-app::" + pwd)));
        }
    }
}
