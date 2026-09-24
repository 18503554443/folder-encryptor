using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace InstantLock
{
    public static class Msg
    {
        public static void Info(IWin32Window o, string t) { MessageBox.Show(o, t, Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information); }
        public static void Warn(IWin32Window o, string t) { MessageBox.Show(o, t, Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        public static void Error(IWin32Window o, string t) { MessageBox.Show(o, t, Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        public static bool Ask(IWin32Window o, string t) { return MessageBox.Show(o, t, Program.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes; }
    }

    public static class Mount
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

        public static string MirrorRoot { get { return Path.Combine(Path.GetTempPath(), "InstantLock"); } }

        public static string CreateMirror(string folder, out char drive)
        {
            drive = '\0';
            string id = Guid.NewGuid().ToString("N").Substring(0, 8);
            string root = Path.Combine(MirrorRoot, "mnt_" + id);
            Directory.CreateDirectory(root);
            CopyTree(folder, root, Vault.ContainerDir);
            string letter = FreeLetter();
            if (letter != null)
            {
                int code = Run("subst.exe", letter + ": \"" + root + "\"");
                if (code == 0) drive = letter[0];
            }
            return root;
        }

        private static string FreeLetter()
        {
            List<char> used = new List<char>();
            foreach (DriveInfo d in DriveInfo.GetDrives()) used.Add(char.ToUpperInvariant(d.Name[0]));
            for (char c = 'Z'; c >= 'D'; c--) if (!used.Contains(c)) return c.ToString();
            return null;
        }

        private static int Run(string exe, string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(exe, args);
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                Process p = Process.Start(psi);
                p.WaitForExit(8000);
                return p.HasExited ? p.ExitCode : -1;
            }
            catch { return -1; }
        }

        public static void RemoveMirror(string root, char drive)
        {
            if (drive != '\0') Run("subst.exe", drive + ": /D");
            try { Locker.DeleteTree(root); } catch { }
        }

        private static void CopyTree(string src, string dst, string skipName)
        {
            foreach (string d in Directory.GetDirectories(src))
            {
                if (string.Equals(Path.GetFileName(d), skipName, StringComparison.OrdinalIgnoreCase)) continue;
                string nd = Path.Combine(dst, Path.GetFileName(d));
                Directory.CreateDirectory(nd);
                CopyTree(d, nd, skipName);
            }
            foreach (string f in Directory.GetFiles(src))
            {
                string nm = Path.GetFileName(f);
                if (string.Equals(nm, "desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                string nf = Path.Combine(dst, nm);
                if (!CreateHardLink(nf, f, IntPtr.Zero))
                {
                    try { File.Copy(f, nf, true); } catch { }
                }
            }
        }
    }

    internal static class Ui
    {
        public static SkinLabel Label(string text, int x, int y, int w, int h, ContentAlignment align, Color color, float size, FontStyle style)
        {
            return Label(text, x, y, w, h, align, color, size, style, Pal.Body);
        }

        public static SkinLabel Label(string text, int x, int y, int w, int h, ContentAlignment align, Color color, float size, FontStyle style, Color back)
        {
            SkinLabel l = new SkinLabel();
            l.Text = text;
            l.Location = new Point(x, y);
            l.Size = new Size(w, h);
            l.Align = align;
            l.ForeColor = color;
            l.BackColor = back;
            l.Font = new Font("SimSun", size, style);
            return l;
        }

        public static void ScaleTree(Control c, float f)
        {
            foreach (Control ch in c.Controls)
            {
                ch.Location = new Point((int)Math.Round(ch.Left * f), (int)Math.Round(ch.Top * f));
                ch.Size = new Size((int)Math.Round(ch.Width * f), (int)Math.Round(ch.Height * f));
                try { if (ch.Font != null) ch.Font = new Font(ch.Font.FontFamily, ch.Font.Size * f, ch.Font.Style); } catch { }
                ScaleTree(ch, f);
            }
        }

        public static SkinLabel Auto(string text, int x, int y, Color color, float size, FontStyle style)
        {
            SkinLabel l = new SkinLabel();
            l.Text = text;
            l.Location = new Point(x, y);
            Font f = new Font("SimSun", size, style);
            l.Font = f;
            Size sz = TextRenderer.MeasureText(text, f);
            l.Size = new Size(sz.Width + 6, Math.Max(18, sz.Height));
            l.Align = ContentAlignment.MiddleLeft;
            l.ForeColor = color;
            l.BackColor = Color.Transparent;
            return l;
        }
    }

    public class MainForm : SkinForm
    {
        private string folder;
        private HeaderBar header;
        private IconButton close, icoFolder, browse, gear, home, update, help;
        private FlatButton btnLock;
        private ArrowStrip toggle;
        private SkinEdit edtPwd, edtRetype, edtHint, edtMail;
        private FlatGauge thin;
        private SkinLabel lblSettings, lblFolder, lblPwd, lblRetype, lblPwdHint, lblSafeMail, lblVersion, lblMailNote;
        private IconButton icoHint, icoMail;
        private bool panelOpen = true;
        private bool busy;
        private List<Control> rightPanel = new List<Control>();

        public MainForm(string startFolder)
        {
            ClientSize = new Size(750, 300);
            Text = Program.AppName;
            folder = startFolder;
            BuildUi();
            LoadFolder(folder);
            if (AppConfig.Current.SaveEmail) edtMail.Text = AppConfig.Current.LastEmail;
        }

        private void BuildUi()
        {
            header = new HeaderBar();
            header.Title = Program.AppName;
            header.Location = new Point(10, 6);
            header.Size = new Size(300, 36);
            Controls.Add(header);
            header.SendToBack();

            gear = new IconButton();
            gear.Icon = Glyph.Gear; gear.IconColor = Color.White;
            gear.Location = new Point(322, 12); gear.Size = new Size(16, 16);
            gear.Click += delegate { OpenSettings(); };
            Controls.Add(gear);

            lblSettings = Ui.Auto("设置", 342, 12, Color.White, 9f, FontStyle.Regular);
            lblSettings.Cursor = Cursors.Hand;
            lblSettings.Click += delegate { OpenSettings(); };
            Controls.Add(lblSettings);
            gear.BringToFront();
            lblSettings.BringToFront();

            close = new IconButton();
            close.Icon = Glyph.Close;
            close.Location = new Point(709, 0); close.Size = new Size(41, 20);
            close.Click += delegate { Close(); };
            Controls.Add(close);

            icoFolder = new IconButton();
            icoFolder.Icon = Glyph.Folder; icoFolder.IconColor = Pal.CyanDark;
            icoFolder.Location = new Point(21, 83); icoFolder.Size = new Size(16, 16);
            Controls.Add(icoFolder);

            lblFolder = Ui.Label("未选择文件夹", 43, 81, 313, 20, ContentAlignment.MiddleLeft, Pal.Muted, 9.75f, FontStyle.Regular);
            lblFolder.Ellipsis = true;
            Controls.Add(lblFolder);

            browse = new IconButton();
            browse.Icon = Glyph.Search; browse.IconColor = Pal.CyanDark;
            browse.Location = new Point(369, 81); browse.Size = new Size(37, 19);
            browse.Click += delegate { PickFolder(); };
            Controls.Add(browse);

            toggle = new ArrowStrip();
            toggle.PointLeft = false;
            toggle.Location = new Point(736, 128); toggle.Size = new Size(13, 45);
            ToolTip tipSide = new ToolTip();
            tipSide.SetToolTip(toggle, "收起 / 展开右侧栏");
            toggle.Click += delegate { TogglePanel(); };
            Controls.Add(toggle);

            lblPwd = Ui.Label("密码：", 2, 131, 84, 24, ContentAlignment.MiddleRight, Pal.Text, 9.75f, FontStyle.Regular);
            Controls.Add(lblPwd);
            edtPwd = new SkinEdit();
            edtPwd.Location = new Point(88, 128); edtPwd.Size = new Size(318, 30);
            edtPwd.UseSystemPasswordChar = true;
            edtPwd.TabIndex = 0;
            edtPwd.Inner.KeyDown += delegate(object k1, KeyEventArgs k2)
            {
                if (k2.KeyCode == Keys.Enter)
                {
                    k2.SuppressKeyPress = true;
                    if (edtRetype.Text.Length == 0) edtRetype.Inner.Focus(); else DoLock();
                }
            };
            Controls.Add(edtPwd);

            lblRetype = Ui.Label("再次输入：", 2, 177, 84, 24, ContentAlignment.MiddleRight, Pal.Text, 9.75f, FontStyle.Regular);

            Controls.Add(lblRetype);
            edtRetype = new SkinEdit();
            edtRetype.Location = new Point(88, 174); edtRetype.Size = new Size(318, 30);
            edtRetype.UseSystemPasswordChar = true;
            edtRetype.TabIndex = 1;
            edtRetype.Inner.KeyDown += delegate(object k1, KeyEventArgs k2)
            {
                if (k2.KeyCode == Keys.Enter) { k2.SuppressKeyPress = true; DoLock(); }
            };
            Controls.Add(edtRetype);

            icoHint = new IconButton();
            icoHint.Icon = Glyph.Key; icoHint.IconColor = Pal.CyanDark;
            icoHint.Location = new Point(452, 103); icoHint.Size = new Size(14, 14);
            lblPwdHint = Ui.Label("密码提示", 471, 100, 244, 20, ContentAlignment.MiddleLeft, Pal.Text, 9.75f, FontStyle.Regular, Pal.Card);
            edtHint = new SkinEdit();
            edtHint.TabIndex = 2;
            edtHint.Location = new Point(458, 124); edtHint.Size = new Size(270, 24);

            icoMail = new IconButton();
            icoMail.Icon = Glyph.Mail; icoMail.IconColor = Pal.CyanDark;
            icoMail.Location = new Point(452, 161); icoMail.Size = new Size(14, 14);
            lblSafeMail = Ui.Label("安全邮箱", 471, 158, 244, 20, ContentAlignment.MiddleLeft, Pal.Text, 9.75f, FontStyle.Regular, Pal.Card);
            edtMail = new SkinEdit();
            edtMail.TabIndex = 3;
            edtMail.Location = new Point(458, 182); edtMail.Size = new Size(270, 24);

            lblMailNote = Ui.Label("提示与邮箱保存在本机加密文件中", 458, 212, 270, 18, ContentAlignment.MiddleLeft, Pal.Muted, 8.25f, FontStyle.Regular, Pal.Card);

            foreach (Control rc in new Control[] { icoHint, lblPwdHint, edtHint, icoMail, lblSafeMail, edtMail, lblMailNote })
            {
                rightPanel.Add(rc);
                Controls.Add(rc);
            }

            btnLock = new FlatButton();
            btnLock.TabIndex = 4;
            btnLock.Text = "加密";
            btnLock.Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold);
            btnLock.Location = new Point(244, 223); btnLock.Size = new Size(162, 32);
            btnLock.Click += delegate { DoLock(); };
            Controls.Add(btnLock);

            thin = new FlatGauge();
            thin.Location = new Point(80, 213); thin.Size = new Size(233, 3);
            thin.Visible = false;
            Controls.Add(thin);

            home = BottomIcon(Glyph.Globe, 21, 279, "官网 / 帮助");
            update = BottomIcon(Glyph.Refresh, 45, 279, "检查更新");
            help = BottomIcon(Glyph.Help, 68, 279, "使用说明");
            home.Click += delegate { Msg.Info(this, "本程序为本地离线工具，没有广告与联网行为。"); };
            update.Click += delegate { Msg.Info(this, "当前版本 " + Program.AppVersion + "，未启用联网更新。"); };
            help.Click += delegate { ShowHelp(); };

            lblVersion = Ui.Label("瞬间加密 " + Program.AppVersion, 380, 277, 120, 20, ContentAlignment.MiddleLeft, Pal.Muted, 8.5f, FontStyle.Regular, Pal.Strip);
            Controls.Add(lblVersion);

            panelOpen = false;
            ApplyPanelState();

            if (AppConfig.Current.StayOnTop) TopMost = true;
        }

        private IconButton BottomIcon(Glyph g, int x, int y, string tip)
        {
            IconButton b = new IconButton();
            b.Icon = g; b.IconColor = Pal.Muted;
            b.Location = new Point(x, y); b.Size = new Size(16, 16);
            ToolTip t = new ToolTip();
            t.SetToolTip(b, tip);
            Controls.Add(b);
            return b;
        }

        private void TogglePanel()
        {
            panelOpen = !panelOpen;
            ApplyPanelState();
        }

        private void ApplyPanelState()
        {
            foreach (Control c in rightPanel) c.Visible = panelOpen;
            int w = panelOpen ? 750 : 439;
            ClientSize = new Size(w, ClientSize.Height);
            close.Location = new Point(w - 41, 0);
            toggle.PointLeft = !panelOpen;
            toggle.Location = panelOpen ? new Point(736, 128) : new Point(426, 128);
            toggle.Invalidate();
            gear.Location = new Point(panelOpen ? 322 : 230, gear.Top);
            lblSettings.Location = new Point(panelOpen ? 342 : 250, lblSettings.Top);
            gear.BringToFront();
            lblSettings.BringToFront();
            if (lblVersion != null) lblVersion.Location = new Point(w - 125, lblVersion.Top);
            Invalidate();
        }

        private void OpenSettings()
        {
            using (SettingsForm f = new SettingsForm())
            {
                f.ShowDialog(this);
                TopMost = AppConfig.Current.StayOnTop;
            }
        }

        private void ShowHelp()
        {
            string t = "【瞬间加密】只改写每个文件的开头 4KB 和文件名，不做整盘搬运，因此几乎瞬间完成；\r\n" +
                "被加密的文件在资源管理器中无法打开，即使改回扩展名也不行。\r\n\r\n" +
                "【完全加密】使用 AES-256 对整个文件重新加密，速度取决于数据量，安全性最高。\r\n\r\n" +
                "密码一旦遗忘无法找回；请务必设置密码提示。";
            Msg.Info(this, t);
        }

        private void PickFolder()
        {
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.Description = "选择要加密的文件夹";
                d.ShowNewFolderButton = true;
                if (d.ShowDialog(this) == DialogResult.OK) LoadFolder(d.SelectedPath);
            }
        }

        private void LoadFolder(string f)
        {
            folder = f;
            if (string.IsNullOrEmpty(f)) { lblFolder.Text = "未选择文件夹"; lblFolder.ForeColor = Pal.Muted; return; }
            if (Vault.IsLocked(f))
            {
                lblFolder.Text = f + "   （已加密，回车或点“解密”即可恢复）";
                lblFolder.ForeColor = Pal.Green;
                btnLock.Text = "解密";
                return;
            }
            lblFolder.Text = f;
            lblFolder.ForeColor = Pal.Text;
            btnLock.Text = "加密";
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            PaintChrome(e.Graphics, 48, true, 32);
            if (panelOpen)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (Pen div = new Pen(Pal.CardLine))
                    g.DrawLine(div, 437, 90, 437, 246);
                Rectangle card = new Rectangle(444, 92, 292, 146);
                Draw.FillRound(g, card, 6, Pal.Card);
                Draw.StrokeRound(g, card, 6, Pal.CardLine);
                using (System.Drawing.Drawing2D.GraphicsPath ap = Draw.Round(new Rectangle(card.X + 1, card.Y + 16, 3, card.Height - 32), 2))
                using (SolidBrush ab = new SolidBrush(Pal.Cyan)) g.FillPath(ab, ap);
            }
            base.OnPaint(e);
        }

        private void DoLock()
        {
            if (busy) return;
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) { Msg.Warn(this, "请先选择要加密的文件夹。"); return; }
            if (Vault.IsLocked(folder))
            {
                UnlockForm u = new UnlockForm(folder);
                u.Show();
                Close();
                return;
            }
            if (edtPwd.Text.Length == 0) { Msg.Warn(this, "请输入密码。"); edtPwd.Inner.Focus(); return; }
            if (edtPwd.Text.Length > 64) { Msg.Warn(this, "密码位数过多，请不要大于 64 个字符。"); return; }
            if (edtPwd.Text != edtRetype.Text) { Msg.Warn(this, "两次密码输入不同，请重新输入。"); edtRetype.Inner.Focus(); return; }

            Strength s = (Strength)AppConfig.Current.DefaultStrength;
            string pwd = edtPwd.Text;
            string hint = edtHint.Text;
            string mail = edtMail.Text;
            bool hide = AppConfig.Current.HideFolder;
            int count = 0;
            try { count = Locker.EnumerateFiles(folder, Vault.ContainerPath(folder)).Count; } catch { }
            bool ok = false;
            busy = true;
            thin.Visible = true;
            thin.Set(0);
            btnLock.Enabled = false;
            Cursor = Cursors.WaitCursor;
            Application.DoEvents();

            int failedCount = 0;
            string failedReport = "";
            try
            {
                List<string> failed;
                if (s == Strength.Instant && count <= 4000)
                {
                    ProgressInfo last = new ProgressInfo();
                    failed = Locker.Lock(folder, pwd, s, hint, mail, hide, delegate(ProgressInfo p) { last = p; thin.Set(p.TotalPercent); Application.DoEvents(); }, null);
                }
                else
                {
                    List<string> failed2 = null;
                    Exception err = null;
                    using (ProgressForm pf = new ProgressForm(s == Strength.Full ? "正在完全加密" : "正在加密"))
                    {
                        pf.Start(delegate(Action<ProgressInfo> report, Func<bool> cancel)
                        {
                            try { failed2 = Locker.Lock(folder, pwd, s, hint, mail, hide, report, cancel); }
                            catch (Exception ex) { err = ex; }
                        });
                        pf.ShowDialog(this);
                    }
                    if (err != null) throw err;
                    failed = failed2;
                }
                if (failed != null && failed.Count > 0)
                {
                    failedCount = failed.Count;
                    StringBuilder sb = new StringBuilder();
                    sb.Append("有 ").Append(failed.Count).Append(" 个文件未能加密，已保留在原位置：\r\n\r\n");
                    for (int i = 0; i < failed.Count && i < 10; i++) sb.Append("· ").Append(failed[i]).Append("\r\n");
                    if (failed.Count > 10) sb.Append("…（其余 ").Append(failed.Count - 10).Append(" 个已省略）\r\n");
                    sb.Append("\r\n其它文件已加密完成，可以用密码解锁。");
                    failedReport = sb.ToString();
                }
                if (AppConfig.Current.CopyUnlocker) Locker.CopyUnlockerInto(folder);
                if (AppConfig.Current.SaveEmail) { AppConfig.Current.LastEmail = mail; AppConfig.Current.Save(); }
                edtPwd.Text = "";
                edtRetype.Text = "";
                if (failedCount > 0) { Msg.Warn(this, failedReport); }
                else ok = true;
            }
            catch (Exception ex)
            {
                Msg.Error(this, ex.Message);
            }
            finally
            {
                busy = false;
                btnLock.Enabled = true;
                thin.Visible = false;
                Cursor = Cursors.Default;
            }
            if (ok) Close();   // 加密完成即退出程序
        }
    }

    public class UnlockForm : SkinForm
    {
        private string folder;
        private Vault info;
        private IconButton close, gear, home, update, help;
        private FlatButton btnUnlock, btnCancel, getHint;
        private SkinEdit edtPwd;
        private FlatRadio rbImage, rbTemp, rbComplete;
        private SkinLabel lblSettings, lblPwd, lblPwdHnt, lblHintVal, lblInfo, lblVersion;
        private ArrowStrip toggle;
        private List<Control> rightPanel = new List<Control>();
        private bool panelOpen = true;
        private IconButton icoHint2, icoInfo2;

        public UnlockForm(string target)
        {
            folder = target;
            ClientSize = new Size(750, 300);
            Text = "解密 - " + Program.AppName;
            try { info = VaultIO.ReadHeaderOnly(folder); } catch { info = null; }
            BuildUi();
            if (AppConfig.Current.StayOnTop) TopMost = true;
        }

        private void BuildUi()
        {
            HeaderBar header = new HeaderBar();
            header.Title = "解密文件夹";
            header.Location = new Point(10, 6);
            header.Size = new Size(300, 36);
            Controls.Add(header);
            header.SendToBack();

            gear = new IconButton();
            gear.Icon = Glyph.Gear; gear.IconColor = Color.White;
            gear.Location = new Point(319, 12); gear.Size = new Size(16, 16);
            gear.Click += delegate { using (SettingsForm f = new SettingsForm()) f.ShowDialog(this); };
            Controls.Add(gear);
            lblSettings = Ui.Auto("设置", 339, 12, Color.White, 9f, FontStyle.Regular);
            lblSettings.Cursor = Cursors.Hand;
            lblSettings.Click += delegate { using (SettingsForm f = new SettingsForm()) f.ShowDialog(this); };
            Controls.Add(lblSettings);
            gear.BringToFront();
            lblSettings.BringToFront();

            close = new IconButton();
            close.Icon = Glyph.Close;
            close.Location = new Point(708, 0); close.Size = new Size(41, 20);
            close.Click += delegate { Close(); };
            Controls.Add(close);

            lblPwd = Ui.Label("密码：", 23, 105, 68, 24, ContentAlignment.MiddleRight, Pal.Text, 9.75f, FontStyle.Regular);
            Controls.Add(lblPwd);
            edtPwd = new SkinEdit();
            edtPwd.TabIndex = 0;
            edtPwd.Location = new Point(99, 102); edtPwd.Size = new Size(296, 30);
            edtPwd.UseSystemPasswordChar = true;
            edtPwd.Inner.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; DoUnlock(); } };
            Controls.Add(edtPwd);

            rbImage = new FlatRadio();
            rbImage.Text = "虚拟磁盘"; rbImage.Location = new Point(46, 163); rbImage.Size = new Size(115, 26);
            rbTemp = new FlatRadio();
            rbTemp.Text = "临时解密"; rbTemp.Location = new Point(167, 163); rbTemp.Size = new Size(112, 26);
            rbComplete = new FlatRadio();
            rbComplete.Text = "完全解密"; rbComplete.Location = new Point(285, 163); rbComplete.Size = new Size(125, 26);
            rbComplete.Checked = true;
            Controls.Add(rbImage); Controls.Add(rbTemp); Controls.Add(rbComplete);

            btnUnlock = new FlatButton();
            btnUnlock.TabIndex = 1;
            btnUnlock.Text = "解密";
            btnUnlock.Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold);
            btnUnlock.Location = new Point(68, 218); btnUnlock.Size = new Size(129, 32);
            btnUnlock.Click += delegate { DoUnlock(); };
            Controls.Add(btnUnlock);

            btnCancel = new FlatButton();
            btnCancel.Accent = false;
            btnCancel.Text = "取消";
            btnCancel.Location = new Point(245, 218); btnCancel.Size = new Size(129, 32);
            btnCancel.Click += delegate { Close(); };
            Controls.Add(btnCancel);

            icoHint2 = new IconButton();
            icoHint2.Icon = Glyph.Key; icoHint2.IconColor = Pal.CyanDark;
            icoHint2.Location = new Point(452, 101); icoHint2.Size = new Size(14, 14);
            lblPwdHnt = Ui.Label("密码提示", 471, 98, 244, 20, ContentAlignment.MiddleLeft, Pal.Text, 9.75f, FontStyle.Regular, Pal.Card);
            string hint = (info != null && !string.IsNullOrEmpty(info.Hint)) ? info.Hint : "（未设置密码提示）";
            lblHintVal = Ui.Label(hint, 458, 122, 263, 30, ContentAlignment.TopLeft, Pal.CyanDeep, 10.5f, FontStyle.Bold, Pal.Card);

            string mode = info == null ? "" : (info.Strength == Strength.Instant ? "瞬间锁定" : "AES-256 完全加密");
            string created = "";
            try { if (info != null && !string.IsNullOrEmpty(info.Created)) created = DateTime.Parse(info.Created).ToString("yyyy-MM-dd HH:mm"); } catch { }
            icoInfo2 = new IconButton();
            icoInfo2.Icon = Glyph.Info; icoInfo2.IconColor = Pal.Muted;
            icoInfo2.Location = new Point(452, 162); icoInfo2.Size = new Size(14, 14);
            lblInfo = Ui.Label(mode + "  ·  " + created, 471, 159, 244, 20, ContentAlignment.MiddleLeft, Pal.Muted, 9f, FontStyle.Regular, Pal.Card);

            getHint = new FlatButton();
            getHint.Accent = false;
            getHint.Text = "忘记密码?";
            getHint.Location = new Point(592, 194); getHint.Size = new Size(129, 26);
            getHint.Click += delegate { Forgot(); };

            foreach (Control rc in new Control[] { icoHint2, lblPwdHnt, lblHintVal, icoInfo2, lblInfo, getHint })
            {
                rightPanel.Add(rc);
                Controls.Add(rc);
            }

            toggle = new ArrowStrip();
            toggle.PointLeft = false;
            toggle.Location = new Point(736, 132); toggle.Size = new Size(13, 45);
            ToolTip tipSide2 = new ToolTip();
            tipSide2.SetToolTip(toggle, "收起 / 展开右侧栏");
            toggle.Click += delegate { TogglePanel(); };
            Controls.Add(toggle);

            home = Ico(Glyph.Globe, 10, 279, "官网 / 帮助");
            update = Ico(Glyph.Refresh, 33, 279, "打开文件夹");
            help = Ico(Glyph.Help, 56, 279, "使用说明");
            home.Click += delegate { Msg.Info(this, "本程序为本地离线工具，没有广告与联网行为。"); };
            update.Click += delegate { try { Process.Start("explorer.exe", "\"" + folder + "\""); } catch { } };
            help.Click += delegate { Msg.Info(this, "选择解密方式后输入密码即可。\r\n\r\n完全解密：恢复全部文件并删除加密数据。\r\n临时解密：立即恢复文件，可随时一键恢复加密。\r\n虚拟磁盘：临时解密并映射一个盘符。"); };

            lblVersion = Ui.Label(folder, 120, 277, 300, 20, ContentAlignment.MiddleLeft, Pal.Muted, 8.5f, FontStyle.Regular, Pal.Strip);
            lblVersion.Ellipsis = true;
            Controls.Add(lblVersion);

            panelOpen = false;
            ApplyPanelState();
        }

        private IconButton Ico(Glyph g, int x, int y, string tip)
        {
            IconButton b = new IconButton();
            b.Icon = g; b.IconColor = Pal.Muted;
            b.Location = new Point(x, y); b.Size = new Size(16, 16);
            ToolTip t = new ToolTip(); t.SetToolTip(b, tip);
            Controls.Add(b);
            return b;
        }

        private void TogglePanel()
        {
            panelOpen = !panelOpen;
            ApplyPanelState();
        }

        private void ApplyPanelState()
        {
            foreach (Control c in rightPanel) c.Visible = panelOpen;
            int w = panelOpen ? 750 : 439;
            ClientSize = new Size(w, ClientSize.Height);
            close.Location = new Point(w - 42, 0);
            toggle.PointLeft = !panelOpen;
            toggle.Location = panelOpen ? new Point(736, 132) : new Point(426, 132);
            toggle.Invalidate();
            gear.Location = new Point(panelOpen ? 319 : 227, gear.Top);
            lblSettings.Location = new Point(panelOpen ? 339 : 247, lblSettings.Top);
            gear.BringToFront();
            lblSettings.BringToFront();
            if (lblVersion != null) lblVersion.Location = new Point(120, lblVersion.Top);
            Invalidate();
        }

        private void Forgot()
        {
            string t = "本程序不会把密码上传到任何服务器，因此密码一旦遗忘无法找回。\r\n\r\n" +
                "可以尝试：\r\n" +
                "1. 用当初设置密码提示回忆密码；\r\n" +
                "2. 若使用“临时解密”，可在解密状态下重新设置密码。";
            Msg.Info(this, t);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            PaintChrome(e.Graphics, 48, true, 32);
            if (panelOpen)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (Pen div = new Pen(Pal.CardLine))
                    g.DrawLine(div, 437, 90, 437, 246);
                Rectangle card = new Rectangle(444, 92, 292, 146);
                Draw.FillRound(g, card, 6, Pal.Card);
                Draw.StrokeRound(g, card, 6, Pal.CardLine);
                using (System.Drawing.Drawing2D.GraphicsPath ap = Draw.Round(new Rectangle(card.X + 1, card.Y + 16, 3, card.Height - 32), 2))
                using (SolidBrush ab = new SolidBrush(Pal.Cyan)) g.FillPath(ab, ap);
            }
            base.OnPaint(e);
        }

        private void DoUnlock()
        {
            if (edtPwd.Text.Length == 0) { Msg.Warn(this, "请输入密码。"); edtPwd.Inner.Focus(); return; }
            string pwd = edtPwd.Text;
            btnUnlock.Enabled = false;
            Cursor = Cursors.WaitCursor;
            try
            {
                if (rbComplete.Checked)
                {
                    RunWork("正在解密", delegate(Action<ProgressInfo> rep, Func<bool> cancel) { Locker.Unlock(folder, pwd, rep, cancel); });
                    Close();
                }
                else
                {
                    Session s = null;
                    RunWork("正在解密", delegate(Action<ProgressInfo> rep, Func<bool> cancel) { s = Locker.TempUnlock(folder, pwd, rep, cancel); });
                    if (rbImage.Checked)
                    {
                        char drive;
                        string root = Mount.CreateMirror(folder, out drive);
                        TempForm tf = new TempForm(s, root, drive);
                        tf.Show();
                    }
                    else
                    {
                        TempForm tf = new TempForm(s, null, '\0');
                        tf.Show();
                    }
                    Close();
                }
            }
            catch (Exception ex)
            {
                Msg.Error(this, ex.Message);
            }
            finally
            {
                btnUnlock.Enabled = true;
                Cursor = Cursors.Default;
            }
        }

        private void RunWork(string title, Action<Action<ProgressInfo>, Func<bool>> work)
        {
            Exception err = null;
            using (ProgressForm pf = new ProgressForm(title))
            {
                pf.Start(delegate(Action<ProgressInfo> rep, Func<bool> cancel)
                {
                    try { work(rep, cancel); }
                    catch (Exception ex) { err = ex; }
                });
                pf.ShowDialog(this);
            }
            if (err != null) throw err;
        }
    }
}









































