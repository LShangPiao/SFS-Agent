// SFS-Agent — 视角控制（缩放 / 平移 / 旋转）
//
// 建造场景与飞行场景用的是两套相机：
//   建造：SFS.Builds.BuildCamera     { CameraPosition, CameraDistance }（属性，只写）
//   飞行：SFS.Cameras.CameraManager  { position, distance, rotation }（*_Local 包装器）
// 飞行时当前激活的相机从 SFS.Cameras.ActiveCamera.main.activeCamera 取。
//
// 全部在主线程执行。

using System;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace SfsAgent
{
    public static class BridgeCamera
    {
        private class Job
        {
            public double X = double.NaN;
            public double Y = double.NaN;
            public double Distance = double.NaN;
            public double ZoomDelta = double.NaN;
            public double Rotation = double.NaN;
        }

        private static readonly System.Collections.Generic.Queue<Job> Pending =
            new System.Collections.Generic.Queue<Job>();
        private static readonly object Gate = new object();

        public static string LastResult = "";
        public static string LastError = "";

        public static void Reset()
        {
            LastResult = "";
            LastError = "";
        }

        public static void Enqueue(double x, double y, double distance, double zoomDelta, double rotation)
        {
            Job job = new Job();
            job.X = x;
            job.Y = y;
            job.Distance = distance;
            job.ZoomDelta = zoomDelta;
            job.Rotation = rotation;
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
                Apply(job);
            }
            catch (Exception ex)
            {
                LastResult = "";
                LastError = ex.GetType().Name + ": " + ex.Message;
            }
        }

        private static void Apply(Job job)
        {
            StringBuilder info = new StringBuilder(96);

            // ---- 建造场景 ----
            Type bsType = BridgeState.FindType("SFS.Builds.BuildState");
            object buildState = BridgeState.GetStatic(bsType, "main");
            object buildCam = BridgeState.Get(buildState, "buildCamera");
            if (buildCam == null)
            {
                Type camType = BridgeState.FindType("SFS.Builds.BuildCamera");
                object[] found = BridgeState.FindObjects(camType);
                if (found.Length > 0)
                {
                    buildCam = found[0];
                }
            }
            if (buildCam != null)
            {
                bool touched = false;
                if (!double.IsNaN(job.X) || !double.IsNaN(job.Y))
                {
                    double cx = job.X;
                    double cy = job.Y;
                    if (!double.IsNaN(cx) && !double.IsNaN(cy))
                    {
                        object v = MakeVector2(cx, cy);
                        SetProperty(buildCam, "CameraPosition", v);
                        touched = true;
                    }
                }
                double dist = double.IsNaN(job.ZoomDelta)
                    ? job.Distance
                    : ReadDistance(buildCam) + job.ZoomDelta;
                if (!double.IsNaN(dist))
                {
                    SetProperty(buildCam, "CameraDistance", Coerce(dist, typeof(float)));
                    touched = true;
                }
                if (touched)
                {
                    info.Append("build");
                }
            }

            // ---- 飞行场景 ----
            Type acType = BridgeState.FindType("SFS.Cameras.ActiveCamera");
            object ac = BridgeState.GetStatic(acType, "main");
            object active = BridgeState.Unwrap(BridgeState.Get(ac, "activeCamera"));
            if (active != null)
            {
                if (info.Length > 0)
                {
                    info.Append("+");
                }
                info.Append("flight");
                if (!double.IsNaN(job.X) && !double.IsNaN(job.Y))
                {
                    SetLocalValue(active, "position", MakeVector2(job.X, job.Y));
                }
                double d = double.IsNaN(job.ZoomDelta)
                    ? job.Distance
                    : ReadLocalFloat(active, "distance") + job.ZoomDelta;
                if (!double.IsNaN(d))
                {
                    SetLocalValue(active, "distance", Coerce(d, typeof(float)));
                }
                if (!double.IsNaN(job.Rotation))
                {
                    SetLocalValue(active, "rotation", Coerce(job.Rotation, typeof(float)));
                }
            }

            if (info.Length == 0)
            {
                LastResult = "";
                LastError = "当前场景没有可控制的相机";
                return;
            }
            LastError = "";
            LastResult = "已调整相机（" + info + "）";
        }

        // -- 反射工具 ---------------------------------------------------------

        private static object MakeVector2(double x, double y)
        {
            Type v2 = BridgeState.FindType("UnityEngine.Vector2");
            if (v2 == null)
            {
                return null;
            }
            ConstructorInfo ctor = v2.GetConstructor(new Type[] { typeof(float), typeof(float) });
            return ctor == null ? null : ctor.Invoke(new object[] { (float)x, (float)y });
        }

        private static object Coerce(double v, Type t)
        {
            if (t == typeof(float))
            {
                return (float)v;
            }
            if (t == typeof(int))
            {
                return (int)v;
            }
            return Convert.ChangeType(v, t, CultureInfo.InvariantCulture);
        }

        private static void SetProperty(object obj, string name, object value)
        {
            if (obj == null || value == null)
            {
                return;
            }
            PropertyInfo p = obj.GetType().GetProperty(
                name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (p != null && p.CanWrite)
            {
                p.SetValue(obj, value, null);
            }
        }

        /// <summary>给 *_Local 包装器的 Value 赋值（字段或属性都兼容）。</summary>
        private static void SetLocalValue(object owner, string fieldName, object value)
        {
            object wrapper = BridgeState.Get(owner, fieldName);
            if (wrapper == null || value == null)
            {
                return;
            }
            Type t = wrapper.GetType();
            const BindingFlags F =
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            FieldInfo f = t.GetField("Value", F);
            if (f != null)
            {
                f.SetValue(wrapper, Coerce2(value, f.FieldType));
                return;
            }
            PropertyInfo p = t.GetProperty("Value", F);
            if (p != null && p.CanWrite)
            {
                p.SetValue(wrapper, Coerce2(value, p.PropertyType), null);
            }
        }

        private static object Coerce2(object value, Type target)
        {
            if (value == null || target.IsInstanceOfType(value))
            {
                return value;
            }
            try
            {
                if (target == typeof(float))
                {
                    return Convert.ToSingle(value, CultureInfo.InvariantCulture);
                }
                return Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
            }
            catch
            {
                return value;
            }
        }

        private static double ReadDistance(object buildCam)
        {
            return ReadLocalFloat(buildCam, "CameraDistance");
        }

        private static double ReadLocalFloat(object owner, string name)
        {
            object v = BridgeState.Get(owner, name);
            object value = BridgeState.Unwrap(v);
            if (value == null)
            {
                return double.NaN;
            }
            try
            {
                return Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return double.NaN;
            }
        }

        public static string ResultJson()
        {
            StringBuilder sb = new StringBuilder(192);
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
