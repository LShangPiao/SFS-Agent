// SFS Agent — 本地 HTTP 服务端
//
// 使用 TcpListener 手写最小 HTTP，而不是 HttpListener：
// HttpListener 在非管理员账户下常需要 URL ACL 预留，TcpListener 无此限制。

using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace SfsAgent
{
    public static class BridgeServer
    {
        private static TcpListener listener;
        private static Thread worker;
        private static volatile bool running;

        public static void Start(int port)
        {
            if (running)
            {
                return;
            }

            listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            running = true;

            worker = new Thread(Loop);
            worker.IsBackground = true;
            worker.Name = "SfsAgentHttp";
            worker.Start();
        }

        public static void Stop()
        {
            running = false;
            try
            {
                if (listener != null)
                {
                    listener.Stop();
                }
            }
            catch
            {
            }
            listener = null;
        }

        private static void Loop()
        {
            while (running)
            {
                TcpClient client = null;
                try
                {
                    client = listener.AcceptTcpClient();
                }
                catch
                {
                    if (!running)
                    {
                        return;
                    }
                    continue;
                }

                try
                {
                    Handle(client);
                }
                catch (Exception ex)
                {
                    Main.Log("request error: " + ex.Message);
                }
                finally
                {
                    try
                    {
                        client.Close();
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static void Handle(TcpClient client)
        {
            client.ReceiveTimeout = 5000;
            NetworkStream stream = client.GetStream();

            string requestLine = ReadLine(stream);
            if (string.IsNullOrEmpty(requestLine))
            {
                return;
            }

            string[] parts = requestLine.Split(' ');
            string method = parts.Length > 0 ? parts[0].ToUpperInvariant() : "GET";
            string path = parts.Length > 1 ? parts[1] : "/";
            string query = "";
            int q = path.IndexOf('?');
            if (q >= 0)
            {
                query = path.Substring(q + 1);
                path = path.Substring(0, q);
            }

            int contentLength = 0;
            while (true)
            {
                string header = ReadLine(stream);
                if (string.IsNullOrEmpty(header))
                {
                    break;
                }
                int colon = header.IndexOf(':');
                if (colon > 0)
                {
                    string key = header.Substring(0, colon).Trim().ToLowerInvariant();
                    string val = header.Substring(colon + 1).Trim();
                    if (key == "content-length")
                    {
                        int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out contentLength);
                    }
                }
            }

            string body = "";
            if (contentLength > 0)
            {
                byte[] buffer = new byte[contentLength];
                int read = 0;
                while (read < contentLength)
                {
                    int n = stream.Read(buffer, read, contentLength - read);
                    if (n <= 0)
                    {
                        break;
                    }
                    read += n;
                }
                body = Encoding.UTF8.GetString(buffer, 0, read);
            }

            string payload = "";
            int status = 200;
            string statusText = "OK";
            string contentType = "application/json; charset=utf-8";
            byte[] outBytes = null;

            if (path == "/" || path == "/index.html")
            {
                contentType = "text/html; charset=utf-8";
                payload = BridgePage.Html(BridgeConfig.Port);
            }
            else if (path == "/screenshot")
            {
                byte[] png = BridgeScreenshot.Capture(6000);
                if (png == null || png.Length == 0)
                {
                    status = 503;
                    statusText = "Service Unavailable";
                    payload = "{\"ok\":false,\"error\":\"screenshot unavailable\"}";
                }
                else
                {
                    contentType = "image/png";
                    outBytes = png;
                }
            }
            else if (path == "/ping")
            {
                payload = "{\"ok\":true,\"mod\":\"sfs_agent\",\"version\":\"0.3.0\""
                    + ",\"key_injection\":\"" + (BridgeKeys.Installed ? "on" : "off") + "\""
                    + ",\"key_injection_info\":\"" + Escape(BridgeKeys.InstallInfo) + "\""
                    + "}";
            }
            else if (path == "/state")
            {
                payload = BridgeState.ToJson();
            }
            else if (path == "/build")
            {
                payload = BridgeBuild.ToJson();
            }
            else if (path == "/command" && method == "POST")
            {
                payload = HandleCommand(body);
            }
            else if (path == "/click" && method == "POST")
            {
                payload = HandleClick(body, false);
            }
            else if (path == "/click_raw" && method == "POST")
            {
                payload = HandleClick(body, true);
            }
            else if (path == "/key" && method == "POST")
            {
                payload = HandleKey(body, false);
            }
            else if (path == "/key_raw" && method == "POST")
            {
                payload = HandleKey(body, true);
            }
            else if (path == "/scroll" && method == "POST")
            {
                payload = HandleScroll(body);
            }
            else if (path == "/exclusive" && method == "POST")
            {
                payload = HandleExclusive(body);
            }
            else if (path == "/config" && method == "POST")
            {
                payload = BridgePage.MergeWriteJson(BridgeConfig.IniPath, body);
                // 语言等改动立即同步到内存（游戏内提示会跟着变）
                BridgeConfig.SyncLang();
            }
            else if (path == "/config")
            {
                payload = BridgePage.ReadAllJson(BridgeConfig.IniPath);
            }
            else if (path == "/camera" && method == "POST")
            {
                payload = HandleCamera(body);
            }
            else if (path == "/build_catalog")
            {
                // 默认走 deep：在主线程调用游戏自己的 LoadParts() 拿全量零件名。
                // 传 deep=0 可只读缓存来源（更快）。
                bool deep = query.IndexOf("deep=0", StringComparison.Ordinal) < 0;
                payload = HandleBuildCatalog(deep);
            }
            else if (path == "/debug_hit")
            {
                // 参数按**归一化 0-1** 给，内部换算成像素
                double nx = QueryNumber(query, "x");
                double ny = QueryNumber(query, "y");
                if (nx <= 0 && ny <= 0)
                {
                    nx = 0.5;
                    ny = 0.5;
                }
                double sw = QueryNumber(query, "w");
                double sh = QueryNumber(query, "h");
                if (sw <= 0)
                {
                    sw = 1600;
                }
                if (sh <= 0)
                {
                    sh = 837;
                }
                payload = BridgePointer.HitTestVerbose(nx * sw, (1.0 - ny) * sh);
            }
            else if (path == "/blueprints")
            {
                payload = HandleBlueprints();
            }
            else if (path == "/blueprint_load" && method == "POST")
            {
                payload = HandleBlueprintLoad(body);
            }
            else if (path == "/debug_examples")
            {
                BridgeParts.RequestExamples();
                for (int i = 0; i < 60 && !BridgeParts.ExamplesReady; i++)
                {
                    System.Threading.Thread.Sleep(100);
                }
                payload = BridgeParts.ExamplesJson();
            }
            else if (path == "/debug_parts")
            {
                payload = HandleDebugParts();
            }
            else if (path == "/build_place" && method == "POST")
            {
                payload = HandleBuildPlace(body);
            }
            else if (path == "/ui")
            {
                // 枚举必须在主线程做，这里发起请求后稍等
                BridgeUi.Request();
                System.Threading.Thread.Sleep(200);
                payload = BridgeUi.ToJson();
            }
            else if (path == "/ui_click" && method == "POST")
            {
                payload = HandleUiClick(body);
            }
            else if (path == "/debug_methods" && method == "POST")
            {
                // 诊断：列出某个元素对象上的输入相关方法
                BridgeUi.Request();
                System.Threading.Thread.Sleep(200);
                payload = BridgeUi.DescribeMethods((int)ExtractNumber(body, "index"));
            }
            else if (path == "/health")
            {
                payload = "{\"ok\":true}";
            }
            else
            {
                status = 404;
                statusText = "Not Found";
                payload = "{\"ok\":false,\"error\":\"not found\"}";
            }

            if (outBytes == null)
            {
                outBytes = Encoding.UTF8.GetBytes(payload);
            }

            string head =
                "HTTP/1.1 " + status + " " + statusText + "\r\n" +
                "Content-Type: " + contentType + "\r\n" +
                "Content-Length: " + outBytes.Length + "\r\n" +
                "Access-Control-Allow-Origin: *\r\n" +
                "Connection: close\r\n\r\n";

            byte[] headBytes = Encoding.ASCII.GetBytes(head);
            stream.Write(headBytes, 0, headBytes.Length);
            stream.Write(outBytes, 0, outBytes.Length);
            stream.Flush();
        }

        private static string HandleCommand(string body)
        {
            string name = ExtractString(body, "name");
            double value = ExtractNumber(body, "value");

            if (string.IsNullOrEmpty(name))
            {
                return "{\"ok\":false,\"error\":\"missing name\"}";
            }

            BridgeCommands.Enqueue(name, value);

            // 指令由主线程执行；等它跑完再如实返回结果，
            // 避免「已排队」被上层误当成「已执行成功」。
            for (int i = 0; i < 20 && BridgeCommands.QueueLength > 0; i++)
            {
                System.Threading.Thread.Sleep(50);
            }
            System.Threading.Thread.Sleep(80);
            return BridgeCommands.ToJson();
        }

        /// <summary>
        /// 点击。x、y 为相对游戏客户区的归一化坐标（0-1）。
        ///
        /// 默认走**游戏内输入派发**（SFS.Input.InputManager），不移动系统鼠标、
        /// 不抢焦点，坐标命中判定也由游戏自己算，因此更准。
        /// raw=true 时才用 Win32 模拟（会抢鼠标/焦点，仅在游戏内派发不可用时使用）。
        /// </summary>
        private static string HandleClick(string body, bool raw)
        {
            double x = ExtractNumber(body, "x");
            double y = ExtractNumber(body, "y");
            if (x < 0 || x > 1 || y < 0 || y > 1)
            {
                return "{\"ok\":false,\"error\":\"x and y must be within 0..1\"}";
            }

            if (raw)
            {
                return BridgeInput.ToJson(BridgeInput.Click(x, y));
            }

            BridgePointer.Reset();
            BridgePointer.EnqueueClick(x, y, 2);
            // 等按下与抬起都跑完再应答，避免「只按了没松」被当成点完
            BridgePointer.WaitIdle(2000);
            bool ok = BridgePointer.LastError.Length == 0 && BridgePointer.LastResult.Length > 0;
            if (!ok && BridgePointer.LastError.Length == 0)
            {
                BridgePointer.LastError =
                    "click state machine did not run (bridge frame loop inactive?)";
            }
            return BridgePointer.ToJson(ok);
        }

        /// <summary>
        /// 按键。vk 为 Win32 虚拟键码（与 UnityEngine.KeyCode 数值一致）。
        ///
        /// 默认走**游戏内按键注入**（Harmony 拦截 UnityEngine.Input），
        /// 不需要游戏在前台，也不会把按键打到别的程序里。
        /// </summary>
        private static string HandleKey(string body, bool raw)
        {
            double vk = ExtractNumber(body, "vk");
            int code = (int)vk;

            if (raw)
            {
                return BridgeInput.ToJson(BridgeInput.KeyPress(code));
            }

            double holdMs = ExtractNumber(body, "hold_ms");
            if (holdMs <= 0)
            {
                holdMs = 120;
            }

            BridgeKeys.Enqueue(code, (int)holdMs);
            System.Threading.Thread.Sleep((int)holdMs + 60);
            if (!BridgeKeys.Installed)
            {
                return "{\"ok\":false,\"mode\":\"in_game_key\",\"error\":\""
                    + Escape(BridgeKeys.InstallInfo) + "\"}";
            }
            return "{\"ok\":true,\"mode\":\"in_game_key\",\"vk\":" + code
                + ",\"hold_ms\":" + ((int)holdMs) + "}";
        }

        /// <summary>
        /// 零件目录。
        ///
        /// 读取会碰 Unity 原生 API（Resources 等），**必须由主线程执行**：
        /// 之前直接在 HTTP 线程上调，把游戏直接打崩了（Resources.LoadAll 的
        /// 原生访问违例），所以这里只请求 + 等待，实际读取在 BridgeParts.Tick()。
        /// </summary>
        private static string HandleBuildCatalog(bool deep)
        {
            BridgeParts.RequestCatalog(deep);
            for (int i = 0; i < 60 && !BridgeParts.CatalogReady; i++)
            {
                System.Threading.Thread.Sleep(100);
            }
            return BridgeParts.CatalogJson();
        }

        /// <summary>
        /// 开关「Agent 独占模式」：开启后用户的鼠标与键盘被吞掉，游戏只接受 agent 的注入。
        /// 屏幕上会显示提示（上下淡蓝渐变 + 「Agent 操作中」+ 一个「解除独占」按钮）。
        /// 解除方式：点那个按钮，或按 F10。
        /// </summary>
        private static string HandleExclusive(string body)
        {
            string onStr = ExtractString(body, "on");
            bool? want = null;
            if (onStr != null)
            {
                string v = onStr.Trim().ToLowerInvariant();
                if (v == "1" || v == "true" || v == "on")
                {
                    want = true;
                }
                else if (v == "0" || v == "false" || v == "off")
                {
                    want = false;
                }
            }
            if (want == null)
            {
                // 没给就切换
                want = !BridgeOverlay.Exclusive;
            }

            BridgeOverlay.Exclusive = want.Value;
            BridgeOverlay.WantVisible = want.Value;
            System.Threading.Thread.Sleep(120);

            return "{\"ok\":true,\"exclusive\":" + (want.Value ? "true" : "false")
                + ",\"overlay\":\"" + Escape(BridgeOverlay.Status()) + "\"}";
        }

        /// <summary>
        /// 视角控制。可选字段：x / y（相机位置）、distance（绝对距离）、
        /// zoom_delta（相对缩放，正数拉远）、rotation（角度）。
        /// 缺省字段不动。
        /// </summary>
        private static string HandleCamera(string body)
        {
            double x = ExtractNumber(body, "x");
            double y = ExtractNumber(body, "y");
            double dist = ExtractNumber(body, "distance");
            double zoom = ExtractNumber(body, "zoom_delta");
            double rot = ExtractNumber(body, "rotation");

            bool hasX = body != null && body.IndexOf("\"x\"", StringComparison.Ordinal) >= 0;
            bool hasY = body != null && body.IndexOf("\"y\"", StringComparison.Ordinal) >= 0;
            bool hasDist = body != null && body.IndexOf("\"distance\"", StringComparison.Ordinal) >= 0;
            bool hasZoom = body != null && body.IndexOf("\"zoom_delta\"", StringComparison.Ordinal) >= 0;
            bool hasRot = body != null && body.IndexOf("\"rotation\"", StringComparison.Ordinal) >= 0;

            if (!hasX && !hasY && !hasDist && !hasZoom && !hasRot)
            {
                return "{\"ok\":false,\"error\":\"需要至少一个字段：x / y / distance / zoom_delta / rotation\"}";
            }

            BridgeCamera.Reset();
            BridgeCamera.Enqueue(
                hasX ? x : double.NaN,
                hasY ? y : double.NaN,
                hasDist ? dist : double.NaN,
                hasZoom ? zoom : double.NaN,
                hasRot ? rot : double.NaN);
            System.Threading.Thread.Sleep(200);
            return BridgeCamera.ResultJson();
        }

        /// <summary>列出游戏存档里的蓝图（走 Blueprint_Saving.GetBlueprintsList）。</summary>
        private static string HandleBlueprints()
        {
            BridgeBlueprint.RequestList();
            for (int i = 0; i < 40 && !BridgeBlueprint.ListReady; i++)
            {
                System.Threading.Thread.Sleep(100);
            }
            return BridgeBlueprint.ListJson();
        }

        /// <summary>按名字加载一个蓝图到建造场景（由游戏自己解析与生成）。</summary>
        private static string HandleBlueprintLoad(string body)
        {
            string name = ExtractString(body, "name");
            if (string.IsNullOrEmpty(name))
            {
                return "{\"ok\":false,\"error\":\"missing name\"}";
            }
            BridgeBlueprint.RequestLoad(name);
            for (int i = 0; i < 120 && !BridgeBlueprint.LoadReady; i++)
            {
                System.Threading.Thread.Sleep(100);
            }
            return BridgeBlueprint.LoadJson();
        }

        /// <summary>诊断：把几个可能的零件名来源一次性 dump 出来（主线程执行）。</summary>
        private static string HandleDebugParts()
        {
            BridgeParts.RequestDiagnostics();
            for (int i = 0; i < 30 && !BridgeParts.DiagnosticsReady; i++)
            {
                System.Threading.Thread.Sleep(100);
            }
            return BridgeParts.DiagnosticsJson();
        }

        /// <summary>把零件直接放到建造网格的指定坐标，不需要拖动。</summary>
        private static string HandleBuildPlace(string body)
        {
            string name = ExtractString(body, "name");
            if (string.IsNullOrEmpty(name))
            {
                return "{\"ok\":false,\"error\":\"missing name\"}";
            }
            double x = ExtractNumber(body, "x");
            double y = ExtractNumber(body, "y");
            string stack = ExtractString(body, "stack");

            BridgeParts.Reset();
            BridgeParts.EnqueuePlace(name, x, y, stack);
            System.Threading.Thread.Sleep(300);
            return BridgeParts.ResultJson();
        }

        /// <summary>
        /// 从查询串里取一个数值（形如 "x=0.5&amp;y=0.3"）。
        /// 不能复用 ExtractNumber —— 那个是给 JSON body 用的，按带引号的 key 找。
        /// </summary>
        private static double QueryNumber(string query, string key)
        {
            if (string.IsNullOrEmpty(query))
            {
                return 0;
            }
            string[] parts = query.Split('&');
            for (int i = 0; i < parts.Length; i++)
            {
                int eq = parts[i].IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }
                if (!string.Equals(parts[i].Substring(0, eq).Trim(), key,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                double v;
                if (double.TryParse(parts[i].Substring(eq + 1).Trim(), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out v))
                {
                    return v;
                }
            }
            return 0;
        }

        private static string Escape(string s)
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

        /// <summary>滚轮：delta 为正向上、负向下（通常 ±120）。</summary>
        private static string HandleScroll(string body)
        {
            double delta = ExtractNumber(body, "delta");
            if (delta == 0)
            {
                delta = 120;
            }
            bool ok = BridgeInput.Scroll((int)delta);
            return BridgeInput.ToJson(ok);
        }

        /// <summary>
        /// 按 /ui 清单的索引点击。
        ///
        /// 首选**游戏内输入派发**（InputManager）：走游戏自己的命中判定与按钮接线，
        /// 因此不会出现「返回成功但其实没点到」的情况，也不移动系统鼠标。
        /// 只有在拿不到坐标（或派发不可用）时，才退回直接触发按钮事件。
        /// </summary>
        private static string HandleUiClick(string body)
        {
            int index = (int)ExtractNumber(body, "index");

            // 清单可能还没抓过，或界面已变；索引越界时先重新抓一次
            if (index < 0 || index >= BridgeUi.Count)
            {
                BridgeUi.Request();
                System.Threading.Thread.Sleep(220);
            }

            double nx, ny;
            if (BridgeUi.TryGetNormalized(index, out nx, out ny))
            {
                // 点击前验证一次命中（只调一次，不会像逐元素过滤那样造成闪烁）
                BridgeUi.RequestVerify(index);
                System.Threading.Thread.Sleep(120);
                string mismatch = BridgeUi.verifyResult;

                BridgePointer.Reset();
                BridgePointer.EnqueueClick(nx, ny, 2);
                if (BridgePointer.WaitIdle(2000)
                    && BridgePointer.LastError.Length == 0
                    && BridgePointer.LastResult.Length > 0)
                {
                    string json = BridgePointer.ToJson(true);
                    if (mismatch.Length > 0)
                    {
                        json = json.Substring(0, json.Length - 1)
                            + ",\"warning\":\"" + Escape(mismatch) + "\"}";
                    }
                    return json;
                }
            }

            // 兜底：直接触发按钮的点击事件（注意：若按钮把逻辑接在 onClick 而非
            // clickEvent 上，这条路径可能“成功但没有效果”，所以只作为最后手段）
            BridgeUi.ResetClickResult();
            BridgeUi.RequestClick(index);
            System.Threading.Thread.Sleep(250);
            return BridgeUi.ClickResultJson();
        }

        private static string ExtractString(string json, string key)
        {
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }
            string needle = "\"" + key + "\"";
            int i = json.IndexOf(needle, StringComparison.Ordinal);
            if (i < 0)
            {
                return null;
            }
            int colon = json.IndexOf(':', i + needle.Length);
            if (colon < 0)
            {
                return null;
            }
            int start = json.IndexOf('"', colon + 1);
            if (start < 0)
            {
                return null;
            }
            int end = json.IndexOf('"', start + 1);
            if (end < 0)
            {
                return null;
            }
            return json.Substring(start + 1, end - start - 1);
        }

        private static double ExtractNumber(string json, string key)
        {
            if (string.IsNullOrEmpty(json))
            {
                return 0;
            }
            string needle = "\"" + key + "\"";
            int i = json.IndexOf(needle, StringComparison.Ordinal);
            if (i < 0)
            {
                return 0;
            }
            int colon = json.IndexOf(':', i + needle.Length);
            if (colon < 0)
            {
                return 0;
            }
            int p = colon + 1;
            while (p < json.Length && (json[p] == ' ' || json[p] == '\t'))
            {
                p++;
            }
            int start = p;
            while (p < json.Length &&
                   (char.IsDigit(json[p]) || json[p] == '-' || json[p] == '+' || json[p] == '.'))
            {
                p++;
            }
            double result;
            if (double.TryParse(json.Substring(start, p - start), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out result))
            {
                return result;
            }
            return 0;
        }

        private static string ReadLine(Stream stream)
        {
            StringBuilder sb = new StringBuilder(128);
            while (true)
            {
                int b = stream.ReadByte();
                if (b < 0)
                {
                    break;
                }
                if (b == '\n')
                {
                    break;
                }
                if (b != '\r')
                {
                    sb.Append((char)b);
                }
                if (sb.Length > 8192)
                {
                    break;
                }
            }
            return sb.ToString();
        }
    }
}
