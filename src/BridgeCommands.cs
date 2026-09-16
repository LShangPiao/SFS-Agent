// SFS Agent — 控制指令
//
// Unity API 只能在主线程调用，因此 HTTP 线程只负责入队，
// 实际执行放在 MonoBehaviour.Update() 里。

using System;
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

        private static void SetLocal(object wrapper, double value)
        {
            if (wrapper == null)
            {
                throw new Exception("target member missing");
            }
            FieldInfo f = wrapper.GetType().GetField(
                "Value",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null)
            {
                throw new Exception("Value field not found on " + wrapper.GetType().Name);
            }
            Type ft = f.FieldType;
            if (ft == typeof(float))
            {
                f.SetValue(wrapper, (float)value);
            }
            else if (ft == typeof(double))
            {
                f.SetValue(wrapper, value);
            }
            else if (ft == typeof(int))
            {
                f.SetValue(wrapper, (int)value);
            }
            else if (ft == typeof(bool))
            {
                f.SetValue(wrapper, value > 0.5);
            }
            else
            {
                f.SetValue(wrapper, Convert.ChangeType(value, ft, CultureInfo.InvariantCulture));
            }
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

            if (name == "stage" || name == "ignite")
            {
                // 分级由游戏自己的逻辑决定（哪一级、何时点火、音效、UI 刷新…），
                // 与其猜内部实现，不如注入一次空格键，让游戏按原生流程执行。
                // 注意：旧实现会在 Staging 上找“含 stag 的无参方法”，
                // 结果命中 RemoveEmptyStages()，属于明确的 bug，已移除。
                BridgeKeys.Enqueue(KeyCodeSpace, 140);
                return;
            }

            if (name == "staging_program")
            {
                // 回车：执行火箭分级控制程序
                BridgeKeys.Enqueue(KeyCodeEnter, 140);
                return;
            }

            throw new Exception("unknown command: " + name);
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
