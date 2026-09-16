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
            int q = path.IndexOf('?');
            if (q >= 0)
            {
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

            if (path == "/screenshot")
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
                payload = "{\"ok\":true,\"mod\":\"sfs_agent\",\"version\":\"0.2.0\"}";
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
                payload = HandleClick(body);
            }
            else if (path == "/key" && method == "POST")
            {
                payload = HandleKey(body);
            }
            else if (path == "/scroll" && method == "POST")
            {
                payload = HandleScroll(body);
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
            return "{\"ok\":true,\"queued\":\"" + name + "\"}";
        }

        /// <summary>点击：x、y 为相对游戏窗口客户区的归一化坐标（0-1）。</summary>
        private static string HandleClick(string body)
        {
            double x = ExtractNumber(body, "x");
            double y = ExtractNumber(body, "y");
            if (x < 0 || x > 1 || y < 0 || y > 1)
            {
                return "{\"ok\":false,\"error\":\"x and y must be within 0..1\"}";
            }
            bool ok = BridgeInput.Click(x, y);
            return BridgeInput.ToJson(ok);
        }

        /// <summary>按键：vk 为 Win32 虚拟键码。</summary>
        private static string HandleKey(string body)
        {
            double vk = ExtractNumber(body, "vk");
            bool ok = BridgeInput.KeyPress((int)vk);
            return BridgeInput.ToJson(ok);
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
        /// 走**游戏内事件注入**（直接触发按钮的 OnInputEnd），不移动系统鼠标。
        /// </summary>
        private static string HandleUiClick(string body)
        {
            int index = (int)ExtractNumber(body, "index");
            BridgeUi.ResetClickResult();
            BridgeUi.RequestClick(index);
            // 点击必须在主线程执行，这里等它跑完
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
