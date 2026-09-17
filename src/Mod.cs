// SFS Agent for Spaceflight Simulator
// Copyright (C) 2026 星河拓航工作室 (Galaxy Exploration Studio)
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// 该 mod 在 SFS 内启动本地 HTTP 服务，对外暴露游戏状态并接收控制指令，
// 供 N.E.K.O 桌面助手（猫娘）读取与操作。
//
// 重要实现约束：本 mod **不引用任何 UnityEngine 程序集**。
// 原因：SFS 的 Unity 模块引用 netstandard 2.1，而 .NET Framework 编译器
// 不支持 netstandard 2.1。所幸 ModLoader.Mod 的基类是 System.Object、
// 成员只用 System 类型，因此可以做到零 Unity 引用：
//   - 帧循环用 Harmony 动态挂载到 SFS.World.PlayerController.LateUpdate
//   - 日志通过反射调用 UnityEngine.Debug.Log
//   - 所有游戏对象访问一律走反射
// 本文件使用 C# 5 语法。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using ModLoader;

namespace SfsAgent
{
    /// <summary>mod 入口。</summary>
    public class Main : Mod
    {
        public static Main Instance;

        public override string ModNameID
        {
            get { return "sfs_agent"; }
        }

        public override string DisplayName
        {
            get { return "SFS Agent"; }
        }

        public override string Author
        {
            get { return "Galaxy Exploration Studio"; }
        }

        public override string MinimumGameVersionNecessary
        {
            get { return "1.5.9.8"; }
        }

        public override string ModVersion
        {
            get { return "v0.4.1"; }
        }

        public override string Description
        {
            get { return "Exposes SFS telemetry over localhost and accepts control commands."; }
        }

        public override void Early_Load()
        {
            Instance = this;
            Log("Early_Load");
        }

        public override void Load()
        {
            BridgeConfig.Load();

            try
            {
                BridgeServer.Start(BridgeConfig.Port);
                Log("HTTP bridge listening on 127.0.0.1:" + BridgeConfig.Port);
            }
            catch (Exception ex)
            {
                Log("server start failed: " + ex.Message);
            }

            if (BridgeConfig.OpenBrowser)
            {
                // 延迟几秒再开，等游戏把窗口立起来，也让服务先就绪
                try
                {
                    Thread opener = new Thread(OpenBrowserLater);
                    opener.IsBackground = true;
                    opener.Name = "SfsAgentBrowser";
                    opener.Start();
                }
                catch (Exception ex)
                {
                    Log("browser thread failed: " + ex.Message);
                }
            }

            // 独占模式与覆盖层：从配置里恢复上次的选择
            try
            {
                BridgeOverlay.Exclusive = BridgePage.ReadFlag(
                    BridgeConfig.IniPath, "exclusive_input", false);
                bool overlayOn = BridgePage.ReadFlag(BridgeConfig.IniPath, "overlay", true);
                BridgeOverlay.WantVisible = BridgeOverlay.Exclusive && overlayOn;
                if (BridgeOverlay.Exclusive)
                {
                    Log("exclusive input mode restored from config");
                }
            }
            catch (Exception ex)
            {
                Log("exclusive restore failed: " + ex.Message);
            }

            try
            {
                BridgePatch.Apply();
            }
            catch (Exception ex)
            {
                Log("patch failed: " + ex.Message);
            }

            try
            {
                BridgeKeys.Install();
                Log("key injection: " + (BridgeKeys.Installed
                    ? "ON (" + BridgeKeys.InstallInfo + ")"
                    : "OFF - " + BridgeKeys.InstallInfo));
            }
            catch (Exception ex)
            {
                Log("key injection install failed: " + ex.Message);
            }
        }

        /// <summary>延迟几秒后用默认浏览器打开配置页。</summary>
        private static void OpenBrowserLater()
        {
            try
            {
                System.Threading.Thread.Sleep(4000);
                BridgePage.OpenInBrowser(BridgeConfig.Port);
                Log("config page opened in browser");
            }
            catch (Exception ex)
            {
                Log("open browser failed: " + ex.Message);
            }
        }

        /// <summary>通过反射调用 UnityEngine.Debug.Log，避免编译期 Unity 依赖。</summary>
        public static void Log(string message)
        {
            // 同时留一份在内存里，供浏览器面板的日志区查看
            BridgeLog.Info(message);
            ToUnity(message);
        }

        /// <summary>写一条警告（进内存日志 + 游戏日志）。</summary>
        public static void LogWarn(string message)
        {
            BridgeLog.Warn(message);
            ToUnity("[warn] " + message);
        }

        /// <summary>写一条错误（进内存日志 + 游戏日志）。</summary>
        public static void LogError(string message)
        {
            BridgeLog.Error(message);
            ToUnity("[error] " + message);
        }

        private static void ToUnity(string message)
        {
            try
            {
                Type debug = BridgeState.FindType("UnityEngine.Debug");
                if (debug == null)
                {
                    return;
                }
                MethodInfo m = debug.GetMethod(
                    "Log",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new Type[] { typeof(object) },
                    null);
                if (m != null)
                {
                    m.Invoke(null, new object[] { "[SfsAgent] " + message });
                }
            }
            catch
            {
            }
        }
    }

    public static class BridgeConfig
    {
        public static int Port = 21578;
        public static bool OpenBrowser = true;
        public static string IniPath = "";

        /// <summary>界面语言：zh / en。配置页与游戏内提示共用。</summary>
        public static string Lang = "zh";

        /// <summary>上次尝试绑定的端口，用于判断要不要真的重开。</summary>
        public static int BoundPort = 0;

        /// <summary>读取 Mods/SFS-Agent/sfs-agent.ini；读不到就用默认值。</summary>
        public static void Load()
        {
            try
            {
                string dir = Path.GetDirectoryName(typeof(BridgeConfig).Assembly.Location);
                if (string.IsNullOrEmpty(dir))
                {
                    return;
                }
                IniPath = Path.Combine(dir, "sfs-agent.ini");
                BridgePage.LoadConfig(IniPath, out Port, out OpenBrowser);
                ApplyLive();
            }
            catch (Exception ex)
            {
                Main.Log("config load failed: " + ex.Message);
            }
        }

        /// <summary>
        /// 把配置里**能实时生效**的部分同步到内存。
        ///
        /// 配置页保存后会调这个，所以除了端口（要么重启游戏、要么重开监听），
        /// 其余改动都不需要重启。
        /// </summary>
        public static void ApplyLive()
        {
            try
            {
                // 界面语言（配置页与游戏内提示共用）
                string v = BridgePage.ReadValue(IniPath, "lang", "zh").Trim().ToLowerInvariant();
                Lang = v == "en" ? "en" : "zh";
                BridgeOverlay.Lang = Lang;

                // 独占模式与覆盖层
                bool excl = BridgePage.ReadFlag(IniPath, "exclusive_input", false);
                bool overlayOn = BridgePage.ReadFlag(IniPath, "overlay", true);
                BridgeOverlay.Exclusive = excl;
                BridgeOverlay.WantVisible = excl && overlayOn;
            }
            catch (Exception ex)
            {
                Main.Log("apply live config failed: " + ex.Message);
            }
        }

        /// <summary>兼容旧调用名。</summary>
        public static void SyncLang()
        {
            ApplyLive();
        }

        // -- 单项读写（游戏内设置面板用）--------------------------------------

        public static bool ReadBool(string key, bool fallback)
        {
            return BridgePage.ReadFlag(IniPath, key, fallback);
        }

        public static int ReadInt(string key, int fallback)
        {
            try
            {
                int v;
                if (int.TryParse(BridgePage.ReadValue(IniPath, key, "").Trim(),
                        NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
                {
                    return v;
                }
            }
            catch
            {
            }
            return fallback;
        }

        public static string ReadText(string key, string fallback)
        {
            return BridgePage.ReadValue(IniPath, key, fallback);
        }

        public static void WriteBool(string key, bool value)
        {
            WriteKey(key, value ? "true" : "false");
        }

        public static void WriteInt(string key, int value)
        {
            WriteKey(key, value.ToString(CultureInfo.InvariantCulture));
        }

        public static void WriteText(string key, string value)
        {
            WriteKey(key, value);
        }

        /// <summary>写单个键（走和浏览器页面同一套合并写入，不会抹掉别的配置）。</summary>
        private static void WriteKey(string key, string value)
        {
            try
            {
                string body = "{\"" + key + "\":\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"}";
                BridgePage.MergeWriteJson(IniPath, body);
            }
            catch (Exception ex)
            {
                Main.LogWarn("写配置 " + key + " 失败：" + ex.Message);
            }
        }

        /// <summary>
        /// 端口热切换：配置页把 port 改了之后调它，无需重启游戏。
        /// 新端口和当前一致就什么都不做。
        /// </summary>
        public static string RestartServer()
        {
            try
            {
                int want;
                if (!int.TryParse(
                        BridgePage.ReadValue(IniPath, "port", "21578").Trim(),
                        NumberStyles.Integer, CultureInfo.InvariantCulture, out want)
                    || want < 1024 || want > 65535)
                {
                    return "端口非法，未改动";
                }
                if (want == Port && BridgeServer.IsRunning)
                {
                    return "端口未变化";
                }

                BridgeServer.Stop();
                Port = want;
                BridgeServer.Start(Port);
                BoundPort = Port;
                return "桥接已在新端口 " + Port + " 上重启";
            }
            catch (Exception ex)
            {
                return "重开桥接失败：" + ex.Message + "（已改回 " + Port + "，重启游戏生效）";
            }
        }
    }

    /// <summary>把帧循环挂到游戏的常驻对象上。</summary>
    public static class BridgePatch
    {
        private static bool applied;
        private static readonly List<string> patched = new List<string>();

        // 按优先级排列的挂载目标。
        // ActionQueue 是 SFS.Core 的全局队列，主菜单 / 建造 / 飞行三个场景都常驻，
        // 因此放在第一位（之前只挂 PlayerController，导致主菜单完全不工作）。
        private static readonly string[][] Targets = new string[][]
        {
            new string[] { "SFS.Core.ActionQueue", "Update" },
            new string[] { "SFS.UI.MsgDrawer", "Update" },
            new string[] { "SFS.UI.UIUpdater", "Update" },
            new string[] { "SFS.World.PlayerController", "LateUpdate" },
            new string[] { "SFS.World.PlayerController", "Update" },
        };

        public static List<string> PatchedTargets
        {
            get { return patched; }
        }

        public static void Apply()
        {
            if (applied)
            {
                return;
            }
            applied = true;

            Harmony harmony = new Harmony("galaxyexplorationstudio.sfs.agent");
            MethodInfo postfix = typeof(BridgePatch).GetMethod(
                "Postfix", BindingFlags.Public | BindingFlags.Static);

            for (int i = 0; i < Targets.Length; i++)
            {
                string typeName = Targets[i][0];
                string methodName = Targets[i][1];
                try
                {
                    Type target = BridgeState.FindType(typeName);
                    if (target == null)
                    {
                        continue;
                    }
                    MethodInfo method = target.GetMethod(
                        methodName,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                        null,
                        Type.EmptyTypes,
                        null);
                    if (method == null)
                    {
                        continue;
                    }
                    harmony.Patch(method, null, new HarmonyMethod(postfix), null);
                    patched.Add(typeName + "." + methodName);
                }
                catch (Exception ex)
                {
                    Main.Log("patch " + typeName + " failed: " + ex.Message);
                }
            }

            if (patched.Count == 0)
            {
                Main.Log("no frame-loop target could be patched");
            }
            else
            {
                Main.Log("patched: " + string.Join(", ", patched.ToArray()));
            }
        }

        public static void Postfix()
        {
            try
            {
                BridgeState.Capture();
                BridgeBuild.Capture();

                // 状态变化检测：界面 / 场景 / 世界 / 火箭变了就记一条日志。
                // 放在 Capture 之后，这样比的是这一帧的新状态。
                BridgeWatch.Poll();
                BridgeCommands.Tick();
                BridgeScreenshot.Tick();
                BridgeInput.Tick();
                BridgeUi.Tick();
                // 游戏内输入注入：按下的键与点击都由这里按帧推进
                BridgeKeys.Tick();
                BridgePointer.Tick();
                BridgeParts.Tick();
                BridgeBlueprint.Tick();
                BridgeCamera.Tick();
                BridgeSettings.Tick();
                BridgeOverlay.Tick();

                // 转发游戏自己的日志：不必每帧读文件，约每秒看一次就够
                if ((frameTick++ % 60) == 0)
                {
                    BridgeGameLog.Poll();
                }
            }
            catch
            {
            }
        }

        /// <summary>帧计数器，用于降低非关键任务的频率。</summary>
        private static int frameTick;
    }
}
