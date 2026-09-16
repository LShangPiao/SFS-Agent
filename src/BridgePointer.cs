// SFS-Agent — 游戏内指针注入
//
// 为什么不用 Win32（SetCursorPos + mouse_event）：
//   那会**抢走用户的鼠标**并把游戏窗口切到前台，用户没法同时用电脑。
//
// 这里改为调用 SFS 自己的输入派发入口：
//     SFS.Input.InputManager.InputStart(index, InputType, TouchPosition)
//     SFS.Input.InputManager.TouchEnd (index, InputType, TouchPosition)
// InputManager 内部会自己做命中判定（CheckMouseOverState + mouseOverElement），
// 所以：
//   - 坐标是游戏自己算的，不会像 WorldToScreenPoint 那样偏十几像素
//   - 命中的是游戏认定的那个元素，不需要我们猜是哪个按钮
//   - 全程不触碰系统鼠标与焦点
//
// InputStart / TouchEnd 必须分帧调用（一次真实点击本来就有按下-抬起的过程），
// 因此这里用一个小状态机在每帧的 Tick() 里推进。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace SfsAgent
{
    public static class BridgePointer
    {
        private class Job
        {
            public double Nx;
            public double Ny;
            public int HoldFrames;
        }

        private static readonly Queue<Job> Pending = new Queue<Job>();
        private static readonly object Gate = new object();

        // 状态机：0=空闲 1=已按下待抬起 2=刚抬起待收尾
        private static int phase;
        private static int framesInPhase;
        private static int lastSeenFrame = int.MinValue;
        private static Job current;

        public static string LastResult = "";
        public static string LastError = "";
        public static volatile bool Installed;

        private const int InputTypeMouseLeft = 1;

        /// <summary>
        /// HTTP 线程调用：排队一次点击。
        /// 坐标是**归一化**的 0-1，y 从顶部算起 —— 与 /ui 返回的 x/y 同一套约定，
        /// 因此把 /ui 的坐标原样回传就能点中同一个元素。
        /// </summary>
        public static void EnqueueClick(double nx, double ny, int holdFrames)
        {
            Job job = new Job();
            job.Nx = nx;
            job.Ny = ny;
            job.HoldFrames = holdFrames < 1 ? 1 : (holdFrames > 20 ? 20 : holdFrames);
            lock (Gate)
            {
                Pending.Enqueue(job);
            }
        }

        public static int QueueLength
        {
            get
            {
                lock (Gate)
                {
                    return Pending.Count;
                }
            }
        }

        public static bool Busy
        {
            get { return phase != 0; }
        }

        /// <summary>状态机是否已空闲（按下与抬起都已完成）。</summary>
        public static bool Idle
        {
            get { return phase == 0 && QueueLength == 0; }
        }

        /// <summary>等待状态机跑完，最多 timeoutMs 毫秒。</summary>
        public static bool WaitIdle(int timeoutMs)
        {
            int waited = 0;
            while (!Idle && waited < timeoutMs)
            {
                System.Threading.Thread.Sleep(25);
                waited += 25;
            }
            return Idle;
        }

        public static void Reset()
        {
            LastResult = "";
            LastError = "";
        }

        // -- 命中判定（供 BridgeUi 过滤不可见元素）------------------------------

        private static object inputManagerCache;

        /// <summary>安静版：不写 LastError，供枚举时的命中判定使用。</summary>
        private static object FindInputManagerQuiet()
        {
            Type t = BridgeState.FindType("SFS.Input.InputManager");
            if (t == null)
            {
                return null;
            }
            object[] found = BridgeState.FindObjects(t);
            if (found.Length == 0)
            {
                return null;
            }
            inputManagerCache = found[0];
            return found[0];
        }

        /// <summary>
        /// 命中判定的前置准备：返回当前 mouseOverElement 以便还原。
        ///
        /// 为什么要还原：`CheckMouseOverState` 会写 `mouseOverElement`，
        /// 而那是游戏**画悬停高亮**用的字段。逐个元素做判定却不还原的话，
        /// 界面上所有按钮会依次闪一遍（用户肉眼可见）——
        /// 实测就是这样，所以判定必须包在 Begin/End 之间。
        /// </summary>
        public static object BeginHitTest()
        {
            object im = inputManagerCache;
            if (im == null)
            {
                im = FindInputManagerQuiet();
            }
            if (im == null)
            {
                return null;
            }
            return BridgeState.Get(im, "mouseOverElement");
        }

        public static void EndHitTest(object savedHover)
        {
            object im = inputManagerCache;
            if (im == null)
            {
                return;
            }
            try
            {
                FieldInfo f = im.GetType().GetField(
                    "mouseOverElement",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null)
                {
                    f.SetValue(im, savedHover);
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// 用游戏自己的命中判定问「这个像素点上是什么元素」。
        ///
        /// 返回 false 表示判定不可用（此时调用方应当**保留**元素，
        /// 退回到旧行为，而不是误删）。hit 为 null 表示这一点上什么都点不到。
        /// 必须在主线程调用；调用前后请配 BeginHitTest / EndHitTest。
        /// </summary>
        public static bool HitTest(double px, double py, out object hit)
        {
            hit = null;
            try
            {
                object im = inputManagerCache;
                if (im == null)
                {
                    im = FindInputManagerQuiet();
                }
                if (im == null)
                {
                    return false;
                }
                object touch = MakeTouchPosition(px, py);
                if (touch == null)
                {
                    return false;
                }
                MethodInfo check = FindMethod(im.GetType(), "CheckMouseOverState", 1);
                if (check == null)
                {
                    return false;
                }
                check.Invoke(im, new object[] { touch });
                hit = BridgeState.Get(im, "mouseOverElement");
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>命中对象是否就是该元素（或它的子/父对象）。</summary>
        public static bool HitMatches(object hit, object element)
        {
            if (hit == null || element == null)
            {
                return false;
            }
            if (ReferenceEquals(hit, element))
            {
                return true;
            }
            try
            {
                object target = BridgeState.Get(element, "transform");
                object tr = BridgeState.Get(hit, "transform");
                if (target == null || tr == null)
                {
                    return false;
                }
                for (int i = 0; i < 10 && tr != null; i++)
                {
                    if (ReferenceEquals(tr, target))
                    {
                        return true;
                    }
                    tr = BridgeState.Get(tr, "parent");
                }
            }
            catch
            {
            }
            return false;
        }

        // -- 主线程 -----------------------------------------------------------

        public static void Tick()
        {
            try
            {
                // 同一帧里帧循环补丁会被调用多次，点击的按下/抬起必须按真实帧推进
                int f = BridgeState.CurrentFrame();
                if (f == lastSeenFrame)
                {
                    return;
                }
                lastSeenFrame = f;
                framesInPhase++;

                if (phase == 0)
                {
                    Job next = null;
                    lock (Gate)
                    {
                        if (Pending.Count > 0)
                        {
                            next = Pending.Dequeue();
                        }
                    }
                    if (next == null)
                    {
                        return;
                    }
                    current = next;
                    if (!BeginPress(current))
                    {
                        current = null;
                        return;
                    }
                    phase = 1;
                    framesInPhase = 0;
                    return;
                }

                if (phase == 1 && framesInPhase >= current.HoldFrames)
                {
                    FinishRelease(current);
                    phase = 2;
                    framesInPhase = 0;
                    return;
                }

                if (phase == 2 && framesInPhase >= 1)
                {
                    current = null;
                    phase = 0;
                    framesInPhase = 0;
                }
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                phase = 0;
                framesInPhase = 0;
                current = null;
            }
        }

        // -- 反射实现 ---------------------------------------------------------

        private static object FindInputManager()
        {
            Type t = BridgeState.FindType("SFS.Input.InputManager");
            if (t == null)
            {
                LastError = "SFS.Input.InputManager not found";
                return null;
            }
            object[] found = BridgeState.FindObjects(t);
            if (found.Length == 0)
            {
                LastError = "no InputManager instance in this scene";
                return null;
            }
            return found[0];
        }

        private static object MakeVector2(double x, double y)
        {
            Type v2 = BridgeState.FindType("UnityEngine.Vector2");
            if (v2 == null)
            {
                return null;
            }
            ConstructorInfo ctor = v2.GetConstructor(new Type[] { typeof(float), typeof(float) });
            if (ctor == null)
            {
                return null;
            }
            return ctor.Invoke(new object[] { (float)x, (float)y });
        }

        private static object MakeTouchPosition(double px, double py)
        {
            Type tp = BridgeState.FindType("SFS.Input.TouchPosition");
            if (tp == null)
            {
                LastError = "SFS.Input.TouchPosition not found";
                return null;
            }
            object v = MakeVector2(px, py);
            if (v == null)
            {
                LastError = "UnityEngine.Vector2 not found";
                return null;
            }
            ConstructorInfo[] ctors = tp.GetConstructors();
            for (int i = 0; i < ctors.Length; i++)
            {
                ParameterInfo[] ps = ctors[i].GetParameters();
                if (ps.Length == 1 && ps[0].ParameterType == v.GetType())
                {
                    return ctors[i].Invoke(new object[] { v });
                }
            }
            LastError = "TouchPosition(Vector2) ctor not found";
            return null;
        }

        private static object MakeInputType()
        {
            Type it = BridgeState.FindType("SFS.Input.InputType");
            if (it == null)
            {
                LastError = "SFS.Input.InputType not found";
                return null;
            }
            return Enum.ToObject(it, InputTypeMouseLeft);
        }

        /// <summary>按名字 + 参数个数找方法（运行时不保证 GetMethods 能枚举到接口实现）。</summary>
        private static MethodInfo FindMethod(Type t, string name, int paramCount)
        {
            MethodInfo[] all = t.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].Name == name && all[i].GetParameters().Length == paramCount)
                {
                    return all[i];
                }
            }
            return null;
        }

        /// <summary>屏幕像素尺寸，取 Unity 的 Screen —— 与 /ui 的坐标来源保持一致。</summary>
        private static bool ScreenSize(out double w, out double h)
        {
            w = 0;
            h = 0;
            Type t = BridgeState.FindType("UnityEngine.Screen");
            if (t == null)
            {
                LastError = "UnityEngine.Screen not found";
                return false;
            }
            object sw = BridgeState.GetStatic(t, "width");
            object sh = BridgeState.GetStatic(t, "height");
            if (sw == null || sh == null)
            {
                LastError = "Screen.width/height unavailable";
                return false;
            }
            try
            {
                w = Convert.ToDouble(sw, CultureInfo.InvariantCulture);
                h = Convert.ToDouble(sh, CultureInfo.InvariantCulture);
            }
            catch
            {
                LastError = "Screen size not numeric";
                return false;
            }
            return w > 0 && h > 0;
        }

        /// <summary>归一化（左上角原点）-> 像素。</summary>
        private static bool ToPixels(Job job, out double px, out double py)
        {
            px = 0;
            py = 0;
            double w, h;
            if (!ScreenSize(out w, out h))
            {
                return false;
            }
            double nx = job.Nx < 0 ? 0 : (job.Nx > 1 ? 1 : job.Nx);
            double ny = job.Ny < 0 ? 0 : (job.Ny > 1 ? 1 : job.Ny);
            px = nx * w;
            py = (1.0 - ny) * h;
            return true;
        }

        private static bool BeginPress(Job job)
        {
            double px, py;
            if (!ToPixels(job, out px, out py))
            {
                return false;
            }
            object im = FindInputManager();
            if (im == null)
            {
                return false;
            }
            object touch = MakeTouchPosition(px, py);
            object type = MakeInputType();
            if (touch == null || type == null)
            {
                return false;
            }

            // 关键：InputManager 的命中判定在 CheckMouseOverState 里，
            // 它把结果写进 mouseOverElement。不先做这一步，InputStart 用的会是
            // 上一次（通常是 null）的悬停元素，点了等于没点。
            MethodInfo hover = FindMethod(im.GetType(), "CheckMouseOverState", 1);
            if (hover != null)
            {
                try
                {
                    hover.Invoke(im, new object[] { touch });
                }
                catch (Exception ex)
                {
                    LastError = "CheckMouseOverState failed: " + ex.Message;
                    return false;
                }
            }

            MethodInfo start = FindMethod(im.GetType(), "InputStart", 3);
            if (start == null)
            {
                LastError = "InputManager.InputStart(int, InputType, TouchPosition) not found";
                return false;
            }

            // 参数类型可能与预期不同，按声明顺序尽量适配
            ParameterInfo[] ps = start.GetParameters();
            object[] args = new object[3];
            args[0] = Adapt(ps[0].ParameterType, 0);
            args[1] = Adapt(ps[1].ParameterType, type);
            args[2] = Adapt(ps[2].ParameterType, touch);
            start.Invoke(im, args);

            LastResult = "press at pixel (" + Num(px) + ", " + Num(py) + ")";
            return true;
        }

        private static void FinishRelease(Job job)
        {
            double px, py;
            if (!ToPixels(job, out px, out py))
            {
                return;
            }
            object im = FindInputManager();
            if (im == null)
            {
                return;
            }
            object touch = MakeTouchPosition(px, py);
            object type = MakeInputType();
            if (touch == null || type == null)
            {
                return;
            }

            MethodInfo end = FindMethod(im.GetType(), "TouchEnd", 3);
            if (end == null)
            {
                LastError = "InputManager.TouchEnd(int, InputType, TouchPosition) not found";
                return;
            }

            ParameterInfo[] ps = end.GetParameters();
            object[] args = new object[3];
            args[0] = Adapt(ps[0].ParameterType, 0);
            args[1] = Adapt(ps[1].ParameterType, type);
            args[2] = Adapt(ps[2].ParameterType, touch);
            end.Invoke(im, args);

            LastResult = "clicked element at (" + Num(job.Nx) + ", " + Num(job.Ny)
                + ") via InputManager";
        }

        private static object Adapt(Type want, object value)
        {
            if (value == null)
            {
                return null;
            }
            if (want.IsInstanceOfType(value))
            {
                return value;
            }
            try
            {
                if (want == typeof(int))
                {
                    return Convert.ToInt32(value, CultureInfo.InvariantCulture);
                }
                if (want == typeof(float))
                {
                    return Convert.ToSingle(value, CultureInfo.InvariantCulture);
                }
            }
            catch
            {
            }
            return value;
        }

        private static string Num(double v)
        {
            return v.ToString("0.#", CultureInfo.InvariantCulture);
        }

        public static string ToJson(bool ok)
        {
            StringBuilder sb = new StringBuilder(192);
            sb.Append("{\"ok\":").Append(ok ? "true" : "false");
            if (LastResult.Length > 0)
            {
                sb.Append(",\"result\":\"").Append(Esc(LastResult)).Append("\"");
            }
            if (LastError.Length > 0)
            {
                sb.Append(",\"error\":\"").Append(Esc(LastError)).Append("\"");
            }
            sb.Append(",\"mode\":\"in_game_pointer\"}");
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
