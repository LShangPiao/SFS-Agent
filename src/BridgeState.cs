// SFS Agent — 游戏状态采集
//
// 全部通过反射读取，避免与游戏程序集产生编译期强耦合：
// 即便某个字段在具体游戏版本中改名，mod 也能正常加载并降级。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace SfsAgent
{
    public static class BridgeState
    {
        private const BindingFlags Flags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        public static bool inWorld;
        public static string rocketName = "";
        public static string planet = "";
        public static double height;
        public static double speed;
        public static double velX;
        public static double velY;
        public static double throttlePercent;
        public static bool throttleOn;
        public static int stageId = -1;
        public static bool hasControl;
        public static double mass;
        public static bool flying;
        public static string lastError = "";

        // ── 姿态与轨道 ──────────────────────────────────────────────────────
        // 角度来自 SFS.World.LocationDrawer.main.currentAngleInfo —— 游戏 HUD
        // 上显示的就是它；轨道由 SFS.World.Orbit.TryCreateOrbit 从飞船的
        // Location 算出来。取不到时保持 NaN，表示「这个数据现在没有意义」。

        /// <summary>火箭姿态角（度）。</summary>
        public static double angle = double.NaN;
        /// <summary>目标姿态角（度），无导航目标时与 angle 相同。</summary>
        public static double targetAngle = double.NaN;

        /// <summary>是否成功算出了轨道。</summary>
        public static bool hasOrbit;
        /// <summary>远点高度（米）。</summary>
        public static double orbitApoapsis = double.NaN;
        /// <summary>近点高度（米）。</summary>
        public static double orbitPeriapsis = double.NaN;
        /// <summary>离心率。</summary>
        public static double orbitEcc = double.NaN;
        /// <summary>轨道周期（秒）。</summary>
        public static double orbitPeriod = double.NaN;

        /// <summary>读取成员（字段或属性）。</summary>
        public static object Get(object obj, string name)
        {
            if (obj == null)
            {
                return null;
            }
            try
            {
                Type t = obj.GetType();
                FieldInfo f = t.GetField(name, Flags);
                if (f != null)
                {
                    return f.GetValue(obj);
                }
                PropertyInfo p = t.GetProperty(name, Flags);
                if (p != null && p.CanRead)
                {
                    return p.GetValue(obj, null);
                }
            }
            catch
            {
            }
            return null;
        }

        /// <summary>读取静态成员。</summary>
        public static object GetStatic(Type t, string name)
        {
            if (t == null)
            {
                return null;
            }
            try
            {
                FieldInfo f = t.GetField(name, Flags);
                if (f != null)
                {
                    return f.GetValue(null);
                }
                PropertyInfo p = t.GetProperty(name, Flags);
                if (p != null && p.CanRead)
                {
                    return p.GetValue(null, null);
                }
            }
            catch
            {
            }
            return null;
        }

        /// <summary>取出 SFS 的 *_Local 包装器里的真实值。</summary>
        public static object Unwrap(object wrapper)
        {
            if (wrapper == null)
            {
                return null;
            }
            object v = Get(wrapper, "Value");
            return v != null ? v : wrapper;
        }

        public static double ToDouble(object o, double fallback)
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

        private static string TypeByName(string fullName)
        {
            Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < asms.Length; i++)
            {
                try
                {
                    Type t = asms[i].GetType(fullName, false);
                    if (t != null)
                    {
                        return t.AssemblyQualifiedName;
                    }
                }
                catch
                {
                }
            }
            return null;
        }

        // 类型查找缓存。
        // FindType 会遍历所有已加载程序集，而 UI 构建/刷新时会被调用上百次，
        // 不缓存的话主线程开销非常可观（实测每帧刷新能把游戏拖到卡顿）。
        private static readonly Dictionary<string, Type> TypeCache =
            new Dictionary<string, Type>();
        private static readonly object TypeCacheGate = new object();

        public static Type FindType(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
            {
                return null;
            }
            lock (TypeCacheGate)
            {
                Type cached;
                if (TypeCache.TryGetValue(fullName, out cached))
                {
                    // 缓存里存 null 表示「确实找不到」，避免反复扫描
                    return cached;
                }
            }

            Type found = ScanForType(fullName);

            // 只缓存**找到**的。找不到很可能是「游戏程序集还没加载完」，
            // 缓存 null 会让后续永远查不到。
            if (found != null)
            {
                lock (TypeCacheGate)
                {
                    TypeCache[fullName] = found;
                }
            }
            return found;
        }

        private static Type ScanForType(string fullName)
        {
            Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < asms.Length; i++)
            {
                try
                {
                    Type t = asms[i].GetType(fullName, false);
                    if (t != null)
                    {
                        return t;
                    }
                }
                catch
                {
                }
            }
            return null;
        }

        /// <summary>
        /// 找出场景里所有某类型的对象（经 UnityEngine.Object.FindObjectsOfType）。
        /// 全场景查找必须在主线程调用。
        /// </summary>
        public static object[] FindObjects(Type type)
        {
            if (type == null)
            {
                return new object[0];
            }
            Type uo = FindType("UnityEngine.Object");
            if (uo == null)
            {
                return new object[0];
            }
            MethodInfo[] methods = uo.GetMethods(BindingFlags.Public | BindingFlags.Static);
            for (int j = 0; j < methods.Length; j++)
            {
                MethodInfo m = methods[j];
                if (m.Name != "FindObjectsOfType" && m.Name != "FindObjectsByType")
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
            return new object[0];
        }

        /// <summary>
        /// 当前帧号。
        ///
        /// 帧循环补丁同时挂在多个 Update 上，同一帧会被调用多次；而按键与点击
        /// 都必须严格按帧推进。这里返回一个**可比较**的帧号，各模块自己记住上次
        /// 处理过的帧号 —— 注意不能做成「消费式」的 NewFrame()，否则同一帧里
        /// 只有第一个模块能拿到 true，后面的模块会永远饿死。
        /// 优先用 UnityEngine.Time.frameCount；取不到时按 8ms 合成一个伪帧号。
        /// </summary>
        public static int CurrentFrame()
        {
            int fc = UnityFrameCount();
            if (fc >= 0)
            {
                return fc;
            }
            return (int)(DateTime.UtcNow.Ticks / (TimeSpan.TicksPerMillisecond * 8));
        }

        public static int UnityFrameCount()
        {
            Type t = FindType("UnityEngine.Time");
            if (t == null)
            {
                return -1;
            }
            object v = GetStatic(t, "frameCount");
            if (v == null)
            {
                return -1;
            }
            try
            {
                return Convert.ToInt32(v, CultureInfo.InvariantCulture);
            }
            catch
            {
                return -1;
            }
        }

        public static void Capture()
        {
            try
            {
                lastError = "";

                Type pcType = FindType("SFS.World.PlayerController");
                if (pcType == null)
                {
                    inWorld = false;
                    return;
                }

                object pc = GetStatic(pcType, "main");
                if (pc == null)
                {
                    inWorld = false;
                    return;
                }

                object playerLocal = Get(pc, "player");
                object player = Unwrap(playerLocal);
                if (player == null)
                {
                    inWorld = false;
                    return;
                }

                inWorld = true;
                hasControl = ToDouble(Unwrap(Get(player, "hasControl")), 0) > 0.5;

                // 位置 / 速度
                object loc = Get(player, "location");
                if (loc != null)
                {
                    height = ToDouble(Get(loc, "Height"), height);

                    object planetLocal = Get(loc, "planet");
                    object planetObj = Unwrap(planetLocal);
                    if (planetObj != null)
                    {
                        object pn = Get(planetObj, "planetName");
                        if (pn == null)
                        {
                            pn = Get(planetObj, "name");
                        }
                        if (pn != null)
                        {
                            planet = Convert.ToString(pn, CultureInfo.InvariantCulture);
                        }
                    }

                    object vel = Unwrap(Get(loc, "velocity"));
                    if (vel != null)
                    {
                        velX = ToDouble(Get(vel, "x"), 0);
                        velY = ToDouble(Get(vel, "y"), 0);
                        speed = Math.Sqrt(velX * velX + velY * velY);
                    }
                }

                // 火箭专属信息
                Type rocketType = FindType("SFS.World.Rocket");
                if (rocketType != null && rocketType.IsInstanceOfType(player))
                {
                    flying = true;

                    object rn = Get(player, "rocketName");
                    if (rn != null)
                    {
                        rocketName = Convert.ToString(rn, CultureInfo.InvariantCulture);
                    }

                    object throttle = Get(player, "throttle");
                    if (throttle != null)
                    {
                        throttlePercent = ToDouble(Unwrap(Get(throttle, "throttlePercent")), throttlePercent);
                        throttleOn = ToDouble(Unwrap(Get(throttle, "throttleOn")), 0) > 0.5;
                    }

                    object staging = Get(player, "staging");
                    if (staging != null)
                    {
                        object sid = Get(staging, "stageId");
                        if (sid != null)
                        {
                            stageId = (int)ToDouble(sid, stageId);
                        }
                    }

                    object massCalc = Get(player, "mass");
                    if (massCalc != null)
                    {
                        object total = Get(massCalc, "totalMass");
                        if (total == null)
                        {
                            total = Get(massCalc, "mass");
                        }
                        mass = ToDouble(total, mass);
                    }

                    CaptureAngle();
                    CaptureOrbit(player);
                }
                else
                {
                    flying = false;
                    hasOrbit = false;
                    angle = double.NaN;
                    targetAngle = double.NaN;
                }
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
            }
        }

        /// <summary>
        /// 读火箭姿态角。游戏 HUD 左下角那个角度就是它，
        /// 来源是 LocationDrawer.main.currentAngleInfo。
        /// </summary>
        private static void CaptureAngle()
        {
            try
            {
                Type drawerType = FindType("SFS.World.LocationDrawer");
                if (drawerType == null)
                {
                    return;
                }
                object drawer = GetStatic(drawerType, "main");
                if (drawer == null)
                {
                    return;
                }
                object info = Get(drawer, "currentAngleInfo");
                if (info == null)
                {
                    angle = double.NaN;
                    targetAngle = double.NaN;
                    return;
                }
                object a = Get(info, "angle");
                if (a != null)
                {
                    double d = ToDouble(a, double.NaN);
                    // 弧度转角度（游戏内部用弧度）
                    angle = double.IsNaN(d) ? double.NaN : d * 180.0 / Math.PI;
                }
                object ta = Get(info, "targetAngle");
                if (ta != null)
                {
                    double d = ToDouble(ta, double.NaN);
                    targetAngle = double.IsNaN(d) ? double.NaN : d * 180.0 / Math.PI;
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// 用飞船当前的位置速度算轨道。游戏自己的地图界面也是这么算的。
        /// 不在轨道上（还在地面/大气里）时 hasOrbit 为 false。
        /// </summary>
        private static void CaptureOrbit(object player)
        {
            hasOrbit = false;
            try
            {
                Type orbitType = FindType("SFS.World.Orbit");
                Type locType = FindType("SFS.World.Location");
                if (orbitType == null || locType == null)
                {
                    return;
                }

                object loc = Get(player, "location");
                if (loc == null || !locType.IsInstanceOfType(loc))
                {
                    return;
                }

                // TryCreateOrbit(Location, bool, bool, out bool)
                MethodInfo best = null;
                MethodInfo[] ms = orbitType.GetMethods(
                    BindingFlags.Public | BindingFlags.Static);
                for (int i = 0; i < ms.Length; i++)
                {
                    if (ms[i].Name != "TryCreateOrbit")
                    {
                        continue;
                    }
                    ParameterInfo[] ps = ms[i].GetParameters();
                    if (ps.Length == 4 && ps[0].ParameterType == locType)
                    {
                        best = ms[i];
                        break;
                    }
                }
                if (best == null)
                {
                    return;
                }

                object[] args = new object[] { loc, false, false, false };
                object orbit = best.Invoke(null, args);

                // out 参数（第 4 个）说明算没算出来
                if (args[3] is bool && !(bool)args[3])
                {
                    return;
                }
                if (orbit == null)
                {
                    return;
                }

                orbitApoapsis = ToDouble(Get(orbit, "apoapsis"), double.NaN);
                orbitPeriapsis = ToDouble(Get(orbit, "periapsis"), double.NaN);
                orbitEcc = ToDouble(Get(orbit, "ecc"), double.NaN);
                orbitPeriod = ToDouble(Get(orbit, "period"), double.NaN);
                hasOrbit = !double.IsNaN(orbitApoapsis);
            }
            catch
            {
                // 算不出来不算错误：火箭还在地面上时本来就没有轨道
            }
        }

        private static string Num(double v)
        {
            // NaN / Infinity 不是合法 JSON —— 直接输出会让解析器报错。
            // 用 null 表示「这个数据现在没有意义」。
            if (double.IsNaN(v) || double.IsInfinity(v))
            {
                return "null";
            }
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string Str(string s)
        {
            if (s == null)
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

        public static string ToJson()
        {
            StringBuilder sb = new StringBuilder(256);
            sb.Append("{");
            sb.Append("\"ok\":true");
            sb.Append(",\"in_world\":").Append(inWorld ? "true" : "false");
            sb.Append(",\"flying\":").Append(flying ? "true" : "false");
            sb.Append(",\"rocket\":\"").Append(Str(rocketName)).Append("\"");
            sb.Append(",\"planet\":\"").Append(Str(planet)).Append("\"");
            sb.Append(",\"height\":").Append(Num(height));
            sb.Append(",\"speed\":").Append(Num(speed));
            sb.Append(",\"velocity_x\":").Append(Num(velX));
            sb.Append(",\"velocity_y\":").Append(Num(velY));
            sb.Append(",\"throttle\":").Append(Num(throttlePercent));
            sb.Append(",\"throttle_on\":").Append(throttleOn ? "true" : "false");
            sb.Append(",\"stage\":").Append(stageId);
            sb.Append(",\"has_control\":").Append(hasControl ? "true" : "false");
            sb.Append(",\"mass\":").Append(Num(mass));

            // 姿态与轨道
            sb.Append(",\"angle\":").Append(Num(angle));
            sb.Append(",\"target_angle\":").Append(Num(targetAngle));
            sb.Append(",\"has_orbit\":").Append(hasOrbit ? "true" : "false");
            if (hasOrbit)
            {
                sb.Append(",\"orbit\":{");
                sb.Append("\"apoapsis\":").Append(Num(orbitApoapsis));
                sb.Append(",\"periapsis\":").Append(Num(orbitPeriapsis));
                sb.Append(",\"eccentricity\":").Append(Num(orbitEcc));
                sb.Append(",\"period\":").Append(Num(orbitPeriod));
                sb.Append("}");
            }

            if (lastError.Length > 0)
            {
                sb.Append(",\"error\":\"").Append(Str(lastError)).Append("\"");
            }
            sb.Append("}");
            return sb.ToString();
        }
    }
}
