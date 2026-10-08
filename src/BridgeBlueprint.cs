// SFS-Agent — 蓝图读取与加载
//
// 走游戏自己的正统路径，而不是自己拼 PartSave：
//
//   Blueprint_Saving.GetBlueprintsList()  -> OrderedPathList.fileNames
//   Blueprint_Saving.GetPath(name)        -> IFolder
//   Blueprint.TryLoad(folder, logger, out Blueprint)   // 由游戏解析
//   BuildState.main.SpawnBlueprint(blueprint, false, null)
//
// 为什么必须这样：蓝图里的零件带 `N`（尺寸/缩放：width_original / width_a /
// width_b / height …）和 `T`（纹理）以及 stages 的 partIndexes。
// 自己拼 PartSave 缺了这些，零件就没有尺寸、也不属于任何分级 ——
// 实测结果是发射时结构散架并直接钻到地下。
//
// 这些方法都是 private static，只能反射调用。

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;

namespace SfsAgent
{
    public static class BridgeBlueprint
    {
        private static readonly List<string> names = new List<string>();
        private static volatile bool listRequested;
        private static volatile bool listReady;
        private static string listError = "";
        private static string bundledError = "";

        private static string loadName;
        private static volatile bool loadRequested;
        private static volatile bool loadReady;
        public static string LoadResult = "";
        public static string LoadError = "";

        // -- HTTP 线程接口 -----------------------------------------------------

        public static void RequestList()
        {
            listReady = false;
            listRequested = true;
        }

        public static bool ListReady
        {
            get { return listReady; }
        }

        public static void RequestLoad(string name)
        {
            loadName = name;
            LoadResult = "";
            LoadError = "";
            loadReady = false;
            loadRequested = true;
        }

        public static bool LoadReady
        {
            get { return loadReady; }
        }

        // -- 主线程 -----------------------------------------------------------

        public static void Tick()
        {
            if (listRequested)
            {
                listRequested = false;
                try
                {
                    CaptureList();
                }
                catch (Exception ex)
                {
                    listError = ex.Message;
                }
                listReady = true;
            }

            if (loadRequested)
            {
                loadRequested = false;
                string name = loadName;
                loadName = null;
                waitingFrames = 0;
                try
                {
                    LoadByName(name);
                }
                catch (Exception ex)
                {
                    LoadError = Describe(ex);
                    loadReady = true;
                }
                // 若走的是带回调的 LoadBlueprint，loadReady 由下面的回调分支置位
                if (waitingFor == null)
                {
                    loadReady = true;
                }
            }

            // 等 LoadBlueprint 的回调
            if (waitingFor != null)
            {
                if (pending != null)
                {
                    object bp = pending;
                    pending = null;
                    string name = waitingFor;
                    waitingFor = null;
                    try
                    {
                        Spawn(bp, name);
                    }
                    catch (Exception ex)
                    {
                        LoadError = Describe(ex);
                    }
                    loadReady = true;
                }
                else if (++waitingFrames > 480)
                {
                    // 回调一直没来：回退到「自己取路径 + Blueprint.TryLoad」再试一次
                    string name = waitingFor;
                    waitingFor = null;
                    try
                    {
                        LoadViaTryLoad(name);
                    }
                    catch (Exception ex)
                    {
                        LoadError = Describe(ex);
                    }
                    if (LoadResult.Length == 0 && LoadError.Length == 0)
                    {
                        LoadError = "加载蓝图失败（LoadBlueprint 回调超时，TryLoad 也没成功）";
                    }
                    loadReady = true;
                }
            }
        }

        /// <summary>把异常链展开 —— 反射调用只会抛出 TargetInvocationException，
        /// 真正的原因在 InnerException 里。</summary>
        private static string Describe(Exception ex)
        {
            StringBuilder sb = new StringBuilder(256);
            int depth = 0;
            for (Exception e = ex; e != null && depth < 4; e = e.InnerException, depth++)
            {
                if (depth > 0)
                {
                    sb.Append(" <- ");
                }
                sb.Append(e.GetType().Name).Append(": ").Append(e.Message);
            }
            return sb.ToString();
        }

        // -- 列表 -------------------------------------------------------------

        private static MethodInfo StaticPrivate(Type t, string name, int paramCount)
        {
            if (t == null)
            {
                return null;
            }
            MethodInfo[] ms = t.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            for (int i = 0; i < ms.Length; i++)
            {
                if (ms[i].Name == name && ms[i].GetParameters().Length == paramCount)
                {
                    return ms[i];
                }
            }
            return null;
        }

        private static Type SavingType()
        {
            return BridgeState.FindType("SFS.Builds.Blueprint_Saving");
        }

        private static void CaptureList()
        {
            names.Clear();
            listError = "";

            // 注意：这里**不再**往玩家的蓝图目录投放任何东西。
            // 预设改为放在模组自己的 presets/ 下，由 BridgePreset 直接加载，
            // 免得污染玩家的蓝图列表。
            bundledError = CleanupOldBundled();

            Type t = SavingType();
            if (t == null)
            {
                listError = "SFS.Builds.Blueprint_Saving not found";
                return;
            }

            MethodInfo get = StaticPrivate(t, "GetBlueprintsList", 0);
            if (get == null)
            {
                listError = "GetBlueprintsList not found";
                return;
            }
            object list = get.Invoke(null, null);
            if (list == null)
            {
                listError = "GetBlueprintsList returned null";
                return;
            }

            object fileNames = BridgeState.Get(list, "fileNames");
            IEnumerable en = fileNames as IEnumerable;
            if (en == null)
            {
                listError = "OrderedPathList.fileNames unavailable";
                return;
            }
            foreach (object o in en)
            {
                string s = o as string;
                if (!string.IsNullOrEmpty(s))
                {
                    names.Add(s);
                }
            }

            // 游戏自己的列表可能是空的 —— GetBlueprintsList 依赖内部缓存，
            // 没进过 Load 对话框时它返回空，终端用户就会看到「一个蓝图都没有」。
            // 这种情况直接扫磁盘兜底。
            if (names.Count == 0)
            {
                ScanDiskFallback();
            }
            names.Sort(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 直接从磁盘找蓝图目录。
        ///
        /// 蓝图规范路径：&lt;游戏目录&gt;/Saving/Blueprints/&lt;名字&gt;/Blueprint.txt
        /// 这里只列目录名（游戏也只显示名字）。
        /// </summary>
        private static void ScanDiskFallback()
        {
            try
            {
                string root = GameRoot();
                if (string.IsNullOrEmpty(root))
                {
                    listError = "empty (and cannot locate game dir)";
                    return;
                }
                string dir = System.IO.Path.Combine(root, "Saving", "Blueprints");
                if (!System.IO.Directory.Exists(dir))
                {
                    listError = "empty; no such dir: " + dir;
                    return;
                }
                string[] subs = System.IO.Directory.GetDirectories(dir);
                for (int i = 0; i < subs.Length; i++)
                {
                    string name = System.IO.Path.GetFileName(subs[i]);
                    if (!string.IsNullOrEmpty(name))
                    {
                        names.Add(name);
                    }
                }
                if (names.Count == 0)
                {
                    listError = "empty; dir has no subfolders: " + dir;
                }
                else
                {
                    listError = "";   // 兜底成功，不算错误
                }
            }
            catch (Exception ex)
            {
                listError = "disk scan failed: " + ex.Message;
            }
        }

        /// <summary>内置蓝图在玩家蓝图列表里显示的名字。</summary>
        public const string BundledName = "示例火箭-基础火箭";

        /// <summary>
        /// 把随模组内置的示例蓝图投放到玩家的蓝图目录。
        ///
        /// 动机：新装的用户可能一枚蓝图都没有，「造个火箭」就无从下手。
        /// 投放后它会自然出现在 /blueprints 列表里，
        /// load_sfs_blueprint 也能直接加载 —— 全程走游戏自己的正统路径。
        ///
        /// 只在**文件不存在**时写入，绝不覆盖玩家自己的蓝图。
        /// 返回空串表示成功（含「已存在」的情况）。
        /// </summary>
        public static string EnsureBundled()
        {
            try
            {
                string root = GameDirFromAssembly();
                if (string.IsNullOrEmpty(root))
                {
                    return "cannot locate game dir";
                }

                string dir = System.IO.Path.Combine(
                    root, "Saving", "Blueprints", BundledName);
                string file = System.IO.Path.Combine(dir, "Blueprint.txt");

                if (System.IO.File.Exists(file))
                {
                    return "";      // 已经有了
                }

                string text = ReadEmbeddedTemplate();
                if (string.IsNullOrEmpty(text))
                {
                    return "embedded template missing";
                }

                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(file, text, new UTF8Encoding(false));
                return "";
            }
            catch (Exception ex)
            {
                return ex.GetType().Name + ": " + ex.Message;
            }
        }

        /// <summary>
        /// 从模组 DLL 的位置反推游戏根目录。
        ///
        /// DLL 在 &lt;game&gt;/Mods/SFS-Agent/SFS-Agent.dll，往上两级就是 &lt;game&gt;。
        /// 这条路径不依赖任何 Unity API，因此**任何线程都能安全调用**
        /// （相比 Application.dataPath 更适合在 HTTP 线程里用）。
        /// </summary>
        private static string GameDirFromAssembly()
        {
            try
            {
                string dll = typeof(BridgeBlueprint).Assembly.Location;
                string dir = System.IO.Path.GetDirectoryName(dll);
                if (string.IsNullOrEmpty(dir))
                {
                    return null;
                }
                System.IO.DirectoryInfo mods = System.IO.Directory.GetParent(dir);
                if (mods == null)
                {
                    return null;
                }
                System.IO.DirectoryInfo game = mods.Parent;
                return game == null ? null : game.FullName;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>读出编译进 DLL 的示例蓝图文本。</summary>
        private static string ReadEmbeddedTemplate()
        {
            try
            {
                Assembly asm = typeof(BridgeBlueprint).Assembly;
                string[] res = asm.GetManifestResourceNames();
                for (int i = 0; i < res.Length; i++)
                {
                    if (!res[i].EndsWith("example.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    System.IO.Stream st = asm.GetManifestResourceStream(res[i]);
                    if (st == null)
                    {
                        continue;
                    }
                    using (st)
                    {
                        System.IO.StreamReader rd =
                            new System.IO.StreamReader(st, Encoding.UTF8);
                        using (rd)
                        {
                            return rd.ReadToEnd();
                        }
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        /// <summary>
        /// 把已解析好的 Blueprint 对象生成到建造区。返回空串表示成功。
        ///
        /// 预设加载也走这里 —— 保证「先清空再生成」的语义和蓝图加载一致。
        /// SpawnBlueprint 是追加语义，不先清空会越堆越多（实测 172 -> 203）。
        /// </summary>
        public static string SpawnIntoBuild(object blueprint, string name)
        {
            try
            {
                Type bsType = BridgeState.FindType("SFS.Builds.BuildState");
                object buildState = BridgeState.GetStatic(bsType, "main");
                if (buildState == null)
                {
                    return "BuildState.main 为空（不在建造场景）";
                }

                ClearBuild(buildState);

                MethodInfo spawn = null;
                MethodInfo[] sm = bsType.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                for (int i = 0; i < sm.Length; i++)
                {
                    if (sm[i].Name == "SpawnBlueprint" && sm[i].GetParameters().Length == 3)
                    {
                        spawn = sm[i];
                        break;
                    }
                }
                if (spawn == null)
                {
                    return "BuildState.SpawnBlueprint not found";
                }

                object spawned = spawn.Invoke(buildState, new object[] { blueprint, false, null });
                Array arr = spawned as Array;
                int count = arr == null ? 0 : arr.Length;
                CenterCameraOnParts(buildState, arr);

                LoadResult = "已加载「" + name + "」，共 " + count + " 个零件";
                LoadError = "";
                return "";
            }
            catch (Exception ex)
            {
                return ex.GetType().Name + ": " + ex.Message;
            }
        }

        /// <summary>供 BridgePreset 复用（MakeLogger 本身是私有的）。</summary>
        public static object MakeLoggerPublic()
        {
            return MakeLogger();
        }

        /// <summary>
        /// 删掉早期版本误投放到玩家蓝图目录里的那份预设。
        ///
        /// v0.6.0 / v0.6.1 会把内置蓝图写进 Saving/Blueprints/，
        /// 现在改成模组自己的 presets/ 了，旧的留着只会让玩家困惑。
        /// 只在**内容与内嵌预设完全一致**时才删 —— 玩家改过就不动。
        /// </summary>
        private static string CleanupOldBundled()
        {
            try
            {
                string root = GameDirFromAssembly();
                if (string.IsNullOrEmpty(root))
                {
                    return "";
                }
                string[] legacy = new string[]
                {
                    "SFS-Agent 示例",
                    "示例火箭-基础火箭",
                };
                for (int i = 0; i < legacy.Length; i++)
                {
                    string dir = System.IO.Path.Combine(root, "Saving", "Blueprints", legacy[i]);
                    string file = System.IO.Path.Combine(dir, "Blueprint.txt");
                    if (!System.IO.File.Exists(file))
                    {
                        continue;
                    }
                    // 与任何一枚内嵌预设内容一致才认为是模组放的
                    bool ours = false;
                    try
                    {
                        string text = System.IO.File.ReadAllText(file, Encoding.UTF8);
                        string[] res = typeof(BridgeBlueprint).Assembly.GetManifestResourceNames();
                        for (int r = 0; r < res.Length; r++)
                        {
                            System.IO.Stream st =
                                typeof(BridgeBlueprint).Assembly.GetManifestResourceStream(res[r]);
                            if (st == null)
                            {
                                continue;
                            }
                            using (st)
                            {
                                System.IO.StreamReader rd =
                                    new System.IO.StreamReader(st, Encoding.UTF8);
                                using (rd)
                                {
                                    if (rd.ReadToEnd() == text)
                                    {
                                        ours = true;
                                    }
                                }
                            }
                            if (ours)
                            {
                                break;
                            }
                        }
                    }
                    catch
                    {
                    }
                    if (ours)
                    {
                        try
                        {
                            System.IO.Directory.Delete(dir, true);
                        }
                        catch
                        {
                        }
                    }
                }
                return "";
            }
            catch
            {
                return "";
            }
        }

        /// <summary>游戏安装根目录（UnityEngine.Application.dataPath 的上一级）。</summary>
        private static string GameRoot()
        {
            try
            {
                Type app = BridgeState.FindType("UnityEngine.Application");
                object dp = app == null ? null : BridgeState.GetStatic(app, "dataPath");
                string s = dp == null ? null : Convert.ToString(dp);
                if (string.IsNullOrEmpty(s))
                {
                    return null;
                }
                System.IO.DirectoryInfo parent = System.IO.Directory.GetParent(s);
                return parent == null ? null : parent.FullName;
            }
            catch
            {
                return null;
            }
        }

        public static string ListJson()
        {
            StringBuilder sb = new StringBuilder(1024);
            sb.Append("{\"ok\":").Append(listError.Length == 0 ? "true" : "false");
            sb.Append(",\"count\":").Append(names.Count);
            if (bundledError.Length > 0)
            {
                sb.Append(",\"bundled_error\":\"").Append(Esc(bundledError)).Append("\"");
            }
            sb.Append(",\"blueprints\":[");
            for (int i = 0; i < names.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(",");
                }
                sb.Append("\"").Append(Esc(names[i])).Append("\"");
            }
            sb.Append("]");
            if (listError.Length > 0)
            {
                sb.Append(",\"error\":\"").Append(Esc(listError)).Append("\"");
            }
            sb.Append("}");
            return sb.ToString();
        }

        // -- 加载 -------------------------------------------------------------

        private static void LoadByName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                LoadError = "缺少蓝图名";
                return;
            }

            Type saving = SavingType();
            if (saving == null)
            {
                LoadError = "SFS.Builds.Blueprint_Saving not found";
                return;
            }

            Type bpType = BridgeState.FindType("SFS.Builds.Blueprint");
            if (bpType == null)
            {
                LoadError = "SFS.Builds.Blueprint not found";
                return;
            }

            // 首选游戏自己的 LoadBlueprint(name, Action<Blueprint>)：
            // 它内部处理路径与解析，比我们自己拼 GetPath + TryLoad 可靠。
            MethodInfo loadMethod = StaticPrivate(saving, "LoadBlueprint", 2);
            if (loadMethod != null)
            {
                Delegate callback = MakeCallback(loadMethod.GetParameters()[1].ParameterType, bpType);
                if (callback != null)
                {
                    pending = null;
                    loadMethod.Invoke(null, new object[] { name, callback });
                    // 回调可能晚一帧触发，交给 Tick 收尾
                    waitingFor = name;
                    return;
                }
            }

            // 兜底：自己取路径再解析
            LoadViaTryLoad(name);
        }

        /// <summary>回退路径：Blueprint_Saving.GetPath + Blueprint.TryLoad。</summary>
        private static void LoadViaTryLoad(string name)
        {
            Type saving = SavingType();
            Type bpType = BridgeState.FindType("SFS.Builds.Blueprint");
            if (saving == null || bpType == null)
            {
                return;
            }

            object folder = null;
            MethodInfo getPath = StaticPrivate(saving, "GetPath", 1);
            if (getPath != null)
            {
                folder = getPath.Invoke(null, new object[] { name });
            }
            if (folder == null)
            {
                LoadError = "找不到蓝图「" + name + "」";
                return;
            }

            MethodInfo tryLoad = null;
            MethodInfo[] ms = bpType.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            for (int i = 0; i < ms.Length; i++)
            {
                if (ms[i].Name == "TryLoad" && ms[i].GetParameters().Length == 3)
                {
                    tryLoad = ms[i];
                    break;
                }
            }
            if (tryLoad == null)
            {
                LoadError = "Blueprint.TryLoad not found";
                return;
            }

            object[] args = new object[] { folder, MakeLogger(), null };
            object okObj = tryLoad.Invoke(null, args);
            object blueprint = args[2];
            bool ok = okObj is bool && (bool)okObj;
            if (!ok || blueprint == null)
            {
                LoadError = "解析蓝图失败（TryLoad 返回 " + (ok ? "true" : "false")
                    + "，路径 " + PathOf(folder) + "）";
                return;
            }
            Spawn(blueprint, name);
        }

        private static string PathOf(object folder)
        {
            try
            {
                string s = Convert.ToString(folder, CultureInfo.InvariantCulture);
                return s == null ? "?" : s;
            }
            catch
            {
                return "?";
            }
        }

        // -- 回调与生成 -------------------------------------------------------

        private static object pending;
        private static string waitingFor;
        private static int waitingFrames;

        /// <summary>
        /// 运行时构造 Action&lt;Blueprint&gt;。编译期拿不到 Blueprint 类型，
        /// 所以用表达式树包一层，把参数转成 object 再回调到我们的静态方法。
        /// </summary>
        private static Delegate MakeCallback(Type actionType, Type bpType)
        {
            try
            {
                MethodInfo handler = typeof(BridgeBlueprint).GetMethod(
                    "OnBlueprintLoaded", BindingFlags.Static | BindingFlags.NonPublic);
                ParameterExpression p = Expression.Parameter(bpType, "bp");
                Expression body = Expression.Call(handler, Expression.Convert(p, typeof(object)));
                return Expression.Lambda(actionType, body, new ParameterExpression[] { p }).Compile();
            }
            catch
            {
                return null;
            }
        }

        private static void OnBlueprintLoaded(object blueprint)
        {
            pending = blueprint;
        }

        private static void Spawn(object blueprint, string name)
        {
            Type bsType = BridgeState.FindType("SFS.Builds.BuildState");
            object buildState = BridgeState.GetStatic(bsType, "main");
            if (buildState == null)
            {
                LoadError = "BuildState.main 为空（不在建造场景）";
                return;
            }

            // 先清空建造区。
            // SpawnBlueprint 是**追加**语义 —— 不清空的话每加载一次就多堆一枚，
            // 实测连续两次加载让零件数从 172 涨到 203。
            // 游戏自己的 Load 按钮同样会先清。
            ClearBuild(buildState);

            MethodInfo spawn = null;
            MethodInfo[] sm = bsType.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            for (int i = 0; i < sm.Length; i++)
            {
                if (sm[i].Name == "SpawnBlueprint" && sm[i].GetParameters().Length == 3)
                {
                    spawn = sm[i];
                    break;
                }
            }
            if (spawn == null)
            {
                LoadError = "BuildState.SpawnBlueprint not found";
                return;
            }

            object spawned = spawn.Invoke(buildState, new object[] { blueprint, false, null });
            Array arr = spawned as Array;
            int count = arr == null ? 0 : arr.Length;
            CenterCameraOnParts(buildState, arr);

            LoadResult = "已加载蓝图「" + name + "」，共 " + count + " 个零件";
            LoadError = "";
        }

        /// <summary>
        /// 清空建造区里已有的零件。
        ///
        /// 必须做：BuildState.SpawnBlueprint 是追加语义，
        /// 不先清空的话连续加载会越堆越多（实测 172 -> 203）。
        /// Clear(bool) 的参数是「是否记入撤销历史」，自动加载传 false。
        /// </summary>
        private static void ClearBuild(object buildState)
        {
            try
            {
                MethodInfo[] ms = buildState.GetType().GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                for (int i = 0; i < ms.Length; i++)
                {
                    if (ms[i].Name != "Clear")
                    {
                        continue;
                    }
                    ParameterInfo[] ps = ms[i].GetParameters();
                    if (ps.Length == 1 && ps[0].ParameterType == typeof(bool))
                    {
                        ms[i].Invoke(buildState, new object[] { false });
                        return;
                    }
                }
            }
            catch
            {
                // 清空失败不该阻断加载，交给后续 SpawnBlueprint 报错
            }
        }

        /// <summary>
        /// 造一个 I_MsgLogger。传 null 会让游戏内部 NRE（实测确认），
        /// 所以优先用游戏自带的空日志器 SFS.MsgNone，退而求其次用 MsgDrawer.main。
        /// </summary>
        private static object MakeLogger()
        {
            try
            {
                Type none = BridgeState.FindType("SFS.MsgNone");
                if (none != null && !none.IsAbstract)
                {
                    return Activator.CreateInstance(none);
                }
                Type drawer = BridgeState.FindType("SFS.UI.MsgDrawer");
                object main = BridgeState.GetStatic(drawer, "main");
                if (main != null)
                {
                    return main;
                }
            }
            catch
            {
            }
            return null;
        }

        private static void CenterCameraOnParts(object buildState, Array parts)
        {
            try
            {
                if (parts == null || parts.Length == 0)
                {
                    return;
                }
                MethodInfo center = buildState.GetType().GetMethod(
                    "CenterCameraOnParts",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (center != null)
                {
                    center.Invoke(buildState, new object[] { parts });
                }
            }
            catch
            {
                // 相机没对上不算致命，零件已经生成了
            }
        }

        // -- 结果 -------------------------------------------------------------

        public static string LoadJson()
        {
            StringBuilder sb = new StringBuilder(256);
            sb.Append("{\"ok\":").Append(LoadError.Length == 0 && LoadResult.Length > 0 ? "true" : "false");
            if (LoadResult.Length > 0)
            {
                sb.Append(",\"result\":\"").Append(Esc(LoadResult)).Append("\"");
            }
            if (LoadError.Length > 0)
            {
                sb.Append(",\"error\":\"").Append(Esc(LoadError)).Append("\"");
            }
            sb.Append("}");
            return sb.ToString();
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
