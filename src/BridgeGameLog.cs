// SFS-Agent — 转发游戏自己的日志
//
// SFS 把日志写到
//     %USERPROFILE%\AppData\LocalLow\Stef Morojna\Spaceflight Simulator\Player.log
// 用户排查问题时经常需要看它，但那个路径又长又难找。
// 这里把它增量读进来，合并到浏览器面板的日志区（前缀 [游戏]），
// 这样模组日志和游戏日志在同一个地方就能看完。
//
// 读取逻辑：记住上次读到的字节位置，只读新增部分；
// 文件被截断或轮转（长度变小）时从头再读。

using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace SfsAgent
{
    public static class BridgeGameLog
    {
        private static long lastLength = -1;
        private static int linesForwarded;
        private static string resolvedPath;
        private static bool disabled;
        private static string lastError = "";

        /// <summary>每分钟最多转发多少行，避免游戏狂刷日志时把面板淹了。</summary>
        private const int MaxLinesPerPoll = 40;

        public static bool Enabled = true;

        public static string Status()
        {
            if (!Enabled)
            {
                return "disabled";
            }
            if (disabled)
            {
                return "unavailable: " + lastError;
            }
            return resolvedPath == null ? "not resolved" : resolvedPath;
        }

        /// <summary>找到 Player.log 的路径。找不到返回 null。</summary>
        public static string ResolvePath()
        {
            if (resolvedPath != null)
            {
                return resolvedPath;
            }
            try
            {
                // AppData\LocalLow 在 .NET 里就是 SpecialFolder.LocalApplicationData
                // 的同级目录，但有专门的枚举值可用
                string low = Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrEmpty(low))
                {
                    // LocalApplicationData 指向 ...\AppData\Local，
                    // LocalLow 是它的兄弟目录
                    string appData = Path.GetDirectoryName(low);
                    if (!string.IsNullOrEmpty(appData))
                    {
                        string candidate = Path.Combine(
                            appData, "LocalLow", "Stef Morojna",
                            "Spaceflight Simulator", "Player.log");
                        if (File.Exists(candidate))
                        {
                            resolvedPath = candidate;
                            return resolvedPath;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
            }
            return null;
        }

        /// <summary>
        /// 主线程调用：把游戏日志的新增部分转发到内存日志。
        /// 只做文件读取，不碰 Unity API，所以开销很小。
        /// </summary>
        public static void Poll()
        {
            if (!Enabled || disabled)
            {
                return;
            }
            try
            {
                string path = ResolvePath();
                if (path == null)
                {
                    return;
                }

                FileInfo fi = new FileInfo(path);
                if (!fi.Exists)
                {
                    return;
                }
                long len = fi.Length;

                if (lastLength < 0)
                {
                    // 首次：只报告一句「已接管」，不把历史整个倒出来
                    lastLength = len;
                    BridgeLog.Info("已开始转发游戏日志（" + fi.Name + "）");
                    return;
                }
                if (len < lastLength)
                {
                    // 文件被截断 / 轮转，从头再读
                    lastLength = 0;
                }
                if (len == lastLength)
                {
                    return;
                }

                using (FileStream fs = new FileStream(
                    path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    fs.Seek(lastLength, SeekOrigin.Begin);
                    byte[] buf = new byte[len - lastLength];
                    int read = fs.Read(buf, 0, buf.Length);
                    lastLength += read;

                    string text = Encoding.UTF8.GetString(buf, 0, read);
                    string[] lines = text.Split('\n');
                    int forwarded = 0;

                    // 最后一段可能是不完整的行，留到下次
                    int limit = lines.Length;
                    if (!text.EndsWith("\n", StringComparison.Ordinal))
                    {
                        limit = lines.Length - 1;
                        lastLength -= Encoding.UTF8.GetByteCount(lines[lines.Length - 1]);
                    }

                    for (int i = 0; i < limit && forwarded < MaxLinesPerPoll; i++)
                    {
                        string line = lines[i].TrimEnd('\r').Trim();
                        if (line.Length == 0)
                        {
                            continue;
                        }
                        // 模组自己写进 Player.log 的行不重复转发（内存日志里已有）
                        if (line.StartsWith("[SfsAgent]", StringComparison.Ordinal))
                        {
                            continue;
                        }
                        BridgeLog.Game(line);
                        forwarded++;
                        linesForwarded++;
                    }
                }
            }
            catch (Exception ex)
            {
                lastError = ex.GetType().Name + ": " + ex.Message;
                // 读不到就安静停掉，不要每帧重试刷日志
                disabled = true;
                BridgeLog.Warn("转发游戏日志失败，已停用：" + lastError);
            }
        }

        public static string ToJson()
        {
            StringBuilder sb = new StringBuilder(256);
            sb.Append("{\"ok\":true")
              .Append(",\"enabled\":").Append(Enabled ? "true" : "false")
              .Append(",\"forwarded\":").Append(linesForwarded)
              .Append(",\"status\":\"").Append(Esc(Status())).Append("\"}");
            return sb.ToString();
        }

        private static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return "";
            }
            StringBuilder sb = new StringBuilder(s.Length + 8);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '"' || c == '\\')
                {
                    sb.Append('\\').Append(c);
                }
                else if (c < 32)
                {
                    sb.Append(' ');
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
    }
}
