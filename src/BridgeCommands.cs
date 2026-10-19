// SFS Agent — 控制指令
//
// Unity API 只能在主线程调用，因此 HTTP 线程只负责入队，
// 实际执行放在 MonoBehaviour.Update() 里。

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace SfsAgent
{
    public static class BridgeCommands
    {
        public class Command
        {
            public string Name;
            public double Value;
        }

        private static readonly Queue<Command> Pending = new Queue<Command>();
        private static readonly object Gate = new object();

        public static string LastResult = "";
        public static string LastEngineError = "";

        // 回车（分级控制程序）发出后，等若干帧再看有没有引擎真的点着。
        // 没点着就退回「直接点燃所有引擎」，避免「回了 ok、火箭却不动」。
        private static int igniteFallbackCountdown;

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

        public static void Enqueue(string name, double value)
        {
            // 清掉上一次的结果，避免 HTTP 线程读到陈旧值当成本次结果
            LastResult = "";
            lock (Gate)
            {
                Pending.Enqueue(new Command { Name = name, Value = value });
            }
        }

        /// <summary>如实报告执行结果：ok 表示主线程执行成功，否则给出原因。</summary>
        public static string ToJson()
        {
            bool ok = LastResult == "ok";
            StringBuilder sb = new StringBuilder(160);
            sb.Append("{\"ok\":").Append(ok ? "true" : "false");
            if (LastResult.Length > 0)
            {
                sb.Append(",\"result\":\"").Append(Escape(LastResult)).Append("\"");
            }
            else
            {
                sb.Append(",\"error\":\"指令尚未被执行（游戏可能没有运行或不在可操作场景）\"");
            }
            sb.Append("}");
            return sb.ToString();
        }

        private static string Escape(string s)
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

        /// <summary>在主线程执行排队中的指令。</summary>
        public static void Tick()
        {
            PumpIgniteFallback();
            while (true)
            {
                Command cmd;
                lock (Gate)
                {
                    if (Pending.Count == 0)
                    {
                        return;
                    }
                    cmd = Pending.Dequeue();
                }
                try
                {
                    Execute(cmd);
                    LastResult = "ok";
                }
                catch (Exception ex)
                {
                    LastResult = "error: " + ex.Message;
                    Main.Log("command '" + cmd.Name + "' failed: " + ex);
                }
            }
        }

        private static object Player()
        {
            Type pcType = BridgeState.FindType("SFS.World.PlayerController");
            object pc = BridgeState.GetStatic(pcType, "main");
            if (pc == null)
            {
                return null;
            }
            return BridgeState.Unwrap(BridgeState.Get(pc, "player"));
        }

        /// <summary>
        /// 写 SFS 的 *_Local 包装器里的真实值。
        ///
        /// 注意：不同版本里 `Value` 有的是**字段**、有的是**属性** ——
        /// 之前只找字段，导致 set_throttle / throttle_on / rcs_* 全部报
        /// 「Value field not found on Bool_Local」而失效（实测踩到）。
        /// 读取侧 BridgeState.Unwrap 本来就兼顾两者，这里补齐写入侧。
        /// </summary>
        private static void SetLocal(object wrapper, double value)
        {
            if (wrapper == null)
            {
                throw new Exception("target member missing");
            }

            Type t = wrapper.GetType();
            const BindingFlags F =
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            FieldInfo f = t.GetField("Value", F);
            if (f != null)
            {
                f.SetValue(wrapper, Coerce(value, f.FieldType));
                return;
            }

            PropertyInfo p = t.GetProperty("Value", F);
            if (p != null && p.CanWrite)
            {
                p.SetValue(wrapper, Coerce(value, p.PropertyType), null);
                return;
            }

            throw new Exception("no writable 'Value' on " + t.Name
                + " (fields: " + FieldNames(t) + ")");
        }

        private static object Coerce(double value, Type target)
        {
            if (target == typeof(float))
            {
                return (float)value;
            }
            if (target == typeof(double))
            {
                return value;
            }
            if (target == typeof(int))
            {
                return (int)value;
            }
            if (target == typeof(bool))
            {
                return value > 0.5;
            }
            return Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
        }

        /// <summary>出错时把可用字段名列出来，便于定位。</summary>
        private static string FieldNames(Type t)
        {
            StringBuilder sb = new StringBuilder();
            FieldInfo[] fs = t.GetFields(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            for (int i = 0; i < fs.Length && i < 8; i++)
            {
                if (i > 0)
                {
                    sb.Append(",");
                }
                sb.Append(fs[i].Name);
            }
            return sb.ToString();
        }

        private static void Execute(Command cmd)
        {
            object player = Player();
            if (player == null)
            {
                throw new Exception("no active vessel");
            }

            string name = cmd.Name;

            if (name == "set_throttle")
            {
                object throttle = BridgeState.Get(player, "throttle");
                SetLocal(BridgeState.Get(throttle, "throttlePercent"), cmd.Value);
                return;
            }

            if (name == "throttle_on" || name == "throttle_off")
            {
                object throttle = BridgeState.Get(player, "throttle");
                SetLocal(BridgeState.Get(throttle, "throttleOn"), name == "throttle_on" ? 1 : 0);
                return;
            }

            if (name == "rcs_on" || name == "rcs_off" || name == "rcs_toggle")
            {
                object arrowkeys = BridgeState.Get(player, "arrowkeys");
                if (arrowkeys == null)
                {
                    throw new Exception("rocket.arrowkeys unavailable");
                }
                object rcs = BridgeState.Get(arrowkeys, "rcs");
                if (rcs == null)
                {
                    throw new Exception("arrowkeys.rcs unavailable");
                }
                double target;
                if (name == "rcs_toggle")
                {
                    target = ToDouble(BridgeState.Unwrap(rcs)) > 0.5 ? 0 : 1;
                }
                else
                {
                    target = name == "rcs_on" ? 1 : 0;
                }
                SetLocal(rcs, target);
                return;
            }

            if (name == "stage" || name == "staging_program")
            {
                // 回车 = 「执行分级控制程序」：会**分离当前级并点燃下一级**。
                // 只在用户明确要「下一级 / 分级」时才按，别拿它当点火用 ——
                // 实测在分级正常的火箭上按回车会把当前级的引擎分离掉，
                // 结果就是「点着了」之后 engines_on 又变回 0。
                BridgeKeys.Enqueue(KeyCodeEnter, 140);
                return;
            }

            if (name == "throttle_toggle")
            {
                // 空格：切换油门闸门
                BridgeKeys.Enqueue(KeyCodeSpace, 140);
                return;
            }

            if (name == "engine_on" || name == "force_ignite" || name == "ignite")
            {
                // 点火 = 直接调每个引擎自己的 EnableEngine()，绕开分级程序：
                // 既适用于程序化搭出来、没有分级程序的火箭，也适用于分级正常的火箭，
                // 而且不会像回车那样把当前级分离掉。
                // 先把油门闸门打开，否则引擎点着了也不出力。
                object throttle = BridgeState.Get(player, "throttle");
                SetLocal(BridgeState.Get(throttle, "throttleOn"), 1);

                int lit = IgniteAllEngines();
                if (lit < 0)
                {
                    throw new Exception("没找到引擎（当前可能不在飞行中）");
                }
                if (lit == 0)
                {
                    throw new Exception("引擎没点着：" + LastEngineError);
                }
                LastResult = "engines=" + lit;
                return;
            }

            throw new Exception("unknown command: " + name);
        }

        // ── 直接点火 ────────────────────────────────────────────────────────

        private static int IgniteAllEngines()
        {
            // 模块挂在 rocket.partHolder.modules（key 是类型，value 是该类型的模块列表），
            // 不是 rocket.parts —— 和遥测里 CaptureEngines() 走的是同一条路径。
            List<object> engines = CollectEngineModules();
            if (engines.Count == 0)
            {
                return -1;
            }
            int lit = 0;
            for (int i = 0; i < engines.Count; i++)
            {
                lit += EnableEngine(engines[i]);
            }
            return lit;
        }

        /// <summary>主线程每帧调用：回车点不着时退回直接点火。</summary>
        private static void PumpIgniteFallback()
        {
            if (igniteFallbackCountdown <= 0)
            {
                return;
            }
            igniteFallbackCountdown--;
            if (igniteFallbackCountdown > 0)
            {
                return;
            }
            if (CountLitEngines() > 0)
            {
                return;
            }
            int lit = IgniteAllEngines();
            if (lit > 0)
            {
                LastResult = "fallback engines=" + lit;
                Main.Log("回车没有点着引擎，已直接点燃 " + lit + " 台");
            }
            else
            {
                LastResult = "fallback engines=0 " + LastEngineError;
                Main.Log("点火失败：" + LastEngineError);
            }
        }

        private static int CountLitEngines()
        {
            List<object> engines = CollectEngineModules();
            int lit = 0;
            for (int i = 0; i < engines.Count; i++)
            {
                object val = BridgeState.Unwrap(BridgeState.Get(engines[i], "engineOn"));
                if (val is bool && (bool)val)
                {
                    lit++;
                }
            }
            return lit;
        }

        private static List<object> CollectEngineModules()
        {
            List<object> found = new List<object>();
            Type pcType = BridgeState.FindType("SFS.World.PlayerController");
            object pc = pcType == null ? null : BridgeState.GetStatic(pcType, "main");
            object playerLocal = pc == null ? null : BridgeState.Get(pc, "player");
            object rocket = BridgeState.Unwrap(playerLocal);
            if (rocket == null)
            {
                return found;
            }
            object holder = BridgeState.Get(rocket, "partHolder");
            if (holder == null)
            {
                return found;
            }
            object modulesObj = BridgeState.Get(holder, "modules");
            IDictionary modules = modulesObj as IDictionary;
            if (modules == null)
            {
                return found;
            }
            foreach (DictionaryEntry entry in modules)
            {
                IEnumerable list = entry.Value as IEnumerable;
                if (list == null)
                {
                    continue;
                }
                foreach (object mod in list)
                {
                    if (mod != null
                        && mod.GetType().Name.IndexOf("EngineModule", StringComparison.Ordinal) >= 0)
                    {
                        found.Add(mod);
                    }
                }
            }
            return found;
        }

        private static int EnableEngine(object module)
        {
            if (module == null)
            {
                return 0;
            }
            Type t = module.GetType();
            if (t.Name.IndexOf("EngineModule", StringComparison.Ordinal) < 0)
            {
                return 0;
            }

            // 已经点着的就别再点：重复 EnableEngine 无益，而一旦退到 ToggleEngine
            // 反而会把引擎关掉（早期版本的坑）。
            // 但**油门一定要补写**：engineOn 只是「点燃」标志，
            // 真正决定出力的是 throttle_Out，不写它就会「点着了但一点推力都没有」。
            object onRef = BridgeState.Get(module, "engineOn");
            object onVal = BridgeState.Unwrap(onRef);
            bool alreadyOn = onVal is bool && (bool)onVal;
            if (!alreadyOn)
            {
                Type loggerType = BridgeState.FindType("I_MsgLogger");
                MethodInfo enable = null;
                MethodInfo[] all = t.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i].Name != "EnableEngine")
                    {
                        continue;
                    }
                    ParameterInfo[] ps = all[i].GetParameters();
                    if (ps.Length != 1)
                    {
                        continue;
                    }
                    if (loggerType == null || ps[0].ParameterType == loggerType)
                    {
                        enable = all[i];
                        break;
                    }
                }
                if (enable == null)
                {
                    return 0;
                }
                try
                {
                    enable.Invoke(module, new object[] { null });
                }
                catch (Exception ex)
                {
                    LastEngineError = ex.GetType().Name + ": " + ex.Message;
                    return 0;
                }
            }

            ApplyThrottle(module);
            return 1;
        }

        /// <summary>把当前油门写进引擎的 throttle_Out，并让它重算一次推力。</summary>
        private static void ApplyThrottle(object module)
        {
            try
            {
                double pct = BridgeState.throttlePercent;
                if (pct > 1.5)
                {
                    pct /= 100.0;
                }
                if (pct < 0)
                {
                    pct = 0;
                }
                if (pct > 1)
                {
                    pct = 1;
                }

                object outRef = BridgeState.Get(module, "throttle_Out");
                if (outRef != null)
                {
                    SetLocal(outRef, pct);
                }

                // 部分版本要显式重算，否则推力表不会跟着油门变。
                MethodInfo recalc = module.GetType().GetMethod(
                    "RecalculateEngineThrottle",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (recalc != null && recalc.GetParameters().Length == 0)
                {
                    recalc.Invoke(module, null);
                }
            }
            catch (Exception ex)
            {
                LastEngineError = ex.GetType().Name + ": " + ex.Message;
            }
        }

        private const int KeyCodeSpace = 32;
        private const int KeyCodeEnter = 13;

        private static double ToDouble(object value)
        {
            if (value == null)
            {
                return 0;
            }
            try
            {
                return Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return 0;
            }
        }
    }
}
