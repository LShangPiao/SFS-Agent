// SFS-Agent — 预设蓝图
//
// 官方示例火箭以「预设」形式提供，**不放进玩家的蓝图目录**。
//
// 为什么：
//   1. 玩家的蓝图列表是自己的东西，不该被模组塞进去的条目污染
//   2. 预设随模组更新，放在玩家目录里会被误改、误删，也没法纠正
//   3. 预设不该出现在游戏的 Load 列表里
//
// 做法：内嵌的预设释放到**模组自己的目录**
//
//   <游戏目录>/Mods/SFS-Agent/presets/<名字>/Blueprint.txt
//
// 用 IFolder 的实现 DefaultFolder(string path, DefaultFolder parent) 把该目录
// 包起来，交给游戏自己的 Blueprint.TryLoad 解析 —— 尺寸、纹理、分级全由游戏处理。
//
// 线程约束（踩过坑，游戏直接崩）：
//   SpawnBlueprint 会创建 Unity Mesh（PartsLoader -> PipeMesh -> new Mesh()），
//   在 HTTP 后台线程调用会让游戏崩溃。所以：
//     HTTP 线程 -> EnqueueLoad() 入队
//     主线程    -> Tick() 真正执行
//   调用方轮询 QueueLength 归零后读 ResultJson()。

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace SfsAgent
{
    public static class BridgePreset
    {
        /// <summary>预设显示名（给玩家和猫娘看）。</summary>
        private static readonly string[] Names =
        {
            "\u57fa\u7840\u706b\u7bad",              // 基础火箭
            "\u4e8c\u7ea7\u706b\u7bad",              // 二级火箭
            "\u4e09\u7ea7\u706b\u7bad",              // 三级火箭
        };

        /// <summary>内嵌资源名（ASCII，避免资源名编码问题）。</summary>
        private static readonly string[] Resources =
        {
            "preset1.txt",
            "preset2.txt",
            "preset3.txt",
        };

        private static readonly object Gate = new object();
        private static readonly Queue<string> Pending = new Queue<string>();
        private static string lastResult = "";
        private static string lastError = "";

        /// <summary>
        /// 有活没干完。
        /// 注意不能只看 QueueLength —— Tick 一出队它就归零了，
        /// 而真正耗时的 SpawnBlueprint 还在后面。HTTP 线程要等这个。
        /// </summary>
        private static volatile bool busy;

        // -- HTTP 线程 ---------------------------------------------------------

        /// <summary>排队加载一个预设。真正的加载在主线程 Tick 里做。</summary>
        public static void EnqueueLoad(string name)
        {
            lock (Gate)
            {
                lastResult = "";
                lastError = "";
                busy = true;
                Pending.Enqueue(name == null ? "" : name.Trim());
            }
        }

        /// <summary>是否还在处理（HTTP 线程轮询用）。</summary>
        public static bool Busy
        {
            get
            {
                return busy;
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

        /// <summary>读结果（HTTP 线程，等 QueueLength 归零后调用）。</summary>
        public static string ResultJson()
        {
            if (lastError.Length > 0)
            {
                return "{\"ok\":false,\"error\":\"" + Esc(lastError) + "\"}";
            }
            return "{\"ok\":true,\"result\":\"" + Esc(lastResult) + "\"}";
        }

        // -- 主线程 -----------------------------------------------------------

        /// <summary>在帧循环里调用（主线程）。</summary>
        public static void Tick()
        {
            string name;
            lock (Gate)
            {
                if (Pending.Count == 0)
                {
                    return;
                }
                name = Pending.Dequeue();
            }

            string msg;
            bool ok = LoadOnMain(name, out msg);
            if (ok)
            {
                lastResult = msg;
                lastError = "";
            }
            else
            {
                lastResult = "";
                lastError = msg;
            }
            busy = false;
        }

        // -- 列表 -------------------------------------------------------------

        public static string ListJson()
        {
            string err = EnsureExtracted();
            StringBuilder sb = new StringBuilder(256);
            sb.Append("{\"ok\":").Append(err.Length == 0 ? "true" : "false");
            sb.Append(",\"count\":").Append(Names.Length);
            sb.Append(",\"presets\":[");
            for (int i = 0; i < Names.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(",");
                }
                sb.Append("\"").Append(Esc(Names[i])).Append("\"");
            }
            sb.Append("]");
            sb.Append(",\"note\":\"");
            sb.Append("\u8fd9\u4e9b\u662f\u6a21\u7ec4\u81ea\u5e26\u7684\u9884\u8bbe\u84dd\u56fe\uff0c");
            sb.Append("\u4e0d\u4f1a\u8fdb\u5165\u73a9\u5bb6\u7684\u84dd\u56fe\u5217\u8868\u3002");
            sb.Append("\u7528 preset_load \u76f4\u63a5\u52a0\u8f7d\u5230\u5efa\u9020\u9875\u9762\u3002\"");
            if (err.Length > 0)
            {
                sb.Append(",\"error\":\"").Append(Esc(err)).Append("\"");
            }
            sb.Append("}");
            return sb.ToString();
        }

        // -- 释放 -------------------------------------------------------------

        /// <summary>把内嵌预设释放到模组目录。已存在且内容一致就跳过。</summary>
        public static string EnsureExtracted()
        {
            try
            {
                string root = ModDir();
                if (string.IsNullOrEmpty(root))
                {
                    return "cannot locate mod dir";
                }
                for (int i = 0; i < Names.Length; i++)
                {
                    string text = Embedded(Resources[i]);
                    if (string.IsNullOrEmpty(text))
                    {
                        continue;
                    }
                    string dir = System.IO.Path.Combine(root, "presets", Names[i]);
                    string file = System.IO.Path.Combine(dir, "Blueprint.txt");
                    if (System.IO.File.Exists(file))
                    {
                        try
                        {
                            if (System.IO.File.ReadAllText(file, Encoding.UTF8) == text)
                            {
                                continue;
                            }
                        }
                        catch
                        {
                        }
                    }
                    System.IO.Directory.CreateDirectory(dir);
                    System.IO.File.WriteAllText(file, text, new UTF8Encoding(false));
                }
                return "";
            }
            catch (Exception ex)
            {
                return ex.GetType().Name + ": " + ex.Message;
            }
        }

        private static string ModDir()
        {
            try
            {
                return System.IO.Path.GetDirectoryName(typeof(BridgePreset).Assembly.Location);
            }
            catch
            {
                return null;
            }
        }

        private static string Embedded(string resourceName)
        {
            try
            {
                Assembly asm = typeof(BridgePreset).Assembly;
                string[] res = asm.GetManifestResourceNames();
                for (int i = 0; i < res.Length; i++)
                {
                    if (!res[i].EndsWith(resourceName, StringComparison.OrdinalIgnoreCase))
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
                        System.IO.StreamReader rd = new System.IO.StreamReader(st, Encoding.UTF8);
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

        // -- 实际加载（主线程）-------------------------------------------------

        private static bool LoadOnMain(string name, out string message)
        {
            message = "";
            int idx = IndexOf(name);
            if (idx < 0)
            {
                message = "没有这个预设：「" + name + "」。可用：" + string.Join("、", Names);
                return false;
            }

            string err = EnsureExtracted();
            if (err.Length > 0)
            {
                message = "释放预设失败：" + err;
                return false;
            }

            string dir = System.IO.Path.Combine(ModDir(), "presets", Names[idx]);
            string file = System.IO.Path.Combine(dir, "Blueprint.txt");
            if (!System.IO.File.Exists(file))
            {
                message = "预设文件缺失：" + dir;
                return false;
            }

            object blueprint;
            string parseErr = Parse(dir, out blueprint);
            if (blueprint == null)
            {
                message = "解析预设失败：" + parseErr;
                return false;
            }

            string spawnErr = BridgeBlueprint.SpawnIntoBuild(blueprint, Names[idx]);
            if (spawnErr.Length > 0)
            {
                message = spawnErr;
                return false;
            }

            message = "已加载预设「" + Names[idx] + "」";
            return true;
        }

        private static int IndexOf(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return -1;
            }
            string want = name.Trim();
            for (int i = 0; i < Names.Length; i++)
            {
                if (string.Equals(Names[i], want, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
            for (int i = 0; i < Names.Length; i++)
            {
                if (Names[i].IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>用 DefaultFolder 包成 IFolder，再交给游戏自己的 Blueprint.TryLoad。</summary>
        private static string Parse(string dir, out object blueprint)
        {
            blueprint = null;
            try
            {
                // IFolder 的实现是 DefaultFolder(string path, DefaultFolder parent)，
                // 而且它在 Assembly-CSharp-firstpass 里 —— 不在 Assembly-CSharp。
                // （SFS.IO.FolderPath 看着像但不是，传进去会抛
                //   "Object of type 'SFS.IO.FolderPath' cannot be converted to type IFolder"）
                Type fpType = FindTypeAnywhere("DefaultFolder");
                if (fpType == null)
                {
                    return "DefaultFolder (IFolder impl) not found";
                }
                object folder = Activator.CreateInstance(fpType, new object[] { dir, null });
                if (folder == null)
                {
                    return "DefaultFolder ctor returned null";
                }

                Type bpType = BridgeState.FindType("SFS.Builds.Blueprint");
                if (bpType == null)
                {
                    return "SFS.Builds.Blueprint not found";
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
                    return "Blueprint.TryLoad not found";
                }

                object[] args = new object[] { folder, BridgeBlueprint.MakeLoggerPublic(), null };
                object okObj = tryLoad.Invoke(null, args);
                blueprint = args[2];
                bool ok = okObj is bool && (bool)okObj;
                if (!ok || blueprint == null)
                {
                    return "TryLoad 返回 " + (ok ? "true" : "false");
                }
                return "";
            }
            catch (Exception ex)
            {
                return ex.GetType().Name + ": " + ex.Message;
            }
        }

        /// <summary>跨程序集找类型（DefaultFolder 在 Assembly-CSharp-firstpass）。</summary>
        private static Type FindTypeAnywhere(string name)
        {
            Type t = BridgeState.FindType(name);
            if (t != null)
            {
                return t;
            }
            try
            {
                Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < asms.Length; i++)
                {
                    try
                    {
                        t = asms[i].GetType(name);
                        if (t != null)
                        {
                            return t;
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
            return null;
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
