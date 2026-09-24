using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace InstantLock
{
    public class AppCtx : ApplicationContext
    {
        public AppCtx(Form main)
        {
            main.FormClosed += delegate
            {
                if (Application.OpenForms.Count == 0) ExitThread();
            };
            MainForm = main;
            main.Show();
        }
    }

    public static class Program
    {
        public const string AppName = "文件夹加密器";
        public const string AppVersion = "v1.0";

        [STAThread]
        public static void Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                Diag.LogError("AppDomain", e.ExceptionObject as Exception);
            };
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
            {
                Diag.LogError("UI", e.Exception);
                MessageBox.Show("发生错误：" + e.Exception.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            AppConfig.Load();
            Diag.Log("启动 " + AppVersion);

            try
            {
                Run(args);
            }
            catch (Exception ex)
            {
                Diag.LogError("Main", ex);
                MessageBox.Show("程序启动失败：" + ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void Run(string[] args)
        {
            string target = null;
            string mode = "";
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "/lock" || a == "/unlock" || a == "/settings") { mode = a; continue; }
                if (Directory.Exists(a)) target = a;
            }
            string selfDir = null;
            try { selfDir = Path.GetDirectoryName(Application.ExecutablePath); } catch { }
            // 未指定文件夹时，默认就是程序所在文件夹（双击即加密当前文件夹）
            if (target == null) target = selfDir;

            if (AppConfig.Current.LoginPwdEnabled && !string.IsNullOrEmpty(AppConfig.Current.LoginPwdHash))
            {
                using (PwdGateForm gate = new PwdGateForm())
                {
                    if (gate.ShowDialog() != DialogResult.OK) { Diag.Log("登录密码取消，退出"); return; }
                }
            }

            if (target != null && Vault.IsLocked(target))
            {
                if (mode == "/lock")
                {
                    MessageBox.Show("该文件夹已经处于加密状态。", AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Diag.Log("打开解密窗口: " + target);
                Application.Run(new AppCtx(new UnlockForm(target)));
                return;
            }

            if (mode == "/settings")
            {
                using (SettingsForm sf = new SettingsForm()) sf.ShowDialog();
                return;
            }

            Diag.Log("打开主窗口 target=" + (target == null ? "(无)" : target));
            Application.Run(new AppCtx(new MainForm(target)));
        }
    }
}





