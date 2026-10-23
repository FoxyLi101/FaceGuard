using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Text;

namespace FaceGuard
{
    internal static class Commands
    {
        public static int SetShots(Dictionary<string, string> o)
        {
            string path = Cli.Get(o, "path", null);
            if (string.IsNullOrEmpty(path))
            {
                Console.WriteLine("用法: FaceGuard.exe set-shots --path \"D:\\某个文件夹\"");
                return 2;
            }
            try
            {
                path = Path.GetFullPath(path);
                Directory.CreateDirectory(path);

                List<string> lines = new List<string>();
                if (File.Exists(Store.ConfigPath)) lines.AddRange(File.ReadAllLines(Store.ConfigPath, Encoding.UTF8));

                bool replaced = false;
                for (int i = 0; i < lines.Count; i++)
                {
                    string t = lines[i].TrimStart();
                    if (t.StartsWith("storageDir", StringComparison.OrdinalIgnoreCase) && t.Contains("="))
                    {
                        lines[i] = "storageDir = " + path;
                        replaced = true;
                    }
                }
                if (!replaced) lines.Add("storageDir = " + path);

                File.WriteAllText(Store.ConfigPath, string.Join("\r\n", lines.ToArray()) + "\r\n", new UTF8Encoding(true));
                Console.WriteLine("照片目录已设为: " + path);
                Console.WriteLine("重启守护进程后生效。");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("设置失败: " + ex.Message);
                return 1;
            }
        }

        public static int Status()
        {
            Config cfg = Store.Cfg;
            long bytes;
            int total = Store.CountFiles(out bytes);
            int today = Store.CountToday();

            Console.WriteLine("FaceGuard 登录留影  —  状态");
            Console.WriteLine("  数据目录  : " + Store.DataDir);
            Console.WriteLine("  配置文件  : " + Store.ConfigPath);
            Console.WriteLine("  照片目录  : " + Store.CaptureDir);
            Console.WriteLine("  摄像头    : " + cfg.Device + "   请求 " + cfg.Width + "x" + cfg.Height + "   质量 " + cfg.Quality);
            Console.WriteLine("  今日照片  : " + today + " 张");
            Console.WriteLine("  累计照片  : " + total + " 张  (" + (bytes / 1024.0 / 1024.0).ToString("F1") + " MB)");
            Console.WriteLine("  锁屏/登录界面: " + (LockScreen.Present() ? "是（正在留影中）" : "否"));

            string procs = RunningProcesses();
            Console.WriteLine("  守护进程  : " + (procs.Length > 0 ? procs : "未运行"));
            Console.WriteLine("  开机任务  : " + (TaskInstalled() ? "已安装 FaceGuard Watch（开机启动，SYSTEM）" : "未安装"));
            Console.WriteLine("  启动项    : " + (StartupShortcut() ? "已安装（登录时启动 agent）" : "未安装"));

            string[] tail = Store.TailEvents(8);
            if (tail.Length > 0)
            {
                Console.WriteLine("  最近事件  :");
                foreach (string s in tail) Console.WriteLine("    " + s);
            }
            return 0;
        }

        public static int Diagnose()
        {
            Console.WriteLine("FaceGuard 自检");
            Console.WriteLine("--------------------------------------------------");
            Console.WriteLine("管理员权限      : " + (Store.IsElevated() ? "是" : "否（watch 模式与 4625 失败登录监控需要管理员）"));
            Console.WriteLine("数据目录可写    : " + (CanWrite(Store.DataDir) ? "是" : "否") + "   " + Store.DataDir);
            Console.WriteLine("配置            : " + Store.ConfigPath);
            Console.WriteLine("锁屏/登录界面   : " + (LockScreen.Present() ? "是" : "否"));
            Console.WriteLine("会话 ID         : " + Process.GetCurrentProcess().SessionId);
            Console.WriteLine();

            List<DeviceInfo> devs = DShow.Enumerate();
            Console.WriteLine("摄像头          : 发现 " + devs.Count + " 个");
            int ir = 0;
            foreach (DeviceInfo d in devs)
            {
                if (d.IsIR) ir++;
                Console.WriteLine("   [" + d.Index + "] " + d.Name + (d.IsIR ? "  (IR/红外 — 本程序永不使用)" : ""));
            }
            if (devs.Count == 0) return 1;
            Console.WriteLine("Windows Hello   : 红外相机由 Hello 独占，本程序只开彩色相机；锁屏轮拍用 " +
                Store.Cfg.PollWidth + "x" + Store.Cfg.PollHeight + " 低分辨率、拍完立即释放（已排除 " + ir + " 个红外设备）");
            Console.WriteLine();

            Console.WriteLine("实拍测试        :");
            DateTime t0 = DateTime.UtcNow;
            try
            {
                string name;
                using (System.Drawing.Bitmap bmp = CaptureEngine.Grab(out name))
                {
                    int ms = (int)(DateTime.UtcNow - t0).TotalMilliseconds;
                    Console.WriteLine("   设备      : " + name);
                    Console.WriteLine("   分辨率    : " + bmp.Width + "x" + bmp.Height);
                    Console.WriteLine("   耗时      : " + ms + " ms");
                    Console.WriteLine("   平均亮度  : " + Store.LumaOf(bmp).ToString("F1"));
                    Console.WriteLine("   结果      : OK");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("   结果      : 失败 — " + ex.Message);
            }
            Console.WriteLine();

            Console.WriteLine("安全日志 4625   : " + SecurityLogState());
            return 0;
        }

        private static string SecurityLogState()
        {
            try
            {
                EventLogQuery q = new EventLogQuery("Security", PathType.LogName, "*[System[(EventID=4625)]]");
                using (EventLogReader r = new EventLogReader(q))
                {
                    int n = 0;
                    EventRecord rec = r.ReadEvent();
                    while (rec != null && n < 20)
                    {
                        n++;
                        rec.Dispose();
                        rec = r.ReadEvent();
                    }
                    return "可读，最新若干条中存在失败登录记录（说明失败审核已开启）";
                }
            }
            catch (UnauthorizedAccessException)
            {
                return "无权读取（需要管理员）";
            }
            catch (Exception ex)
            {
                return "不可用: " + ex.Message;
            }
        }

        private static bool CanWrite(string dir)
        {
            try
            {
                string p = Path.Combine(dir, ".diag-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                File.WriteAllText(p, "1");
                File.Delete(p);
                return true;
            }
            catch { return false; }
        }

        private static string RunningProcesses()
        {
            StringBuilder sb = new StringBuilder();
            string[] names = new string[] { "FaceGuardDaemon", "FaceGuard" };
            foreach (string n in names)
            {
                try
                {
                    Process[] ps = Process.GetProcessesByName(n);
                    foreach (Process p in ps)
                    {
                        try
                        {
                            if (p.Id == Process.GetCurrentProcess().Id) continue;
                            if (sb.Length > 0) sb.Append(", ");
                            sb.Append(p.ProcessName).Append("#").Append(p.Id);
                        }
                        catch { }
                        finally { p.Dispose(); }
                    }
                }
                catch { }
            }
            return sb.ToString();
        }

        private static bool TaskInstalled()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("schtasks.exe", "/query /tn \"FaceGuard Watch\"");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi))
                {
                    if (p == null) return false;
                    p.StandardOutput.ReadToEnd();
                    p.WaitForExit(6000);
                    return p.ExitCode == 0;
                }
            }
            catch { return false; }
        }

        private static bool StartupShortcut()
        {
            try
            {
                string startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                return File.Exists(Path.Combine(startup, "FaceGuard 守护.lnk"))
                    || File.Exists(Path.Combine(startup, "FaceGuard Daemon.lnk"));
            }
            catch { return false; }
        }
    }
}
