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
            Job job = new Job();
            job.Name = name;
            job.X = x;
            job.Y = y;
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
            return ctor.Invoke(new object[] { 0f, 0f, 0f });
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

            // 2) Blueprint
            Array saves = Array.CreateInstance(partSaveType, 1);
            saves.SetValue(save, 0);

            Type stageSaveType = BridgeState.FindType("SFS.World.StageSave");
            Array stages = stageSaveType == null
                ? Array.CreateInstance(typeof(object), 0)
                : Array.CreateInstance(stageSaveType, 0);

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

            // SpawnBlueprint 只是**创建**零件对象：统计、分级栏都能看到它们，
            // 但主视口渲染的是 BuildGrid 的内容，没注册进去就看不见。
            // 所以必须再手动加进建造网格。
            if (arr != null && arr.Length > 0)
            {
                steps += "spawn=" + arr.Length + ";";
                AddToBuildGrid(buildStateType, buildState, arr);
                SetCamera(buildStateType, buildState, arr);
            }
            return count;
        }

        private static object GetBuildGrid(Type buildStateType, object buildState)
        {
            try
            {
                object grid = BridgeState.Get(buildState, "buildGrid");
                if (grid != null)
                {
                    return grid;
                }
                Type mgrType = BridgeState.FindType("SFS.Builds.BuildManager");
                object mgr = BridgeState.GetStatic(mgrType, "main");
                return BridgeState.Get(mgr, "buildGrid");
            }
            catch
            {
                return null;
            }
        }

        private static void AddToBuildGrid(Type buildStateType, object buildState, Array parts)
        {
            try
            {
                object grid = GetBuildGrid(buildStateType, buildState);
                if (grid == null)
                {
                    steps += "grid=missing;";
                    return;
                }
                MethodInfo add = null;
                MethodInfo[] ms = grid.GetType().GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                for (int i = 0; i < ms.Length; i++)
                {
                    if (ms[i].Name == "AddParts" && ms[i].GetParameters().Length == 4)
                    {
                        add = ms[i];
                        break;
                    }
                }
                if (add == null)
                {
                    steps += "gridAdd=notfound;";
                    return;
                }
                add.Invoke(grid, new object[] { true, true, true, parts });
                steps += "gridAdd=ok;";
            }
            catch (Exception ex)
            {
                steps += "gridAdd=" + ex.GetType().Name + ";";
            }
        }

        /// <summary>
        /// 把摄像机挪到新零件上。
        ///
        /// 这里**不用** BuildState.CenterCameraOnParts：它内部依赖建造网格，
        /// 零件还没进网格时会把镜头带到莫名其妙的地方（实测主视口直接空了）。
        /// 改为显式设置 BuildCamera 的 CameraPosition。
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
