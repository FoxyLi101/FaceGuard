using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;

namespace FaceGuard
{
    internal static class CaptureEngine
    {
        private static int _backend; // 0=未知 1=DirectShow 2=ffmpeg

        public static Bitmap Grab(out string deviceName)
        {
            Config cfg = Store.Cfg;
            deviceName = "";

            List<DeviceInfo> devs = DShow.Enumerate();
            DeviceInfo dev = Select(devs, cfg.Device);
            deviceName = dev.Name;

            if (_backend == 0)
            {
                string b = (cfg.Backend == null ? "auto" : cfg.Backend).Trim().ToLowerInvariant();
                if (b == "auto")
                {
                    string remembered = ReadBackend();
                    if (remembered == "ffmpeg") { _backend = 2; b = ""; }
                    else if (remembered == "dshow") { _backend = 1; b = ""; }
                }
                if (b == "ffmpeg") _backend = 2;
                else if (b == "dshow") _backend = 1;
                if (_backend == 0)
                {
                    try
                    {
                        string d;
                        Bitmap bmp = DShow.Grab(dev, cfg.Width, cfg.Height, Math.Min(cfg.CaptureTimeoutMs, 4000), out d);
                        _backend = 1;
                        if (!string.IsNullOrEmpty(d)) deviceName = d;
                        WriteBackend("dshow");
                        Log.Info("取流后端: DirectShow");
                        return bmp;
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("DirectShow 取流不可用，切换到 ffmpeg 后端: " + ex.Message);
                        _backend = 2;
                        WriteBackend("ffmpeg");
                    }
                }
            }

            if (_backend == 1)
            {
                try
                {
                    string d;
                    Bitmap bmp = DShow.Grab(dev, cfg.Width, cfg.Height, cfg.CaptureTimeoutMs, out d);
                    if (!string.IsNullOrEmpty(d)) deviceName = d;
                    return bmp;
                }
                catch (Exception ex)
                {
                    Log.Warn("DirectShow 失败，改用 ffmpeg: " + ex.Message);
                    _backend = 2;
                }
            }

            int timeout = Math.Max(cfg.CaptureTimeoutMs, 8000);
            return Ffmpeg.Grab(dev, cfg.Width, cfg.Height, timeout);
        }

        private static string ReadBackend()
        {
            try
            {
                string p = Path.Combine(Store.StateDir, "backend.txt");
                if (!File.Exists(p)) return "";
                return File.ReadAllText(p).Trim().ToLowerInvariant();
            }
            catch { return ""; }
        }

        private static void WriteBackend(string name)
        {
            try { File.WriteAllText(Path.Combine(Store.StateDir, "backend.txt"), name); }
            catch { }
        }

        public static DeviceInfo Select(List<DeviceInfo> devs, string sel)
        {
            if (devs == null || devs.Count == 0) throw new CaptureException("系统里没有找到摄像头");

            DeviceInfo chosen = null;
            if (string.IsNullOrEmpty(sel) || sel.Trim().ToLowerInvariant() == "auto")
            {
                List<DeviceInfo> cands = devs.FindAll(delegate(DeviceInfo d) { return !d.IsIR; });
                if (cands.Count == 0)
                {
                    // 只剩红外相机：那是 Windows Hello 的专用相机，宁可不拍也不抢占
                    throw new CaptureException("只找到红外相机（Windows Hello 专用），已放弃拍摄以免干扰刷脸登录");
                }
                foreach (DeviceInfo d in cands)
                {
                    string n = d.Name.ToLowerInvariant();
                    if (n.Contains("webcam") || n.Contains("fhd") || n.Contains("usb")) { chosen = d; break; }
                }
                if (chosen == null) chosen = cands[0];
            }
            else
            {
                int idx;
                if (int.TryParse(sel.Trim(), out idx) && idx >= 0 && idx < devs.Count)
                {
                    chosen = devs[idx];
                }
                else
                {
                    foreach (DeviceInfo d in devs)
                    {
                        if (d.Name.IndexOf(sel, StringComparison.OrdinalIgnoreCase) >= 0) { chosen = d; break; }
                    }
                }
                if (chosen == null) throw new CaptureException("找不到指定摄像头: " + sel);
            }

            // 硬性保护：红外相机由 Windows Hello 独占，任何情况下都不用
            if (chosen.IsIR)
            {
                throw new CaptureException("拒绝使用红外相机（" + chosen.Name + "）：它用于 Windows Hello 刷脸登录，占用会干扰登录");
            }
            return chosen;
        }
    }

    internal static class CaptureTools
    {
        public static int ListDevices()
        {
            List<DeviceInfo> list = DShow.Enumerate();
            if (list.Count == 0)
            {
                Console.WriteLine("未发现任何摄像头（检查设备管理器 / 隐私设置里是否允许桌面应用使用摄像头）。");
                return 1;
            }
            Console.WriteLine("发现 " + list.Count + " 个视频输入设备：");
            foreach (DeviceInfo d in list)
            {
                Console.WriteLine(string.Format("  [{0}] {1}{2}", d.Index, d.Name,
                    d.IsIR ? "    <- 红外/IR，Windows Hello 用，通常不能当普通相机拍照" : ""));
            }
            string ff = Ffmpeg.Find();
            Console.WriteLine("取流后端: DirectShow 优先，失败时用 ffmpeg（" + (ff == null ? "未找到 ffmpeg" : ff) + "）");
            return 0;
        }

        public static int CmdCapture(Dictionary<string, string> o)
        {
            return DoShot(o, false);
        }

        public static int CmdTest(Dictionary<string, string> o)
        {
            return DoShot(o, true);
        }

        private static int DoShot(Dictionary<string, string> o, bool test)
        {
            Config cfg = Store.Cfg;
            string devOverride = Cli.Get(o, "device", null);
            string device = devOverride != null ? devOverride : cfg.Device;
            int w = Cli.GetInt(o, "width", cfg.Width);
            int h = Cli.GetInt(o, "height", cfg.Height);
            bool force = Cli.Has(o, "force") || test;
            string reason = Cli.Get(o, "reason", test ? "test" : "manual");
            string outPath = Cli.Get(o, "out", null);

            DateTime t0 = DateTime.UtcNow;
            string devName = "";
            Bitmap bmp;
            try
            {
                bmp = GrabWith(cfg, device, w, h, out devName);
            }
            catch (Exception ex)
            {
                Console.WriteLine("取流失败: " + ex.Message);
                Log.Warn("手工拍摄失败: " + ex.Message);
                return 1;
            }

            int ms = (int)(DateTime.UtcNow - t0).TotalMilliseconds;
            try
            {
                double luma = Store.LumaOf(bmp);
                string path = Store.SaveCapture(bmp, reason, force, devName, ms);

                if (!string.IsNullOrEmpty(outPath))
                {
                    string full = Path.GetFullPath(outPath);
                    string dir = Path.GetDirectoryName(full);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    Store.SaveJpegFile(bmp, full, cfg.Quality);
                    path = full;
                }

                Console.WriteLine("设备    : " + devName);
                Console.WriteLine("分辨率  : " + bmp.Width + "x" + bmp.Height);
                Console.WriteLine("耗时    : " + ms + " ms");
                Console.WriteLine("平均亮度: " + luma.ToString("F1") + (luma < 5 ? "   (接近全黑：镜头被遮挡或隐私开关关闭)" : ""));
                Console.WriteLine("文件    : " + (path == null ? "(去重，本次未保存)" : path));
                Console.WriteLine("结果    : OK");
                if (luma < 5) return 3;
                return 0;
            }
            finally
            {
                bmp.Dispose();
            }
        }

        private static Bitmap GrabWith(Config cfg, string device, int w, int h, out string devName)
        {
            Config save = Store.Cfg;
            string savedDevice = save.Device;
            int savedW = save.Width;
            int savedH = save.Height;
            try
            {
                save.Device = device;
                save.Width = w;
                save.Height = h;
                return CaptureEngine.Grab(out devName);
            }
            finally
            {
                save.Device = savedDevice;
                save.Width = savedW;
                save.Height = savedH;
            }
        }
    }
}
