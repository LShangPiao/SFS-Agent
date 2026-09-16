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
                if (Active.Count == 0)
                {
                    return true;
                }
                int key;
                if (!TryKey(__args, out key))
                {
                    return true;
                }
                for (int i = 0; i < Active.Count; i++)
                {
                    if (Active[i].Key == key && !Active[i].Expired)
                    {
                        __result = true;
                        return false;
                    }
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
                if (Active.Count == 0)
                {
                    return true;
                }
                int key;
                if (!TryKey(__args, out key))
                {
                    return true;
                }
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
            catch
            {
            }
            return true;
        }

        public static bool GetKeyUpPrefix(object[] __args, ref bool __result)
        {
            try
            {
                if (Released.Count == 0)
                {
                    return true;
                }
                int key;
                if (!TryKey(__args, out key))
                {
                    return true;
                }
                for (int i = 0; i < Released.Count; i++)
                {
                    if (Released[i].Key == key && Released[i].StartFrame == frameId)
                    {
                        __result = true;
                        return false;
                    }
                }
            }
            catch
            {
            }
            return true;
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
