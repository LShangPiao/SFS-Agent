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
        // 这些值一直在变，逐帧记录会把日志刷爆。所以只在**跨过阈值**时记。
        //
        // 阈值是**按高度自适应**的 —— 刚起飞时每 100 米都值得记（此时正是
        // 最需要看清状态的阶段），到了几十公里高空再按 1000 米记，
        // 免得几十条「爬升到 xx km」淹没有用信息。
        //
        //   高度 < 1 km   → 每 100 m
        //   高度 < 10 km  → 每 500 m
        //   高度 < 100 km → 每 1 km
        //   更高          → 每 10 km
        //
        // 角度每 5° 记一次（转向/俯仰在飞行中是关键动作）。

        private const double AngleStep = 5;

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

        /// <summary>当前高度下，多少米才值得记一条。</summary>
        private static double HeightStepFor(double h)
        {
            double a = Math.Abs(h);
            if (a < 1000)
            {
                return 100;
            }
            if (a < 10000)
            {
                return 500;
            }
            if (a < 100000)
            {
                return 1000;
            }
            return 10000;
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
                // 首次：记一条当前状态，并把基准对齐到当前档位，
                // 免得紧接着又报一条「下降到 4.5 km」
                lastLoggedHeight = Math.Floor(h / HeightStepFor(h)) * HeightStepFor(h);
                BridgeLog.Flight(BridgeLang.T("当前高度 ", "altitude ") + M(h)
                    + BridgeLang.T("、速度 ", ", speed ") + M(BridgeState.speed) + "/s");
                return;
            }

            double step = HeightStepFor(h);
            // 落在哪个档位上（按当前档位取整）
            double bucket = Math.Floor(h / step) * step;
            if (bucket == lastLoggedHeight)
            {
                return;
            }

            bool up = bucket > lastLoggedHeight;
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
                BridgeLog.Flight(BridgeLang.T("当前姿态角 ", "attitude ") + Deg(a));
                return;
            }
            if (Math.Abs(a - lastLoggedAngle) < AngleStep)
            {
                return;
            }
            double from = lastLoggedAngle;
            lastLoggedAngle = a;
            BridgeLog.Flight(BridgeLang.T("姿态角 ", "attitude ") + Deg(from) + " → " + Deg(a));
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

            double Re = BridgeState.PlanetRadius();
            double atm = BridgeState.AtmosphereHeight();
            string desc = "\u8f68\u9053\uff1a\u8fd1\u70b9 " + M(BridgeState.orbitPeriapsis - Re)
                + "\u3001\u8fdc\u70b9 " + M(BridgeState.orbitApoapsis - Re);
            if (!double.IsNaN(BridgeState.orbitPeriod) && BridgeState.orbitPeriod > 0)
            {
                desc += "\u3001\u5468\u671f " + Time(BridgeState.orbitPeriod);
            }
            // \u5165\u8f68\u5224\u5b9a\u7528\u5929\u4f53\u771f\u5b9e\u5927\u6c14\u9ad8\u5ea6\uff0c\u4e0d\u80fd\u5199\u6b7b 70 km
            if (BridgeState.orbitPeriapsis - Re > atm)
            {
                desc += " \u2014\u2014 \u5df2\u5165\u8f68";
            }
            else
            {
                desc += " \u2014\u2014 \u8fd1\u70b9\u5728\u5927\u6c14\u5185\uff08\u4f1a\u518d\u5165\uff09";
            }
            BridgeLog.Flight((first ? "\u8fdb\u5165\u8f68\u9053\uff1a" : "") + desc.Substring(desc.IndexOf(':') + 1));
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
            BridgeLog.State(BridgeLang.T("界面变化：", "UI changed: ") + BridgeUi.Describe());
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
            BridgeLog.State(BridgeLang.T("场景变化：", "scene: ") + Label(was) + " → " + Label(scene));
        }

        private static string Label(string mode)
        {
            if (mode == "build")
            {
                return BridgeLang.T("建造", "build");
            }
            if (mode == "flight")
            {
                return BridgeLang.T("飞行", "flight");
            }
            if (mode == "idle")
            {
                return BridgeLang.T("空闲", "idle");
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
                BridgeLog.State(BridgeLang.T("开始飞行", "flight started")
                    + (BridgeState.planet.Length > 0
                        ? BridgeLang.T("（", " (") + BridgeState.planet
                            + BridgeLang.T("）", ")")
                        : ""));
            }
            else if (BridgeState.inWorld && !wasInWorld)
            {
                BridgeLog.State(BridgeLang.T("进入世界", "entered world")
                    + (BridgeState.planet.Length > 0
                        ? BridgeLang.T("：", ": ") + BridgeState.planet
                        : ""));
            }
            else if (!BridgeState.inWorld && wasInWorld)
            {
                BridgeLog.State(BridgeLang.T("离开世界（回到菜单或加载中）", "left world (menu or loading)"));
            }
            else if (!BridgeState.flying && wasFlying)
            {
                BridgeLog.State(BridgeLang.T("结束飞行", "flight ended"));
            }
            else
            {
                BridgeLog.State(BridgeLang.T("世界状态变化：", "world state: ")
                    + (BridgeState.inWorld
                        ? BridgeLang.T("在世界中", "in world")
                        : BridgeLang.T("不在世界", "not in world"))
                    + (BridgeState.flying
                        ? BridgeLang.T("、飞行中", ", flying")
                        : ""));
            }
        }

        // -- 火箭 -------------------------------------------------------------
        //
        // 质量在飞行中**每帧都在变**（烧燃料），零件数也会因分级而变。
        // 只有零件数变化才值得记（那才是「设计变了」）；
        // 质量变化记在飞行日志里，这里不掺和。

        private static int lastPartCount = -1;
        private static string lastBuildMode;

        private static void CheckBuild()
        {
            string mode = BridgeBuild.mode;
            int count = BridgeBuild.partCount;

            // 首次记录
            if (lastPartCount < 0)
            {
                lastPartCount = count;
                lastBuildMode = mode;
                return;
            }

            // 场景切换时零件数本来就会变，交给 CheckScene 记，这里不重复
            if (mode != lastBuildMode)
            {
                lastBuildMode = mode;
                lastPartCount = count;
                return;
            }

            if (count == lastPartCount)
            {
                return;
            }

            int was = lastPartCount;
            lastPartCount = count;
            changes++;
            BridgeLog.State(BridgeLang.T("火箭零件数变化：", "parts: ")
                + was.ToString(CultureInfo.InvariantCulture) + " → "
                + count.ToString(CultureInfo.InvariantCulture)
                + " (" + BridgeBuild.totalMass.ToString("0.#", CultureInfo.InvariantCulture)
                + BridgeLang.T(" 吨", " t") + ")");
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
