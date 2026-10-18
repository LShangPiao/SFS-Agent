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

            // 上一次放置推迟下来的贴合，先在这里做掉（用上一帧建好的网格）。
            if (pendingPart != null)
            {
                FinishPending();
                return;
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
                LastResult = "placed " + job.Name + " at (" + Num(placedX) + ", " + Num(placedY)
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
                // 顺手填进模板缓存，省得 FindTemplate 再调一次
                if (loaded != null && cachedParts == null)
                {
                    cachedParts = loaded;
                }
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

        /// <summary>在主线程调用 PartsLoader.LoadParts() 并缓存返回值。</summary>
        private static object LoadedParts(Type loaderType)
        {
            // 只缓存「成功」的结果。以前这里有个 cachedPartsTried 一次性闸门，
            // 只要第一次调用时零件库还没就绪（比如刚进游戏），就会把 null 永久缓存住，
            // 于是目录里明明有 56 种零件，放零件却一律回「未知零件名」。
            if (cachedParts == null)
            {
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

        // 本次放置任务的基准零件与叠放方向。
        // HTTP 线程只入队，主线程 Tick 里才真正解析，所以这几个字段
        // 只在主线程读写，不需要加锁。
        private static object stackAnchor;
        private static string stackMode = "none";
        private static double alignGap = -1;
        private static double placedX;
        private static double placedY;

        // 贴合要推迟一帧：零件是 SpawnBlueprint 刚造出来的，**同一帧里
        // GetAttachmentSurfacesWorld() 还是空的**（网格还没 RegenerateMesh），
        // 实测会稳定地返回 align=noSurf。所以这一帧只登记，下一帧的 Tick
        // 里再按几何量把它挪到位（HTTP 那边等 300ms，完全够）。
        private static object pendingPart;
        private static object pendingAnchor;
        private static string pendingMode;
        private static string pendingName;

        /// <summary>
        /// 选出这次要叠在哪个零件上，并把初始位置直接设成基准零件的位置。
        ///
        /// 为什么不再用「按零件高度算 y」：SFS 的零件高度差别很大，而且
        /// N.height 只对一部分零件有意义（锥头是 size、整流罩是 width/height），
        /// 算出来的间距经常差半米到一米 —— 表现就是零件一个个悬在空中、
        /// 互相没贴上，发射时直接散架。**正确做法是把两个零件的贴合面重合**，
        /// 所以这里只负责选基准，真正的贴合交给 AlignToAnchor 按几何量。
        ///
        /// stack 取值：
        ///   top    叠在最上面那个零件的顶上（默认，往上长）
        ///   bottom 接在最下面那个零件的底下（往下长，常用于装引擎）
        ///   none   完全用调用方给的坐标
        ///   auto   场景里有零件就按 top，没有就用调用方坐标
        /// </summary>
        private static void ResolveStack(Job job)
        {
            stackAnchor = null;
            stackMode = "none";
            alignGap = -1;

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
                if (mode != "auto")
                {
                    steps += "stack=noAnchor;";
                }
                return;   // 场景里没零件，就用调用方给的坐标
            }
            if (mode == "auto")
            {
                mode = "top";
            }
            if (mode != "top" && mode != "bottom")
            {
                return;
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
            stackAnchor = anchor;
            stackMode = mode;
            job.X = BridgeState.ToDouble(BridgeState.Get(anchorPos, "x"), 0);
            job.Y = anchorY;
            steps += "stack=" + mode + "@" + Num(job.X) + "," + Num(anchorY) + ";";
        }

        /// <summary>
        /// 把一个零件的所有附着面读成 {startX,startY,endX,endY}。
        /// Part.GetAttachmentSurfacesWorld() 返回 Line2[]，是**世界坐标**，
        /// 所以「零件放没放平、有没有贴身」可以直接用几何判定。
        /// </summary>
        private static List<double[]> Surfaces(object part)
        {
            List<double[]> list = new List<double[]>();
            if (part == null)
            {
                return list;
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
                    return list;
                }
                Array arr = m.Invoke(part, null) as Array;
                if (arr == null)
                {
                    return list;
                }
                for (int i = 0; i < arr.Length; i++)
                {
                    object line = arr.GetValue(i);
                    object s = BridgeState.Get(line, "start");
                    object e = BridgeState.Get(line, "end");
                    if (s == null || e == null)
                    {
                        continue;
                    }
                    list.Add(new double[]
                    {
                        BridgeState.ToDouble(BridgeState.Get(s, "x"), 0),
                        BridgeState.ToDouble(BridgeState.Get(s, "y"), 0),
                        BridgeState.ToDouble(BridgeState.Get(e, "x"), 0),
                        BridgeState.ToDouble(BridgeState.Get(e, "y"), 0),
                    });
                }
            }
            catch
            {
            }
            return list;
        }

        /// <summary>
        /// 按方向挑一个贴合面：top 要「朝上那一面」，bottom 要「朝下那一面」。
        ///
        /// **只看水平面**（两个端点 y 相同）。零件上还有大量竖着的侧挂面，
        /// 它们的 y 中点可能比真正的顶面还高 —— 之前不区分方向，结果挑中了
        /// 侧面，把三个油箱叠成了同一个位置（实测总高 2 m 而不是 12 m）。
        /// 先按长度过滤（取最长的那一档），再按 y 挑上下。
        /// </summary>
        private static double[] PickSurface(List<double[]> surfaces, bool wantTop, double preferLen)
        {
            List<double[]> horiz = new List<double[]>();
            List<double> lens = new List<double>();
            double maxLen = 0;
            for (int i = 0; i < surfaces.Count; i++)
            {
                double[] s = surfaces[i];
                if (Math.Abs(s[3] - s[1]) > 0.05)
                {
                    continue;   // 竖直/斜的面，不参与上下叠放
                }
                double len = Math.Abs(s[2] - s[0]);
                if (len <= 0.0001)
                {
                    continue;
                }
                horiz.Add(s);
                lens.Add(len);
                if (len > maxLen)
                {
                    maxLen = len;
                }
            }
            if (horiz.Count == 0)
            {
                return null;
            }

            // 优先挑「和对面那条面差不多宽」的，这样顶面配顶面、底面配底面
            List<int> pick = new List<int>();
            if (preferLen > 0.0001)
            {
                for (int i = 0; i < horiz.Count; i++)
                {
                    if (Math.Abs(lens[i] - preferLen) <= 0.05)
                    {
                        pick.Add(i);
                    }
                }
            }
            if (pick.Count == 0)
            {
                for (int i = 0; i < horiz.Count; i++)
                {
                    if (lens[i] >= maxLen * 0.85)
                    {
                        pick.Add(i);
                    }
                }
            }
            if (pick.Count == 0)
            {
                for (int i = 0; i < horiz.Count; i++)
                {
                    pick.Add(i);
                }
            }

            double[] best = null;
            double bestY = 0;
            for (int k = 0; k < pick.Count; k++)
            {
                double[] s = horiz[pick[k]];
                double midY = (s[1] + s[3]) / 2.0;
                if (best == null
                    || (wantTop && midY > bestY)
                    || (!wantTop && midY < bestY))
                {
                    best = s;
                    bestY = midY;
                }
            }
            return best;
        }

        /// <summary>
        /// 把一个零件的所有附着面压成「包围盒」：{minY, maxY, minX, maxX}。
        /// 叠放时只用 Y 的两个极值：最上面那圈和最下面那圈才是真正能贴合的地方。
        /// </summary>
        private static double[] Range(List<double[]> surfaces)
        {
            if (surfaces == null || surfaces.Count == 0)
            {
                return null;
            }
            double minY = double.MaxValue;
            double maxY = double.MinValue;
            double minX = double.MaxValue;
            double maxX = double.MinValue;
            for (int i = 0; i < surfaces.Count; i++)
            {
                double[] s = surfaces[i];
                minY = Math.Min(minY, Math.Min(s[1], s[3]));
                maxY = Math.Max(maxY, Math.Max(s[1], s[3]));
                minX = Math.Min(minX, Math.Min(s[0], s[2]));
                maxX = Math.Max(maxX, Math.Max(s[0], s[2]));
            }
            return new double[] { minY, maxY, minX, maxX };
        }

        /// <summary>
        /// 把新零件的贴合面挪到基准零件的贴合面上（真的移动零件，不是估算）。
        ///
        /// 做法：新零件先和基准零件放在同一个位置，此时两边的附着面都在
        /// 同一坐标系里，直接算两条线中点的差值就是需要平移的量。
        /// 移完再读一次，返回残余缝隙（米的量级，0 表示严丝合缝）。
        /// </summary>
        private static double AlignToAnchor(object part, object anchor, string mode)
        {
            if (part == null || anchor == null)
            {
                return -1;
            }
            try
            {
                bool anchorTop = mode == "top";

                // 用「轮廓极值」而不是某一条面来对齐。
                // SFS 的附着面列表里既有真正的顶/底环，也有中段的侧面轮廓，
                // 按「最宽 / 最高」挑面会挑到腰线：三个油箱叠成同一位置、
                // 引擎和分离器重合，都是这么来的。直接拿最高点/最低点最稳。
                double[] a = Range(Surfaces(anchor));
                if (a == null)
                {
                    steps += "align=noSurfA;";
                    return -1;
                }
                double[] b = Range(Surfaces(part));
                if (b == null)
                {
                    steps += "align=noSurfB;";
                    return -1;
                }

                // top：基准的上沿贴新零件的下沿；bottom：基准的下沿贴新零件的上沿
                double targetY = anchorTop ? a[1] : a[0];
                double fromY = anchorTop ? b[0] : b[1];
                double dy = targetY - fromY;

                steps += "anchorY=[" + Num(a[0]) + "," + Num(a[1])
                    + "] partY=[" + Num(b[0]) + "," + Num(b[1]) + "];";

                object pos = BridgeState.Get(part, "Position");
                double px = BridgeState.ToDouble(BridgeState.Get(pos, "x"), 0);
                double py = BridgeState.ToDouble(BridgeState.Get(pos, "y"), 0);
                SetMember(part, "Position", MakeVector2(px, py + dy));

                // 复测：移动之后再读一次，算残余间距
                double[] b2 = Range(Surfaces(part));
                double gap = b2 == null
                    ? -1
                    : Math.Abs(targetY - (anchorTop ? b2[0] : b2[1]));
                steps += "align=" + mode + "(dy=" + Num(dy)
                    + ",gap=" + Num(gap) + ");";
                return gap;
            }
            catch (Exception ex)
            {
                steps += "align=" + ex.GetType().Name + ";";
                return -1;
            }
        }

        /// <summary>
        /// 把推迟下来的贴合真正做掉（由下一帧的 Tick 调用）。
        /// 读不到附着面时先逼一次 RegenerateMesh，再不行就退回「按高度估算」
        /// —— 宁可差几十厘米，也不能让零件悬在半空。
        /// </summary>
        private static void FinishPending()
        {
            object part = pendingPart;
            object anchor = pendingAnchor;
            string mode = pendingMode ?? "top";
            string name = pendingName ?? "?";

            pendingPart = null;
            pendingAnchor = null;
            pendingMode = null;
            pendingName = null;

            if (part == null)
            {
                return;
            }

            if (Surfaces(part).Count == 0)
            {
                RegenerateMesh(part);
            }

            double gap = AlignToAnchor(part, anchor, mode);
            if (gap < 0)
            {
                gap = FallbackStack(part, anchor, mode, name);
            }
            alignGap = gap;

            try
            {
                object pos = BridgeState.Get(part, "Position");
                placedX = BridgeState.ToDouble(BridgeState.Get(pos, "x"), placedX);
                placedY = BridgeState.ToDouble(BridgeState.Get(pos, "y"), placedY);
            }
            catch
            {
            }

            LastResult = "placed " + name + " at (" + Num(placedX) + ", " + Num(placedY)
                + "), count=" + PlacedCount + (gap >= 0 ? ", gap=" + Num(gap) : "");
        }

        private static void RegenerateMesh(object part)
        {
            if (part == null)
            {
                return;
            }
            try
            {
                string[] names = new string[] { "RegenerateMesh", "InitializePart", "UpdateMesh" };
                for (int i = 0; i < names.Length; i++)
                {
                    MethodInfo m = part.GetType().GetMethod(
                        names[i],
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                        null,
                        Type.EmptyTypes,
                        null);
                    if (m == null)
                    {
                        continue;
                    }
                    m.Invoke(part, null);
                }
            }
            catch
            {
            }
        }

        /// <summary>兜底：附着面读不到时按零件高度把 y 挪开，至少不重叠。</summary>
        private static double FallbackStack(object part, object anchor, string mode, string name)
        {
            try
            {
                double ah = PartHeight(anchor);
                double bh = PartHeight(part);
                if (bh < 0)
                {
                    bh = TemplateHeight(name);
                }
                if (ah <= 0 || bh <= 0)
                {
                    steps += "fallback=noHeight;";
                    return -1;
                }
                double dy = (ah + bh) / 2.0 * (mode == "bottom" ? -1 : 1);
                object pos = BridgeState.Get(part, "Position");
                double px = BridgeState.ToDouble(BridgeState.Get(pos, "x"), 0);
                double py = BridgeState.ToDouble(BridgeState.Get(pos, "y"), 0);
                SetMember(part, "Position", MakeVector2(px, py + dy));
                steps += "fallback=height(dy=" + Num(dy) + ");";
                return -1;
            }
            catch
            {
                return -1;
            }
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

                // 贴合：新零件先和基准零件同位置生成，再按附着面把两者贴死。
                // 不做这一步的话，零件是「摆在那儿」而不是「接上了」——
                // 表现就是一堆悬空的零件，发射瞬间散架。
                // 注意这里是**登记**，真正的移动在下一帧的 FinishPending 里做。
                if (stackAnchor != null && stackMode != "none")
                {
                    pendingPart = arr.GetValue(0);
                    pendingAnchor = stackAnchor;
                    pendingMode = stackMode;
                    pendingName = job.Name;
                    steps += "align=deferred;";
                }

                try
                {
                    object finalPos = BridgeState.Get(arr.GetValue(0), "Position");
                    placedX = BridgeState.ToDouble(BridgeState.Get(finalPos, "x"), job.X);
                    placedY = BridgeState.ToDouble(BridgeState.Get(finalPos, "y"), job.Y);
                }
                catch
                {
                    placedX = job.X;
                    placedY = job.Y;
                }

                SetCamera(buildStateType, buildState, arr);
            }
            else
            {
                placedX = job.X;
                placedY = job.Y;
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
            sb.Append(",\"x\":").Append(Num(placedX));
            sb.Append(",\"y\":").Append(Num(placedY));
            if (stackMode != null && stackMode != "none")
            {
                sb.Append(",\"stack\":\"").Append(Esc(stackMode)).Append("\"");
                sb.Append(",\"gap\":").Append(Num(alignGap));
            }
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
