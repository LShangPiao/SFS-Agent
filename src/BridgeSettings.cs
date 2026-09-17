// SFS-Agent — 游戏设置读写（音量 / 画面 / 视角）
//
// 重要：SFS 的设置类结构是
//     SFS.VideoSettings : SettingsBase<VideoSettings.Data>
//     SFS.Audio.AudioSettings : SettingsBase<AudioSettings.Data>
// 其中 **真实数据在基类的 `settings` 字段**（一个 Data 对象）：
//
//     VideoSettings.Data { freeOrientation, fps, menuScale, menuOpacity,
//                          cameraShake, orbitLinesCount, FXAA }
//     AudioSettings.Data { soundVolume, musicVolume }
//
// 那些 `soundVolume` / `menuScale` 同名的 *_Local 字段只是 UI 绑定的镜像，
// 读写它们不会真正改变游戏行为（实测：写进去能读到，但声音没变）。
// 因此这里统一走 `settings` 里的 Data 字段，改完再调 SettingsBase.Save() 落盘。
//
// 全部在主线程执行；不引任何 UnityEngine 程序集。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace SfsAgent
{
    public static class BridgeSettings
    {
        private class Job
        {
            public string Key;
            public double Value;
        }

        private static readonly Queue<Job> Pending = new Queue<Job>();
        private static readonly object Gate = new object();

        public static string LastResult = "";
        public static string LastError = "";

        // key  ->  { 设置类, Data 字段名, 类型, 最小值, 最大值, 说明 }
        //
        // 注意用的是 **VideoSettingsPC** 而不是 VideoSettings：
        // SFS 有两套视频设置，PC 版走 VideoSettingsPC（Data 里有 uiScale / fps /
        // windowMode 等），通用的 VideoSettings 在 PC 上根本不会被实例化
        // （实测 main 一直为 null）。音频则只有一套 AudioSettings。
        private static readonly string[][] Map = new string[][]
        {
            new string[] { "sound_volume",    "SFS.Audio.AudioSettings", "soundVolume",     "float", "0",   "1",   "音效音量" },
            new string[] { "music_volume",    "SFS.Audio.AudioSettings", "musicVolume",     "float", "0",   "1",   "音乐音量" },
            new string[] { "ui_scale",        "SFS.VideoSettingsPC",     "uiScale",         "float", "0.5", "2",   "界面缩放" },
            new string[] { "ui_opacity",      "SFS.VideoSettingsPC",     "uiOpacity",       "float", "0",   "1",   "界面不透明度" },
            new string[] { "camera_shake",    "SFS.VideoSettingsPC",     "cameraShake",     "bool",  "0",   "1",   "镜头抖动" },
            new string[] { "fxaa",            "SFS.VideoSettingsPC",     "FXAA",            "bool",  "0",   "1",   "抗锯齿 FXAA" },
            new string[] { "orbit_lines",     "SFS.VideoSettingsPC",     "orbitLinesCount", "int",   "0",   "100", "轨道线数量" },
            new string[] { "fps",             "SFS.VideoSettingsPC",     "fps",             "int",   "30",  "240", "帧率上限" },
            new string[] { "vertical_sync",   "SFS.VideoSettingsPC",     "verticalSync",    "int",   "0",   "4",   "垂直同步（0 关 / 1 开 / 2 自适应）" },
            new string[] { "window_mode",     "SFS.VideoSettingsPC",     "windowMode",      "int",   "0",   "3",   "窗口模式（枚举值，改前建议记下原值）" },
        };

        public static void Reset()
        {
            LastResult = "";
            LastError = "";
        }

        public static void Enqueue(string key, double value)
        {
            Job job = new Job();
            job.Key = key;
            job.Value = value;
            lock (Gate)
            {
                Pending.Enqueue(job);
            }
        }

        // -- 主线程 -----------------------------------------------------------

        public static void Tick()
        {
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
                LastResult = Apply(job);
                LastError = "";
            }
            catch (Exception ex)
            {
                LastResult = "";
                LastError = Describe(ex);
            }
        }

        private static string Describe(Exception ex)
        {
            StringBuilder sb = new StringBuilder(160);
            int d = 0;
            for (Exception e = ex; e != null && d < 3; e = e.InnerException, d++)
            {
                if (d > 0)
                {
                    sb.Append(" <- ");
                }
                sb.Append(e.GetType().Name).Append(": ").Append(e.Message);
            }
            return sb.ToString();
        }

        // -- 反射 -------------------------------------------------------------

        private static string[] Find(string key)
        {
            for (int i = 0; i < Map.Length; i++)
            {
                if (Map[i][0] == key)
                {
                    return Map[i];
                }
            }
            return null;
        }

        /// <summary>取设置类基类里的 `settings`（Data 对象）。</summary>
        private static object DataOf(string typeName)
        {
            Type t = BridgeState.FindType(typeName);
            if (t == null)
            {
                return null;
            }
            object main = BridgeState.GetStatic(t, "main");
            return main == null ? null : BridgeState.Get(main, "settings");
        }

        private static void CallSave(string typeName)
        {
            try
            {
                Type t = BridgeState.FindType(typeName);
                object main = t == null ? null : BridgeState.GetStatic(t, "main");
                if (main == null)
                {
                    return;
                }
                MethodInfo m = main.GetType().GetMethod(
                    "Save",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null, Type.EmptyTypes, null);
                if (m != null)
                {
                    m.Invoke(main, null);
                }
            }
            catch
            {
                // 存不了不算致命，值已经改了
            }
        }

        private static string Apply(Job job)
        {
            string[] info = Find(job.Key);
            if (info == null)
            {
                throw new Exception("未知设置项：" + job.Key
                    + "（GET /settings 可以看到全部可写项）");
            }

            string typeName = info[1];
            string field = info[2];
            string kind = info[3];
            double lo = double.Parse(info[4], CultureInfo.InvariantCulture);
            double hi = double.Parse(info[5], CultureInfo.InvariantCulture);
            string label = info[6];

            if (job.Value < lo || job.Value > hi)
            {
                throw new Exception(label + "需要在 " + info[4] + "-" + info[5] + " 之间");
            }

            object data = DataOf(typeName);
            if (data == null)
            {
                throw new Exception(typeName + " 尚未初始化（进一次游戏设置界面即可）");
            }

            FieldInfo f = data.GetType().GetField(
                field,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null)
            {
                throw new Exception("找不到字段 " + data.GetType().Name + "." + field);
            }

            f.SetValue(data, Coerce(job.Value, f.FieldType, kind));
            CallSave(typeName);

            if (kind == "bool")
            {
                return "已" + (job.Value > 0.5 ? "开启" : "关闭") + label;
            }
            if (kind == "int")
            {
                return "已设置" + label + "为 " + ((int)job.Value).ToString(CultureInfo.InvariantCulture);
            }
            return "已设置" + label + "为 " + Num(job.Value);
        }

        private static object Coerce(double v, Type target, string kind)
        {
            if (target == typeof(bool))
            {
                return v > 0.5;
            }
            if (target == typeof(float))
            {
                return (float)v;
            }
            if (target == typeof(int))
            {
                return (int)v;
            }
            if (target == typeof(double))
            {
                return v;
            }
            return Convert.ChangeType(v, target, CultureInfo.InvariantCulture);
        }

        // -- 读取 -------------------------------------------------------------

        public static string ToJson()
        {
            StringBuilder sb = new StringBuilder(1400);
            sb.Append("{\"ok\":true,\"settings\":[");

            // 按设置类缓存 Data 对象，避免每项都反射一次
            Dictionary<string, object> cache = new Dictionary<string, object>();
            bool anyMissing = false;
            bool first = true;

            for (int i = 0; i < Map.Length; i++)
            {
                string[] info = Map[i];
                object data;
                if (!cache.TryGetValue(info[1], out data))
                {
                    data = DataOf(info[1]);
                    cache[info[1]] = data;
                }
                if (data == null)
                {
                    anyMissing = true;
                    continue;
                }
                FieldInfo f = data.GetType().GetField(
                    info[2],
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f == null)
                {
                    continue;
                }
                object v = null;
                try
                {
                    v = f.GetValue(data);
                }
                catch
                {
                    continue;
                }
                if (!first)
                {
                    sb.Append(",");
                }
                first = false;
                sb.Append("{\"key\":\"").Append(info[0]).Append("\",\"value\":");
                if (info[3] == "bool")
                {
                    sb.Append(v is bool && (bool)v ? "true" : "false");
                }
                else if (info[3] == "int")
                {
                    sb.Append(Convert.ToInt32(v, CultureInfo.InvariantCulture));
                }
                else
                {
                    sb.Append(Num(Convert.ToDouble(v, CultureInfo.InvariantCulture)));
                }
                sb.Append(",\"type\":\"").Append(info[3])
                  .Append("\",\"range\":\"").Append(info[4]).Append("-").Append(info[5])
                  .Append("\",\"desc\":\"").Append(Esc(info[6])).Append("\"}");
            }

            sb.Append("]");
            if (anyMissing)
            {
                sb.Append(",\"note\":\"部分设置类尚未初始化，进一次游戏的设置界面即可\"");
            }
            sb.Append("}");
            return sb.ToString();
        }

        // -- 工具 -------------------------------------------------------------

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
            if (LastResult.Length > 0)
            {
                sb.Append(",\"result\":\"").Append(Esc(LastResult)).Append("\"");
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
