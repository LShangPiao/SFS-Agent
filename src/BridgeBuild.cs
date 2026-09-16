// SFS-Agent — 火箭设计读取
//
// 读取当前场景中的火箭零件，用于让猫娘评审设计。
// 建造场景与飞行场景都能读：
//   - 建造场景：零件散布在场景里，通过 FindObjectsOfType 枚举
//   - 飞行场景：优先用 Rocket.partHolder.parts
// 仍然全反射，不引用 UnityEngine。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace SfsAgent
{
    public static class BridgeBuild
    {
        public const int MaxPartKinds = 12;

        public static int partCount;
        public static int stageCount;
        public static double totalMass;
        public static string mode = "unknown";
        public static string lastError = "";
        public static readonly List<string> partNames = new List<string>();
        public static readonly List<int> partNameCounts = new List<int>();

        private static object[] FindObjectsOfType(Type type)
        {
            if (type == null)
            {
                return new object[0];
            }

            Type uo = BridgeState.FindType("UnityEngine.Object");
            if (uo == null)
            {
                return new object[0];
            }

            string[] candidates = new string[] { "FindObjectsOfType", "FindObjectsByType" };
            for (int i = 0; i < candidates.Length; i++)
            {
                MethodInfo[] methods = uo.GetMethods(
                    BindingFlags.Public | BindingFlags.Static);
                for (int j = 0; j < methods.Length; j++)
                {
                    MethodInfo m = methods[j];
                    if (m.Name != candidates[i])
                    {
                        continue;
                    }
                    ParameterInfo[] ps = m.GetParameters();
                    if (ps.Length < 1 || ps[0].ParameterType != typeof(Type))
                    {
                        continue;
                    }
                    try
                    {
                        object[] args = new object[ps.Length];
                        args[0] = type;
                        for (int k = 1; k < ps.Length; k++)
                        {
                            // 其余参数多为枚举（排序模式），取默认值 0
                            args[k] = ps[k].ParameterType.IsEnum
                                ? Enum.ToObject(ps[k].ParameterType, 0)
                                : Activator.CreateInstance(ps[k].ParameterType);
                        }
                        Array arr = m.Invoke(null, args) as Array;
                        if (arr == null)
                        {
                            continue;
                        }
                        object[] result = new object[arr.Length];
                        arr.CopyTo(result, 0);
                        return result;
                    }
                    catch
                    {
                    }
                }
            }

            return new object[0];
        }

        private static string GetPartName(object part)
        {
            if (part == null)
            {
                return "";
            }
            try
            {
                object name = BridgeState.Get(part, "Name");
                if (name != null)
                {
                    string s = Convert.ToString(name, CultureInfo.InvariantCulture);
                    if (!string.IsNullOrEmpty(s))
                    {
                        return s.Trim();
                    }
                }
            }
            catch
            {
            }
            try
            {
                // displayName 是 TranslationVariable，退一步取它的字符串表示
                object dn = BridgeState.Get(part, "displayName");
                object value = BridgeState.Get(dn, "Value");
                if (value == null)
                {
                    value = dn;
                }
                string s = Convert.ToString(value, CultureInfo.InvariantCulture);
                return s == null ? "" : s.Trim();
            }
            catch
            {
                return "";
            }
        }

        private static double GetPartMass(object part)
        {
            try
            {
                object composed = BridgeState.Get(part, "mass");
                if (composed == null)
                {
                    return 0;
                }
                object value = BridgeState.Get(composed, "Value");
                if (value == null)
                {
                    value = composed;
                }
                return Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return 0;
            }
        }

        public static void Capture()
        {
            try
            {
                lastError = "";
                Type partType = BridgeState.FindType("SFS.Parts.Part");
                if (partType == null)
                {
                    mode = "unavailable";
                    return;
                }

                object[] parts = FindObjectsOfType(partType);
                partCount = parts.Length;

                // 场景判定：能取到飞行中的火箭就是飞行场景
                bool flying = false;
                Type pcType = BridgeState.FindType("SFS.World.PlayerController");
                object pc = BridgeState.GetStatic(pcType, "main");
                if (pc != null)
                {
                    object player = BridgeState.Unwrap(BridgeState.Get(pc, "player"));
                    Type rocketType = BridgeState.FindType("SFS.World.Rocket");
                    if (player != null && rocketType != null && rocketType.IsInstanceOfType(player))
                    {
                        flying = true;
                    }
                }
                mode = flying ? "flight" : (partCount > 0 ? "build" : "idle");

                // 分类型统计
                Dictionary<string, int> counts = new Dictionary<string, int>();
                double massSum = 0;
                for (int i = 0; i < parts.Length; i++)
                {
                    string name = GetPartName(parts[i]);
                    if (name.Length > 0)
                    {
                        if (counts.ContainsKey(name))
                        {
                            counts[name] = counts[name] + 1;
                        }
                        else
                        {
                            counts[name] = 1;
                        }
                    }
                    massSum += GetPartMass(parts[i]);
                }
                totalMass = massSum;

                // 按数量降序取前 N 种
                List<KeyValuePair<string, int>> pairs =
                    new List<KeyValuePair<string, int>>(counts);
                pairs.Sort(delegate (KeyValuePair<string, int> a, KeyValuePair<string, int> b)
                {
                    return b.Value.CompareTo(a.Value);
                });

                partNames.Clear();
                partNameCounts.Clear();
                int limit = pairs.Count < MaxPartKinds ? pairs.Count : MaxPartKinds;
                for (int i = 0; i < limit; i++)
                {
                    partNames.Add(pairs[i].Key);
                    partNameCounts.Add(pairs[i].Value);
                }

                // 分级数：优先读飞行中火箭的 staging
                stageCount = 0;
                if (flying)
                {
                    object player = BridgeState.Unwrap(
                        BridgeState.Get(pc, "player"));
                    object staging = BridgeState.Get(player, "staging");
                    if (staging != null)
                    {
                        object sn = BridgeState.Get(staging, "stages");
                        if (sn is Array)
                        {
                            stageCount = ((Array)sn).Length;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
            }
        }

        private static string JsonEscape(string s)
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

        private static string Num(double v)
        {
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }

        public static string ToJson()
        {
            StringBuilder sb = new StringBuilder(320);
            sb.Append("{");
            sb.Append("\"ok\":true");
            sb.Append(",\"mode\":\"").Append(JsonEscape(mode)).Append("\"");
            sb.Append(",\"part_count\":").Append(partCount);
            sb.Append(",\"stage_count\":").Append(stageCount);
            sb.Append(",\"total_mass\":").Append(Num(totalMass));
            sb.Append(",\"part_kinds\":[");

            for (int i = 0; i < partNames.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(",");
                }
                sb.Append("{\"name\":\"").Append(JsonEscape(partNames[i]))
                  .Append("\",\"count\":").Append(partNameCounts[i]).Append("}");
            }

            sb.Append("]");
            if (lastError.Length > 0)
            {
                sb.Append(",\"error\":\"").Append(JsonEscape(lastError)).Append("\"");
            }
            sb.Append("}");
            return sb.ToString();
        }
    }
}
