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
            get { return "v0.3.0"; }
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
                SyncLang();
            }
            catch (Exception ex)
            {
                Main.Log("config load failed: " + ex.Message);
            }
        }

        /// <summary>把 ini 里的 lang 同步到内存（配置页改完也会调）。</summary>
        public static void SyncLang()
        {
            try
            {
                string v = BridgePage.ReadValue(IniPath, "lang", "zh").Trim().ToLowerInvariant();
                Lang = v == "en" ? "en" : "zh";
                BridgeOverlay.Lang = Lang;
            }
            catch
            {
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
                BridgeOverlay.Tick();
            }
            catch
            {
            }
        }
    }
}
