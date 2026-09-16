// SFS-Agent — UI 操作（Win32 输入模拟）
//
// 为什么用 Win32 而不是直接调 SFS.UI.Button：
//   SFS 用的是自己的按钮类，标签在子对象里，逐个逆向成本高且随版本变化。
//   改为「猫娘看截图 → 决定点哪里 → 发送归一化坐标 → mod 模拟真实鼠标」，
//   这样对任何 UI（主菜单、建造菜单、设置面板）都通用。
//
// 坐标约定：归一化 0-1，相对游戏窗口的客户区左上角。

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SfsAgent
{
    public static class BridgeInput
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_WHEEL = 0x0800;

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, uint dx, uint dy, int dwData, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private const uint KEYEVENTF_KEYUP = 0x0002;

        public static string LastResult = "";
        public static string LastError = "";

        /// <summary>本游戏的窗口句柄。</summary>
        public static IntPtr GameWindow()
        {
            try
            {
                return Process.GetCurrentProcess().MainWindowHandle;
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        /// <summary>客户区尺寸，失败返回 false。</summary>
        public static bool ClientSize(out int width, out int height)
        {
            width = 0;
            height = 0;
            IntPtr hwnd = GameWindow();
            if (hwnd == IntPtr.Zero)
            {
                return false;
            }
            RECT rect;
            if (!GetClientRect(hwnd, out rect))
            {
                return false;
            }
            width = rect.Right - rect.Left;
            height = rect.Bottom - rect.Top;
            return width > 0 && height > 0;
        }

        /// <summary>把归一化坐标换算成屏幕绝对坐标。</summary>
        private static bool ToScreen(double nx, double ny, out int sx, out int sy)
        {
            sx = 0;
            sy = 0;
            IntPtr hwnd = GameWindow();
            if (hwnd == IntPtr.Zero)
            {
                return false;
            }
            RECT rect;
            if (!GetClientRect(hwnd, out rect))
            {
                return false;
            }
            int w = rect.Right - rect.Left;
            int h = rect.Bottom - rect.Top;
            if (w <= 0 || h <= 0)
            {
                return false;
            }

            POINT pt = new POINT();
            pt.X = (int)Math.Round(nx * (w - 1));
            pt.Y = (int)Math.Round(ny * (h - 1));
            if (!ClientToScreen(hwnd, ref pt))
            {
                return false;
            }
            sx = pt.X;
            sy = pt.Y;
            return true;
        }

        /// <summary>在归一化坐标处点击左键。</summary>
        public static bool Click(double nx, double ny)
        {
            LastError = "";
            try
            {
                int sx, sy;
                if (!ToScreen(nx, ny, out sx, out sy))
                {
                    LastError = "cannot resolve window rect";
                    return false;
                }

                IntPtr hwnd = GameWindow();
                if (GetForegroundWindow() != hwnd)
                {
                    SetForegroundWindow(hwnd);
                }

                SetCursorPos(sx, sy);
                System.Threading.Thread.Sleep(30);
                mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
                System.Threading.Thread.Sleep(40);
                mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
                LastResult = "clicked at normalized (" + nx.ToString("0.###")
                    + ", " + ny.ToString("0.###") + ")";
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return false;
            }
        }

        /// <summary>滚轮。delta 为正向上。</summary>
        public static bool Scroll(int delta)
        {
            LastError = "";
            try
            {
                mouse_event(MOUSEEVENTF_WHEEL, 0, 0, delta, UIntPtr.Zero);
                LastResult = "scrolled " + delta;
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return false;
            }
        }

        /// <summary>按下并释放一个虚拟键码。</summary>
        public static bool KeyPress(int vk)
        {
            LastError = "";
            try
            {
                if (vk <= 0 || vk > 255)
                {
                    LastError = "invalid vk code";
                    return false;
                }
                IntPtr hwnd = GameWindow();
                if (GetForegroundWindow() != hwnd)
                {
                    SetForegroundWindow(hwnd);
                }
                keybd_event((byte)vk, 0, 0, UIntPtr.Zero);
                System.Threading.Thread.Sleep(30);
                keybd_event((byte)vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                LastResult = "key " + vk;
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 预留的主线程钩子。Win32 输入本身不要求主线程，
        /// 这里保留是因为将来若要直接调 Unity UI 事件就需要。
        /// </summary>
        public static void Tick()
        {
        }

        public static string ToJson(bool ok)
        {
            string json = "{\"ok\":" + (ok ? "true" : "false");
            if (LastResult.Length > 0)
            {
                json += ",\"result\":\"" + LastResult.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            }
            if (LastError.Length > 0)
            {
                json += ",\"error\":\"" + LastError.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            }
            int w, h;
            if (ClientSize(out w, out h))
            {
                json += ",\"client_width\":" + w + ",\"client_height\":" + h;
            }
            json += "}";
            return json;
        }
    }
}
