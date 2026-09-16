// SFS Agent — 游戏状态采集
//
// 全部通过反射读取，避免与游戏程序集产生编译期强耦合：
// 即便某个字段在具体游戏版本中改名，mod 也能正常加载并降级。

using System;
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

        private static double ToDouble(object o, double fallback)
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

        public static Type FindType(string fullName)
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
                }
                else
                {
                    flying = false;
                }
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
            }
        }

        private static string Num(double v)
        {
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
            if (lastError.Length > 0)
            {
                sb.Append(",\"error\":\"").Append(Str(lastError)).Append("\"");
            }
            sb.Append("}");
            return sb.ToString();
        }
    }
}
