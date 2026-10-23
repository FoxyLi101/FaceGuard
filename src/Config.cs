using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace FaceGuard
{
    internal sealed class Config
    {
        public string Device = "auto";
        public int Width = 1280;
        public int Height = 720;
        public int Quality = 85;
        public string StorageDir = "";
        public int RetentionDays = 14;
        public int MaxFiles = 3000;

        public string Backend = "auto";
        public string FfmpegPath = "";

        public bool OnLock = true;
        public bool OnUnlock = true;
        public bool OnResume = true;
        public int LockPollMs = 4000;
        public int PollWidth = 640;
        public int PollHeight = 480;
        public int UnlockBurst = 3;
        public int BurstIntervalMs = 600;
        public int CaptureTimeoutMs = 8000;
        public int CooldownMs = 1500;

        public bool FailedLogon = true;
        public bool SuccessLogon = true;

        public bool Dedupe = true;
        public int DedupeWindowMs = 6000;
        public int DedupeDistance = 6;

        public bool StartupToast = true;

        private Dictionary<string, string> _raw = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static Config Load(string path)
        {
            Config c = new Config();
            if (!File.Exists(path)) return c;
            string[] lines;
            try { lines = File.ReadAllLines(path, Encoding.UTF8); }
            catch { return c; }

            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";") || line.StartsWith("[")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string k = line.Substring(0, eq).Trim();
                string v = line.Substring(eq + 1).Trim();
                c._raw[k] = v;
            }

            c.Device = c.Str("device", c.Device);
            c.Width = c.Int("width", c.Width);
            c.Height = c.Int("height", c.Height);
            c.Quality = c.Int("quality", c.Quality);
            c.StorageDir = c.Str("storageDir", c.StorageDir);
            c.RetentionDays = c.Int("retentionDays", c.RetentionDays);
            c.MaxFiles = c.Int("maxFiles", c.MaxFiles);
            c.Backend = c.Str("backend", c.Backend);
            c.FfmpegPath = c.Str("ffmpegPath", c.FfmpegPath);

            c.OnLock = c.Bool("onLock", c.OnLock);
            c.OnUnlock = c.Bool("onUnlock", c.OnUnlock);
            c.OnResume = c.Bool("onResume", c.OnResume);
            c.LockPollMs = c.Int("lockPollMs", c.LockPollMs);
            c.PollWidth = c.Int("pollWidth", c.PollWidth);
            c.PollHeight = c.Int("pollHeight", c.PollHeight);
            c.UnlockBurst = c.Int("unlockBurst", c.UnlockBurst);
            c.BurstIntervalMs = c.Int("burstIntervalMs", c.BurstIntervalMs);
            c.CaptureTimeoutMs = c.Int("captureTimeoutMs", c.CaptureTimeoutMs);
            c.CooldownMs = c.Int("cooldownMs", c.CooldownMs);

            c.FailedLogon = c.Bool("failedLogon", c.FailedLogon);
            c.SuccessLogon = c.Bool("successLogon", c.SuccessLogon);

            c.Dedupe = c.Bool("dedupe", c.Dedupe);
            c.DedupeWindowMs = c.Int("dedupeWindowMs", c.DedupeWindowMs);
            c.DedupeDistance = c.Int("dedupeDistance", c.DedupeDistance);
            c.StartupToast = c.Bool("startupToast", c.StartupToast);

            if (c.Width < 160) c.Width = 160;
            if (c.Height < 120) c.Height = 120;
            if (c.Quality < 30) c.Quality = 30;
            if (c.Quality > 100) c.Quality = 100;
            if (c.RetentionDays < 1) c.RetentionDays = 1;
            return c;
        }

        private string Str(string key, string def)
        {
            string v;
            return _raw.TryGetValue(key, out v) && v.Length > 0 ? v : def;
        }

        private int Int(string key, int def)
        {
            string v;
            if (!_raw.TryGetValue(key, out v)) return def;
            int n;
            return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : def;
        }

        private bool Bool(string key, bool def)
        {
            string v;
            if (!_raw.TryGetValue(key, out v)) return def;
            v = v.Trim().ToLowerInvariant();
            if (v == "1" || v == "true" || v == "yes" || v == "on") return true;
            if (v == "0" || v == "false" || v == "no" || v == "off") return false;
            return def;
        }

        public static void WriteTemplate(string path)
        {
            string text =
"# ============================================================\r\n" +
"#  FaceGuard 登录留影  配置文件\r\n" +
"#  修改后需要重启守护进程（agent / watch）才生效\r\n" +
"#  布尔值: 1=开  0=关\r\n" +
"# ============================================================\r\n" +
"\r\n" +
"# 摄像头: auto=自动选非红外的彩色摄像头；也可写序号(0,1..) 或名称片段(如 FHD)\r\n" +
"device = auto\r\n" +
"\r\n" +
"# 拍摄分辨率（摄像头不支持时自动协商到最接近的值）\r\n" +
"width = 1280\r\n" +
"height = 720\r\n" +
"\r\n" +
"# JPEG 质量 30-100\r\n" +
"quality = 85\r\n" +
"\r\n" +
"# 取流后端: auto=先 DirectShow，失败自动改用 ffmpeg；也可强制 dshow / ffmpeg\r\n" +
"backend = auto\r\n" +
"\r\n" +
"# ffmpeg.exe 路径，留空=自动查找（程序目录、PATH、常见安装位置）\r\n" +
"ffmpegPath =\r\n" +
"\r\n" +
"# 照片根目录，留空 = 自动（C:\\ProgramData\\FaceGuard 或 %LOCALAPPDATA%\\FaceGuard）\r\n" +
"storageDir =\r\n" +
"\r\n" +
"# 保留天数 / 最多文件数（超出后自动删最旧的）\r\n" +
"retentionDays = 14\r\n" +
"maxFiles = 3000\r\n" +
"\r\n" +
"# ------- 触发条件 -------\r\n" +
"# 锁屏瞬间\r\n" +
"onLock = 1\r\n" +
"# 解锁瞬间（拍到一张即停，不连拍、不重复重试）\r\n" +
"onUnlock = 1\r\n" +
"unlockBurst = 3\r\n" +
"# 睡眠唤醒\r\n" +
"onResume = 1\r\n" +
"\r\n" +
"# 停留在锁屏/登录界面时，每隔多少毫秒拍一张（0=关闭）\r\n" +
"# 轮拍用较低分辨率，占用 USB 带宽很小，避免影响 Windows Hello 刷脸\r\n" +
"lockPollMs = 4000\r\n" +
"pollWidth = 640\r\n" +
"pollHeight = 480\r\n" +
"\r\n" +
"# 失败后的最大尝试次数 / 单次取流超时 / 两次触发最小间隔\r\n" +
"burstIntervalMs = 600\r\n" +
"captureTimeoutMs = 8000\r\n" +
"cooldownMs = 1500\r\n" +
"\r\n" +
"# ------- 安全日志触发（需管理员，watch 模式）-------\r\n" +
"# 4625 登录失败（试错密码）\r\n" +
"failedLogon = 1\r\n" +
"# 4624 交互式登录成功\r\n" +
"successLogon = 1\r\n" +
"\r\n" +
"# ------- 去重 -------\r\n" +
"# 画面几乎没变就不重复存（省磁盘，也避免连拍留下 3 张一样的）\r\n" +
"dedupe = 1\r\n" +
"dedupeWindowMs = 6000\r\n" +
"dedupeDistance = 6\r\n" +
"\r\n" +
"# 登录后是否弹一次状态提示（1=弹，0=不弹；不常驻托盘）\r\n" +
"startupToast = 1\r\n";
            File.WriteAllText(path, text, new UTF8Encoding(true));
        }
    }
}
