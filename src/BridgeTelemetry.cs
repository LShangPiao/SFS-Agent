// SFS-Agent — 飞行遥测增强
//
// 补上三个此前读不到的东西：
//
//   ① 分级状态  —— 之前 stageId 死钉 -1，因为读的是 Staging 上的字段，
//                  而真正的分级在 Staging.stages（List<Stage>）里。
//   ② 燃料与引擎 —— 之前 Thrust / TWR 报 0。
//                  BoosterModule.fuelPercent 是剩余燃料，
//                  EngineModule.thrust / engineOn 是引擎状态。
//   ③ 导航数据  —— 目标距离与相对速度（对接时最关心的两个数）。
//
// 全部走反射，主线程执行。

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace SfsAgent
{
    public static class BridgeTelemetry
    {
        // ── ① 分级 ──────────────────────────────────────────────────────────

        public static int stageCount;
        public static int activeStageIndex = -1;   // 0 = 最后一级（最先点火）
        public static int currentStageParts;
        public static string stageSummary = "";

        // ── ② 燃料与引擎 ────────────────────────────────────────────────────

        public static double totalThrust;          // 当前推力（kN）
        public static double twr;                  // 推重比
        public static int activeEngines;
        public static int totalEngines;
        public static double fuelPercent = double.NaN;   // 当前级剩余燃料 0-1
        public static double rcsFuelPercent = double.NaN;
        public static bool ispKnown;
        public static double isp;

        // ── ③ 导航 ──────────────────────────────────────────────────────────

        public static bool hasTarget;
        public static string targetName = "";
        public static double targetDistance = double.NaN;      // 米
        public static double targetRelSpeed = double.NaN;      // 米/秒
        public static double targetClosingSpeed = double.NaN;  // 正值 = 正在接近

        public static string lastError = "";

        // -- 入口 -------------------------------------------------------------

        /// <summary>每帧在 Capture() 之后调用。全部在主线程。</summary>
        public static void Capture()
        {
            lastError = "";
            try
            {
                object rocket = CurrentRocket();
                if (rocket == null)
                {
                    Reset();
                    return;
                }
                CaptureStages(rocket);
                CaptureEngines(rocket);
                CaptureNavigation(rocket);
            }
            catch (Exception ex)
            {
                lastError = ex.GetType().Name + ": " + ex.Message;
            }
        }

        private static void Reset()
        {
            stageCount = 0;
            activeStageIndex = -1;
            currentStageParts = 0;
            stageSummary = "";
            totalThrust = 0;
            twr = 0;
            activeEngines = 0;
            totalEngines = 0;
            fuelPercent = double.NaN;
            rcsFuelPercent = double.NaN;
            hasTarget = false;
            targetName = "";
            targetDistance = double.NaN;
            targetRelSpeed = double.NaN;
            targetClosingSpeed = double.NaN;
        }

        private static object CurrentRocket()
        {
            Type pcType = BridgeState.FindType("SFS.World.PlayerController");
            object pc = pcType == null ? null : BridgeState.GetStatic(pcType, "main");
            object playerLocal = pc == null ? null : BridgeState.Get(pc, "player");
            object player = BridgeState.Unwrap(playerLocal);
            if (player == null)
            {
                return null;
            }
            Type rocketType = BridgeState.FindType("SFS.World.Rocket");
            return (rocketType != null && rocketType.IsInstanceOfType(player)) ? player : null;
        }

        // ── ① 分级 ──────────────────────────────────────────────────────────

        private static void CaptureStages(object rocket)
        {
            object staging = BridgeState.Get(rocket, "staging");
            if (staging == null)
            {
                return;
            }

            object listObj = BridgeState.Get(staging, "stages");
            IList stages = listObj as IList;
            if (stages == null)
            {
                return;
            }

            stageCount = stages.Count;

            // 找「当前级」：还没用过的第一个（parts 最多且未标识为已用）。
            // SFS 的分级是从最后往上点，这里只给出可读的概览，
            // 具体哪一级该点火由 Enter 键交给游戏自己判断。
            StringBuilder sb = new StringBuilder(96);
            int bestIdx = -1;
            int bestParts = -1;
            for (int i = 0; i < stages.Count; i++)
            {
                object st = stages[i];
                if (st == null)
                {
                    continue;
                }
                object pc = BridgeState.Get(st, "PartCount");
                if (pc == null)
                {
                    pc = BridgeState.Get(st, "parts");
                }
                int n = (int)BridgeState.ToDouble(pc, 0);

                object idObj = BridgeState.Get(st, "stageId");
                int id = (int)BridgeState.ToDouble(idObj, i);

                if (i > 0)
                {
                    sb.Append(" / ");
                }
                sb.Append("#").Append(id).Append("(").Append(n).Append("件)");

                // PartCount 最大的通常是还没分离的主级
                if (n > bestParts)
                {
                    bestParts = n;
                    bestIdx = i;
                }
            }
            stageSummary = sb.ToString();

            if (bestIdx >= 0 && stages.Count > 0)
            {
                activeStageIndex = bestIdx;
                currentStageParts = bestParts;
            }
        }

        // ── ② 引擎与燃料 ────────────────────────────────────────────────────

        private static void CaptureEngines(object rocket)
        {
            totalThrust = 0;
            activeEngines = 0;
            totalEngines = 0;
            fuelPercent = double.NaN;
            rcsFuelPercent = double.NaN;

            object holder = BridgeState.Get(rocket, "partHolder");
            if (holder == null)
            {
                return;
            }
            object modulesObj = BridgeState.Get(holder, "modules");
            IDictionary modules = modulesObj as IDictionary;
            if (modules == null)
            {
                return;
            }

            Type engineType = BridgeState.FindType("SFS.Parts.Modules.EngineModule");
            Type boosterType = BridgeState.FindType("SFS.Parts.Modules.BoosterModule");
            Type rcsType = BridgeState.FindType("SFS.Parts.Modules.RcsModule");

            double boosterFuelSum = 0;
            int boosterFuelCount = 0;
            double rcsFuelSum = 0;
            int rcsFuelCount = 0;

            foreach (DictionaryEntry entry in modules)
            {
                object modTypeKey = entry.Key;
                if (modTypeKey == null)
                {
                    continue;
                }

                // modules 的 key 是类型，value 是该类型的所有模块列表
                object val = entry.Value;
                IEnumerable list = val as IEnumerable;
                if (list == null)
                {
                    continue;
                }

                Type keyType = modTypeKey as Type;
                string keyName = keyType != null ? keyType.FullName : modTypeKey.ToString();

                foreach (object mod in list)
                {
                    if (mod == null)
                    {
                        continue;
                    }

                    if (engineType != null && engineType.IsInstanceOfType(mod))
                    {
                        totalEngines++;
                        object on = BridgeState.Get(mod, "engineOn");
                        bool isOn = BridgeState.ToDouble(BridgeState.Unwrap(on), 0) > 0.5;
                        if (isOn)
                        {
                            activeEngines++;
                        }
                        // thrust 是 Composed_Float，取值要 unwrap
                        object th = BridgeState.Get(mod, "thrust");
                        double t = BridgeState.ToDouble(BridgeState.Unwrap(th), 0);
                        if (isOn)
                        {
                            totalThrust += t;
                        }
                        if (!ispKnown)
                        {
                            double ispv = BridgeState.ToDouble(
                                BridgeState.Unwrap(BridgeState.Get(mod, "ISP")), double.NaN);
                            if (!double.IsNaN(ispv) && ispv > 0)
                            {
                                isp = ispv;
                                ispKnown = true;
                            }
                        }
                    }
                    else if (boosterType != null && boosterType.IsInstanceOfType(mod))
                    {
                        object fp = BridgeState.Get(mod, "fuelPercent");
                        double f = BridgeState.ToDouble(BridgeState.Unwrap(fp), double.NaN);
                        if (!double.IsNaN(f))
                        {
                            boosterFuelSum += f;
                            boosterFuelCount++;
                        }
                    }
                    else if (rcsType != null && rcsType.IsInstanceOfType(mod))
                    {
                        object fp = BridgeState.Get(mod, "fuelPercent");
                        double f = BridgeState.ToDouble(BridgeState.Unwrap(fp), double.NaN);
                        if (!double.IsNaN(f))
                        {
                            rcsFuelSum += f;
                            rcsFuelCount++;
                        }
                    }
                }
            }

            if (boosterFuelCount > 0)
            {
                fuelPercent = boosterFuelSum / boosterFuelCount;
            }
            if (rcsFuelCount > 0)
            {
                rcsFuelPercent = rcsFuelSum / rcsFuelCount;
            }

            // 推重比 = 推力 / (质量 × 当地重力)
            if (totalThrust > 0 && BridgeState.mass > 0)
            {
                double g = LocalGravity();
                if (g > 0)
                {
                    twr = totalThrust / (BridgeState.mass * g);
                }
            }
        }

        private static double LocalGravity()
        {
            try
            {
                Type pcType = BridgeState.FindType("SFS.World.PlayerController");
                object pc = pcType == null ? null : BridgeState.GetStatic(pcType, "main");
                object playerLocal = pc == null ? null : BridgeState.Get(pc, "player");
                object player = BridgeState.Unwrap(playerLocal);
                if (player == null)
                {
                    return 0;
                }
                object loc = BridgeState.Get(player, "location");
                object pl = loc == null ? null : BridgeState.Get(loc, "planet");
                object planet = BridgeState.Unwrap(pl);
                if (planet == null)
                {
                    return 0;
                }
                Type locType = BridgeState.FindType("SFS.World.Location");
                object realLoc = locType != null && locType.IsInstanceOfType(loc)
                    ? loc
                    : (loc == null ? null : BridgeState.Get(loc, "Value"));
                if (realLoc == null)
                {
                    return 0;
                }
                MethodInfo m = planet.GetType().GetMethod(
                    "GetGravity",
                    BindingFlags.Public | BindingFlags.Instance,
                    null, new Type[] { typeof(double) }, null);
                if (m == null)
                {
                    return 0;
                }
                object r = BridgeState.Get(realLoc, "Radius");
                double radius = BridgeState.ToDouble(r, 0);
                if (radius <= 0)
                {
                    radius = BridgeState.height + BridgeState.PlanetRadius();
                }
                object g = m.Invoke(planet, new object[] { radius });
                return BridgeState.ToDouble(g, 0);
            }
            catch
            {
                return 0;
            }
        }

        // ── ③ 导航 ──────────────────────────────────────────────────────────

        private static void CaptureNavigation(object rocket)
        {
            hasTarget = false;
            targetName = "";
            targetDistance = double.NaN;
            targetRelSpeed = double.NaN;
            targetClosingSpeed = double.NaN;

            try
            {
                // 目标挂在火箭的导航组件上。SFS 的类名可能随版本变化，
                // 所以按名字找「有 target / Target 字段」的组件。
                object nav = FindTargetHolder(rocket);
                if (nav == null)
                {
                    return;
                }

                object target = BridgeState.Get(nav, "target");
                if (target == null)
                {
                    target = BridgeState.Get(nav, "Target");
                }
                if (target == null)
                {
                    return;
                }
                hasTarget = true;

                object nm = BridgeState.Get(target, "name");
                if (nm == null)
                {
                    nm = BridgeState.Get(target, "Name");
                }
                if (nm == null)
                {
                    nm = BridgeState.Get(target, "DisplayName");
                }
                targetName = nm == null ? "" : Convert.ToString(nm, CultureInfo.InvariantCulture);

                // 目标相对本体的位置与速度
                object tro = BridgeState.Get(target, "position");
                object trv = BridgeState.Get(target, "velocity");
                if (tro == null || trv == null)
                {
                    return;
                }

                double tx = BridgeState.ToDouble(BridgeState.Get(tro, "x"), 0);
                double ty = BridgeState.ToDouble(BridgeState.Get(tro, "y"), 0);
                double tvx = BridgeState.ToDouble(BridgeState.Get(trv, "x"), 0);
                double tvy = BridgeState.ToDouble(BridgeState.Get(trv, "y"), 0);

                // 本方位置（把"径向朝外"当 y 轴）
                double px = 0;
                double py = BridgeState.height + BridgeState.PlanetRadius();

                double dx = tx - px;
                double dy = ty - py;
                targetDistance = Math.Sqrt(dx * dx + dy * dy);

                // 相对速度：目标速度 - 本方速度
                double rvx = tvx - BridgeState.velX;
                double rvy = tvy - BridgeState.velY;
                targetRelSpeed = Math.Sqrt(rvx * rvx + rvy * rvy);

                // 接近速度 = 相对速度在连线方向上的投影（正 = 正在靠近）
                if (targetDistance > 1e-6)
                {
                    targetClosingSpeed = -(dx * rvx + dy * rvy) / targetDistance;
                }
            }
            catch
            {
            }
        }

        /// <summary>找一个带 target 字段的组件（导航/对接目标挂在哪）。</summary>
        private static object FindTargetHolder(object rocket)
        {
            try
            {
                // 常见候选：Rocket 自己，或它下面的 Navigation 组件
                string[] memberNames = new string[] { "navigation", "Navigation", "targeter" };
                for (int i = 0; i < memberNames.Length; i++)
                {
                    object v = BridgeState.Get(rocket, memberNames[i]);
                    if (v != null)
                    {
                        return BridgeState.Unwrap(v);
                    }
                }
                // 退回 rocket 自身（有些版本 target 直接挂在 Rocket 上）
                if (BridgeState.Get(rocket, "target") != null
                    || BridgeState.Get(rocket, "Target") != null)
                {
                    return rocket;
                }
            }
            catch
            {
            }
            return null;
        }

        // -- JSON -------------------------------------------------------------

        public static string ToJson()
        {
            StringBuilder sb = new StringBuilder(512);

            sb.Append(",\"stages\":{");
            sb.Append("\"count\":").Append(stageCount);
            sb.Append(",\"current_index\":").Append(activeStageIndex);
            sb.Append(",\"current_parts\":").Append(currentStageParts);
            sb.Append(",\"summary\":\"").Append(Esc(stageSummary)).Append("\"");
            sb.Append("}");

            sb.Append(",\"propulsion\":{");
            sb.Append("\"thrust\":").Append(Num(totalThrust));
            sb.Append(",\"twr\":").Append(Num(twr));
            sb.Append(",\"engines_on\":").Append(activeEngines);
            sb.Append(",\"engines_total\":").Append(totalEngines);
            sb.Append(",\"fuel_percent\":").Append(Num(fuelPercent));
            sb.Append(",\"rcs_fuel_percent\":").Append(Num(rcsFuelPercent));
            sb.Append(",\"isp\":").Append(Num(isp));
            sb.Append("}");

            if (hasTarget)
            {
                sb.Append(",\"target\":{");
                sb.Append("\"name\":\"").Append(Esc(targetName)).Append("\"");
                sb.Append(",\"distance\":").Append(Num(targetDistance));
                sb.Append(",\"relative_speed\":").Append(Num(targetRelSpeed));
                sb.Append(",\"closing_speed\":").Append(Num(targetClosingSpeed));
                sb.Append("}");
            }

            if (lastError.Length > 0)
            {
                sb.Append(",\"telemetry_error\":\"").Append(Esc(lastError)).Append("\"");
            }
            return sb.ToString();
        }

        private static string Num(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v))
            {
                return "null";
            }
            return v.ToString("0.###", CultureInfo.InvariantCulture);
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
