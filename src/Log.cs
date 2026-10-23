using System;
using System.IO;
using System.Text;

namespace FaceGuard
{
    internal static class Log
    {
        private static string _path;
        private static readonly object _lock = new object();
        private const long MaxBytes = 2 * 1024 * 1024;

        public static void Init(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                _path = Path.Combine(dir, "FaceGuard.log");
            }
            catch { _path = null; }
        }

        public static void Info(string msg) { Write("INFO", msg); }
        public static void Warn(string msg) { Write("WARN", msg); }
        public static void Error(string msg) { Write("ERROR", msg); }

        private static void Write(string level, string msg)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [" + level + "] " + msg;
            if (_path == null) { Echo(line); return; }
            lock (_lock)
            {
                try
                {
                    FileInfo fi = new FileInfo(_path);
                    if (fi.Exists && fi.Length > MaxBytes)
                    {
                        try { if (File.Exists(_path + ".1")) File.Delete(_path + ".1"); } catch { }
                        try { File.Move(_path, _path + ".1"); } catch { }
                        fi.Refresh();
                    }
                    if (!fi.Exists || fi.Length == 0)
                    {
                        File.WriteAllText(_path, line + "\r\n", new UTF8Encoding(true));
                    }
                    else
                    {
                        File.AppendAllText(_path, line + "\r\n", new UTF8Encoding(false));
                    }
                }
                catch { }
            }
            Echo(line);
        }

        private static void Echo(string line)
        {
            try { Console.WriteLine(line); } catch { }
        }
    }
}
