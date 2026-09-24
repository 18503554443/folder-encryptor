using System;
using System.IO;
using System.Text;

namespace InstantLock
{
    public static class Diag
    {
        private static readonly object Gate = new object();
        public static string LogPath
        {
            get { return Path.Combine(AppConfig.Dir, "log.txt"); }
        }
        public static void Log(string msg)
        {
            try
            {
                lock (Gate)
                {
                    string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + Environment.NewLine;
                    File.AppendAllText(LogPath, line, Encoding.UTF8);
                }
            }
            catch { }
        }
        public static void LogError(string where, Exception ex)
        {
            Log("ERROR [" + where + "] " + ex.GetType().FullName + ": " + ex.Message + Environment.NewLine + ex.StackTrace);
        }
    }
}
