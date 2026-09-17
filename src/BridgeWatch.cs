// SFS-Agent — 状态变化检测
//
// 之前的日志只有「HTTP 请求」—— 界面变成什么样、进没进世界、火节点没点着，
// 这些**游戏自己发生的变化没有任何请求承载**，所以日志里看不到。
//
// 这里在每帧采集完状态后比对一下，变了就记一条。这样日志才是完整的：
//     [09:58:12] [状态] [信息] 界面变化：9 个元素 → Play / Mod Loader / Settings ...
//     [09:58:20] [状态] [信息] 进入世界
//     [09:58:25] [状态] [信息] 场景变化：idle → build
//
// 比对只做字符串比较，开销很小；同一状态重复出现不会重复记录。

using System;
using System.Globalization;
using System.Text;

namespace SfsAgent
{
    public static class BridgeWatch
    {
        public static bool Enabled = true;

        // 各状态的上次值。null 表示还没记录过（首次不报，避免启动就刷一堆）
        private static string lastUiKey;
        private static string lastScene;
        private static string lastWorldKey;
        private static string lastBuildKey;

        private static int changes;

        public static string ToJson()
        {
            StringBuilder sb = new StringBuilder(192);
            sb.Append("{\"ok\":true,\"enabled\":").Append(Enabled ? "true" : "false")
              .Append(",\"changes\":").Append(changes)
              .Append(",\"ui\":\"").Append(Esc(lastUiKey))
              .Append("\",\"scene\":\"").Append(Esc(lastScene))
              .Append("\"}");
            return sb.ToString();
        }

        /// <summary>每帧调用（在主线程）。状态没变就什么都不做。</summary>
        public static void Poll()
        {
            if (!Enabled)
            {
                return;
            }
            try
            {
                CheckUi();
                CheckScene();
                CheckWorld();
                CheckBuild();
            }
            catch
            {
            }
        }

        // -- 界面 -------------------------------------------------------------

        private static void CheckUi()
        {
            // 用「数量 + 前几个标签」做指纹，够区分不同界面，又不会太长
            string key = BridgeUi.Fingerprint();
            if (key == null)
            {
                return;
            }
            if (lastUiKey == null)
            {
                lastUiKey = key;
                return;
            }
            if (key == lastUiKey)
            {
                return;
            }
            lastUiKey = key;
            changes++;
            BridgeLog.State("界面变化：" + BridgeUi.Describe());
        }

        // -- 场景（idle / build / flight）-------------------------------------

        private static void CheckScene()
        {
            string scene = BridgeBuild.mode;
            if (string.IsNullOrEmpty(scene))
            {
                return;
            }
            if (lastScene == null)
            {
                lastScene = scene;
                return;
            }
            if (scene == lastScene)
            {
                return;
            }
            string was = lastScene;
            lastScene = scene;
            changes++;
            BridgeLog.State("场景变化：" + Label(was) + " → " + Label(scene));
        }

        private static string Label(string mode)
        {
            if (mode == "build")
            {
                return "建造";
            }
            if (mode == "flight")
            {
                return "飞行";
            }
            if (mode == "idle")
            {
                return "空闲";
            }
            return mode;
        }

        // -- 世界状态 ---------------------------------------------------------

        private static void CheckWorld()
        {
            string key = (BridgeState.inWorld ? "1" : "0")
                + (BridgeState.flying ? "1" : "0")
                + "|" + BridgeState.planet;

            if (lastWorldKey == null)
            {
                lastWorldKey = key;
                return;
            }
            if (key == lastWorldKey)
            {
                return;
            }

            bool wasInWorld = lastWorldKey.StartsWith("1", StringComparison.Ordinal);
            bool wasFlying = lastWorldKey.Length > 1
                && lastWorldKey[1] == '1';
            lastWorldKey = key;
            changes++;

            if (BridgeState.flying && !wasFlying)
            {
                BridgeLog.State("开始飞行"
                    + (BridgeState.planet.Length > 0 ? "（" + BridgeState.planet + "）" : ""));
            }
            else if (BridgeState.inWorld && !wasInWorld)
            {
                BridgeLog.State("进入世界"
                    + (BridgeState.planet.Length > 0 ? "：" + BridgeState.planet : ""));
            }
            else if (!BridgeState.inWorld && wasInWorld)
            {
                BridgeLog.State("离开世界（回到菜单或加载中）");
            }
            else if (!BridgeState.flying && wasFlying)
            {
                BridgeLog.State("结束飞行");
            }
            else
            {
                BridgeLog.State("世界状态变化："
                    + (BridgeState.inWorld ? "在世界中" : "不在世界")
                    + (BridgeState.flying ? "、飞行中" : ""));
            }
        }

        // -- 火箭 -------------------------------------------------------------

        private static void CheckBuild()
        {
            string key = BridgeBuild.partCount.ToString(CultureInfo.InvariantCulture)
                + "|" + BridgeBuild.totalMass.ToString("0.#", CultureInfo.InvariantCulture)
                + "|" + BridgeBuild.mode;
            if (lastBuildKey == null)
            {
                lastBuildKey = key;
                return;
            }
            if (key == lastBuildKey)
            {
                return;
            }
            lastBuildKey = key;
            changes++;
            BridgeLog.State("火箭变化："
                + BridgeBuild.partCount.ToString(CultureInfo.InvariantCulture) + " 个零件、"
                + BridgeBuild.totalMass.ToString("0.#", CultureInfo.InvariantCulture) + " 吨");
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
