using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Security.Principal;
using System.Text;

namespace FaceGuard
{
    internal static class Store
    {
        public static string DataDir;
        public static string CaptureDir;
        public static string StateDir;
        public static string ConfigPath;
        public static Config Cfg;

        private static readonly object FileLock = new object();
        private static ImageCodecInfo _jpeg;

        public static void Init()
        {
            string dir;
            bool createdHere = false;

            string envDir = Environment.GetEnvironmentVariable("FACEGUARD_DATA");
            if (!string.IsNullOrEmpty(envDir))
            {
                dir = Path.GetFullPath(envDir);
                createdHere = !Directory.Exists(dir);
                Directory.CreateDirectory(dir);
            }
            else
            {
                string common = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "FaceGuard");
                string local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FaceGuard");

                dir = common;
                try
                {
                    if (!Directory.Exists(common))
                    {
                        Directory.CreateDirectory(common);
                        createdHere = true;
                    }
                    string probe = Path.Combine(common, ".wtest");
                    File.WriteAllText(probe, "1");
                    try { File.Delete(probe); } catch { }
                }
                catch
                {
                    dir = local;
                    createdHere = !Directory.Exists(local);
                    try { Directory.CreateDirectory(local); } catch { }
                }
            }

            DataDir = dir;
            CaptureDir = Path.Combine(dir, "captures");
            StateDir = Path.Combine(dir, "state");
            ConfigPath = Path.Combine(dir, "config.ini");

            Directory.CreateDirectory(CaptureDir);
            Directory.CreateDirectory(StateDir);

            if (!File.Exists(ConfigPath)) Config.WriteTemplate(ConfigPath);
            Cfg = Config.Load(ConfigPath);

            if (!string.IsNullOrEmpty(Cfg.StorageDir))
            {
                try
                {
                    CaptureDir = Cfg.StorageDir;
                    Directory.CreateDirectory(CaptureDir);
                }
                catch (Exception ex)
                {
                    Log.Warn("storageDir 不可用，回退默认目录: " + ex.Message);
                }
            }

            Log.Init(dir);
            if (createdHere) HardenAcl(dir);
        }

        private static void HardenAcl(string dir)
        {
            string marker = Path.Combine(dir, "state", ".acl-done");
            if (File.Exists(marker)) return;
            try
            {
                WindowsIdentity id = WindowsIdentity.GetCurrent();
                string userSid = id.User.Value;
                string grant = string.Format("\"{0}\" /grant:r \"*S-1-5-18:(OI)(CI)F\" \"*S-1-5-32-544:(OI)(CI)F\" \"*{1}:(OI)(CI)F\"", dir, userSid);
                RunIcacls(grant);
                RunIcacls(string.Format("\"{0}\" /inheritance:r", dir));
                File.WriteAllText(marker, DateTime.Now.ToString("s") + " user=" + id.Name);
                Log.Info("已收紧数据目录权限（SYSTEM / Administrators / 当前用户）: " + dir);
            }
            catch (Exception ex)
            {
                Log.Warn("收紧目录权限失败（不影响使用）: " + ex.Message);
            }
        }

        private static void RunIcacls(string args)
        {
            ProcessStartInfo psi = new ProcessStartInfo("icacls.exe", args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            using (Process p = Process.Start(psi))
            {
                if (p == null) return;
                p.WaitForExit(8000);
            }
        }

        public static bool IsElevated()
        {
            try
            {
                WindowsIdentity id = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        // ---------------- 保存 / 去重 ----------------

        public static string SaveCapture(Bitmap bmp, string reason, bool forceSave, string device, int elapsedMs)
        {
            Config cfg = Cfg;
            ulong hash = HashOf(bmp);
            double luma = LumaOf(bmp);
            DateTime now = DateTime.Now;

            if (!forceSave && cfg.Dedupe && IsDuplicate(hash, now, cfg))
            {
                Log.Info("拍摄 " + reason + " -> 画面与上一张几乎相同，跳过保存");
                return null;
            }

            string day = now.ToString("yyyy-MM-dd");
            string dir = Path.Combine(CaptureDir, day);
            string path;
            lock (FileLock)
            {
                Directory.CreateDirectory(dir);
                string name = now.ToString("HH-mm-ss-fff") + "_" + SafeToken(reason) + ".jpg";
                path = UniquePath(Path.Combine(dir, name));
                SaveJpegFile(bmp, path, cfg.Quality);
            }

            WriteLast(hash, now, path);
            AppendEvent(now, reason, path, device, bmp.Width, bmp.Height, elapsedMs, luma, hash, forceSave);
            return path;
        }

        private static string UniquePath(string path)
        {
            if (!File.Exists(path)) return path;
            string dir = Path.GetDirectoryName(path);
            string baseName = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);
            for (int i = 2; i < 1000; i++)
            {
                string p = Path.Combine(dir, baseName + "_" + i + ext);
                if (!File.Exists(p)) return p;
            }
            return path;
        }

        public static void SaveJpegFile(Bitmap bmp, string path, int quality)
        {
            if (_jpeg == null)
            {
                foreach (ImageCodecInfo c in ImageCodecInfo.GetImageEncoders())
                {
                    if (c.FormatID == ImageFormat.Jpeg.Guid) { _jpeg = c; break; }
                }
            }
            using (EncoderParameters eps = new EncoderParameters(1))
            {
                eps.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)quality);
                bmp.Save(path, _jpeg, eps);
            }
        }

        // ---------------- 画面哈希（8x8 均值哈希） ----------------

        public static ulong HashOf(Bitmap bmp)
        {
            using (Bitmap small = new Bitmap(8, 8))
            {
                using (Graphics g = Graphics.FromImage(small))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(bmp, 0, 0, 8, 8);
                }
                int[] v = new int[64];
                int sum = 0;
                for (int y = 0; y < 8; y++)
                {
                    for (int x = 0; x < 8; x++)
                    {
                        Color c = small.GetPixel(x, y);
                        int l = (c.R * 299 + c.G * 587 + c.B * 114) / 1000;
                        v[y * 8 + x] = l;
                        sum += l;
                    }
                }
                double mean = sum / 64.0;
                ulong h = 0;
                for (int i = 0; i < 64; i++)
                {
                    if (v[i] > mean) h |= (1UL << i);
                }
                return h;
            }
        }

        public static double LumaOf(Bitmap bmp)
        {
            using (Bitmap small = new Bitmap(16, 16))
            {
                using (Graphics g = Graphics.FromImage(small))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.DrawImage(bmp, 0, 0, 16, 16);
                }
                double sum = 0;
                for (int y = 0; y < 16; y++)
                {
                    for (int x = 0; x < 16; x++)
                    {
                        Color c = small.GetPixel(x, y);
                        sum += (c.R * 299 + c.G * 587 + c.B * 114) / 1000.0;
                    }
                }
                return sum / 256.0;
            }
        }

        public static int Hamming(ulong a, ulong b)
        {
            ulong x = a ^ b;
            int n = 0;
            while (x != 0) { n += (int)(x & 1); x >>= 1; }
            return n;
        }

        private static bool IsDuplicate(ulong hash, DateTime now, Config cfg)
        {
            try
            {
                string p = Path.Combine(StateDir, "last.txt");
                if (!File.Exists(p)) return false;
                string[] parts = File.ReadAllText(p).Split('|');
                if (parts.Length < 2) return false;
                long ticks;
                if (!long.TryParse(parts[0], out ticks)) return false;
                ulong prev;
                if (!ulong.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out prev)) return false;
                DateTime then = new DateTime(ticks);
                if ((now - then).TotalMilliseconds > cfg.DedupeWindowMs) return false;
                return Hamming(prev, hash) <= cfg.DedupeDistance;
            }
            catch { return false; }
        }

        private static void WriteLast(ulong hash, DateTime now, string path)
        {
            try
            {
                File.WriteAllText(Path.Combine(StateDir, "last.txt"),
                    now.Ticks.ToString(CultureInfo.InvariantCulture) + "|" + hash.ToString("x16", CultureInfo.InvariantCulture) + "|" + path);
            }
            catch { }
        }

        // ---------------- 事件日志 ----------------

        private static void AppendEvent(DateTime now, string reason, string path, string device, int w, int h, int ms, double luma, ulong hash, bool forced)
        {
            try
            {
                string rel = path;
                try { rel = path.Substring(DataDir.Length).TrimStart('\\'); } catch { }
                StringBuilder sb = new StringBuilder();
                sb.Append("{\"ts\":\"").Append(now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz")).Append("\"");
                sb.Append(",\"event\":\"capture\"");
                sb.Append(",\"reason\":").Append(J(reason));
                sb.Append(",\"file\":").Append(J(rel));
                sb.Append(",\"device\":").Append(J(device));
                sb.Append(",\"w\":").Append(w);
                sb.Append(",\"h\":").Append(h);
                sb.Append(",\"ms\":").Append(ms);
                sb.Append(",\"luma\":").Append(luma.ToString("F1", CultureInfo.InvariantCulture));
                sb.Append(",\"hash\":\"").Append(hash.ToString("x16", CultureInfo.InvariantCulture)).Append("\"");
                sb.Append(",\"forced\":").Append(forced ? "true" : "false");
                sb.Append("}");

                lock (FileLock)
                {
                    File.AppendAllText(Path.Combine(DataDir, "events.jsonl"), sb.ToString() + "\r\n", new UTF8Encoding(false));
                }
            }
            catch { }
        }

        public static void AppendNote(string evt, string message)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.Append("{\"ts\":\"").Append(DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz")).Append("\"");
                sb.Append(",\"event\":").Append(J(evt));
                sb.Append(",\"msg\":").Append(J(message));
                sb.Append("}");
                lock (FileLock)
                {
                    File.AppendAllText(Path.Combine(DataDir, "events.jsonl"), sb.ToString() + "\r\n", new UTF8Encoding(false));
                }
            }
            catch { }
        }

        public static string[] TailEvents(int n)
        {
            try
            {
                string p = Path.Combine(DataDir, "events.jsonl");
                if (!File.Exists(p)) return new string[0];
                string[] all = File.ReadAllLines(p);
                int start = Math.Max(0, all.Length - n);
                string[] res = new string[all.Length - start];
                Array.Copy(all, start, res, 0, res.Length);
                return res;
            }
            catch { return new string[0]; }
        }

        private static string J(string s)
        {
            if (s == null) return "null";
            StringBuilder sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 32) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append("\"");
            return sb.ToString();
        }

        private static string SafeToken(string s)
        {
            if (string.IsNullOrEmpty(s)) return "shot";
            StringBuilder sb = new StringBuilder();
            foreach (char c in s)
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_') sb.Append(c);
                else sb.Append('_');
            }
            return sb.ToString();
        }

        // ---------------- 留存清理 ----------------

        public static void Cleanup(bool verbose)
        {
            int removedDirs = 0, removedFiles = 0;
            try
            {
                if (!Directory.Exists(CaptureDir)) return;
                DateTime cutoff = DateTime.Today.AddDays(-Cfg.RetentionDays);
                foreach (string d in Directory.GetDirectories(CaptureDir))
                {
                    DateTime day;
                    if (!DateTime.TryParseExact(Path.GetFileName(d), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day)) continue;
                    if (day < cutoff)
                    {
                        try { Directory.Delete(d, true); removedDirs++; } catch { }
                    }
                }

                List<FileInfo> files = new List<FileInfo>();
                foreach (string f in Directory.GetFiles(CaptureDir, "*.jpg", SearchOption.AllDirectories))
                {
                    try { files.Add(new FileInfo(f)); } catch { }
                }
                if (files.Count > Cfg.MaxFiles)
                {
                    files.Sort(delegate(FileInfo a, FileInfo b) { return a.CreationTimeUtc.CompareTo(b.CreationTimeUtc); });
                    int extra = files.Count - Cfg.MaxFiles;
                    for (int i = 0; i < extra; i++)
                    {
                        try { files[i].Delete(); removedFiles++; } catch { }
                    }
                }
                if (verbose) Console.WriteLine(string.Format("清理完成: 删除 {0} 个日期目录, {1} 个超出限额的文件", removedDirs, removedFiles));
            }
            catch (Exception ex)
            {
                Log.Warn("清理失败: " + ex.Message);
            }
        }

        public static int CountFiles(out long bytes)
        {
            int n = 0;
            bytes = 0;
            try
            {
                foreach (string f in Directory.GetFiles(CaptureDir, "*.jpg", SearchOption.AllDirectories))
                {
                    n++;
                    try { bytes += new FileInfo(f).Length; } catch { }
                }
            }
            catch { }
            return n;
        }

        public static int CountToday()
        {
            try
            {
                string d = Path.Combine(CaptureDir, DateTime.Today.ToString("yyyy-MM-dd"));
                if (!Directory.Exists(d)) return 0;
                return Directory.GetFiles(d, "*.jpg").Length;
            }
            catch { return 0; }
        }
    }
}
