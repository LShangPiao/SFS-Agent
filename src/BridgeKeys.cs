// SFS-Agent — 游戏内按键注入
//
// 为什么不用 Win32 keybd_event：
//   那需要先把游戏窗口切到前台（SetForegroundWindow），会**抢走用户的焦点**；
//   而且如果游戏不在前台，按键会打到用户正在用的其它程序里，非常危险。
//
// 这里改为 Harmony 拦截 UnityEngine.Input.GetKey / GetKeyDown / GetKeyUp：
//   - 只对我们正在注入的那几个键覆盖返回值，其余键一律走原方法
//   - 游戏自己的输入逻辑完全不变，因此分级、转向、菜单都按原生行为工作
//   - 不触碰系统输入，游戏也不需要在前台
//
// 关于方法签名：UnityEngine.KeyCode 是游戏程序集里的类型，本 mod 不引用任何
// Unity 程序集，无法在编译期声明 KeyCode 形参。因此前缀用 Harmony 的
// object[] __args 注入实参（Harmony 2.x 支持）。若当前 Harmony 版本不支持，
// Install() 会捕获异常并如实上报，不会静默失效。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;

namespace SfsAgent
{
    public static class BridgeKeys
    {
        private class Injection
        {
            public int Key;
            public long EndTicks;
            public int StartFrame = -1;
            public bool Expired;
        }

        private static readonly List<Injection> Pending = new List<Injection>();
        private static readonly List<Injection> Active = new List<Injection>();
        private static readonly List<Injection> Released = new List<Injection>();
        private static readonly object Gate = new object();

        private static int frameId;
        private static int lastSeenFrame = int.MinValue;
        private const int KeyCodeF10 = 285;

        /// <summary>agent 注入的鼠标按键编号（0=左 1=右 2=中）。</summary>
        private static readonly List<int> ActiveMouse = new List<int>();
        private static readonly List<int> PendingMouse = new List<int>();

        public static bool Installed;
        public static string InstallInfo = "";

        /// <summary>HTTP 线程调用：排队一次按键（按下后保持 holdMs 毫秒再抬起）。</summary>
        public static void Enqueue(int keyCode, int holdMs)
        {
            if (holdMs < 40)
            {
                holdMs = 40;
            }
            if (holdMs > 10000)
            {
                holdMs = 10000;
            }
            Injection inj = new Injection();
            inj.Key = keyCode;
            inj.EndTicks = DateTime.UtcNow.Ticks + holdMs * TimeSpan.TicksPerMillisecond;
            lock (Gate)
            {
                Pending.Add(inj);
            }
        }

        public static bool HasInjection
        {
            get
            {
                lock (Gate)
                {
                    return Active.Count > 0 || Pending.Count > 0;
                }
            }
        }

        /// <summary>主线程：推进帧计数、把新注入转入生效、回收过期的。</summary>
        public static void Tick()
        {
            // 同一帧里帧循环补丁会被调用多次，只按真正的帧推进
            int f = BridgeState.CurrentFrame();
            if (f == lastSeenFrame)
            {
                return;
            }
            lastSeenFrame = f;
            frameId++;
            try
            {
                lock (Gate)
                {
                    if (Pending.Count > 0)
                    {
                        for (int i = 0; i < Pending.Count; i++)
                        {
                            Pending[i].StartFrame = frameId + 1;
                            Active.Add(Pending[i]);
                        }
                        Pending.Clear();
                    }
                    if (PendingMouse.Count > 0)
                    {
                        ActiveMouse.AddRange(PendingMouse);
                        PendingMouse.Clear();
                    }
                }

                long now = DateTime.UtcNow.Ticks;
                for (int i = Active.Count - 1; i >= 0; i--)
                {
                    if (now >= Active[i].EndTicks)
                    {
                        Release(Active[i]);
                        Active.RemoveAt(i);
                    }
                }

                // GetKeyUp 只在释放后的那一帧返回 true
                for (int i = Released.Count - 1; i >= 0; i--)
                {
                    if (Released[i].StartFrame < frameId - 1)
                    {
                        Released.RemoveAt(i);
                    }
                }

                // 鼠标注入只维持几帧，够游戏读到即可
                if (ActiveMouse.Count > 0 && (frameId % 6) == 0)
                {
                    ActiveMouse.Clear();
                }
            }
            catch (Exception ex)
            {
                InstallInfo = "tick error: " + ex.Message;
            }
        }

        private static void Release(Injection inj)
        {
            inj.Expired = true;
            inj.StartFrame = frameId;
            Released.Add(inj);
        }

        // -- Harmony 前缀 -----------------------------------------------------
        //
        // 约定：返回 false 表示不再执行原方法，__result 由我们给出。

        public static bool GetKeyPrefix(object[] __args, ref bool __result)
        {
            try
            {
                int key;
                if (!TryKey(__args, out key))
                {
                    return true;
                }

                // 1) agent 注入的键优先
                if (Active.Count > 0)
                {
                    for (int i = 0; i < Active.Count; i++)
                    {
                        if (Active[i].Key == key && !Active[i].Expired)
                        {
                            __result = true;
                            return false;
                        }
                    }
                }

                // 2) 独占模式：吞掉用户自己的按键，游戏只接受 agent 的注入
                if (BridgeOverlay.Exclusive)
                {
                    __result = false;
                    return false;
                }
            }
            catch
            {
                // 出错时一律放行原方法，绝不把游戏输入搞坏
            }
            return true;
        }

        public static bool GetKeyDownPrefix(object[] __args, ref bool __result)
        {
            try
            {
                int key;
                if (!TryKey(__args, out key))
                {
                    return true;
                }

                // 独占模式下 F10 作为「解除」的快捷键（鼠标被吞掉了，留个后门）
                if (BridgeOverlay.Exclusive && key == KeyCodeF10)
                {
                    BridgeOverlay.Exclusive = false;
                    BridgeOverlay.WantVisible = false;
                    __result = true;
                    return false;
                }

                if (Active.Count > 0)
                {
                    for (int i = 0; i < Active.Count; i++)
                    {
                        Injection inj = Active[i];
                        if (inj.Key != key || inj.Expired)
                        {
                            continue;
                        }
                        // 只在注入生效后的那一帧算作“刚按下”，避免重复触发（分级只能触发一次）
                        if (inj.StartFrame == frameId)
                        {
                            __result = true;
                            return false;
                        }
                        return true;
                    }
                }

                if (BridgeOverlay.Exclusive)
                {
                    __result = false;
                    return false;
                }
            }
            catch
            {
            }
            return true;
        }

        public static bool GetKeyUpPrefix(object[] __args, ref bool __result)
        {
            try
            {
                int key;
                if (!TryKey(__args, out key))
                {
                    return true;
                }
                if (Released.Count > 0)
                {
                    for (int i = 0; i < Released.Count; i++)
                    {
                        if (Released[i].Key == key && Released[i].StartFrame == frameId)
                        {
                            __result = true;
                            return false;
                        }
                    }
                }
                if (BridgeOverlay.Exclusive)
                {
                    __result = false;
                    return false;
                }
            }
            catch
            {
            }
            return true;
        }

        // -- 鼠标（独占时吞掉用户点击，但放行「解除」按钮上的点击）-------------

        public static bool GetMousePrefix(object[] __args, ref bool __result)
        {
            try
            {
                if (ActiveMouse.Count > 0)
                {
                    int btn;
                    if (TryKey(__args, out btn) && ActiveMouse.Contains(btn))
                    {
                        __result = true;
                        return false;
                    }
                }

                if (!BridgeOverlay.Exclusive)
                {
                    return true;
                }

                // 用户点在「解除」按钮上：放行这次点击，并退出独占
                BridgeOverlay.RefreshGeometry();
                double mx, my;
                if (TryMousePosition(out mx, out my) && BridgeOverlay.IsUnlockHit(mx, my))
                {
                    BridgeOverlay.Exclusive = false;
                    BridgeOverlay.WantVisible = false;
                    __result = false;   // 这次点击不传给游戏
                    return false;
                }

                __result = false;       // 其余点击一律吞掉
                return false;
            }
            catch
            {
            }
            return true;
        }

        private static bool TryMousePosition(out double x, out double y)
        {
            x = 0;
            y = 0;
            try
            {
                Type inputType = BridgeState.FindType("UnityEngine.Input");
                object p = BridgeState.GetStatic(inputType, "mousePosition");
                if (p == null)
                {
                    return false;
                }
                // Unity 的屏幕坐标原点在左下角，这里换算成左上角
                double px = BridgeState.ToDouble(BridgeState.Get(p, "x"), 0);
                double py = BridgeState.ToDouble(BridgeState.Get(p, "y"), 0);
                double h = BridgeState.ToDouble(
                    BridgeState.GetStatic(BridgeState.FindType("UnityEngine.Screen"), "height"), 0);
                x = px;
                y = (h > 0 ? h : py) - py;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryKey(object[] args, out int key)
        {
            key = 0;
            if (args == null || args.Length != 1 || args[0] == null)
            {
                return false;
            }
            try
            {
                key = Convert.ToInt32(args[0], CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // -- 安装 -------------------------------------------------------------

        public static void Install()
        {
            if (Installed)
            {
                return;
            }
            try
            {
                Type inputType = BridgeState.FindType("UnityEngine.Input");
                if (inputType == null)
                {
                    InstallInfo = "UnityEngine.Input not found";
                    return;
                }
                Type keyCodeType = BridgeState.FindType("UnityEngine.KeyCode");
                if (keyCodeType == null)
                {
                    InstallInfo = "UnityEngine.KeyCode not found";
                    return;
                }

                Harmony harmony = new Harmony("galaxyexplorationstudio.sfs.agent.keys");
                MethodInfo pKey = typeof(BridgeKeys).GetMethod(
                    "GetKeyPrefix", BindingFlags.Public | BindingFlags.Static);
                MethodInfo pDown = typeof(BridgeKeys).GetMethod(
                    "GetKeyDownPrefix", BindingFlags.Public | BindingFlags.Static);
                MethodInfo pUp = typeof(BridgeKeys).GetMethod(
                    "GetKeyUpPrefix", BindingFlags.Public | BindingFlags.Static);

                int patched = 0;
                patched += PatchOne(harmony, inputType, "GetKey", keyCodeType, pKey);
                patched += PatchOne(harmony, inputType, "GetKeyDown", keyCodeType, pDown);
                patched += PatchOne(harmony, inputType, "GetKeyUp", keyCodeType, pUp);

                // 鼠标：独占模式需要把用户的点击也吞掉
                MethodInfo pMouse = typeof(BridgeKeys).GetMethod(
                    "GetMousePrefix", BindingFlags.Public | BindingFlags.Static);
                patched += PatchOne(harmony, inputType, "GetMouseButton", typeof(int), pMouse);
                patched += PatchOne(harmony, inputType, "GetMouseButtonDown", typeof(int), pMouse);
                patched += PatchOne(harmony, inputType, "GetMouseButtonUp", typeof(int), pMouse);

                if (patched == 0)
                {
                    InstallInfo = "no Input method could be patched";
                    return;
                }

                Installed = true;
                InstallInfo = "patched " + patched + " Input method(s)";
            }
            catch (Exception ex)
            {
                InstallInfo = ex.GetType().Name + ": " + ex.Message;
            }
        }

        private static int PatchOne(
            Harmony harmony, Type inputType, string name, Type keyCodeType, MethodInfo prefix)
        {
            try
            {
                MethodInfo target = inputType.GetMethod(
                    name,
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new Type[] { keyCodeType },
                    null);
                if (target == null)
                {
                    return 0;
                }
                harmony.Patch(
                    target,
                    new HarmonyMethod(prefix),
                    null,
                    null,
                    null,
                    null);
                return 1;
            }
            catch (Exception ex)
            {
                Main.Log("patch Input." + name + " failed: " + ex.Message);
                InstallInfo = name + " patch failed: " + ex.Message;
                return 0;
            }
        }
    }
}
