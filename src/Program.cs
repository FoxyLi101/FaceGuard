using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace FaceGuard
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            string cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "help";
            Dictionary<string, string> opt = Cli.Parse(args, 1);

            if (cmd == "help" || cmd == "-h" || cmd == "--help" || cmd == "/?")
            {
                Cli.Help();
                return 0;
            }

            try
            {
                Store.Init();
            }
            catch (Exception ex)
            {
                try { Console.Error.WriteLine("初始化失败: " + ex.Message); }
                catch { }
                return 1;
            }

            try
            {
                switch (cmd)
                {
                    case "list-devices":
                    case "devices":
                        return CaptureTools.ListDevices();
                    case "capture":
                        return CaptureTools.CmdCapture(opt);
                    case "test":
                        return CaptureTools.CmdTest(opt);
                    case "status":
                        return Commands.Status();
                    case "diagnose":
                        return Commands.Diagnose();
                    case "cleanup":
                        Store.Cleanup(true);
                        return 0;
                    case "set-shots":
                        return Commands.SetShots(opt);
                    case "agent":
                        return Agent.Run();
                    case "watch":
                        return SystemWatch.Run();
                    case "notify":
                        return Notify.Run();
                    default:
                        Console.Error.WriteLine("未知命令: " + cmd);
                        Cli.Help();
                        return 2;
                }
            }
            catch (Exception ex)
            {
                Log.Error("命令 " + cmd + " 失败: " + ex);
                try { Console.Error.WriteLine("错误: " + ex.Message); }
                catch { }
                return 1;
            }
        }
    }

    internal static class Cli
    {
        public static Dictionary<string, string> Parse(string[] args, int start)
        {
            Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = start; i < args.Length; i++)
            {
                string a = args[i];
                if (a.StartsWith("--"))
                {
                    string k = a.Substring(2);
                    string v = "1";
                    int eq = k.IndexOf('=');
                    if (eq > 0)
                    {
                        v = k.Substring(eq + 1);
                        k = k.Substring(0, eq);
                    }
                    else if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                    {
                        v = args[++i];
                    }
                    d[k] = v;
                }
            }
            return d;
        }

        public static string Get(Dictionary<string, string> o, string key, string def)
        {
            string v;
            return o.TryGetValue(key, out v) ? v : def;
        }

        public static int GetInt(Dictionary<string, string> o, string key, int def)
        {
            int v;
            return int.TryParse(Get(o, key, null), out v) ? v : def;
        }

        public static bool Has(Dictionary<string, string> o, string key)
        {
            return o.ContainsKey(key);
        }

        public static void Help()
        {
            Console.WriteLine("FaceGuard 登录留影 v1.0  —  锁屏 / 登录界面 / 唤醒时自动拍摄");
            Console.WriteLine();
            Console.WriteLine("用法: FaceGuard.exe <命令> [--参数 值]");
            Console.WriteLine();
            Console.WriteLine("命令:");
            Console.WriteLine("  test                 拍一张测试照，打印设备/分辨率/亮度/耗时（推荐先跑这个）");
            Console.WriteLine("  list-devices         列出所有摄像头（标出红外/IR）");
            Console.WriteLine("  capture              立即拍一张（等同手工快门）");
            Console.WriteLine("  status               查看数据目录、今日照片数、最近事件、守护是否在跑");
            Console.WriteLine("  diagnose             环境自检（权限、摄像头、锁屏状态、安全日志）");
            Console.WriteLine("  cleanup              立即执行一次留存清理");
            Console.WriteLine("  set-shots --path X   把照片输出目录改到 X（例如桌面某个文件夹）");
            Console.WriteLine("  agent                前台运行“用户态守护”（托盘图标，锁屏/解锁/唤醒触发）");
            Console.WriteLine("  watch                前台运行“开机态守护”（需管理员；覆盖开机登录界面 + 失败登录）");
            Console.WriteLine("  notify               只做一次启动自检并弹提示，然后退出（登录时用）");
            Console.WriteLine();
            Console.WriteLine("capture / test 可选参数:");
            Console.WriteLine("  --device auto|序号|名称片段   默认 auto（自动避开红外摄像头）");
            Console.WriteLine("  --reason 标签                 写入文件名的事件标签，默认 manual");
            Console.WriteLine("  --out 路径                    另存到指定文件");
            Console.WriteLine("  --width 1280 --height 720     分辨率");
            Console.WriteLine("  --force                       忽略去重，强制保存");
            Console.WriteLine();
            Console.WriteLine("说明: 本程序不联网、不上传，照片只保存在本机数据目录。");
        }
    }
}
