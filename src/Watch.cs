using System;
using System.Collections.Concurrent;
using Microsoft.Win32;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Drawing;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace FaceGuard
{
    internal static class LockScreen
    {
        public static bool Present()
        {
            try
            {
                Process[] ps = Process.GetProcessesByName("LogonUI");
                bool any = ps.Length > 0;
                foreach (Process p in ps) { try { p.Dispose(); } catch { } }
                return any;
            }
            catch { return false; }
        }
    }

    internal sealed class Job
    {
        public string Reason;
        public int Burst;
    }

    internal sealed class Watcher
    {
        private readonly BlockingCollection<Job> _q = new BlockingCollection<Job>(new ConcurrentQueue<Job>(), 16);
        private volatile bool _stop;
        private DateTime _lastEnqueue = DateTime.MinValue;
        private string _lastReason;
        private DateTime _lastReasonAt = DateTime.MinValue;
        private int _failStreak;
        private bool _wasLocked;
        private EventLogWatcher _w4625;
        private EventLogWatcher _w4624;

        public void Start()
        {
            _wasLocked = LockScreen.Present();
            Thread t = new Thread(Loop);
            t.IsBackground = true;
            t.Name = "FaceGuard.Watch";
            t.Start();
        }

        public void Stop() { _stop = true; }

        public void Trigger(string reason, int burst, bool bypassCooldown)
        {
            try
            {
                // 同一原因 8 秒内只触发一次：解锁会同时被 SessionSwitch 和轮询检测到，避免连拍翻倍
                if (reason != "lockscreen" && reason == _lastReason
                    && (DateTime.UtcNow - _lastReasonAt).TotalSeconds < 8) return;

                if (!bypassCooldown && (DateTime.UtcNow - _lastEnqueue).TotalMilliseconds < Store.Cfg.CooldownMs) return;
                _lastEnqueue = DateTime.UtcNow;
                _lastReason = reason;
                _lastReasonAt = DateTime.UtcNow;
                Job j = new Job();
                j.Reason = reason;
                j.Burst = burst;
                _q.TryAdd(j);
            }
            catch { }
        }

        private void Loop()
        {
            Thread worker = new Thread(Worker);
            worker.IsBackground = true;
            worker.Start();

            DateTime lastCleanup = DateTime.MinValue;
            Log.Info("watcher: 启动，当前状态=" + (_wasLocked ? "锁屏/登录界面" : "已解锁"));

            while (!_stop)
            {
                try { Tick(); }
                catch (Exception ex) { Log.Warn("tick: " + ex.Message); }

                if ((DateTime.UtcNow - lastCleanup).TotalHours >= 6)
                {
                    lastCleanup = DateTime.UtcNow;
                    try { Store.Cleanup(false); } catch { }
                }
                Thread.Sleep(600);
            }
            StopLogWatchers();
            Log.Info("watcher: 已停止");
        }

        private void Tick()
        {
            Config cfg = Store.Cfg;
            bool locked = LockScreen.Present();

            if (locked && cfg.LockPollMs > 0 && _q.Count == 0)
            {
                if ((DateTime.UtcNow - _lastEnqueue).TotalMilliseconds >= cfg.LockPollMs)
                {
                    Trigger("lockscreen", 1, true);
                }
            }

            if (_wasLocked && !locked)
            {
                Log.Info("检测到解锁/登录完成");
                if (cfg.OnUnlock) Trigger("unlock", cfg.UnlockBurst, true);
            }
            else if (!_wasLocked && locked)
            {
                Log.Info("检测到锁屏/进入登录界面");
                if (cfg.OnLock) Trigger("lock", 2, true);
            }
            _wasLocked = locked;
        }

        private void Worker()
        {
            while (!_stop)
            {
                Job j = null;
                try { if (!_q.TryTake(out j, 500)) continue; }
                catch { break; }
                if (j == null) continue;
                try { Shoot(j); _failStreak = 0; }
                catch (Exception ex)
                {
                    _failStreak++;
                    Log.Warn("拍摄 " + j.Reason + " 失败（连续 " + _failStreak + " 次）: " + ex.Message);
                    if (_failStreak >= 3)
                    {
                        Log.Warn("连续失败，暂停 2 分钟再试（检查相机是否被其它程序占用 / 隐私开关 / 锁屏下是否可用）");
                        Thread.Sleep(120000);
                        _failStreak = 0;
                    }
                    else
                    {
                        Thread.Sleep(1200);
                    }
                }
            }
        }

        private void Shoot(Job j)
        {
            Config cfg = Store.Cfg;
            int n = j.Burst < 1 ? 1 : j.Burst;

            // 锁屏轮拍用低分辨率：占用小、释放快，尽量不影响 Windows Hello 刷脸
            int savedW = cfg.Width;
            int savedH = cfg.Height;
            if (j.Reason == "lockscreen" && cfg.PollWidth >= 160 && cfg.PollHeight >= 120)
            {
                cfg.Width = cfg.PollWidth;
                cfg.Height = cfg.PollHeight;
            }

            try
            {
                // 一次事件只留一张：拍到就停，不再连拍/重试
                for (int i = 0; i < n && !_stop; i++)
                {
                    DateTime t0 = DateTime.UtcNow;
                    string dev;
                    bool captured = false;
                    using (Bitmap bmp = CaptureEngine.Grab(out dev))
                    {
                        captured = true;
                        int ms = (int)(DateTime.UtcNow - t0).TotalMilliseconds;
                        string path = Store.SaveCapture(bmp, j.Reason, false, dev, ms);
                        if (path != null)
                        {
                            Log.Info(string.Format("拍摄 {0}: {1}  {2}x{3}  {4}ms",
                                j.Reason, Path.GetFileName(path), bmp.Width, bmp.Height, ms));
                        }
                    }
                    if (captured) break;
                    if (i + 1 < n) Thread.Sleep(cfg.BurstIntervalMs);
                }
            }
            finally
            {
                cfg.Width = savedW;
                cfg.Height = savedH;
            }
        }

        // ---------------- 安全日志（需管理员） ----------------

        public void EnableLogonEvents()
        {
            if (Store.Cfg.FailedLogon) _w4625 = StartLogWatcher(4625, "failed-logon");
            if (Store.Cfg.SuccessLogon) _w4624 = StartLogWatcher(4624, "logon");
        }

        private EventLogWatcher StartLogWatcher(int id, string reason)
        {
            try
            {
                string query = "*[System[(EventID=" + id + ")]]";
                EventLogWatcher w = new EventLogWatcher(new EventLogQuery("Security", PathType.LogName, query));
                w.EventRecordWritten += delegate(object s, EventRecordWrittenEventArgs e)
                {
                    if (e == null || e.EventRecord == null) return;
                    try
                    {
                        string xml = e.EventRecord.ToXml();
                        e.EventRecord.Dispose();
                        string lt = DataValue(xml, "LogonType");
                        int t;
                        if (!int.TryParse(lt, out t)) return;
                        if (t != 2 && t != 7 && t != 11) return; // 只关心本机交互 / 解锁 / 缓存登录

                        if (reason == "failed-logon")
                        {
                            Log.Warn("安全日志: 登录失败 (LogonType=" + t + ") -> 立即拍摄");
                            Trigger("failed-logon", 2, true);
                        }
                        else
                        {
                            Log.Info("安全日志: 登录成功 (LogonType=" + t + ") -> 拍摄");
                            Trigger("logon", 2, true);
                        }
                    }
                    catch { }
                };
                w.Enabled = true;
                Log.Info("安全日志监控已开启: EventID " + id);
                return w;
            }
            catch (Exception ex)
            {
                Log.Warn("安全日志 " + id + " 监控不可用（需要管理员且已开启审核策略）: " + ex.Message);
                return null;
            }
        }

        private void StopLogWatchers()
        {
            try { if (_w4625 != null) { _w4625.Enabled = false; _w4625.Dispose(); _w4625 = null; } } catch { }
            try { if (_w4624 != null) { _w4624.Enabled = false; _w4624.Dispose(); _w4624 = null; } } catch { }
        }

        private static string DataValue(string xml, string name)
        {
            Match m = Regex.Match(xml, "<Data Name=\"" + name + "\">([^<]*)</Data>");
            return m.Success ? m.Groups[1].Value : "";
        }
    }

    internal static class SystemWatch
    {
        public static int Run()
        {
            Watcher w = new Watcher();
            w.EnableLogonEvents();
            w.Start();
            Log.Info("watch: 开机态守护已启动（elevated=" + Store.IsElevated() + "）");

            ManualResetEvent stop = new ManualResetEvent(false);
            Console.CancelKeyPress += delegate(object s, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                stop.Set();
            };
            stop.WaitOne();
            w.Stop();
            Thread.Sleep(800);
            return 0;
        }
    }

    internal static class Agent
    {
        public static int Run()
        {
            bool created;
            using (Mutex m = new Mutex(true, "Local\\FaceGuard.Agent", out created))
            {
                if (!created)
                {
                    Log.Info("agent: 已有实例在运行，本次退出");
                    return 0;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                Watcher w = new Watcher();
                w.EnableLogonEvents();
                w.Start();

                using (AgentForm f = new AgentForm(w))
                {
                    Application.Run(f);
                }
                w.Stop();
                return 0;
            }
        }
    }

    internal sealed class AgentForm : Form
    {
        private readonly Watcher _w;
        private readonly System.Windows.Forms.Timer _startTimer = new System.Windows.Forms.Timer();

        public AgentForm(Watcher w)
        {
            _w = w;
            ShowInTaskbar = false;
            WindowState = FormWindowState.Minimized;
            FormBorderStyle = FormBorderStyle.None;
            Opacity = 0;
            SystemEvents.SessionSwitch += OnSessionSwitch;
            SystemEvents.PowerModeChanged += OnPower;

            // 界面完全隐藏；登录后延迟一下弹一次状态提示（不常驻托盘）
            _startTimer.Interval = 1200;
            _startTimer.Tick += delegate(object s, EventArgs e)
            {
                _startTimer.Stop();
                ShowStartupToast();
            };
            _startTimer.Start();

            Log.Info("agent: 已启动");
        }

        protected override void SetVisibleCore(bool value)
        {
            base.SetVisibleCore(false);
        }

        private void ShowStartupToast()
        {
            // 登录后的第一张：同一开机周期只拍一次（手动重启守护进程不会重复拍）
            if (Health.ShouldLogonCapture()) _w.Trigger("logon", 1, true);

            if (!Store.Cfg.StartupToast) return;
            string err = Health.Check();
            if (err.Length == 0)
            {
                Toast.Show("FaceGuard", "已启动，运行正常", 4000);
                Log.Info("启动自检: 通过");
            }
            else
            {
                Toast.Show("FaceGuard", "启动异常：" + err, 8000);
                Log.Warn("启动自检未通过: " + err);
            }
        }

        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            try
            {
                switch (e.Reason)
                {
                    case SessionSwitchReason.SessionLock:
                        Log.Info("会话锁定");
                        if (Store.Cfg.OnLock) _w.Trigger("lock", 2, true);
                        break;
                    case SessionSwitchReason.SessionUnlock:
                        Log.Info("会话解锁");
                        if (Store.Cfg.OnUnlock) _w.Trigger("unlock", Store.Cfg.UnlockBurst, true);
                        break;
                    case SessionSwitchReason.ConsoleConnect:
                    case SessionSwitchReason.RemoteConnect:
                        _w.Trigger("connect", 1, true);
                        break;
                }
            }
            catch { }
        }

        private void OnPower(object sender, PowerModeChangedEventArgs e)
        {
            try
            {
                if (e.Mode == PowerModes.Resume && Store.Cfg.OnResume)
                {
                    Log.Info("系统从睡眠唤醒");
                    _w.Trigger("resume", 2, true);
                }
            }
            catch { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { SystemEvents.SessionSwitch -= OnSessionSwitch; } catch { }
                try { SystemEvents.PowerModeChanged -= OnPower; } catch { }
                try { _startTimer.Stop(); _startTimer.Dispose(); } catch { }
                _w.Stop();
            }
            base.Dispose(disposing);
        }
    }
}
