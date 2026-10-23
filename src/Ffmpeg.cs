using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;

namespace FaceGuard
{
    internal static class Ffmpeg
    {
        private static string _cached;
        private static bool _searched;

        public static string Find()
        {
            if (_searched) return _cached;
            _searched = true;
            try
            {
                string cfg = Store.Cfg == null ? "" : Store.Cfg.FfmpegPath;
                if (!string.IsNullOrEmpty(cfg) && File.Exists(cfg)) { _cached = cfg; return _cached; }

                string local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
                if (File.Exists(local)) { _cached = local; return _cached; }

                string[] guesses = new string[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"ffmpeg\bin\ffmpeg.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"ffmpeg\bin\ffmpeg.exe"),
                    @"C:\ffmpeg\bin\ffmpeg.exe",
                    @"C:\ffmpeg\ffmpeg.exe"
                };
                foreach (string g in guesses)
                {
                    if (File.Exists(g)) { _cached = g; return _cached; }
                }

                string path = Environment.GetEnvironmentVariable("PATH");
                if (!string.IsNullOrEmpty(path))
                {
                    foreach (string dir in path.Split(';'))
                    {
                        if (string.IsNullOrEmpty(dir)) continue;
                        try
                        {
                            string f = Path.Combine(dir.Trim(), "ffmpeg.exe");
                            if (File.Exists(f)) { _cached = f; return _cached; }
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return null;
        }

        public static Bitmap Grab(DeviceInfo dev, int width, int height, int timeoutMs)
        {
            string exe = Find();
            if (string.IsNullOrEmpty(exe))
            {
                throw new CaptureException("未找到 ffmpeg.exe（DirectShow 取流失败，且没有可用的 ffmpeg 后备）");
            }

            string tmp = Path.Combine(Store.StateDir, "shot_" + Guid.NewGuid().ToString("N") + ".jpg");
            try
            {
                string lastErr = "";
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    // 第一次先按请求分辨率，失败再让 ffmpeg 自己协商
                    string err;
                    int code = Run(exe, dev.Name, width, height, tmp, timeoutMs, out err);
                    if (code == 0)
                    {
                        Bitmap ok = TryLoad(tmp);
                        if (ok != null) return ok;
                    }

                    if (attempt == 0 && width > 0)
                    {
                        string err2;
                        int code2 = Run(exe, dev.Name, 0, 0, tmp, timeoutMs, out err2);
                        if (code2 == 0)
                        {
                            Bitmap ok2 = TryLoad(tmp);
                            if (ok2 != null) return ok2;
                        }
                        lastErr = err2.Length > 0 ? err2 : err;
                    }
                    else
                    {
                        lastErr = err;
                    }

                    Log.Warn("ffmpeg 第 " + (attempt + 1) + " 次取流失败: " + Short(lastErr));
                    try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                    if (attempt < 2) Thread.Sleep(350);
                }
                throw new CaptureException("ffmpeg 取流失败: " + Short(lastErr));
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        }

        private static Bitmap TryLoad(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                FileInfo fi = new FileInfo(path);
                if (fi.Length < 1024) return null;
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    using (Image img = Image.FromStream(fs))
                    {
                        return new Bitmap(img);
                    }
                }
            }
            catch { return null; }
        }

        private static int Run(string exe, string deviceName, int width, int height, string outPath, int timeoutMs, out string stderr)
        {
            string sizeArg = width > 0 && height > 0
                ? string.Format("-video_size {0}x{1} ", width, height)
                : "";
            string args = string.Format(
                "-hide_banner -loglevel error -f dshow {0}-i \"video={1}\" -frames:v 1 -y -update 1 \"{2}\"",
                sizeArg, deviceName, outPath);

            ProcessStartInfo psi = new ProcessStartInfo(exe, args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardError = true;
            psi.RedirectStandardOutput = true;

            StringBuilder sb = new StringBuilder();
            using (Process p = new Process())
            {
                p.StartInfo = psi;
                p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
                {
                    if (e.Data != null) { lock (sb) sb.AppendLine(e.Data); }
                };
                p.Start();
                p.BeginErrorReadLine();
                p.BeginOutputReadLine();
                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(); } catch { }
                    stderr = "超时 " + timeoutMs + "ms";
                    return -1;
                }
                stderr = sb.ToString();
                return p.ExitCode;
            }
        }

        private static string Short(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Trim();
            if (s.Length > 300) s = s.Substring(s.Length - 300);
            return s.Replace("\r", " ").Replace("\n", " | ");
        }
    }
}
