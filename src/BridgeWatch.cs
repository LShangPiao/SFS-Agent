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
                CheckFlight();
            }
            catch
            {
            }
        }

        // -- 飞行数据（高度 / 角度 / 轨道）------------------------------------
        //
        // 这些值一直在变，逐帧记录会把日志刷爆。所以只在**跨过阈值**时记：
        //   高度   —— 每变化 1000 m 记一次（上升/下降分别记）
        //   角度   —— 每变化 15° 记一次（转向/俯仰）
        //   轨道   —— 近点/远点/周期变化就记（这是离散事件，不会刷屏）

        private const double HeightStep = 1000;
        private const double AngleStep = 15;

        private static double lastLoggedHeight = double.NaN;
        private static double lastLoggedAngle = double.NaN;
        private static string lastOrbitKey;

        private static void CheckFlight()
        {
            if (!BridgeState.flying)
            {
                lastLoggedHeight = double.NaN;
                lastLoggedAngle = double.NaN;
                lastOrbitKey = null;
                return;
            }

            CheckHeight();
            CheckAngle();
            CheckOrbit();
        }

        private static void CheckHeight()
        {
            double h = BridgeState.height;
            if (double.IsNaN(h))
            {
                return;
            }
            if (double.IsNaN(lastLoggedHeight))
            {
                lastLoggedHeight = h;
                BridgeLog.Flight("当前高度 " + M(h));
                return;
            }
            double d = h - lastLoggedHeight;
            if (Math.Abs(d) < HeightStep)
            {
                return;
            }
            // 落在哪个 1000 米档位上
            double bucket = Math.Floor(h / HeightStep) * HeightStep;
            if (bucket == lastLoggedHeight)
            {
                return;
            }
            bool up = d > 0;
            lastLoggedHeight = bucket;
            BridgeLog.Flight((up ? "爬升到 " : "下降到 ") + M(bucket)
                + "（速度 " + M(BridgeState.speed) + "/s）");
        }

        private static void CheckAngle()
        {
            double a = BridgeState.angle;
            if (double.IsNaN(a))
            {
                return;
            }
            if (double.IsNaN(lastLoggedAngle))
            {
                lastLoggedAngle = a;
                return;
            }
            if (Math.Abs(a - lastLoggedAngle) < AngleStep)
            {
                return;
            }
            double from = lastLoggedAngle;
            lastLoggedAngle = a;
            BridgeLog.Flight("姿态角 " + Deg(from) + " → " + Deg(a));
        }

        private static void CheckOrbit()
        {
            if (!BridgeState.hasOrbit)
            {
                return;
            }
            // 轨道是离散量，直接比对；用人话描述成 ap/pe 组合
            string key = R(BridgeState.orbitApoapsis) + "|"
                + R(BridgeState.orbitPeriapsis) + "|"
                + R(BridgeState.orbitPeriod);
            if (key == lastOrbitKey)
            {
                return;
            }
            bool first = lastOrbitKey == null;
            lastOrbitKey = key;

            string desc = "轨道：近点 " + M(BridgeState.orbitPeriapsis)
                + "、远点 " + M(BridgeState.orbitApoapsis);
            if (!double.IsNaN(BridgeState.orbitPeriod) && BridgeState.orbitPeriod > 0)
            {
                desc += "、周期 " + Time(BridgeState.orbitPeriod);
            }
            if (BridgeState.orbitPeriapsis > 70000 && BridgeState.orbitApoapsis > 70000)
            {
                desc += " —— 已入轨";
            }
            else if (BridgeState.orbitPeriapsis < 0)
            {
                desc += " —— 近点在地面以下（会再入）";
            }
            BridgeLog.Flight((first ? "进入轨道：" : "轨道变化：") + desc.Substring(desc.IndexOf(':') + 1));
        }

        private static string R(double v)
        {
            if (double.IsNaN(v))
            {
                return "-";
            }
            // 100 米粒度，避免末位抖动导致误报
            return Math.Round(v / 100).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>把米数写成 km / m。</summary>
        private static string M(double meters)
        {
            if (double.IsNaN(meters))
            {
                return "?";
            }
            double a = Math.Abs(meters);
            if (a >= 1000)
            {
                return (meters / 1000).ToString("0.##", CultureInfo.InvariantCulture) + " km";
            }
            return meters.ToString("0.#", CultureInfo.InvariantCulture) + " m";
        }

        private static string Deg(double d)
        {
            return d.ToString("0.#", CultureInfo.InvariantCulture) + "\u00b0";
        }

        private static string Time(double seconds)
        {
            if (seconds >= 3600)
            {
                return (seconds / 3600).ToString("0.##", CultureInfo.InvariantCulture) + " 小时";
            }
            if (seconds >= 60)
            {
                return (seconds / 60).ToString("0.#", CultureInfo.InvariantCulture) + " 分钟";
            }
            return seconds.ToString("0", CultureInfo.InvariantCulture) + " 秒";
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
