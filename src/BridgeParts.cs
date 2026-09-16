// SFS-Agent — 零件目录与定点放置
//
// 背景：建造界面里零件必须从左侧菜单**拖**到火箭上，纯点击放不上去。
// 因此这里不走输入，而是直接构造游戏自己的数据结构：
//
//   SFS.Parts.PartSave { string name; Vector2 position; Orientation orientation; ... }
//     -> SFS.Builds.Blueprint(PartSave[] parts, StageSave[] stages, float center,
//                             float rotation, bool interiorView)
//     -> SFS.Builds.BuildState.main.SpawnBlueprint(blueprint, applyUndo, logger)
//
// 这样就能「把某个零件放到指定坐标」，不需要拖动，也不碰系统鼠标。
//
// PartSave 的构造函数带几个泛型字典参数，编译期无法表达，因此改用无参构造 +
// 反射设置字段。

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace SfsAgent
{
    public static class BridgeParts
    {
        private class Job
        {
            public string Name;
            public double X;
            public double Y;
            public string Stack;   // top / bottom / same / none
        }

        private static readonly Queue<Job> Pending = new Queue<Job>();
        private static readonly object Gate = new object();

        public static string LastResult = "";
        public static string LastError = "";
        private static string steps = "";

        // 零件目录的缓存与请求标志。
        // 目录读取会碰 Unity 原生 API（Resources 等），**必须在主线程做**，
        // 因此 HTTP 线程只能置标志，真正读取放在 Tick() 里。
        private static volatile bool catalogRequested;
        private static volatile bool catalogReady;
        private static volatile bool catalogDeep;
        private static string catalogError = "";
        private static int catalogCount;
        private static readonly List<string> catalog = new List<string>();

        public static bool CatalogReady
        {
            get { return catalogReady; }
        }

        /// <summary>HTTP 线程：请求在主线程刷新零件目录。</summary>
        public static void RequestCatalog()
        {
            RequestCatalog(false);
        }

        /// <summary>
        /// 请求刷新目录。deep=true 时额外调用游戏自己的 PartsLoader.LoadParts()
        /// 以拿到全量零件名（内部走 Resources.LoadAll，只允许主线程执行）。
        /// </summary>
        public static void RequestCatalog(bool deep)
        {
            catalogReady = false;
            catalogError = "";
            catalogDeep = deep;
            catalogRequested = true;
        }

        public static int PlacedCount;

        // 官方示例火箭：BuildManager.exampleRockets[].json 就是保证能飞的蓝图 JSON。
        // 读它必须走主线程（会碰 Unity 对象），所以同样是「请求 + 缓存」模式。
        private static volatile bool examplesRequested;
        private static volatile bool examplesReady;
        private static string examplesJson = "{}";

        public static void RequestExamples()
        {
            examplesReady = false;
            examplesRequested = true;
        }

        public static bool ExamplesReady
        {
            get { return examplesReady; }
        }

        public static string ExamplesJson()
        {
            return examplesJson;
        }

        private static string BuildExamplesJson()
        {
            StringBuilder sb = new StringBuilder(4096);
            sb.Append("{\"ok\":true");
            try
            {
                Type mgrType = BridgeState.FindType("SFS.Builds.BuildManager");
                object mgr = BridgeState.GetStatic(mgrType, "main");
                if (mgr == null)
                {
                    sb.Append(",\"error\":\"BuildManager.main is null\"}");
                    return sb.ToString();
                }
                object examples = BridgeState.Get(mgr, "exampleRockets");
                Array arr = examples as Array;
                sb.Append(",\"count\":").Append(arr == null ? 0 : arr.Length);
                sb.Append(",\"examples\":[");
                if (arr != null)
                {
                    for (int i = 0; i < arr.Length; i++)
                    {
                        if (i > 0)
                        {
                            sb.Append(",");
                        }
                        object ex = arr.GetValue(i);
                        object name = BridgeState.Get(ex, "rocketName");
                        object json = BridgeState.Get(ex, "json");
                        string nameStr = name == null
                            ? ""
                            : Convert.ToString(name, CultureInfo.InvariantCulture);
                        string jsonStr = json == null
                            ? ""
                            : Convert.ToString(json, CultureInfo.InvariantCulture);
                        sb.Append("{\"name\":\"").Append(Esc(nameStr))
                          .Append("\",\"json_len\":").Append(jsonStr == null ? 0 : jsonStr.Length)
                          .Append(",\"json\":\"").Append(Esc(jsonStr)).Append("\"}");
                    }
                }
                sb.Append("]");
            }
            catch (Exception ex)
            {
                sb.Append(",\"error\":\"").Append(Esc(ex.Message)).Append("\"");
            }
            sb.Append("}");
            return sb.ToString();
        }

        // 综合诊断：一次性把几个可能的零件名来源都 dump 出来，
        // 避免为了定位一个字段反复重启游戏。
        private static volatile bool diagRequested;
        private static volatile bool diagReady;
        private static string diagJson = "{}";

        public static void RequestDiagnostics()
        {
            diagReady = false;
            diagRequested = true;
        }

        public static bool DiagnosticsReady
        {
            get { return diagReady; }
        }

        public static string DiagnosticsJson()
        {
            return diagJson;
        }

        private static string DescribeStatic(Type t, string field)
        {
            if (t == null)
            {
                return "\"<no type>\"";
            }
            object v = BridgeState.GetStatic(t, field);
            if (v == null)
            {
                return "null";
            }
            return DescribeObject(v);
        }

        private static string DescribeObject(object v)
        {
            if (v == null)
            {
                return "null";
            }
            StringBuilder sb = new StringBuilder(512);
            IDictionary d = v as IDictionary;
            if (d != null)
            {
                sb.Append("{\"kind\":\"dict\",\"count\":").Append(d.Count)
                  .Append(",\"keys\":[");
                int n = 0;
                foreach (DictionaryEntry e in d)
                {
                    if (n >= 15)
                    {
                        break;
                    }
                    if (n > 0)
                    {
                        sb.Append(",");
                    }
                    sb.Append("\"").Append(Esc(KeyToString(e.Key))).Append("\"");
                    n++;
                }
                sb.Append("]}");
                return sb.ToString();
            }
            ICollection c = v as ICollection;
            if (c != null)
            {
                return "{\"kind\":\"collection\",\"count\":" + c.Count + "}";
            }
            return "{\"kind\":\""
                + Esc(v.GetType().Name) + "\",\"text\":\""
                + Esc(Convert.ToString(v, CultureInfo.InvariantCulture)) + "\"}";
        }

        private static string DescribeInstances(string typeName)
        {
            Type t = BridgeState.FindType(typeName);
            if (t == null)
            {
                return "\"<no type>\"";
            }
            object[] found = BridgeState.FindObjects(t);
            StringBuilder sb = new StringBuilder(1024);
            sb.Append("{\"instances\":").Append(found.Length);
            if (found.Length > 0)
            {
                object first = found[0];
                sb.Append(",\"createdParts\":")
                  .Append(DescribeObject(BridgeState.Get(first, "createdParts")));
                sb.Append(",\"createdIcons\":")
                  .Append(DescribeObject(BridgeState.Get(first, "createdIcons")));
                sb.Append(",\"icons\":")
                  .Append(DescribeObject(BridgeState.Get(first, "icons")));
            }
            sb.Append("}");
            return sb.ToString();
        }

        private static string BuildDiagnostics()
        {
            StringBuilder sb = new StringBuilder(4096);
            sb.Append("{\"ok\":true");

            Type loader = BridgeState.FindType("SFS.Parts.PartsLoader");
            sb.Append(",\"PartsLoader\":{\"parts\":").Append(DescribeStatic(loader, "parts"))
              .Append(",\"partVariants\":").Append(DescribeStatic(loader, "partVariants"))
              .Append("}");

            sb.Append(",\"PickGridUI\":").Append(DescribeInstances("SFS.Builds.PickGridUI"));

            Type mgr = BridgeState.FindType("SFS.Builds.BuildManager");
            object m = BridgeState.GetStatic(mgr, "main");
            sb.Append(",\"BuildManager\":{\"main\":").Append(m == null ? "null" : "ok");
            if (m != null)
            {
                sb.Append(",\"exampleRockets\":")
                  .Append(DescribeObject(BridgeState.Get(m, "exampleRockets")));
                sb.Append(",\"pickGrid\":")
                  .Append(DescribeObject(BridgeState.Get(m, "pickGrid")));
            }
            sb.Append("}");

            sb.Append("}");
            return sb.ToString();
        }

        /// <summary>HTTP 线程调用：排队一次放置。</summary>
        public static void EnqueuePlace(string name, double x, double y)
        {
            EnqueuePlace(name, x, y, null);
        }

        public static void EnqueuePlace(string name, double x, double y, string stack)
        {
            Job job = new Job();
            job.Name = name;
            job.X = x;
            job.Y = y;
            job.Stack = stack;
            lock (Gate)
            {
                Pending.Enqueue(job);
            }
        }

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

        public static void Reset()
        {
            LastResult = "";
            LastError = "";
        }

        // -- 主线程 -----------------------------------------------------------

        public static void Tick()
        {
            if (examplesRequested)
            {
                examplesRequested = false;
                try
                {
                    examplesJson = BuildExamplesJson();
                }
                catch (Exception ex)
                {
                    examplesJson = "{\"ok\":false,\"error\":\"" + Esc(ex.Message) + "\"}";
                }
                examplesReady = true;
            }

            if (diagRequested)
            {
                diagRequested = false;
                try
                {
                    diagJson = BuildDiagnostics();
                }
                catch (Exception ex)
                {
                    diagJson = "{\"ok\":false,\"error\":\""
                        + Esc(ex.GetType().Name + ": " + ex.Message) + "\"}";
                }
                diagReady = true;
            }

            if (catalogRequested)
            {
                catalogRequested = false;
                try
                {
                    CaptureCatalog(catalogDeep);
                }
                catch (Exception ex)
                {
                    catalogError = ex.Message;
                }
                catalogReady = true;
            }

            Job job = null;
            lock (Gate)
            {
                if (Pending.Count > 0)
                {
                    job = Pending.Dequeue();
                }
            }
            if (job == null)
            {
                return;
            }
            try
            {
                // 目录没准备好就现场加载。
                // 放置不应该依赖「先调过 /build_catalog」这种调用顺序 ——
                // 上层忘了先查目录时，这里要自己兜住，而不是报一个莫名其妙的错。
                if (!catalogReady || catalog.Count == 0)
                {
                    try
                    {
                        CaptureCatalog(true);
                    }
                    catch (Exception ex)
                    {
                        catalogError = ex.Message;
                    }
                    catalogReady = true;
                }

                steps = "";
                ResolveStack(job);
                PlacedCount = PlacePart(job);
                LastResult = "placed " + job.Name + " at (" + Num(job.X) + ", " + Num(job.Y)
                    + "), count=" + PlacedCount;
                LastError = "";            }
            catch (Exception ex)
            {
                PlacedCount = 0;
                LastResult = "";
                LastError = ex.GetType().Name + ": " + ex.Message;
            }
        }

        // -- 目录 -------------------------------------------------------------

        /// <summary>
        /// 读取零件目录。**只能在主线程调用** —— 这条路径会碰 Unity 原生 API。
        ///
        /// 依次尝试多个来源（不同游戏版本的存放位置不一样），最后兜底用场景里
        /// 已有零件的名字，保证至少能拿到一组**确定合法**的零件名。
        /// 绝不在这里调用 PartsLoader.LoadParts()：它会走 Resources.LoadAll，
        /// 在非主线程会直接把游戏打崩。
        /// </summary>
        private static void CaptureCatalog()
        {
            CaptureCatalog(false);
        }

        /// <summary>
        /// 读取零件目录。**只能在主线程调用**。
        ///
        /// 依次尝试多个来源（不同游戏版本存放位置不同），最后兜底用场景里已有
        /// 零件的名字，保证至少拿到一组**确定合法**的零件名。
        /// </summary>
        public static void CaptureCatalog(bool deep)
        {
            catalog.Clear();
            catalogCount = 0;
            catalogError = "";
            sources = "";

            // 来源 1：PartsLoader 的静态字典（实测当前版本为 null，保留兼容）
            Type loaderType = BridgeState.FindType("SFS.Parts.PartsLoader");
            if (loaderType != null)
            {
                AddDictionary(BridgeState.GetStatic(loaderType, "parts"), "PartsLoader.parts");
                AddDictionary(BridgeState.GetStatic(loaderType, "partVariants"), "PartsLoader.partVariants");
            }

            // 来源 2：建造界面的零件菜单。
            // PickGridUI 平时是非激活对象，FindObjectsOfType 找不到它，
            // 只能通过 BuildManager.main.pickGrid 拿到实例。
            Type mgrType = BridgeState.FindType("SFS.Builds.BuildManager");
            object mgr = BridgeState.GetStatic(mgrType, "main");
            if (mgr != null)
            {
                object pick = BridgeState.Get(mgr, "pickGrid");
                AddDictionary(BridgeState.Get(pick, "createdParts"), "PickGridUI.createdParts");
                AddDictionary(BridgeState.Get(pick, "icons"), "PickGridUI.icons");

                // 来源 3：内置示例火箭的设计里出现过的零件名
                AddExampleRockets(BridgeState.Get(mgr, "exampleRockets"));
            }

            // 来源 4：兜底 —— 场景里已经存在的零件名（一定合法）
            AddSceneParts();

            // 来源 5（可选，deep）：调用游戏自己的 LoadParts()。
            // 它内部走 Resources.LoadAll，**只有在主线程调用才安全** ——
            // 之前崩游戏就是因为这个调用跑在了 HTTP 线程上。
            // 注意：LoadParts() 是**返回值**方式，不会写回静态字段 parts，
            // 所以必须用它的返回值。
            if (deep && loaderType != null)
            {
                object loaded = CallLoadParts(loaderType);
                AddDictionary(loaded, "PartsLoader.LoadParts()");
            }

            Dedupe();
            catalogCount = catalog.Count;
            if (catalogCount == 0 && catalogError.Length == 0)
            {
                catalogError = "没有找到任何零件名（游戏可能不在建造场景）";
            }
        }

        /// <summary>
        /// 在主线程调用游戏的 PartsLoader.LoadParts()，返回它加载出来的字典。
        /// 失败返回 null。
        /// </summary>
        private static object CallLoadParts(Type loaderType)
        {
            MethodInfo load = loaderType.GetMethod(
                "LoadParts",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                null,
                Type.EmptyTypes,
                null);
            if (load == null)
            {
                return null;
            }
            try
            {
                return load.Invoke(null, null);
            }
            catch (Exception ex)
            {
                if (sources.Length > 0)
                {
                    sources += "+";
                }
                sources += "LoadParts(failed:" + ex.GetType().Name + ")";
                return null;
            }
        }

        private static void AddExampleRockets(object examples)
        {
            Array arr = examples as Array;
            if (arr == null)
            {
                return;
            }
            int before = catalog.Count;
            for (int i = 0; i < arr.Length; i++)
            {
                object bp = BridgeState.Get(arr.GetValue(i), "blueprint");
                if (bp == null)
                {
                    bp = BridgeState.Get(arr.GetValue(i), "rocket");
                }
                object saves = BridgeState.Get(bp, "parts");
                Array sa = saves as Array;
                if (sa == null)
                {
                    continue;
                }
                for (int k = 0; k < sa.Length; k++)
                {
                    object n = BridgeState.Get(sa.GetValue(k), "name");
                    string s = n == null ? "" : Convert.ToString(n, CultureInfo.InvariantCulture);
                    if (!string.IsNullOrEmpty(s))
                    {
                        catalog.Add(s.Trim());
                    }
                }
            }
            if (catalog.Count > before)
            {
                if (sources.Length > 0)
                {
                    sources += "+";
                }
                sources += "exampleRockets";
            }
        }

        private static void AddSceneParts()
        {
            Type partType = BridgeState.FindType("SFS.Parts.Part");
            if (partType == null)
            {
                return;
            }
            object[] parts = BridgeState.FindObjects(partType);
            int before = catalog.Count;
            for (int i = 0; i < parts.Length; i++)
            {
                object n = BridgeState.Get(parts[i], "Name");
                string s = n == null ? "" : Convert.ToString(n, CultureInfo.InvariantCulture);
                if (IsPlausiblePartName(s))
                {
                    catalog.Add(s.Trim());
                }
            }
            if (catalog.Count > before)
            {
                if (sources.Length > 0)
                {
                    sources += "+";
                }
                sources += "scene_parts";
            }
        }

        private static string sources = "";

        /// <summary>
        /// 过滤掉明显不是零件名的键。
        /// 从 PickGridUI 的字典里捞的时候会混进分级按钮之类的键（例如 "1"），
        /// 这类名字如果留在目录里，就可能被上层传回游戏当零件名用。
        /// </summary>
        private static bool IsPlausiblePartName(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length > 60)
            {
                return false;
            }
            for (int i = 0; i < s.Length; i++)
            {
                if (char.IsLetter(s[i]))
                {
                    return true;
                }
            }
            return false;
        }

        private static void AddDictionary(object dict, string label)
        {
            if (dict == null)
            {
                return;
            }
            IDictionary d = dict as IDictionary;
            if (d == null || d.Count == 0)
            {
                return;
            }
            int before = catalog.Count;
            foreach (DictionaryEntry e in d)
            {
                string s = KeyToString(e.Key);
                if (IsPlausiblePartName(s))
                {
                    catalog.Add(s);
                }
            }
            if (catalog.Count > before)
            {
                if (sources.Length > 0)
                {
                    sources += "+";
                }
                sources += label;
            }
        }

        private static void Dedupe()
        {
            catalog.Sort(StringComparer.OrdinalIgnoreCase);
            for (int i = catalog.Count - 1; i > 0; i--)
            {
                if (string.Equals(catalog[i], catalog[i - 1], StringComparison.OrdinalIgnoreCase))
                {
                    catalog.RemoveAt(i);
                }
            }
        }

        private static string KeyToString(object key)
        {
            if (key == null)
            {
                return "";
            }
            if (key is string)
            {
                return (string)key;
            }
            // VariantRef 之类：优先取 name / Value
            object n = BridgeState.Get(key, "name");
            if (n == null)
            {
                n = BridgeState.Get(key, "Name");
            }
            if (n == null)
            {
                n = BridgeState.Get(key, "Value");
            }
            string s = n == null
                ? Convert.ToString(key, CultureInfo.InvariantCulture)
                : Convert.ToString(n, CultureInfo.InvariantCulture);
            return s == null ? "" : s.Trim();
        }

        /// <summary>只读缓存结果，任何线程都能安全调用。</summary>
        public static string CatalogJson()
        {
            StringBuilder sb = new StringBuilder(1024);
            sb.Append("{\"ok\":").Append(catalogError.Length == 0 ? "true" : "false");
            sb.Append(",\"ready\":").Append(catalogReady ? "true" : "false");
            sb.Append(",\"count\":").Append(catalogCount);
            sb.Append(",\"source\":\"").Append(Esc(sources)).Append("\"");
            sb.Append(",\"parts\":[");
            for (int i = 0; i < catalog.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(",");
                }
                sb.Append("\"").Append(Esc(catalog[i])).Append("\"");
            }
            sb.Append("]");
            if (catalogError.Length > 0)
            {
                sb.Append(",\"error\":\"").Append(Esc(catalogError)).Append("\"");
            }
            sb.Append("}");
            return sb.ToString();
        }

        // -- 放置 -------------------------------------------------------------

        private static object MakeVector2(double x, double y)
        {
            Type v2 = BridgeState.FindType("UnityEngine.Vector2");
            if (v2 == null)
            {
                throw new Exception("UnityEngine.Vector2 not found");
            }
            ConstructorInfo ctor = v2.GetConstructor(new Type[] { typeof(float), typeof(float) });
            if (ctor == null)
            {
                throw new Exception("Vector2(float,float) ctor not found");
            }
            return ctor.Invoke(new object[] { (float)x, (float)y });
        }

        /// <summary>
        /// 零件的标准朝向。
        ///
        /// **不是 (0,0,0)** —— 实测官方示例蓝图里所有零件的 o 都是 (1, 1, 0)：
        /// x/y 是表面贴合偏移，z 是绕轴角度。用 (0,0,0) 会让零件朝向非法，
        /// 接不上别的零件，物理直接失效（表现就是发射时散架、钻到地下）。
        /// </summary>
        private static object MakeOrientation()
        {
            Type t = BridgeState.FindType("SFS.Parts.Modules.Orientation");
            if (t == null)
            {
                return null;
            }
            ConstructorInfo ctor = t.GetConstructor(
                new Type[] { typeof(float), typeof(float), typeof(float) });
            if (ctor == null)
            {
                return null;
            }
            return ctor.Invoke(new object[] { 1f, 1f, 0f });
        }

        /// <summary>
        /// 从「同名零件的真实实例或预制体」反推一份完整的 PartSave，抄走 N/T 变量。
        ///
        /// 这是零件能被正确生成的关键：蓝图里每个零件都带尺寸（N）与纹理（T），
        /// 只给名字 + 坐标的话零件是没有尺寸的，物理与几何都会失效。
        /// 找不到模板就如实标记，让上层知道这次放置可能不完整。
        /// </summary>
        private static void ApplyTemplate(Type partSaveType, object save, string name)
        {
            try
            {
                object template = FindTemplate(name);
                if (template == null)
                {
                    steps += "template=missing;";
                    return;
                }

                MethodInfo createSaves = partSaveType.GetMethod(
                    "CreateSaves",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (createSaves == null)
                {
                    steps += "template=noCreateSaves;";
                    return;
                }

                Array one = Array.CreateInstance(template.GetType(), 1);
                one.SetValue(template, 0);
                Array saves = createSaves.Invoke(null, new object[] { one }) as Array;
                if (saves == null || saves.Length == 0)
                {
                    steps += "template=empty;";
                    return;
                }

                object src = saves.GetValue(0);
                SetMember(save, "NUMBER_VARIABLES", BridgeState.Get(src, "NUMBER_VARIABLES"));
                SetMember(save, "TOGGLE_VARIABLES", BridgeState.Get(src, "TOGGLE_VARIABLES"));
                SetMember(save, "TEXT_VARIABLES", BridgeState.Get(src, "TEXT_VARIABLES"));
                steps += "template=ok;";
            }
            catch (Exception ex)
            {
                steps += "template=" + ex.GetType().Name + ";";
            }
        }

        /// <summary>
        /// 找零件模板。**必须用内部名匹配** —— `Part.Name` 返回的是显示名
        /// （"Valiant Engine"），而 PartSave 里用的是内部名（"Engine Valiant"），
        /// 两者不一样。所以主路径是 PartsLoader 的字典（键就是内部名）。
        /// </summary>
        private static object FindTemplate(string name)
        {
            try
            {
                // 主路径：PartsLoader.LoadParts() 的返回值，键是内部名
                Type loaderType = BridgeState.FindType("SFS.Parts.PartsLoader");
                if (loaderType != null)
                {
                    object dict = LoadedParts(loaderType);
                    IDictionary d = dict as IDictionary;
                    if (d != null)
                    {
                        foreach (DictionaryEntry e in d)
                        {
                            if (string.Equals(KeyToString(e.Key), name,
                                    StringComparison.OrdinalIgnoreCase))
                            {
                                return e.Value;
                            }
                        }
                    }
                }

                // 兜底：场景里已有的零件，按显示名比对
                Type partType = BridgeState.FindType("SFS.Parts.Part");
                if (partType != null)
                {
                    object[] parts = BridgeState.FindObjects(partType);
                    for (int i = 0; i < parts.Length; i++)
                    {
                        object n = BridgeState.Get(parts[i], "Name");
                        string s = n == null ? "" : Convert.ToString(n, CultureInfo.InvariantCulture);
                        if (string.Equals(s, name, StringComparison.OrdinalIgnoreCase))
                        {
                            return parts[i];
                        }
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        private static object cachedParts;
        private static bool cachedPartsTried;

        /// <summary>在主线程调用 PartsLoader.LoadParts() 并缓存返回值。</summary>
        private static object LoadedParts(Type loaderType)
        {
            if (!cachedPartsTried)
            {
                cachedPartsTried = true;
                try
                {
                    MethodInfo load = loaderType.GetMethod(
                        "LoadParts",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                        null,
                        Type.EmptyTypes,
                        null);
                    if (load != null)
                    {
                        cachedParts = load.Invoke(null, null);
                    }
                }
                catch
                {
                    cachedParts = null;
                }
            }
            return cachedParts;
        }

        private static void SetMember(object obj, string name, object value)
        {
            if (obj == null || value == null)
            {
                return;
            }
            Type t = obj.GetType();
            const BindingFlags F =
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            FieldInfo f = t.GetField(name, F);
            if (f != null)
            {
                f.SetValue(obj, value);
                return;
            }
            PropertyInfo p = t.GetProperty(name, F);
            if (p != null && p.CanWrite)
            {
                p.SetValue(obj, value, null);
            }
        }

        /// <summary>
        /// 按「叠在已有零件的上面/下面/同一贴合点」自动算位置。
        ///
        /// 为什么需要：每个零件的高度不同（Fuel Tank 的 N.height 是 4.0，锥头、
        /// 引擎都不一样）。**统一用固定间距会让零件之间留缝或重叠** ——
        /// 实测后果是发射时上面的零件掉下来把火箭砸爆。
        /// 正确位置 = 两者的贴合面重合，即 y = anchorY ± (anchorH + newH) / 2。
        /// </summary>
        private static void ResolveStack(Job job)
        {
            string mode = job.Stack == null ? "" : job.Stack.Trim().ToLowerInvariant();
            if (mode.Length == 0 || mode == "none")
            {
                return;
            }

            Type partType = BridgeState.FindType("SFS.Parts.Part");
            if (partType == null)
            {
                return;
            }
            object[] parts = BridgeState.FindObjects(partType);
            if (parts.Length == 0)
            {
                steps += "stack=noAnchor;";
                return;   // 场景里没零件，就用调用方给的坐标
            }

            object anchor = null;
            double anchorY = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                object pos = BridgeState.Get(parts[i], "Position");
                if (pos == null)
                {
                    continue;
                }
                double y = BridgeState.ToDouble(BridgeState.Get(pos, "y"), 0);
                if (anchor == null
                    || (mode == "top" && y > anchorY)
                    || (mode == "bottom" && y < anchorY))
                {
                    anchor = parts[i];
                    anchorY = y;
                }
            }
            if (anchor == null)
            {
                steps += "stack=noAnchorPos;";
                return;
            }

            object anchorPos = BridgeState.Get(anchor, "Position");
            double ax = BridgeState.ToDouble(BridgeState.Get(anchorPos, "x"), 0);

            double anchorH = PartHeight(anchor);
            double newH = TemplateHeight(job.Name);
            if (anchorH <= 0 || newH <= 0)
            {
                // 读不到高度就退化为「同一贴合点」（引擎贴罐底那种）
                job.X = ax;
                job.Y = anchorY;
                steps += "stack=same(h=" + Num(anchorH) + "/" + Num(newH) + ");";
                return;
            }

            job.X = ax;
            if (mode == "bottom")
            {
                job.Y = anchorY - (anchorH + newH) / 2.0;
            }
            else
            {
                job.Y = anchorY + (anchorH + newH) / 2.0;
            }
            steps += "stack=" + mode + "(aH=" + Num(anchorH) + ",nH=" + Num(newH) + ");";
        }

        /// <summary>从零件的 PartSave 里读 N.height。</summary>
        private static double PartHeight(object part)
        {
            try
            {
                Type saveType = BridgeState.FindType("SFS.Parts.PartSave");
                if (saveType == null || part == null)
                {
                    return -1;
                }
                MethodInfo createSaves = saveType.GetMethod(
                    "CreateSaves",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (createSaves == null)
                {
                    return -1;
                }
                Array one = Array.CreateInstance(part.GetType(), 1);
                one.SetValue(part, 0);
                Array saves = createSaves.Invoke(null, new object[] { one }) as Array;
                if (saves == null || saves.Length == 0)
                {
                    return -1;
                }
                return HeightOf(saves.GetValue(0));
            }
            catch
            {
                return -1;
            }
        }

        private static double TemplateHeight(string name)
        {
            object t = FindTemplate(name);
            if (t == null)
            {
                return -1;
            }
            return PartHeight(t);
        }

        private static double HeightOf(object partSave)
        {
            try
            {
                object n = BridgeState.Get(partSave, "NUMBER_VARIABLES");
                IDictionary d = n as IDictionary;
                if (d == null)
                {
                    return -1;
                }
                foreach (DictionaryEntry e in d)
                {
                    if (string.Equals(KeyToString(e.Key), "height",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return BridgeState.ToDouble(e.Value, -1);
                    }
                }
            }
            catch
            {
            }
            return -1;
        }

        private static int PlacePart(Job job)
        {
            // 先校验：零件名必须在游戏已加载的目录里。
            // 绝不把未经验证的名字交给游戏内部 —— 上一次就是因为把不可信的数据
            // 交给 Unity 原生 API，把游戏直接打崩了。
            if (!catalogReady || catalog.Count == 0)
            {
                throw new Exception(
                    "读不到零件的名称清单，无法确认 '" + job.Name
                    + "' 是合法零件名。请确认游戏处于建造场景。");
            }
            bool known = false;
            for (int i = 0; i < catalog.Count; i++)
            {
                if (string.Equals(catalog[i], job.Name, StringComparison.OrdinalIgnoreCase))
                {
                    known = true;
                    job.Name = catalog[i]; // 用目录里的原始大小写
                    break;
                }
            }
            if (!known)
            {
                throw new Exception(
                    "未知零件名 '" + job.Name + "'。请先用 /build_catalog 查询可用零件。");
            }

            Type partSaveType = BridgeState.FindType("SFS.Parts.PartSave");
            if (partSaveType == null)
            {
                throw new Exception("SFS.Parts.PartSave not found");
            }
            Type blueprintType = BridgeState.FindType("SFS.Builds.Blueprint");
            if (blueprintType == null)
            {
                throw new Exception("SFS.Builds.Blueprint not found");
            }
            Type buildStateType = BridgeState.FindType("SFS.Builds.BuildState");
            if (buildStateType == null)
            {
                throw new Exception("SFS.Builds.BuildState not found");
            }
            object buildState = BridgeState.GetStatic(buildStateType, "main");
            if (buildState == null)
            {
                throw new Exception("BuildState.main is null (不在建造场景)");
            }

            // 1) PartSave
            object save = Activator.CreateInstance(partSaveType);
            SetMember(save, "name", job.Name);
            SetMember(save, "position", MakeVector2(job.X, job.Y));
            SetMember(save, "orientation", MakeOrientation());

            // 关键：把零件的 N/T 变量抄过来。
            // 蓝图里每个零件都带 N（width_original / width_a / width_b / height …）
            // 和 T（纹理）。**没有这些，零件就没有尺寸** —— 实测结果是发射时
            // 结构散架并直接钻到地下。这里用游戏自己的 PartSave.CreateSaves()
            // 从一个真实零件反推出完整的 PartSave，再抄字段。
            ApplyTemplate(partSaveType, save, job.Name);

            // 2) Blueprint
            Array saves = Array.CreateInstance(partSaveType, 1);
            saves.SetValue(save, 0);

            // 分级归属：蓝图里的 stages 记录 partIndexes，零件不属于任何分级的话
            // **引擎永远不会点火**（实测：推力恒为 0t）。这里把这一个零件登记进
            // 第 1 级，让它至少是可点火的。
            Type stageSaveType = BridgeState.FindType("SFS.World.StageSave");
            Array stages;
            if (stageSaveType == null)
            {
                stages = Array.CreateInstance(typeof(object), 0);
            }
            else
            {
                stages = Array.CreateInstance(stageSaveType, 1);
                try
                {
                    object stageSave = Activator.CreateInstance(stageSaveType);
                    SetMember(stageSave, "stageId", 1);
                    SetMember(stageSave, "partIndexes", new int[] { 0 });
                    stages.SetValue(stageSave, 0);
                    steps += "stage=1;";
                }
                catch (Exception ex)
                {
                    steps += "stage=" + ex.GetType().Name + ";";
                }
            }

            object blueprint = null;
            ConstructorInfo[] bctors = blueprintType.GetConstructors();
            for (int i = 0; i < bctors.Length; i++)
            {
                ParameterInfo[] ps = bctors[i].GetParameters();
                if (ps.Length != 5)
                {
                    continue;
                }
                try
                {
                    blueprint = bctors[i].Invoke(new object[]
                    {
                        saves, stages, 0f, 0f, false,
                    });
                    break;
                }
                catch
                {
                }
            }
            if (blueprint == null)
            {
                throw new Exception("Blueprint(PartSave[], StageSave[], float, float, bool) ctor failed");
            }

            // 3) SpawnBlueprint
            MethodInfo spawn = null;
            MethodInfo[] methods = buildStateType.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            for (int i = 0; i < methods.Length; i++)
            {
                if (methods[i].Name == "SpawnBlueprint" && methods[i].GetParameters().Length == 3)
                {
                    spawn = methods[i];
                    break;
                }
            }
            if (spawn == null)
            {
                throw new Exception("BuildState.SpawnBlueprint not found");
            }

            object[] args = new object[] { blueprint, false, null };
            object result = spawn.Invoke(buildState, args);
            Array arr = result as Array;
            int count = arr == null ? 0 : arr.Length;

            // 注意：**不要**再调 BuildGrid.AddParts —— SpawnBlueprint 已经把这些
            // 零件登记进建造网格了。重复登记会抛异常，而且零件数会 +2 而不是 +1
            // （实测：121 -> 123）。这里只需要把镜头挪过去。
            if (arr != null && arr.Length > 0)
            {
                steps += "spawn=" + arr.Length + ";";
                SetCamera(buildStateType, buildState, arr);
            }
            return count;
        }

        /// <summary>
        /// 把摄像机挪到新零件上。
        ///
        /// 这里**不用** BuildState.CenterCameraOnParts：它依赖建造网格的内部状态，
        /// 实测会把镜头带到莫名其妙的地方，导致零件生成了但主视口里一片空白
        /// （这才是「看不到零件」的真正原因）。改为显式设置 BuildCamera.CameraPosition。
        /// </summary>
        private static void SetCamera(Type buildStateType, object buildState, Array parts)
        {
            try
            {
                object cam = BridgeState.Get(buildState, "buildCamera");
                if (cam == null)
                {
                    Type camType = BridgeState.FindType("SFS.Builds.BuildCamera");
                    object[] found = BridgeState.FindObjects(camType);
                    if (found.Length == 0)
                    {
                        steps += "camera=missing;";
                        return;
                    }
                    cam = found[0];
                }

                // 取新零件的平均位置作为镜头目标
                double sx = 0;
                double sy = 0;
                int n = 0;
                for (int i = 0; i < parts.Length; i++)
                {
                    object pos = BridgeState.Get(parts.GetValue(i), "Position");
                    if (pos == null)
                    {
                        continue;
                    }
                    sx += BridgeState.ToDouble(BridgeState.Get(pos, "x"), 0);
                    sy += BridgeState.ToDouble(BridgeState.Get(pos, "y"), 0);
                    n++;
                }
                if (n == 0)
                {
                    steps += "camera=noPos;";
                    return;
                }

                object vector = MakeVector2(sx / n, sy / n);
                PropertyInfo camPos = cam.GetType().GetProperty(
                    "CameraPosition",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (camPos == null || !camPos.CanWrite)
                {
                    steps += "camera=noProp;";
                    return;
                }
                camPos.SetValue(cam, vector, null);
                steps += "camera=ok@" + Num(sx / n) + "," + Num(sy / n) + ";";
            }
            catch (Exception ex)
            {
                steps += "camera=" + ex.GetType().Name + ";";
            }
        }

        // -- 结果 -------------------------------------------------------------

        private static string Num(double v)
        {
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

        public static string ResultJson()
        {
            StringBuilder sb = new StringBuilder(256);
            sb.Append("{\"ok\":").Append(LastError.Length == 0 && LastResult.Length > 0 ? "true" : "false");
            sb.Append(",\"placed\":").Append(PlacedCount);
            if (LastResult.Length > 0)
            {
                sb.Append(",\"result\":\"").Append(Esc(LastResult)).Append("\"");
            }
            if (steps.Length > 0)
            {
                sb.Append(",\"steps\":\"").Append(Esc(steps)).Append("\"");
            }
            if (LastError.Length > 0)
            {
                sb.Append(",\"error\":\"").Append(Esc(LastError)).Append("\"");
            }
            sb.Append("}");
            return sb.ToString();
        }
    }
}
