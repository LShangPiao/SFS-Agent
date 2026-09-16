// SFS Agent — 控制指令
//
// Unity API 只能在主线程调用，因此 HTTP 线程只负责入队，
// 实际执行放在 MonoBehaviour.Update() 里。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

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

        public static void Enqueue(string name, double value)
        {
            lock (Gate)
            {
                Pending.Enqueue(new Command { Name = name, Value = value });
            }
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

            if (name == "stage")
            {
                InvokeStage(player);
                return;
            }

            throw new Exception("unknown command: " + name);
        }

        /// <summary>触发下一级分离。不同版本方法名可能不同，逐个尝试。</summary>
        private static void InvokeStage(object player)
        {
            object staging = BridgeState.Get(player, "staging");
            if (staging == null)
            {
                throw new Exception("staging unavailable");
            }

            string[] candidates = new string[]
            {
                "ActivateNextStage", "ActivateStage", "NextStage", "Stage", "Separate"
            };

            Type t = staging.GetType();
            for (int i = 0; i < candidates.Length; i++)
            {
                MethodInfo m = t.GetMethod(
                    candidates[i],
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null,
                    Type.EmptyTypes,
                    null);
                if (m != null)
                {
                    m.Invoke(staging, null);
                    return;
                }
            }

            // 退而求其次：找到任何名字含 "stag" 且无参的方法
            MethodInfo[] all = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].GetParameters().Length == 0 &&
                    all[i].Name.ToLowerInvariant().IndexOf("stag") >= 0 &&
                    all[i].ReturnType == typeof(void))
                {
                    all[i].Invoke(staging, null);
                    return;
                }
            }

            throw new Exception("no staging method found on " + t.Name);
        }
    }
}
