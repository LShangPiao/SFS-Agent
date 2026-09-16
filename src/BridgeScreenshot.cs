// SFS Agent — 截图
//
// 通过反射调用 Unity 的 ScreenCapture，避免编译期引用 UnityEngine。
// Unity 的截图 API 必须在主线程、且在帧末写入文件，
// 因此由服务线程发起请求、主线程执行、服务线程等待文件落地。

using System;
using System.IO;
using System.Reflection;
using System.Threading;

namespace SfsAgent
{
    public static class BridgeScreenshot
    {
        private static readonly object Gate = new object();
        private static string pendingPath = "";
        private static volatile bool completed;
        private static long lastRequestTicks;

        /// <summary>截图的落盘目录。</summary>
        public static string ShotDir
        {
            get
            {
                string dir = Path.Combine(Path.GetTempPath(), "neko_sfs");
                try
                {
                    Directory.CreateDirectory(dir);
                }
                catch
                {
                }
                return dir;
            }
        }

        /// <summary>主线程调用：若存在待处理请求，则触发 Unity 截图。</summary>
        public static void Tick()
        {
            string path;
            lock (Gate)
            {
                path = pendingPath;
                if (path.Length == 0)
                {
                    return;
                }
                pendingPath = "";
            }

            try
            {
                Type sc = BridgeState.FindType("UnityEngine.ScreenCapture");
                if (sc == null)
                {
                    completed = true;
                    Main.Log("ScreenCapture type not found");
                    return;
                }

                MethodInfo m = sc.GetMethod(
                    "CaptureScreenshot",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new Type[] { typeof(string), typeof(int) },
                    null);
                if (m != null)
                {
                    m.Invoke(null, new object[] { path, 1 });
                }
                else
                {
                    m = sc.GetMethod(
                        "CaptureScreenshot",
                        BindingFlags.Public | BindingFlags.Static,
                        null,
                        new Type[] { typeof(string) },
                        null);
                    if (m == null)
                    {
                        completed = true;
                        Main.Log("CaptureScreenshot not found");
                        return;
                    }
                    m.Invoke(null, new object[] { path });
                }
            }
            catch (Exception ex)
            {
                completed = true;
                Main.Log("capture failed: " + ex.Message);
            }
        }

        /// <summary>
        /// 服务线程调用：请求一张截图并等待文件就绪，返回文件字节。
        /// 失败返回 null。
        /// </summary>
        public static byte[] Capture(int timeoutMs)
        {
            string path = Path.Combine(
                ShotDir,
                "shot_" + DateTime.Now.Ticks.ToString() + ".png");

            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }

            lock (Gate)
            {
                pendingPath = path;
                completed = false;
                lastRequestTicks = DateTime.Now.Ticks;
            }

            // 等待主线程完成截图并写入文件
            long deadline = DateTime.Now.Ticks + (long)timeoutMs * 10000L;
            while (DateTime.Now.Ticks < deadline)
            {
                if (File.Exists(path))
                {
                    try
                    {
                        // 文件可能还在写入，等待长度稳定
                        long a = new FileInfo(path).Length;
                        Thread.Sleep(60);
                        long b = new FileInfo(path).Length;
                        if (a > 0 && a == b)
                        {
                            byte[] data = File.ReadAllBytes(path);
                            try
                            {
                                File.Delete(path);
                            }
                            catch
                            {
                            }
                            return data;
                        }
                    }
                    catch
                    {
                    }
                }
                if (completed && !File.Exists(path))
                {
                    // 主线程报告失败
                    Thread.Sleep(120);
                    if (!File.Exists(path))
                    {
                        return null;
                    }
                }
                Thread.Sleep(40);
            }

            Main.Log("screenshot timeout");
            return null;
        }
    }
}
