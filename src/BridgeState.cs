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
        /// <summary>目标姿态角（度），无导航目标时为 NaN。</summary>
        public static double targetAngle = double.NaN;
        /// <summary>速度方向角（度）。0° = 水平向前，90° = 垂直向上。</summary>
        public static double flightPathAngle = double.NaN;
        /// <summary>攻角（度）：火箭朝向与速度方向的夹角。这才是航天上说的「倾角」。</summary>
        public static double pitchAngle = double.NaN;

        /// <summary>是否成功算出了轨道。</summary>
        public static bool hasOrbit;
        /// <summary>当前真近点角（度）。</summary>
        public static double trueAnomaly = double.NaN;
        /// <summary>到下一次过近点还有多久（秒）。</summary>
        public static double timeToPeriapsis = double.NaN;
        /// <summary>到下一次过远点还有多久（秒）。</summary>
        public static double timeToApoapsis = double.NaN;

        /// <summary>轨道算不出来时的原因（诊断用）。</summary>
        public static string orbitFailReason = "";

        /// <summary>记下轨道失败的原因（只记第一条，避免刷屏）。</summary>
        private static void Note(string why)
        {
            if (orbitFailReason.Length == 0)
            {
                orbitFailReason = why;
            }
        }
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

                    CaptureAngle(player);
                    CaptureOrbit(player);
                }
                else
                {
                    flying = false;
                    hasOrbit = false;
                    angle = double.NaN;
                    targetAngle = double.NaN;
                    flightPathAngle = double.NaN;
                    pitchAngle = double.NaN;
                    trueAnomaly = double.NaN;
                    timeToPeriapsis = double.NaN;
                    timeToApoapsis = double.NaN;
                }
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
            }
        }

        /// <summary>
        /// 读火箭姿态。
        ///
        /// **不用** LocationDrawer.currentAngleInfo —— 实测它在飞行场景里一直是 0
        /// （那个组件只在地图/追踪视图下更新）。改成自己算：
        ///
        ///   angle       = 火箭朝向（Rigidbody2D.rotation），0° = 机头朝上
        ///   flightPath  = 速度方向（由 velocity 分量 atan2 得到）
        ///   pitch       = 攻角，即朝向与速度方向的夹角 —— 这才是航天里说的「倾角」
        /// </summary>
        private static void CaptureAngle(object player)
        {
            try
            {
                // ① 火箭朝向：Rigidbody2D.rotation（度）
                object rb = Get(player, "rb2d");
                if (rb != null)
                {
                    object rot = Get(rb, "rotation");
                    if (rot != null)
                    {
                        angle = Normalize(ToDouble(rot, double.NaN));
                    }
                }

                // ② 速度方向角：atan2(vy, vx)，转成「0° = 朝上」的习惯
                if (!double.IsNaN(velX) || !double.IsNaN(velY))
                {
                    double sp = Math.Sqrt(velX * velX + velY * velY);
                    if (sp > 0.5)
                    {
                        // 屏幕坐标系里 y 向上，所以朝上 = 90°
                        double dir = Math.Atan2(velY, velX) * 180.0 / Math.PI;
                        flightPathAngle = Normalize(dir - 90.0);
                        // 攻角：朝向与速度方向的夹角
                        pitchAngle = Normalize(angle - flightPathAngle);
                    }
                    else
                    {
                        flightPathAngle = double.NaN;
                        pitchAngle = double.NaN;
                    }
                }

                // ③ 顺带读一下导航目标角（有的话）
                Type drawerType = FindType("SFS.World.LocationDrawer");
                if (drawerType != null)
                {
                    object drawer = GetStatic(drawerType, "main");
                    object info = drawer == null ? null : Get(drawer, "currentAngleInfo");
                    if (info != null)
                    {
                        object ta = Get(info, "targetAngle");
                        if (ta != null)
                        {
                            double d = ToDouble(ta, double.NaN);
                            // 只在它非零时采信（零说明这个组件没在工作）
                            if (!double.IsNaN(d) && Math.Abs(d) > 0.0001)
                            {
                                targetAngle = Normalize(d * 180.0 / Math.PI);
                            }
                        }
                    }
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// 算到近点 / 到远点的时间。
        ///
        /// 用游戏自己的 Orbit.GetNextTrueAnomalyPassTime(target, start)：
        /// 近点 = 真近点角 0，远点 = π。拿不到就保持 NaN（页面显示 —）。
        /// </summary>
        private static void CaptureApsisTimes(object orbit, object player)
        {
            try
            {
                // 当前时刻（从玩家位置取）
                object loc = Get(player, "location");
                double now = double.NaN;
                if (loc != null)
                {
                    object t = Get(loc, "time");
                    if (t == null)
                    {
                        object v = Get(loc, "Value");
                        t = v == null ? null : Get(v, "time");
                    }
                    now = ToDouble(t, double.NaN);
                }
                if (double.IsNaN(now))
                {
                    Note("取时间失败：location=" + (loc == null ? "null" : loc.GetType().Name));
                    return;
                }

                MethodInfo tan = null;
                MethodInfo[] ms = orbit.GetType().GetMethods(
                    BindingFlags.Public | BindingFlags.Instance);
                for (int i = 0; i < ms.Length; i++)
                {
                    if (ms[i].Name == "GetTrueAnomaly" && ms[i].GetParameters().Length == 1)
                    {
                        tan = ms[i];
                    }
                
                }

                if (tan != null)
                {
                    double a = ToDouble(tan.Invoke(orbit, new object[] { now }), double.NaN);
                    if (!double.IsNaN(a))
                    {
                        trueAnomaly = Normalize(a * 180.0 / Math.PI);
                    }
                }

                // 到近点 / 到远点的时间，用「真近点角 + 周期」推算。
                //
                // 游戏自己的 GetNextTrueAnomalyPassTime 在飞行场景里一直返回 NaN
                // （实测），所以改成自己算——反正真近点角和周期都已经有了。
                //
                //   真近点角每秒变化 = 2π / T
                //   到近点（ν=0）  = (2π - ν) / 角速度   （ν>0）或 (-ν) / 角速度
                //   到远点（ν=π）  同理。
                if (!double.IsNaN(trueAnomaly) && !double.IsNaN(orbitPeriod) && orbitPeriod > 0)
                {
                    double nu = trueAnomaly;                    // 已归一化到 (-180,180]
                    double rate = 360.0 / orbitPeriod;          // 度/秒

                    // 到近点：真近点角回到 0
                    double dPeri = nu >= 0 ? (360.0 - nu) : (-nu);
                    timeToPeriapsis = dPeri / rate;

                    // 到远点：真近点角到 180
                    double dApo = 180.0 - nu;
                    if (dApo < 0)
                    {
                        dApo += 360.0;
                    }
                    timeToApoapsis = dApo / rate;
                }
            }
            catch (Exception ex)
            {
                Note("near/far point calc failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        /// <summary>把任意角度折到 (-180, 180]，这样读数才稳定。</summary>
        private static double Normalize(double deg)
        {
            if (double.IsNaN(deg) || double.IsInfinity(deg))
            {
                return deg;
            }
            double d = deg % 360.0;
            if (d > 180.0)
            {
                d -= 360.0;
            }
            else if (d <= -180.0)
            {
                d += 360.0;
            }
            return d;
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
                    Note("找不到类型 Orbit=" + (orbitType != null)
                        + " Location=" + (locType != null));
                    return;
                }

                // player.location 是 SFS.World.WorldLocation（MonoBehaviour），
                // 而 TryCreateOrbit 需要的是 SFS.World.Location —— 两个是**不相关**
                // 的类型（WorldLocation 的基类是 MonoBehaviour，不是 Location）。
                // 真正的 Location 要从 WorldLocation.Value 属性取。
                object wl = Get(player, "location");
                if (wl == null)
                {
                    Note("player.location 为 null");
                    return;
                }

                object loc = locType.IsInstanceOfType(wl) ? wl : Get(wl, "Value");
                if (loc == null)
                {
                    Note("从 " + wl.GetType().Name + " 取 Value 失败");
                    return;
                }
                if (!locType.IsInstanceOfType(loc))
                {
                    Note("Value 类型不符：" + loc.GetType().FullName);
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
                    Note("没找到签名为 (Location,bool,bool,out bool) 的 TryCreateOrbit");
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

                // TryCreateOrbit 返回的对象只填了远近点 —— 实测 ecc 和 period
                // 都是 0（它没算）。这里用轨道力学自己补：
                //   e = (ra - rp) / (ra + rp)
                //   a = (ra + rp) / 2
                //   T = 2*pi*sqrt(a^3 / mu)
                double Re = 6371000.0;          // 地球半径
                double ra = orbitApoapsis + Re;
                double rp = orbitPeriapsis + Re;
                if (ra > 0 && rp > 0)
                {
                    orbitEcc = (ra - rp) / (ra + rp);
                    double a = (ra + rp) / 2.0;
                    double mu = 3.5316e14;      // 地球 GM
                    if (a > 0)
                    {
                        orbitPeriod = 2.0 * Math.PI * Math.Sqrt(a * a * a / mu);
                    }
                }

                hasOrbit = !double.IsNaN(orbitApoapsis);

                // 到近点 / 到远点的时间。游戏自己提供了 API：
                //   GetNextTrueAnomalyPassTime(targetAnomaly, startTime)
                // 近点是真近点角 0，远点是 π。
                if (hasOrbit)
                {
                    CaptureApsisTimes(orbit, player);
                }
            }
            catch (Exception ex)
            {
                // 算不出来不算错误（火箭在地面上时本来就没有轨道），
                // 但记下原因方便排查
                if (orbitFailReason.Length == 0)
                {
                    orbitFailReason = ex.GetType().Name + ": " + ex.Message;
                }
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
            sb.Append(",\"flight_path_angle\":").Append(Num(flightPathAngle));
            sb.Append(",\"pitch_angle\":").Append(Num(pitchAngle));
            sb.Append(",\"has_orbit\":").Append(hasOrbit ? "true" : "false");
            if (orbitFailReason.Length > 0)
            {
                sb.Append(",\"orbit_error\":\"").Append(Str(orbitFailReason)).Append("\"");
            }
            if (hasOrbit)
            {
                sb.Append(",\"true_anomaly\":").Append(Num(trueAnomaly));
            sb.Append(",\"time_to_peri\":").Append(Num(timeToPeriapsis));
            sb.Append(",\"time_to_apo\":").Append(Num(timeToApoapsis));
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
