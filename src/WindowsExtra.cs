using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace InstantLock
{
    public class FlatRadio : Control
    {
        private bool hover;
        private bool check;
        public string GroupName = "g";
        public event EventHandler CheckedChanged;
        public FlatRadio()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            Font = new Font("SimSun", 9.75f);
        }
        public bool Checked
        {
            get { return check; }
            set
            {
                if (check == value) return;
                check = value;
                Invalidate();
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnClick(EventArgs e)
        {
            if (!Enabled) return;
            if (Parent != null)
            {
                foreach (Control c in Parent.Controls)
                {
                    FlatRadio r = c as FlatRadio;
                    if (r != null && r != this && r.GroupName == GroupName) r.Checked = false;
                }
            }
            Checked = true;
            base.OnClick(e);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            int d = 13;
            Rectangle c = new Rectangle(0, (Height - d) / 2, d, d);
            using (SolidBrush b = new SolidBrush(Color.White)) g.FillEllipse(b, c);
            using (Pen p = new Pen(Checked ? Pal.CyanDark : (hover ? Pal.CyanDark : Pal.Line), 1.4f)) g.DrawEllipse(p, c);
            if (Checked)
            {
                Rectangle inr = new Rectangle(c.X + 3, c.Y + 3, d - 6, d - 6);
                using (SolidBrush b = new SolidBrush(Pal.CyanDark)) g.FillEllipse(b, inr);
            }
            Rectangle t = new Rectangle(c.Right + 7, 0, Math.Max(10, Width - c.Right - 7), Height);
            TextRenderer.DrawText(g, Text, Font, t, Enabled ? Pal.Text : Pal.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
    }

    public class ProgressForm : SkinForm
    {
        private FlatGauge g1, g2;
        private SkinLabel lblFile, lblTitle;
        private FlatButton stop;
        private volatile bool cancelled;
        private Thread worker;
        private bool finished;

        public ProgressForm(string title)
        {
            ClientSize = new Size(360, 210);
            Text = title;
            Build(title);
        }

        private void Build(string title)
        {
            IconButton close = new IconButton();
            close.Icon = Glyph.Close;
            close.Location = new Point(319, 0); close.Size = new Size(41, 20);
            close.Click += delegate { cancelled = true; };
            Controls.Add(close);

            lblTitle = Ui.Label(title, 12, 8, 280, 20, ContentAlignment.MiddleLeft, Pal.White, 10.5f, FontStyle.Bold, Pal.HeaderAt(0.04f));
            Controls.Add(lblTitle);

            SkinLabel d1 = Ui.Label("当前文件：", 22, 42, 78, 20, ContentAlignment.MiddleLeft, Pal.Text, 9f, FontStyle.Regular);
            Controls.Add(d1);
            lblFile = Ui.Label("", 99, 42, 236, 16, ContentAlignment.MiddleLeft, Pal.Muted, 9f, FontStyle.Regular);
            lblFile.Ellipsis = true;
            Controls.Add(lblFile);

            g1 = new FlatGauge();
            g1.Location = new Point(22, 61); g1.Size = new Size(313, 25);
            Controls.Add(g1);

            SkinLabel d2 = Ui.Label("总进度：", 22, 110, 83, 20, ContentAlignment.MiddleLeft, Pal.Text, 9f, FontStyle.Regular);
            Controls.Add(d2);

            g2 = new FlatGauge();
            g2.Location = new Point(22, 129); g2.Size = new Size(313, 25);
            Controls.Add(g2);

            stop = new FlatButton();
            stop.Accent = false;
            stop.Text = "停止";
            stop.Location = new Point(240, 174); stop.Size = new Size(90, 28);
            stop.Click += delegate { cancelled = true; stop.Enabled = false; };
            Controls.Add(stop);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            PaintChrome(e.Graphics, 36, false, 0);
            base.OnPaint(e);
        }

        public void Start(Action<Action<ProgressInfo>, Func<bool>> work)
        {
            IntPtr force = Handle;
            worker = new Thread(delegate()
            {
                try
                {
                    work(delegate(ProgressInfo p) { Report(p); }, delegate { return cancelled; });
                }
                catch { }
                finally { Done(); }
            });
            worker.IsBackground = true;
            worker.Start();
        }

        private void Report(ProgressInfo p)
        {
            if (finished || !IsHandleCreated) return;
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    if (finished) return;
                    if (!string.IsNullOrEmpty(p.CurrentFile)) { lblFile.Text = p.CurrentFile; lblFile.Invalidate(); }
                    if (p.CurrentPercent > 0) g1.Set(p.CurrentPercent);
                    if (p.TotalPercent > 0) g2.Set(p.TotalPercent);
                });
            }
            catch { }
        }

        private bool pendingClose;
        private void Done()
        {
            if (finished) return;
            finished = true;
            try
            {
                if (IsHandleCreated)
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (!Visible) { pendingClose = true; return; }
                        DialogResult = DialogResult.OK;
                        Close();
                    });
                }
            }
            catch { }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (pendingClose || finished)
            {
                DialogResult = DialogResult.OK;
                Close();
            }
        }
    }

    public class TempForm : SkinForm
    {
        private Session session;
        private string mirrorRoot;
        private char drive;
        private NotifyIcon tray;
        private FlatCheck chkTop, chkTray;
        private SkinLabel lblFolder;
        private bool relocked;
        private bool cancelProtection;

        public TempForm(Session s, string mirror, char drv)
        {
            session = s; mirrorRoot = mirror; drive = drv;
            ClientSize = new Size(370, 245);
            Text = "临时解密";
            Build();
        }

        private void Build()
        {
            IconButton close = new IconButton();
            close.Icon = Glyph.Close;
            close.Location = new Point(327, 0); close.Size = new Size(41, 20);
            close.Click += delegate { Close(); };
            Controls.Add(close);

            IconButton ico = new IconButton();
            ico.Icon = Glyph.Folder; ico.IconColor = Pal.CyanDark;
            ico.Location = new Point(7, 53); ico.Size = new Size(16, 16);
            Controls.Add(ico);

            lblFolder = Ui.Label(Path.GetFileName(session.Folder), 30, 52, 330, 20, ContentAlignment.MiddleLeft, Pal.Text, 9f, FontStyle.Bold);
            lblFolder.Ellipsis = true;
            Controls.Add(lblFolder);

            FlatButton b1 = new FlatButton();
            b1.Accent = false;
            b1.Text = "最小化到托盘";
            b1.Location = new Point(58, 87); b1.Size = new Size(259, 28);
            b1.Click += delegate { ToTray(); };
            Controls.Add(b1);

            FlatButton b2 = new FlatButton();
            b2.Text = "恢复加密";
            b2.Location = new Point(58, 124); b2.Size = new Size(259, 28);
            b2.Click += delegate { Relock(); };
            Controls.Add(b2);

            FlatButton b3 = new FlatButton();
            b3.Accent = false;
            b3.Text = "取消文件加密";
            b3.Location = new Point(58, 163); b3.Size = new Size(259, 28);
            b3.Click += delegate { CancelProtection(); };
            Controls.Add(b3);

            chkTop = new FlatCheck();
            chkTop.Text = "窗口始终在最前方";
            chkTop.Checked = AppConfig.Current.StayOnTop;
            chkTop.Location = new Point(58, 203); chkTop.Size = new Size(231, 25);
            chkTop.CheckedChanged += delegate { TopMost = chkTop.Checked; AppConfig.Current.StayOnTop = chkTop.Checked; AppConfig.Current.Save(); };
            Controls.Add(chkTop);

            chkTray = new FlatCheck();
            chkTray.Text = "解密后自动最小化到托盘";
            chkTray.Checked = AppConfig.Current.AutoToTray;
            chkTray.Location = new Point(58, 228); chkTray.Size = new Size(231, 23);
            chkTray.CheckedChanged += delegate { AppConfig.Current.AutoToTray = chkTray.Checked; AppConfig.Current.Save(); };
            Controls.Add(chkTray);

            TopMost = AppConfig.Current.StayOnTop;

            tray = new NotifyIcon();
            tray.Icon = SystemIcons.Shield;
            tray.Text = Program.AppName + " - 临时解密中";
            tray.Visible = false;
            ContextMenuStrip cm = new ContextMenuStrip();
            cm.Items.Add("显示窗口", null, delegate { FromTray(); });
            cm.Items.Add("恢复加密", null, delegate { FromTray(); Relock(); });
            tray.ContextMenuStrip = cm;
            tray.DoubleClick += delegate { FromTray(); };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            PaintChrome(e.Graphics, 44, false, 0);
            string cap = "临时解密中";
            if (drive != '\0') cap += "   已映射 " + drive + ": 盘";
            using (SolidBrush b = new SolidBrush(Pal.White))
            using (Font f = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold))
                e.Graphics.DrawString(cap, f, b, 10, 12);
            base.OnPaint(e);
        }

        private void ToTray()
        {
            tray.Visible = true;
            tray.BalloonTipTitle = Program.AppName;
            tray.BalloonTipText = "文件夹处于临时解密状态，双击图标可以恢复窗口。";
            tray.ShowBalloonTip(2000);
            Hide();
        }

        private void FromTray()
        {
            tray.Visible = false;
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        private void Relock()
        {
            Exception err = null;
            using (ProgressForm pf = new ProgressForm("正在恢复加密"))
            {
                pf.Start(delegate(Action<ProgressInfo> rep, Func<bool> cancel)
                {
                    try { Locker.Relock(session, rep, cancel); }
                    catch (Exception ex) { err = ex; }
                });
                pf.ShowDialog(this);
            }
            if (err != null) { Msg.Error(this, err.Message); return; }
            relocked = true;
            Cleanup();
            Close();
        }

        private void CancelProtection()
        {
            if (!Msg.Ask(this, "取消加密会彻底删除加密信息，文件夹将保持未加密状态。是否继续？")) return;
            cancelProtection = true;
            try
            {
                SessionStore.Remove(session.Folder);
                string c = Vault.ContainerPath(session.Folder);
                if (Directory.Exists(c)) Locker.DeleteTree(c);
            }
            catch { }
            Cleanup();
            Close();
        }

        private void Cleanup()
        {
            if (mirrorRoot != null) Mount.RemoveMirror(mirrorRoot, drive);
            if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!relocked && !cancelProtection)
            {
                // 关闭窗口即静默恢复加密；想保持解密状态请点“取消文件加密”
                e.Cancel = true;
                Relock();
                return;
            }
            Cleanup();
            base.OnFormClosing(e);
        }
    }

    public class SettingsForm : SkinForm
    {
        private FlatButton tabGeneral, tabSearch, tabDiag, ok, cancel;
        private Panel pnlGeneral, pnlSearch, pnlDiag;
        private ComboBox cbStrength, cbLang;
        private FlatCheck chkLoginPwd, chkCopy, chkShowSign, chkRightMenu;
        private FlatRadio rbWinIcon, rbRecIcon;
        private SkinEdit edtRightMenu;
        private FlatButton btnChangePwd;
        private ListView list;
        private ComboBox cbDrive;
        private SkinEdit edtPath, edtNewPwd;
        private SkinLabel lblStatus;

        public SettingsForm()
        {
            ClientSize = new Size(480, 356);
            Text = "设置";
            Build();
        }

        private void Build()
        {
            IconButton close = new IconButton();
            close.Icon = Glyph.Close;
            close.Location = new Point(401, 0); close.Size = new Size(41, 20);
            close.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(close);

            IconButton lgo = new IconButton();
            lgo.Icon = Glyph.Gear; lgo.IconColor = Pal.White;
            lgo.Location = new Point(9, 11); lgo.Size = new Size(16, 16);
            Controls.Add(lgo);
            SkinLabel t = Ui.Label("设置", 30, 11, 60, 18, ContentAlignment.MiddleLeft, Pal.White, 9.5f, FontStyle.Bold, Pal.HeaderAt(0.07f));
            Controls.Add(t);

            tabGeneral = Tab("常规", 10, 34, 68);
            tabSearch = Tab("搜索", 84, 34, 68);
            tabDiag = Tab("密码与诊断", 158, 34, 120);
            tabGeneral.Click += delegate { ShowTab(0); };
            tabSearch.Click += delegate { ShowTab(1); };
            tabDiag.Click += delegate { ShowTab(2); };

            pnlGeneral = Panel(65);
            pnlSearch = Panel(65);
            pnlDiag = Panel(65);

            BuildGeneral();
            BuildSearch();
            BuildDiag();

            ok = new FlatButton();
            ok.Text = "确定";
            ok.Location = new Point(158, 322); ok.Size = new Size(129, 28);
            ok.Click += delegate { Apply(); DialogResult = DialogResult.OK; Close(); };
            Controls.Add(ok);

            cancel = new FlatButton();
            cancel.Accent = false;
            cancel.Text = "取消";
            cancel.Location = new Point(298, 322); cancel.Size = new Size(129, 28);
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancel);

            ShowTab(0);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            PaintChrome(e.Graphics, 60, false, 0);
            base.OnPaint(e);
        }

        private FlatButton Tab(string text, int x, int y, int w)
        {
            FlatButton b = new FlatButton();
            b.Accent = false;
            b.Text = text;
            b.Location = new Point(x, y); b.Size = new Size(w, 30);
            Controls.Add(b);
            return b;
        }

        private Panel Panel(int y)
        {
            Panel p = new Panel();
            p.Location = new Point(10, y);
            p.Size = new Size(460, 246);
            p.BackColor = Color.White;
            p.Visible = false;
            Controls.Add(p);
            p.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen pen = new Pen(Pal.Line)) e.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1);
            };
            return p;
        }

        private SkinLabel L(Control p, string text, int x, int y, int w, int h, ContentAlignment a, Color c, float sz, FontStyle st)
        {
            SkinLabel l = Ui.Label(text, x, y, w, h, a, c, sz, st, Color.White);
            p.Controls.Add(l);
            return l;
        }

        private ComboBox Cb(Control p, int x, int y, int w)
        {
            ComboBox c = new ComboBox();
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.FlatStyle = FlatStyle.Flat;
            c.Font = new Font("Microsoft YaHei UI", 9f);
            c.Location = new Point(x, y); c.Size = new Size(w, 24);
            p.Controls.Add(c);
            return c;
        }

        private void BuildGeneral()
        {
            Panel p = pnlGeneral;
            L(p, "加密强度：", 34, 24, 84, 20, ContentAlignment.MiddleLeft, Pal.Text, 9f, FontStyle.Regular);
            cbStrength = Cb(p, 149, 20, 240);
            cbStrength.Items.Add("普通 (瞬间锁定)");
            cbStrength.Items.Add("高级 (AES-256 完全加密)");
            cbStrength.SelectedIndex = AppConfig.Current.DefaultStrength == 1 ? 1 : 0;

            L(p, "语言：", 34, 52, 61, 20, ContentAlignment.MiddleLeft, Pal.Text, 9f, FontStyle.Regular);
            cbLang = Cb(p, 149, 50, 240);
            cbLang.Items.Add("简体中文");
            cbLang.SelectedIndex = 0;
            chkLoginPwd = new FlatCheck();
            chkLoginPwd.Text = "登录密码";
            chkLoginPwd.Checked = AppConfig.Current.LoginPwdEnabled;
            chkLoginPwd.Location = new Point(29, 83); chkLoginPwd.Size = new Size(132, 25);
            p.Controls.Add(chkLoginPwd);

            btnChangePwd = new FlatButton();
            btnChangePwd.Accent = false;
            btnChangePwd.Text = "更改...";
            btnChangePwd.Location = new Point(293, 86); btnChangePwd.Size = new Size(96, 22);
            btnChangePwd.Click += delegate { ChangeLoginPwd(); };
            p.Controls.Add(btnChangePwd);

            chkCopy = new FlatCheck();
            chkCopy.Text = "在加密文件夹内创建解密程序";
            chkCopy.Checked = AppConfig.Current.CopyUnlocker;
            chkCopy.Location = new Point(29, 112); chkCopy.Size = new Size(400, 25);
            p.Controls.Add(chkCopy);

            chkShowSign = new FlatCheck();
            chkShowSign.Text = "显示加密标志（显示为特殊文件夹图标）";
            chkShowSign.Checked = AppConfig.Current.ShowLockIcon;
            chkShowSign.Location = new Point(29, 137); chkShowSign.Size = new Size(400, 22);
            p.Controls.Add(chkShowSign);

            rbWinIcon = new FlatRadio();
            rbWinIcon.Text = "隐藏文件夹";
            rbWinIcon.GroupName = "icon";
            rbWinIcon.Checked = AppConfig.Current.HideFolder;
            rbWinIcon.Location = new Point(55, 162); rbWinIcon.Size = new Size(129, 20);
            p.Controls.Add(rbWinIcon);

            rbRecIcon = new FlatRadio();
            rbRecIcon.Text = "保持可见（推荐）";
            rbRecIcon.GroupName = "icon";
            rbRecIcon.Checked = !AppConfig.Current.HideFolder;
            rbRecIcon.Location = new Point(223, 162); rbRecIcon.Size = new Size(186, 23);
            p.Controls.Add(rbRecIcon);

            chkRightMenu = new FlatCheck();
            chkRightMenu.Text = "在资源管理器右键菜单中显示加密/解密";
            chkRightMenu.Checked = AppConfig.Current.RightMenuEnabled;
            chkRightMenu.Location = new Point(29, 187); chkRightMenu.Size = new Size(400, 25);
            p.Controls.Add(chkRightMenu);
            L(p, "显示文字：", 38, 218, 88, 20, ContentAlignment.MiddleRight, Pal.Text, 9f, FontStyle.Regular);
            edtRightMenu = new SkinEdit();
            edtRightMenu.Location = new Point(130, 217); edtRightMenu.Size = new Size(228, 22);
            edtRightMenu.Text = AppConfig.Current.RightMenuText;
            p.Controls.Add(edtRightMenu);
        }

        private void BuildSearch()
        {
            Panel p = pnlSearch;
            L(p, "扫描指定盘符，查找所有被本程序加密的文件夹。", 32, 13, 380, 20, ContentAlignment.TopLeft, Pal.Muted, 9f, FontStyle.Regular);
            L(p, "盘符：", 42, 63, 40, 20, ContentAlignment.MiddleLeft, Pal.Text, 9f, FontStyle.Regular);
            cbDrive = Cb(p, 80, 61, 209);
            foreach (DriveInfo d in DriveInfo.GetDrives())
            {
                try { if (d.IsReady) cbDrive.Items.Add(d.Name + "  " + d.VolumeLabel); } catch { }
            }
            if (cbDrive.Items.Count > 0) cbDrive.SelectedIndex = 0;

            FlatButton btn = new FlatButton();
            btn.Text = "搜索";
            btn.Location = new Point(324, 56); btn.Size = new Size(90, 28);
            btn.Click += delegate { DoSearch(); };
            p.Controls.Add(btn);

            list = new ListView();
            list.Location = new Point(32, 90); list.Size = new Size(382, 138);
            list.View = View.Details;
            list.FullRowSelect = true;
            list.GridLines = false;
            list.Columns.Add("名称", 120);
            list.Columns.Add("路径", 240);
            list.DoubleClick += delegate { if (list.SelectedItems.Count > 0) Process.Start("explorer.exe", "\"" + list.SelectedItems[0].SubItems[1].Text + "\""); };
            p.Controls.Add(list);

            lblStatus = Ui.Label("就绪", 32, 228, 380, 20, ContentAlignment.MiddleLeft, Pal.Muted, 9f, FontStyle.Regular);
            p.Controls.Add(lblStatus);
        }

        private void BuildDiag()
        {
            Panel p = pnlDiag;
            L(p, "修改加密文件夹的密码，或检查加密数据是否完整。所有操作都需要原密码。", 32, 22, 400, 20, ContentAlignment.TopLeft, Pal.Muted, 9f, FontStyle.Regular);
            L(p, "选择要处理的文件夹：", 32, 69, 220, 20, ContentAlignment.MiddleLeft, Pal.Text, 9f, FontStyle.Regular);

            edtPath = new SkinEdit();
            edtPath.Location = new Point(32, 88); edtPath.Size = new Size(319, 22);
            p.Controls.Add(edtPath);

            FlatButton sel = new FlatButton();
            sel.Accent = false;
            sel.Text = "...";
            sel.Location = new Point(357, 88); sel.Size = new Size(49, 22);
            sel.Click += delegate
            {
                using (FolderBrowserDialog d = new FolderBrowserDialog())
                {
                    d.Description = "选择加密的文件夹";
                    if (d.ShowDialog(this) == DialogResult.OK) edtPath.Text = d.SelectedPath;
                }
            };
            p.Controls.Add(sel);

            L(p, "原密码：", 32, 120, 148, 20, ContentAlignment.MiddleLeft, Pal.Text, 9f, FontStyle.Regular);
            SkinEdit oldPwd = new SkinEdit();
            oldPwd.Location = new Point(32, 139); oldPwd.Size = new Size(180, 22);
            oldPwd.UseSystemPasswordChar = true;
            p.Controls.Add(oldPwd);

            L(p, "新密码：", 222, 120, 148, 20, ContentAlignment.MiddleLeft, Pal.Text, 9f, FontStyle.Regular);
            edtNewPwd = new SkinEdit();
            edtNewPwd.Location = new Point(222, 139); edtNewPwd.Size = new Size(184, 22);
            edtNewPwd.UseSystemPasswordChar = true;
            p.Controls.Add(edtNewPwd);
            FlatButton chg = new FlatButton();
            chg.Text = "修改密码";
            chg.Location = new Point(32, 173); chg.Size = new Size(129, 28);
            chg.Click += delegate
            {
                if (edtPath.Text.Length == 0 || oldPwd.Text.Length == 0 || edtNewPwd.Text.Length == 0) { Msg.Warn(this, "请填写文件夹、原密码和新密码。"); return; }
                try
                {
                    VaultIO.ChangePassword(edtPath.Text, oldPwd.Text, edtNewPwd.Text);
                    Msg.Info(this, "密码已修改。");
                }
                catch (Exception ex) { Msg.Error(this, ex.Message); }
            };
            p.Controls.Add(chg);

            FlatButton chk = new FlatButton();
            chk.Accent = false;
            chk.Text = "检查完整性";
            chk.Location = new Point(175, 173); chk.Size = new Size(129, 28);
            chk.Click += delegate
            {
                if (edtPath.Text.Length == 0 || oldPwd.Text.Length == 0) { Msg.Warn(this, "请填写文件夹和密码。"); return; }
                try
                {
                    byte[] ke, km;
                    Vault v = VaultIO.Open(edtPath.Text, oldPwd.Text, out ke, out km);
                    int files = 0;
                    foreach (Entry e in v.Entries) if (!e.IsDir) files++;
                    Msg.Info(this, "加密信息完整。\r\n\r\n文件数量：" + files + "\r\n加密方式：" + (v.Strength == Strength.Instant ? "瞬间锁定" : "AES-256 完全加密"));
                }
                catch (Exception ex) { Msg.Error(this, ex.Message); }
            };
            p.Controls.Add(chk);
        }

        private void ShowTab(int i)
        {
            pnlGeneral.Visible = i == 0;
            pnlSearch.Visible = i == 1;
            pnlDiag.Visible = i == 2;
            tabGeneral.Accent = i == 0; tabGeneral.Invalidate();
            tabSearch.Accent = i == 1; tabSearch.Invalidate();
            tabDiag.Accent = i == 2; tabDiag.Invalidate();
        }

        private void DoSearch()
        {
            if (cbDrive.SelectedIndex < 0) { Msg.Warn(this, "请选择盘符。"); return; }
            string drive = cbDrive.Items[cbDrive.SelectedIndex].ToString().Substring(0, 2);
            list.Items.Clear();
            lblStatus.Text = "正在搜索 " + drive + " ...";
            Application.DoEvents();
            int found = 0;
            try
            {
                Queue<string> q = new Queue<string>();
                q.Enqueue(drive + "\\");
                while (q.Count > 0)
                {
                    string d = q.Dequeue();
                    string[] subs;
                    try { subs = Directory.GetDirectories(d); } catch { continue; }
                    foreach (string s in subs)
                    {
                        if (File.Exists(Path.Combine(s, Vault.ContainerDir, Vault.VaultFile)))
                        {
                            ListViewItem it = new ListViewItem(Path.GetFileName(s));
                            it.SubItems.Add(s);
                            list.Items.Add(it);
                            found++;
                        }
                        q.Enqueue(s);
                    }
                    Application.DoEvents();
                }
                lblStatus.Text = "搜索完成，共找到 " + found + " 个加密文件夹。"; lblStatus.Invalidate();
            }
            catch (Exception ex) { lblStatus.Text = "搜索中断：" + ex.Message; }
        }

        private void ChangeLoginPwd()
        {
            using (PwdGateForm f = new PwdGateForm(true))
            {
                if (f.ShowDialog(this) == DialogResult.OK)
                {
                    AppConfig.Current.LoginPwdEnabled = true;
                    AppConfig.Current.LoginPwdHash = AppConfig.HashPassword(f.Result);
                    Msg.Info(this, "登录密码已设置。下次启动本程序时需要输入。");
                }
            }
        }

        private void Apply()
        {
            AppConfig.Current.DefaultStrength = cbStrength.SelectedIndex;
            AppConfig.Current.CopyUnlocker = chkCopy.Checked;
            AppConfig.Current.ShowLockIcon = chkShowSign.Checked;
            AppConfig.Current.HideFolder = rbWinIcon.Checked;
            AppConfig.Current.RightMenuEnabled = chkRightMenu.Checked;
            if (edtRightMenu.Text.Length > 0) AppConfig.Current.RightMenuText = edtRightMenu.Text;
            if (!chkLoginPwd.Checked) { AppConfig.Current.LoginPwdEnabled = false; AppConfig.Current.LoginPwdHash = ""; }
            AppConfig.Current.Save();
            Shell.Apply();
        }
    }

    public class PwdGateForm : SkinForm
    {
        private SkinEdit edt;
        private bool setMode;
        public string Result = "";

        public PwdGateForm() : this(false) { }

        public PwdGateForm(bool setNew)
        {
            setMode = setNew;
            ClientSize = new Size(390, 170);
            Text = "登录密码";
            Build();
        }

        private void Build()
        {
            IconButton close = new IconButton();
            close.Icon = Glyph.Close;
            close.Location = new Point(349, 0); close.Size = new Size(41, 20);
            close.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(close);

            SkinLabel t = Ui.Label(setMode ? "设置登录密码" : "请输入登录密码", 16, 40, 358, 22, ContentAlignment.MiddleLeft, Pal.Text, 10f, FontStyle.Bold);
            Controls.Add(t);

            edt = new SkinEdit();
            edt.Location = new Point(16, 72); edt.Size = new Size(358, 28);
            edt.UseSystemPasswordChar = true;
            Controls.Add(edt);

            FlatButton ok = new FlatButton();
            ok.Text = "确定";
            ok.Location = new Point(140, 118); ok.Size = new Size(110, 30);
            ok.Click += delegate { Ok(); };
            Controls.Add(ok);

            FlatButton no = new FlatButton();
            no.Accent = false;
            no.Text = "取消";
            no.Location = new Point(264, 118); no.Size = new Size(110, 30);
            no.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(no);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            PaintChrome(e.Graphics, 28, false, 0);
            base.OnPaint(e);
        }

        private void Ok()
        {
            if (edt.Text.Length == 0) { Msg.Warn(this, "请输入密码。"); return; }
            if (setMode)
            {
                if (edt.Text.Length < 4) { Msg.Warn(this, "登录密码至少 4 位。"); return; }
                Result = edt.Text;
                DialogResult = DialogResult.OK;
                Close();
                return;
            }
            if (AppConfig.HashPassword(edt.Text) != AppConfig.Current.LoginPwdHash)
            {
                Msg.Warn(this, "密码不正确！");
                edt.Text = "";
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    public static class Shell
    {
        private const string Key = @"Software\Classes\Directory\shell\InstantLockFolder";

        public static void Apply()
        {
            try
            {
                if (AppConfig.Current.RightMenuEnabled)
                {
                    using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Key))
                    {
                        k.SetValue("", AppConfig.Current.RightMenuText);
                        k.SetValue("Icon", Application.ExecutablePath);
                        using (RegistryKey c = k.CreateSubKey("command"))
                            c.SetValue("", "\"" + Application.ExecutablePath + "\" \"%1\"");
                    }
                }
                else
                {
                    Registry.CurrentUser.DeleteSubKeyTree(Key, false);
                }
            }
            catch { }
        }
    }
}













