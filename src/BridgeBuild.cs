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
        public static readonly List<string> partSamples = new List<string>();

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

            // 优先用 PartSave.name —— 这是零件在蓝图里的真实名字（"Fuel Tank" 之类）。
            // 直接读 Part.Name 在**刚生成的零件**上会拿到 TranslationVariable 对象，
            // Convert.ToString 之后变成 "SFS.Translations.TranslationVariable"，
            // 把 part_kinds 整个污染掉（实测踩过）。
            try
            {
                Type partSaveType = BridgeState.FindType("SFS.Parts.PartSave");
                if (partSaveType != null)
                {
                    MethodInfo createSaves = partSaveType.GetMethod(
                        "CreateSaves",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (createSaves != null)
                    {
                        Array one = Array.CreateInstance(part.GetType(), 1);
                        one.SetValue(part, 0);
                        Array saves = createSaves.Invoke(null, new object[] { one }) as Array;
                        if (saves != null && saves.Length > 0)
                        {
                            string s = Convert.ToString(
                                BridgeState.Get(saves.GetValue(0), "name"),
                                CultureInfo.InvariantCulture);
                            if (IsUsableName(s))
                            {
                                return s.Trim();
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            try
            {
                object name = BridgeState.Get(part, "Name");
                if (name != null)
                {
                    string s = Convert.ToString(name, CultureInfo.InvariantCulture);
                    if (IsUsableName(s))
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
                return IsUsableName(s) ? s.Trim() : "";
            }
            catch
            {
                return "";
            }
        }

        /// <summary>过滤掉「类型名冒充零件名」的情况。</summary>
        private static bool IsUsableName(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return false;
            }
            string t = s.Trim();
            if (t.Length == 0)
            {
                return false;
            }
            if (t.StartsWith("SFS.", StringComparison.Ordinal)
                || t.StartsWith("UnityEngine.", StringComparison.Ordinal)
                || t.StartsWith("System.", StringComparison.Ordinal))
            {
                return false;
            }
            // 类型名不带空格、带点；零件显示名一般有空格或没有点
            return t.IndexOf('.') < 0;
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

        private static string GetPartPosition(object part)
        {
            try
            {
                object pos = BridgeState.Get(part, "Position");
                if (pos == null)
                {
                    return "(?)";
                }
                double x = ToDouble(BridgeState.Get(pos, "x"), 0);
                double y = ToDouble(BridgeState.Get(pos, "y"), 0);
                return "(" + x.ToString("0.#", CultureInfo.InvariantCulture)
                    + "," + y.ToString("0.#", CultureInfo.InvariantCulture) + ")";
            }
            catch
            {
                return "(?)";
            }
        }

        private static double ToDouble(object o, double fallback)
        {
            if (o == null)
            {
                return fallback;
            }
            try
            {
                return Convert.ToDouble(o, CultureInfo.InvariantCulture);
            }
            catch
            {
                return fallback;
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
                partSamples.Clear();
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

                    // 记录少量零件的位置：既能排障，也能让上层知道东西放在哪
                    if (partSamples.Count < 8)
                    {
                        partSamples.Add(name + "@" + GetPartPosition(parts[i]));
                    }
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

                // 分级数：飞行场景读火箭的 staging，建造场景读 StagingDrawer 的 staging。
                //
                // 旧实现写成 `if (sn is Array) ((Array)sn).Length`，而
                // Staging.stages 是 **List<Stage>**，永远匹配不上 Array，
                // 于是 stage_count 恒为 0（猫娘据此判断「读不到分级」）。
                // 现在统一交给 CountStages 按 ICollection/IList/IEnumerable 依次兜底。
                object stagingObj = null;
                if (flying)
                {
                    object player = BridgeState.Unwrap(
                        BridgeState.Get(pc, "player"));
                    stagingObj = BridgeState.Get(player, "staging");
                }
                else
                {
                    stagingObj = FindBuildStaging();
                }
                stageCount = CountStages(stagingObj);

                // 连通性是按需要算的：它是 O(n²)，不能每帧都跑
                if (connectivityRequested)
                {
                    connectivityRequested = false;
                    ComputeConnectivity(parts);
                }
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
            }
        }

        /// <summary>
        /// 建造场景里的 Staging 对象。
        ///
        /// SFS 的分级数据只有两个来源：飞行中 Rocket.staging，或建造界面
        /// StagingDrawer.main.staging（类型是 Staging_Local，要 Unwrap 出 Staging）。
        /// 建造场景没有 Rocket，所以只能走后者。
        /// </summary>
        private static object FindBuildStaging()
        {
            try
            {
                Type drawerType = BridgeState.FindType("SFS.World.StagingDrawer");
                if (drawerType == null)
                {
                    return null;
                }
                object drawer = BridgeState.GetStatic(drawerType, "main");
                if (drawer == null)
                {
                    return null;
                }
                object local = BridgeState.Get(drawer, "staging");
                if (local == null)
                {
                    return null;
                }
                object staging = BridgeState.Unwrap(local);
                return staging != null ? staging : local;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 数分级数量。stages 是 List&lt;Stage&gt;，不是数组，
        /// 所以先 ICollection、再 IList、最后 IEnumerable 兜底。
        /// </summary>
        private static int CountStages(object staging)
        {
            if (staging == null)
            {
                return 0;
            }
            object stages;
            try
            {
                stages = BridgeState.Get(staging, "stages");
            }
            catch
            {
                return 0;
            }
            if (stages == null)
            {
                return 0;
            }
            System.Collections.ICollection col = stages as System.Collections.ICollection;
            if (col != null)
            {
                return col.Count;
            }
            System.Collections.IList list = stages as System.Collections.IList;
            if (list != null)
            {
                return list.Count;
            }
            System.Collections.IEnumerable en = stages as System.Collections.IEnumerable;
            if (en != null)
            {
                int n = 0;
                foreach (object o in en)
                {
                    n++;
                    if (n > 1000)
                    {
                        break;
                    }
                }
                return n;
            }
            return 0;
        }

        // -- 连通性 -----------------------------------------------------------
        //
        // 「零件是不是连成一体」不能靠猜：SFS 在发射时是按**零件位置**重新拼装的，
        // 位置差一点就是一堆悬空的零件。这里用每个零件的附着面算出包围盒，
        // 然后把「包围盒相接」的零件并成一组，孤立的就是没接上的。

        public static volatile bool connectivityRequested;
        public static int looseCount;
        public static int bodyCount;

        public static void RequestConnectivity()
        {
            connectivityRequested = true;
        }

        private static void ComputeConnectivity(object[] parts)
        {
            looseCount = 0;
            bodyCount = 0;
            if (parts == null || parts.Length == 0 || parts.Length > 600)
            {
                return;
            }

            int n = parts.Length;
            double[] minX = new double[n];
            double[] maxX = new double[n];
            double[] minY = new double[n];
            double[] maxY = new double[n];
            bool[] has = new bool[n];

            for (int i = 0; i < n; i++)
            {
                double[] box = BoundingBox(parts[i]);
                if (box == null)
                {
                    has[i] = false;
                    continue;
                }
                has[i] = true;
                minX[i] = box[0];
                minY[i] = box[1];
                maxX[i] = box[2];
                maxY[i] = box[3];
                // 完全没有面积的零件（单点）给一点余量，避免永远判成孤立
                if (maxX[i] - minX[i] < 0.01)
                {
                    maxX[i] = minX[i] + 0.05;
                }
                if (maxY[i] - minY[i] < 0.01)
                {
                    maxY[i] = minY[i] + 0.05;
                }
            }

            int[] parent = new int[n];
            for (int i = 0; i < n; i++)
            {
                parent[i] = i;
            }

            const double Margin = 0.08;
            for (int i = 0; i < n; i++)
            {
                if (!has[i])
                {
                    continue;
                }
                for (int j = i + 1; j < n; j++)
                {
                    if (!has[j])
                    {
                        continue;
                    }
                    if (maxX[i] + Margin < minX[j] || maxX[j] + Margin < minX[i]
                        || maxY[i] + Margin < minY[j] || maxY[j] + Margin < minY[i])
                    {
                        continue;
                    }
                    Union(parent, i, j);
                }
            }

            Dictionary<int, int> sizes = new Dictionary<int, int>();
            for (int i = 0; i < n; i++)
            {
                if (!has[i])
                {
                    continue;
                }
                int root = Find(parent, i);
                if (sizes.ContainsKey(root))
                {
                    sizes[root] = sizes[root] + 1;
                }
                else
                {
                    sizes[root] = 1;
                }
            }
            bodyCount = sizes.Count;
            int loose = 0;
            foreach (KeyValuePair<int, int> kv in sizes)
            {
                if (kv.Value == 1)
                {
                    loose++;
                }
            }
            looseCount = loose;
        }

        /// <summary>用附着面求零件的包围盒：{minX, minY, maxX, maxY}。</summary>
        private static double[] BoundingBox(object part)
        {
            if (part == null)
            {
                return null;
            }
            try
            {
                MethodInfo m = part.GetType().GetMethod(
                    "GetAttachmentSurfacesWorld",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null,
                    Type.EmptyTypes,
                    null);
                if (m == null)
                {
                    return null;
                }
                Array arr = m.Invoke(part, null) as Array;
                if (arr == null || arr.Length == 0)
                {
                    return null;
                }
                double minX = double.MaxValue;
                double minY = double.MaxValue;
                double maxX = double.MinValue;
                double maxY = double.MinValue;
                bool any = false;
                for (int i = 0; i < arr.Length; i++)
                {
                    object line = arr.GetValue(i);
                    object s = BridgeState.Get(line, "start");
                    object e = BridgeState.Get(line, "end");
                    if (s == null || e == null)
                    {
                        continue;
                    }
                    double sx = BridgeState.ToDouble(BridgeState.Get(s, "x"), 0);
                    double sy = BridgeState.ToDouble(BridgeState.Get(s, "y"), 0);
                    double ex = BridgeState.ToDouble(BridgeState.Get(e, "x"), 0);
                    double ey = BridgeState.ToDouble(BridgeState.Get(e, "y"), 0);
                    if (sx < minX) { minX = sx; }
                    if (ex < minX) { minX = ex; }
                    if (sx > maxX) { maxX = sx; }
                    if (ex > maxX) { maxX = ex; }
                    if (sy < minY) { minY = sy; }
                    if (ey < minY) { minY = ey; }
                    if (sy > maxY) { maxY = sy; }
                    if (ey > maxY) { maxY = ey; }
                    any = true;
                }
                if (!any)
                {
                    return null;
                }
                return new double[] { minX, minY, maxX, maxY };
            }
            catch
            {
                return null;
            }
        }

        private static int Find(int[] parent, int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }
            return i;
        }

        private static void Union(int[] parent, int a, int b)
        {
            int ra = Find(parent, a);
            int rb = Find(parent, b);
            if (ra != rb)
            {
                parent[rb] = ra;
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
            sb.Append(",\"bodies\":").Append(bodyCount);
            sb.Append(",\"loose_parts\":").Append(looseCount);
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

            sb.Append(",\"part_samples\":[");
            for (int i = 0; i < partSamples.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(",");
                }
                sb.Append("\"").Append(JsonEscape(partSamples[i])).Append("\"");
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
