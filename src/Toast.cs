using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace FaceGuard
{
    /// <summary>启动自检：只看目录是否可写、有没有摄像头、取流组件在不在，不打开摄像头。</summary>
    internal static class Health
    {
        public static string Check()
        {
            try
            {
                if (!Directory.Exists(Store.CaptureDir)) return "存储目录不可用";
                string probe = Path.Combine(Store.CaptureDir, ".check");
                File.WriteAllText(probe, "1");
                File.Delete(probe);

                if (DShow.Enumerate().Count == 0) return "未检测到摄像头";

                string b = (Store.Cfg.Backend == null ? "auto" : Store.Cfg.Backend).Trim().ToLowerInvariant();
                if (b == "ffmpeg" && Ffmpeg.Find() == null) return "缺少取流组件";
                return "";
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }
    }

    /// <summary>右下角自动消失的小提示，无按钮、不进任务栏、点击即关。</summary>
    internal sealed class ToastForm : Form
    {
        private readonly Timer _timer = new Timer();

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE：不抢焦点
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW：不进 Alt+Tab
                return cp;
            }
        }

        public ToastForm(string title, string text, int ms)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(28, 28, 30);
            ForeColor = Color.White;
            Size = new Size(340, 96);

            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Right - Width - 24, wa.Bottom - Height - 24);

            Label t = new Label();
            t.Text = title;
            t.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
            t.ForeColor = Color.White;
            t.AutoSize = false;
            t.SetBounds(18, 14, Width - 36, 26);
            t.BackColor = Color.Transparent;

            Label m = new Label();
            m.Text = text;
            m.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular);
            m.ForeColor = Color.FromArgb(220, 220, 220);
            m.AutoSize = false;
            m.SetBounds(18, 46, Width - 36, 36);
            m.BackColor = Color.Transparent;

            Controls.Add(t);
            Controls.Add(m);

            EventHandler close = delegate { Close(); };
            Click += close;
            t.Click += close;
            m.Click += close;

            _timer.Interval = ms < 1200 ? 1200 : ms;
            _timer.Tick += delegate { _timer.Stop(); Close(); };
            _timer.Start();
        }
    }

    internal static class Toast
    {
        public static void Show(string title, string text, int ms)
        {
            ToastForm f = new ToastForm(title, text, ms);
            f.Show();
        }

        public static void ShowBlocking(string title, string text, int ms)
        {
            using (ToastForm f = new ToastForm(title, text, ms))
            {
                Application.Run(f);
            }
        }
    }

    internal static class Notify
    {
        public static int Run()
        {
            Application.EnableVisualStyles();
            string err = Health.Check();
            if (Store.Cfg.StartupToast)
            {
                string text = err.Length == 0 ? "已启动，运行正常" : ("启动异常：" + err);
                Toast.ShowBlocking("FaceGuard", text, err.Length == 0 ? 4000 : 8000);
            }
            Log.Info("启动自检: " + (err.Length == 0 ? "通过" : err));
            return 0;
        }
    }
}
