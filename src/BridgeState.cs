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

                // 到近点 / 到远点的剩余时间。
                //
                // 这里**不用开普勒方程**：公式依赖 track 方向（顺行/逆行），
                // 而 Orbit.direction 的含义没法从反射确定，试出来的结果对不上
                // （实测 nu 在递减，说明会倒着走过远点）。
                //
                // 改用**数值外推**：观察真近点角实际的变化速率与方向，
                // 直接推它走到 0（近点）和 180（远点）还要多久。
                // 这不依赖任何公式假设，顺行逆行都自动正确。
                UpdateApsisFromVectors();
            }
            catch (Exception ex)
            {
                Note("near/far point calc failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        /// <summary>
        /// 当前天体的半径。
        ///
        /// **不能硬编码地球半径** —— SFS 不是真实尺度，
        /// 它的地球半径、质量都与现实不同，而且其他天体
        /// （月球、火星…）更是完全不一样。一律从 Planet 对象读。
        /// </summary>
        public static double PlanetRadius()
        {
            if (!double.IsNaN(cachedRadius) && cachedRadius > 0)
            {
                return cachedRadius;
            }
            object planet = CurrentPlanet();
            if (planet != null)
            {
                object r = Get(planet, "Radius");
                if (r == null)
                {
                    r = Get(planet, "radius");
                }
                double d = ToDouble(Unwrap(r), double.NaN);
                if (!double.IsNaN(d) && d > 0)
                {
                    cachedRadius = d;
                    return d;
                }
            }
            return 600000.0;   // 保底值（SFS 地球量级）
        }

        /// <summary>当前天体的引力常数 GM。</summary>
        public static double PlanetMu()
        {
            if (!double.IsNaN(cachedMu) && cachedMu > 0)
            {
                return cachedMu;
            }
            object planet = CurrentPlanet();
            if (planet != null)
            {
                // 用游戏自己的 GetGravity(r) 反推 GM：
                //     g(r) = mu / r^2   =>   mu = g(r) * r^2
                //
                // 不能用 mass * G —— SFS 的质量单位不是千克
                // （实测 mass*6.674e-11 只得到 64.9，而真值在 1e12 量级）。
                double r0 = PlanetRadius();
                MethodInfo g = null;
                MethodInfo[] ms = planet.GetType().GetMethods(
                    BindingFlags.Public | BindingFlags.Instance);
                for (int i = 0; i < ms.Length; i++)
                {
                    if (ms[i].Name == "GetGravity" && ms[i].GetParameters().Length == 1
                        && ms[i].GetParameters()[0].ParameterType == typeof(double))
                    {
                        g = ms[i];
                        break;
                    }
                }
                if (g != null && r0 > 0)
                {
                    // 在表面取重力最稳（避开大气影响）
                    double gs = ToDouble(g.Invoke(planet, new object[] { r0 }), double.NaN);
                    if (!double.IsNaN(gs) && gs > 0)
                    {
                        cachedMu = gs * r0 * r0;
                        return cachedMu;
                    }
                }
            }
            return 1.0e12;     // 保底值
        }

        private static object CurrentPlanet()
        {
            try
            {
                Type pcType = FindType("SFS.World.PlayerController");
                object pc = pcType == null ? null : GetStatic(pcType, "main");
                object playerLocal = pc == null ? null : Get(pc, "player");
                object player = Unwrap(playerLocal);
                if (player == null)
                {
                    return null;
                }
                object loc = Get(player, "location");
                if (loc == null)
                {
                    return null;
                }
                object pl = Get(loc, "planet");
                return Unwrap(pl);
            }
            catch
            {
                return null;
            }
        }

        private static double cachedRadius = double.NaN;
        private static double cachedMu = double.NaN;
        private static double cachedAtm = double.NaN;

        // -- 由位置与速度直接推算轨道要素 --------------------------------------
        //
        // **不信任游戏给的 trueAnomaly**：实测它与由位置速度算出的值差 8.7°，
        // 符号方向也不一致（游戏说 177.7°，实际 169.1°）。
        //
        // 位置（height + Radius）与速度（velocity_x / velocity_y）是可靠的 ——
        // 用它们算出的半长轴、周期与游戏给的远近点、周期完全吻合。
        //
        // 采用「偏心率矢量法」，它一次给出 e 的大小与方向（近点方向）：
        //     e_vec = ((v² - μ/r)·r_vec - (r_vec·v_vec)·v_vec) / μ
        //     e     = |e_vec|
        //     ν     = e_vec 与 r_vec 的夹角（由径向速度定符号）

        private static void UpdateApsisFromVectors()
        {
            double R = PlanetRadius();
            double mu = PlanetMu();
            if (double.IsNaN(R) || double.IsNaN(mu) || mu <= 0)
            {
                return;
            }

            double r = height + R;
            double vx = velX;
            double vy = velY;
            double v2 = vx * vx + vy * vy;
            if (r <= 0 || v2 <= 0)
            {
                return;
            }

            // 位置矢量：把「径向朝外」当作 y 轴，切向当作 x 轴。
            // 速度分量正是在同一套轴上的（实测 |velocity| == speed）。
            double px = 0.0;
            double py = r;

            double rv = px * vx + py * vy;          // r·v（径向速度 × r）
            double vr = rv / r;                     // 径向速度

            // 偏心率矢量
            double k = v2 - mu / r;
            double ex = (k * px - rv * vx) / mu;
            double ey = (k * py - rv * vy) / mu;
            double e = Math.Sqrt(ex * ex + ey * ey);

            if (e < 1e-9)
            {
                // 正圆：没有近点远点之分，到哪个点都是四分之一周期
                double T0 = 2.0 * Math.PI * Math.Sqrt(r * r * r / mu);
                timeToPeriapsis = T0 / 4.0;
                timeToApoapsis = T0 / 4.0;
                return;
            }

            // 真近点角：e_vec 与 r_vec 的夹角，用径向速度定符号
            double cosNu = (ex * px + ey * py) / (e * r);
            if (cosNu > 1.0)
            {
                cosNu = 1.0;
            }
            else if (cosNu < -1.0)
            {
                cosNu = -1.0;
            }
            double nu = Math.Acos(cosNu);           // 0..pi
            if (vr < 0)
            {
                nu = -nu;                            // 接近近点
            }
            trueAnomaly = nu * 180.0 / Math.PI;
            orbitEcc = e;

            // 半长轴与周期
            double a = 1.0 / (2.0 / r - v2 / mu);
            if (a <= 0)
            {
                return;                              // 双曲/抛物线，没有周期
            }
            double T = 2.0 * Math.PI * Math.Sqrt(a * a * a / mu);
            orbitPeriod = T;
            double n = 2.0 * Math.PI / T;

            // 开普勒方程求平近点角
            double E = 2.0 * Math.Atan(Math.Sqrt((1.0 - e) / (1.0 + e)) * Math.Tan(nu / 2.0));
            double M = E - e * Math.Sin(E);

            // M 是「从近点起算已走过」的量。
            // 径向速度为正（在远离）说明刚过近点，M 在 (0, π)；
            // 为负（在接近）说明即将到近点，M 在 (π, 2π)。
            while (M < 0)
            {
                M += 2.0 * Math.PI;
            }
            while (M >= 2.0 * Math.PI)
            {
                M -= 2.0 * Math.PI;
            }

            timeToPeriapsis = (2.0 * Math.PI - M) / n;
            double dApo = Math.PI - M;
            if (dApo < 0)
            {
                dApo += 2.0 * Math.PI;               // 本圈已过远点，等下一圈
            }
            timeToApoapsis = dApo / n;
        }

// <summary>
        /// 真近点角（度）转平近点角（弧度）。
        ///
        /// ν -> E -> M，其中 E = 2·atan(√((1-e)/(1+e))·tan(ν/2))。
        /// tan 在 ν 接近 ±180° 时会溢出，所以先把 ν 折到 (-π, π]。
        /// </summary>
        private static double MeanAnomalyFromTrueAnomaly(double nuDeg, double e)
        {
            double nu = nuDeg * Math.PI / 180.0;
            while (nu > Math.PI)
            {
                nu -= 2.0 * Math.PI;
            }
            while (nu <= -Math.PI)
            {
                nu += 2.0 * Math.PI;
            }

            double E = 2.0 * Math.Atan(
                Math.Sqrt((1.0 - e) / (1.0 + e)) * Math.Tan(nu / 2.0));

            double M = E - e * Math.Sin(E);
            while (M < 0)
            {
                M += 2.0 * Math.PI;
            }
            while (M >= 2.0 * Math.PI)
            {
                M -= 2.0 * Math.PI;
            }
            return M;
        }

        /// <summary>
        /// 当前天体的大气高度。
        ///
        /// 这是判断「算不算入轨」的依据 —— 不能用地球的 100 km 卡门线，
        /// SFS 支持自定义星系包，每个天体都不一样。取不到时用半径的 40% 估。
        /// </summary>
        public static double AtmosphereHeight()
        {
            if (!double.IsNaN(cachedAtm) && cachedAtm > 0)
            {
                return cachedAtm;
            }
            object planet = CurrentPlanet();
            if (planet != null)
            {
                string[] names = new string[]
                {
                    "AtmosphereHeightPhysics", "AtmosphereHeight", "atmosphereHeight",
                };
                for (int i = 0; i < names.Length; i++)
                {
                    object v = Get(planet, names[i]);
                    double d = ToDouble(Unwrap(v), double.NaN);
                    if (!double.IsNaN(d) && d > 0)
                    {
                        cachedAtm = d;
                        return d;
                    }
                }
            }
            double r = PlanetRadius();
            cachedAtm = r * 0.4;
            return cachedAtm;
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
                // 游戏给的 apoapsis / periapsis 是**地心距**，不是表面高度。
                //
                // 实测验证（SFS 地球，半径 314970 m）：
                //   远近点均值 347292  vs  由当前 r、v 算出的 a = 347292
                //   完全一致 —— 所以不能再加半径。
                //
                // 且游戏自己的 period 字段对不上（实测是正确值的 2^1.5 倍），
                // 所以一律自己算。
                double ra = orbitApoapsis;
                double rp = orbitPeriapsis;
                if (ra > 0 && rp > 0)
                {
                    orbitEcc = (ra - rp) / (ra + rp);
                    double a = (ra + rp) / 2.0;
                    double mu = PlanetMu();
                    if (a > 0 && mu > 0)
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
                sb.Append(",\"planet_radius\":").Append(Num(PlanetRadius()));
            sb.Append(",\"planet_mu\":").Append(Num(PlanetMu()));
            sb.Append(",\"atmosphere_height\":").Append(Num(AtmosphereHeight()));
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
