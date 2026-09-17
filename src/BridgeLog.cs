// SFS-Agent — 运行日志环形缓冲
//
// 模组原来只把日志丢给 UnityEngine.Debug.Log（进游戏自己的 Player.log），
// 用户在浏览器面板上看不到。这里再留一份在内存里，供 /log 接口读取，
// 方便排查「为什么连不上」「为什么点了没反应」这类问题。
//
// 环形缓冲：固定容量，满了覆盖最旧的，不会无限增长。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SfsAgent
{
    public static class BridgeLog
    {
        public const int Capacity = 400;

        private class Entry
        {
            public long Seq;
            public string Time;
            public string Level;
            public string Status;   // ok / fail / busy / none
            public bool Poll;       // true = 页面轮询产生的噪声，可被前端隐藏
            public string Text;
        }

        private static readonly List<Entry> Items = new List<Entry>();
        private static readonly object Gate = new object();
        private static long seq;
        private static long dropped;

        // ── 日志格式 ────────────────────────────────────────────────────────
        //
        // 统一成四栏，方便扫读：
        //
        //     [09:46:26] [模组] [信息] 已开始转发游戏日志
        //     [09:46:31] [HTTP] [信息] POST /key {"vk":32} → 已发送按键 32
        //     [09:46:40] [游戏] [信息] Unloading 527 unused Assets
        //
        // 来源栏有三类：模组（自身逻辑）/ HTTP（接口调用）/ 游戏（转发的 Player.log）
        // 级别栏有三类：信息 / 警告 / 错误

        private const string SrcMod = "\u6a21\u7ec4";     // 模组
        private const string SrcHttp = "HTTP";
        private const string SrcGame = "\u6e38\u620f";    // 游戏
        private const string SrcState = "\u72b6\u6001";   // 状态
        private const string SrcUser = "\u73a9\u5bb6";    // 玩家
        private const string SrcFlight = "\u98de\u884c";  // 飞行

        private const string LvInfo = "\u4fe1\u606f";     // 信息
        private const string LvWarn = "\u8b66\u544a";     // 警告
        private const string LvError = "\u9519\u8bef";    // 错误

        /// <summary>记一条日志。任何线程都能调。</summary>
        public static void Write(string level, string text)
        {
            Write(SrcMod, level, text, "none");
        }

        public static void Write(string source, string level, string text)
        {
            Write(source, level, text, "none");
        }

        /// <summary>
        /// 记一条日志。
        /// status 是**执行结果**，独立于级别：
        ///   ok   —— 成功（页面标绿）
        ///   fail —— 失败（标红）
        ///   busy —— 执行中（标蓝）
        ///   none —— 无所谓成败的普通信息（不标色）
        /// </summary>
        public static void Write(string source, string level, string text, string status)
        {
            Write(source, level, text, status, false);
        }

        /// <summary>
        /// poll=true 表示这是页面/agent 的周期性轮询（如 GET /ping），
        /// 页面默认会把它折叠起来，免得把真正的操作淹没。
        /// </summary>
        public static void Write(
            string source, string level, string text, string status, bool poll)
        {
            if (text == null)
            {
                return;
            }
            try
            {
                Entry e = new Entry();
                e.Seq = ++seq;
                e.Time = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
                e.Level = LvOf(level);
                e.Status = StatusOf(status);
                e.Poll = poll;
                e.Text = "[" + srcOf(source) + "] [" + e.Level + "] " + Clip(text);

                lock (Gate)
                {
                    Items.Add(e);
                    while (Items.Count > Capacity)
                    {
                        Items.RemoveAt(0);
                        dropped++;
                    }
                }
            }
            catch
            {
            }
        }

        private static string StatusOf(string status)
        {
            if (status == "ok" || status == "fail" || status == "busy")
            {
                return status;
            }
            return "none";
        }

        private static string Clip(string text)
        {
            // 多行内容压成一行，避免把日志区撑爆
            StringBuilder sb = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length && i < 400; i++)
            {
                char c = text[i];
                sb.Append(c == '\n' || c == '\r' ? ' ' : c);
            }
            if (text.Length > 400)
            {
                sb.Append("…");
            }
            return sb.ToString();
        }

        private static string srcOf(string source)
        {
            if (source == "http")
            {
                return SrcHttp;
            }
            if (source == "game")
            {
                return SrcGame;
            }
            if (source == "state")
            {
                return SrcState;
            }
            if (source == "user")
            {
                return SrcUser;
            }
            if (source == "flight")
            {
                return SrcFlight;
            }
            return SrcMod;
        }

        private static string LvOf(string level)
        {
            if (level == "warn" || level == LvWarn)
            {
                return LvWarn;
            }
            if (level == "error" || level == LvError)
            {
                return LvError;
            }
            return LvInfo;
        }

        public static void Info(string text)
        {
            Write(SrcMod, "info", text);
        }

        public static void Warn(string text)
        {
            Write(SrcMod, "warn", text);
        }

        public static void Error(string text)
        {
            Write(SrcMod, "error", text);
        }

        /// <summary>接口调用日志（来源栏固定为 HTTP）。</summary>
        public static void Http(string text)
        {
            Write("http", "info", text, "none");
        }

        public static void HttpWarn(string text)
        {
            Write("http", "warn", text, "none");
        }

        /// <summary>接口调用：成功。</summary>
        public static void HttpOk(string text)
        {
            Write("http", "info", text, "ok", false);
        }

        /// <summary>接口调用：成功，但属于轮询噪声。</summary>
        public static void HttpPoll(string text)
        {
            Write("http", "info", text, "ok", true);
        }

        /// <summary>接口调用：失败。</summary>
        public static void HttpFail(string text)
        {
            Write("http", "error", text, "fail");
        }

        /// <summary>接口调用：执行中。</summary>
        public static void HttpBusy(string text)
        {
            Write("http", "info", text, "busy");
        }

        /// <summary>转发的游戏日志。</summary>
        public static void Game(string text)
        {
            Write("game", "info", text, "none");
        }

        /// <summary>游戏状态变化（场景切换、界面切换、进入世界等）。</summary>
        public static void State(string text)
        {
            Write("state", "info", text, "ok");
        }

        /// <summary>玩家的操作（按键、点击）。</summary>
        public static void User(string text)
        {
            Write("user", "info", text, "ok");
        }

        /// <summary>飞行数据变化（高度、姿态角、轨道）。</summary>
        public static void Flight(string text)
        {
            Write("flight", "info", text, "ok");
        }

        /// <summary>
        /// 取日志。sinceSeq 为 0 时返回最近 limit 条；否则只返回比它新的
        /// （供页面增量拉取，不重复刷）。
        /// </summary>
        public static string ToJson(long sinceSeq, int limit)
        {
            if (limit <= 0 || limit > Capacity)
            {
                limit = 200;
            }

            StringBuilder sb = new StringBuilder(4096);
            List<Entry> snapshot;
            long total;
            long lost;
            lock (Gate)
            {
                snapshot = new List<Entry>(Items);
                total = seq;
                lost = dropped;
            }

            int start = 0;
            if (sinceSeq > 0)
            {
                // 找到第一条比 sinceSeq 大的
                start = snapshot.Count;
                for (int i = 0; i < snapshot.Count; i++)
                {
                    if (snapshot[i].Seq > sinceSeq)
                    {
                        start = i;
                        break;
                    }
                }
            }
            else if (snapshot.Count > limit)
            {
                start = snapshot.Count - limit;
            }

            sb.Append("{\"ok\":true,\"total\":").Append(total)
              .Append(",\"dropped\":").Append(lost)
              .Append(",\"entries\":[");

            bool first = true;
            for (int i = start; i < snapshot.Count; i++)
            {
                Entry e = snapshot[i];
                if (!first)
                {
                    sb.Append(",");
                }
                first = false;
                sb.Append("{\"seq\":").Append(e.Seq)
                  .Append(",\"time\":\"").Append(Esc(e.Time))
                  .Append("\",\"level\":\"").Append(Esc(e.Level))
                  .Append("\",\"status\":\"").Append(Esc(e.Status))
                  .Append("\",\"poll\":").Append(e.Poll ? "true" : "false")
                  .Append(",\"text\":\"").Append(Esc(e.Text)).Append("\"}");
            }
            sb.Append("]}");
            return sb.ToString();
        }

        public static void Clear()
        {
            lock (Gate)
            {
                Items.Clear();
                dropped = 0;
            }
            Info("日志已清空");
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
                else if (c == '\n')
                {
                    sb.Append("\\n");
                }
                else if (c == '\r')
                {
                    // 丢掉
                }
                else if (c == '\t')
                {
                    sb.Append("\\t");
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
