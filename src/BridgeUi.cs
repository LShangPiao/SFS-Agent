// SFS-Agent — UI 元素枚举与点击
//
// 点击采用**游戏内事件注入**：直接触发按钮自己的点击事件，而不是移动真实鼠标。
// 这样猫娘操作游戏时不会干扰用户的其他操作（实测点击前后系统光标坐标不变）。
//
// 实测得到的 SFS 界面结构（游戏 v1.6.00.16）：
//   菜单上的按钮运行时类型是 SFS.UI.ButtonPC : SFS.UI.Button : MonoBehaviour
//   SFS.UI.Button 以**显式接口实现**提供 SFS.Input.I_Touchable 的成员，
//   方法名为 "SFS.Input.I_Touchable.OnInputEnd"（private），参数 OnInputEndData
//   SFS.UI.Button 的字段：clickEvent(SFS.UI.ClickUnityEvent : UnityEvent<OnInputEndData>)
//                          onClick / onUp / onRightClick(OptionalDelegate<OnInputEndData>)
//                          buttonEnabled(bool)
//
// 重要（踩过的坑）：**在游戏运行时 GetMethods() 枚举不到那些显式接口实现**
//   （离线反射同一个 Assembly-CSharp.dll 可以枚举到，运行时返回空）。
//   因此走 OnInputEnd 的路径在游戏内不可用，实际生效的是 clickEvent.Invoke(data)。
//   GetFields() 在运行时工作正常，所以优先直接用字段触发。
//
// 点击尝试顺序：clickEvent.Invoke → OnInputEnd（离线/其他版本兜底）→ onClick。
//
// Win32 鼠标（BridgeInput）保留为兜底：当界面元素枚举不到时仍能按坐标点。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace SfsAgent
{
    public static class BridgeUi
    {
        public const int MaxElements = 40;

        private static readonly List<string> labels = new List<string>();
        private static readonly List<double> normX = new List<double>();
        private static readonly List<double> normY = new List<double>();
        private static readonly List<double> pixelX = new List<double>();
        private static readonly List<double> pixelY = new List<double>();
        private static readonly List<object> elements = new List<object>();

        private static volatile bool captureRequested;
        private static volatile bool clickRequested;
        private static int clickIndex = -1;

        public static string LastResult = "";
        public static string LastError = "";
        public static string lastError = "";

        // -- 主线程调度 -------------------------------------------------------

        public static void Request()
        {
            captureRequested = true;
        }

        /// <summary>服务线程：请求点击第 index 个元素（在主线程执行）。</summary>
        public static void RequestClick(int index)
        {
            clickIndex = index;
            clickRequested = true;
        }

        /// <summary>主线程：处理枚举与点击请求。</summary>
        public static void Tick()
        {
            if (captureRequested)
            {
                captureRequested = false;
                Capture();
            }
            if (clickRequested)
            {
                clickRequested = false;
                int index = clickIndex;
                clickIndex = -1;
                ClickIndex(index);
            }
        }

        // -- 基础设施 ---------------------------------------------------------

        private static object[] FindObjects(Type type)
        {
            if (type == null)
            {
                return new object[0];
            }
            Type uo = BridgeState.FindType("UnityEngine.Object");
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

        private static string ReadLabel(object button)
        {
            try
            {
                object go = BridgeState.Get(button, "gameObject");
                if (go == null)
                {
                    return "";
                }

                string[] textTypes = new string[]
                {
                    "UnityEngine.UI.Text",
                    "TMPro.TextMeshProUGUI",
                    "TMPro.TMP_Text",
                };

                for (int i = 0; i < textTypes.Length; i++)
                {
                    Type textType = BridgeState.FindType(textTypes[i]);
                    if (textType == null)
                    {
                        continue;
                    }
                    MethodInfo m = go.GetType().GetMethod(
                        "GetComponentsInChildren",
                        BindingFlags.Public | BindingFlags.Instance,
                        null,
                        new Type[] { typeof(Type) },
                        null);
                    if (m == null)
                    {
                        continue;
                    }
                    Array arr = m.Invoke(go, new object[] { textType }) as Array;
                    if (arr == null || arr.Length == 0)
                    {
                        continue;
                    }
                    for (int k = 0; k < arr.Length; k++)
                    {
                        object text = BridgeState.Get(arr.GetValue(k), "text");
                        string s = Convert.ToString(text, CultureInfo.InvariantCulture);
                        if (!string.IsNullOrEmpty(s) && s.Trim().Length > 0)
                        {
                            return s.Trim();
                        }
                    }
                }
            }
            catch
            {
            }
            return "";
        }

        /// <summary>
        /// 取 UI 元素的屏幕像素坐标。
        ///
        /// 优先用 RectTransform.GetWorldCorners 求矩形中心：
        /// Screen Space Overlay 画布下世界坐标就是屏幕像素，而且矩形中心比
        /// transform.position（枢轴点，常在角上）更能代表按钮实际可点区域，
        /// 命中率明显更高。
        /// </summary>
        private static bool TryGetPixel(object button, out double px, out double py)
        {
            px = 0;
            py = 0;
            try
            {
                object tr = BridgeState.Get(button, "transform");
                if (tr == null)
                {
                    return false;
                }

                Type v3 = BridgeState.FindType("UnityEngine.Vector3");
                if (v3 != null)
                {
                    MethodInfo gwc = tr.GetType().GetMethod(
                        "GetWorldCorners",
                        BindingFlags.Public | BindingFlags.Instance);
                    if (gwc != null)
                    {
                        try
                        {
                            Array corners = Array.CreateInstance(v3, 4);
                            gwc.Invoke(tr, new object[] { corners });
                            object c0 = corners.GetValue(0);
                            object c2 = corners.GetValue(2);
                            double x0 = Convert.ToDouble(
                                BridgeState.Get(c0, "x"), CultureInfo.InvariantCulture);
                            double y0 = Convert.ToDouble(
                                BridgeState.Get(c0, "y"), CultureInfo.InvariantCulture);
                            double x2 = Convert.ToDouble(
                                BridgeState.Get(c2, "x"), CultureInfo.InvariantCulture);
                            double y2 = Convert.ToDouble(
                                BridgeState.Get(c2, "y"), CultureInfo.InvariantCulture);
                            double cx = (x0 + x2) / 2.0;
                            double cy = (y0 + y2) / 2.0;
                            if (cx != 0 || cy != 0)
                            {
                                px = cx;
                                py = cy;
                                return true;
                            }
                        }
                        catch
                        {
                        }
                    }
                }

                // 退回 transform.position + WorldToScreenPoint
                object worldPos = BridgeState.Get(tr, "position");
                if (worldPos == null)
                {
                    return false;
                }

                Type cameraType = BridgeState.FindType("UnityEngine.Camera");
                Type rtu = BridgeState.FindType("UnityEngine.RectTransformUtility");
                if (rtu == null)
                {
                    return false;
                }
                MethodInfo m = rtu.GetMethod(
                    "WorldToScreenPoint",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new Type[] { cameraType, worldPos.GetType() },
                    null);
                if (m == null)
                {
                    return false;
                }

                object screen = m.Invoke(null, new object[] { null, worldPos });
                if (screen == null)
                {
                    return false;
                }
                px = Convert.ToDouble(BridgeState.Get(screen, "x"), CultureInfo.InvariantCulture);
                py = Convert.ToDouble(BridgeState.Get(screen, "y"), CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // -- 枚举 -------------------------------------------------------------

        public static void Capture()
        {
            labels.Clear();
            normX.Clear();
            normY.Clear();
            pixelX.Clear();
            pixelY.Clear();
            elements.Clear();
            lastError = "";

            try
            {
                Type buttonType = BridgeState.FindType("SFS.UI.Button");
                if (buttonType == null)
                {
                    lastError = "SFS.UI.Button not found";
                    return;
                }

                double screenW = 0;
                double screenH = 0;
                Type screenType = BridgeState.FindType("UnityEngine.Screen");
                if (screenType != null)
                {
                    screenW = Convert.ToDouble(
                        BridgeState.GetStatic(screenType, "width"),
                        CultureInfo.InvariantCulture);
                    screenH = Convert.ToDouble(
                        BridgeState.GetStatic(screenType, "height"),
                        CultureInfo.InvariantCulture);
                }

                object[] buttons = FindObjects(buttonType);
                for (int i = 0; i < buttons.Length && labels.Count < MaxElements; i++)
                {
                    object button = buttons[i];

                    // 跳过被禁用的按钮（buttonEnabled 为 false）
                    object enabled = BridgeState.Get(button, "buttonEnabled");
                    if (enabled is bool && !(bool)enabled)
                    {
                        continue;
                    }

                    string label = ReadLabel(button);
                    double px, py;
                    bool hasPixel = TryGetPixel(button, out px, out py);
                    if (label.Length == 0 && !hasPixel)
                    {
                        continue;
                    }

                    labels.Add(label);
                    elements.Add(button);
                    if (hasPixel)
                    {
                        pixelX.Add(px);
                        pixelY.Add(py);
                        double nx = screenW > 0 ? px / screenW : -1;
                        double ny = screenH > 0 ? 1.0 - (py / screenH) : -1;
                        normX.Add(nx >= 0 && nx <= 1 ? nx : -1);
                        normY.Add(ny >= 0 && ny <= 1 ? ny : -1);
                    }
                    else
                    {
                        pixelX.Add(-1);
                        pixelY.Add(-1);
                        normX.Add(-1);
                        normY.Add(-1);
                    }
                }
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
            }
        }

        // -- 点击（游戏内注入）------------------------------------------------

        private static object MakeVector2(double x, double y)
        {
            Type v2 = BridgeState.FindType("UnityEngine.Vector2");
            if (v2 == null)
            {
                return null;
            }
            ConstructorInfo ctor = v2.GetConstructor(new Type[] { typeof(float), typeof(float) });
            if (ctor == null)
            {
                return null;
            }
            return ctor.Invoke(new object[] { (float)x, (float)y });
        }

        /// <summary>
        /// 找到显式接口实现的方法，例如 "SFS.Input.I_Touchable.OnInputEnd"。
        ///
        /// 不比较 Type 对象本身，只比较类型**名称**（显式接口实现的方法名带接口前缀，
        /// 所以按后缀匹配）。参数类型名必须一致：绝不退回「随便找个同名方法」，
        /// 否则可能调用到编译器生成的闭包方法并误报成功。
        /// </summary>
        private static MethodInfo FindTouchMethod(Type t, string suffix, string paramTypeName)
        {
            MethodInfo[] methods = t.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo m = methods[i];
                if (!m.Name.EndsWith(suffix, StringComparison.Ordinal))
                {
                    continue;
                }
                ParameterInfo[] ps = m.GetParameters();
                if (ps.Length == 1 && ps[0].ParameterType.Name == paramTypeName)
                {
                    return m;
                }
            }
            return null;
        }

        /// <summary>按构造函数的参数类型动态构造输入数据，不依赖硬编码类型。</summary>
        private static object BuildInputData(Type dataType, double px, double py, bool withClick)
        {
            ConstructorInfo[] ctors = dataType.GetConstructors();
            for (int c = 0; c < ctors.Length; c++)
            {
                ParameterInfo[] ps = ctors[c].GetParameters();
                object[] args = new object[ps.Length];
                bool ok = true;
                for (int i = 0; i < ps.Length; i++)
                {
                    Type pt = ps[i].ParameterType;
                    if (pt.IsEnum)
                    {
                        args[i] = Enum.ToObject(pt, 1); // InputType.MouseLeft
                    }
                    else if (pt == typeof(bool))
                    {
                        args[i] = withClick;
                    }
                    else if (pt == typeof(float))
                    {
                        args[i] = 0f;
                    }
                    else if (pt.Name == "TouchPosition")
                    {
                        args[i] = BuildTouchPosition(pt, px, py);
                    }
                    else
                    {
                        ok = false;
                        break;
                    }
                }
                if (ok)
                {
                    return ctors[c].Invoke(args);
                }
            }
            return null;
        }

        private static object BuildTouchPosition(Type touchPosType, double px, double py)
        {
            object vector = MakeVector2(px, py);
            if (vector == null)
            {
                return null;
            }
            ConstructorInfo[] ctors = touchPosType.GetConstructors();
            for (int i = 0; i < ctors.Length; i++)
            {
                ParameterInfo[] ps = ctors[i].GetParameters();
                if (ps.Length == 1 && ps[0].ParameterType == vector.GetType())
                {
                    return ctors[i].Invoke(new object[] { vector });
                }
            }
            return null;
        }

        /// <summary>诊断：列出按钮对象上的全部方法名。</summary>
        public static string DescribeMethods(int index)
        {
            if (index < 0 || index >= elements.Count)
            {
                return "{\"ok\":false,\"error\":\"index out of range\"}";
            }
            Type t = elements[index].GetType();
            StringBuilder sb = new StringBuilder(2048);
            sb.Append("{\"ok\":true,\"type\":\"").Append(Esc(t.FullName))
              .Append("\",\"interfaces\":[");
            Type[] ifaces = t.GetInterfaces();
            for (int i = 0; i < ifaces.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(",");
                }
                sb.Append("\"").Append(Esc(ifaces[i].FullName)).Append("\"");
            }
            sb.Append("],\"inputMethods\":[");
            MethodInfo[] methods = t.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            bool first = true;
            for (int i = 0; i < methods.Length; i++)
            {
                string n = methods[i].Name;
                if (n.IndexOf("Input", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
                if (!first)
                {
                    sb.Append(",");
                }
                first = false;
                ParameterInfo[] ps = methods[i].GetParameters();
                sb.Append("\"").Append(Esc(n)).Append("(").Append(ps.Length).Append(")");
                for (int k = 0; k < ps.Length; k++)
                {
                    sb.Append(":").Append(Esc(ps[k].ParameterType.Name));
                }
                sb.Append("\"");
            }
            sb.Append("],\"clickFields\":[");
            FieldInfo[] fields = t.GetFields(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            first = true;
            for (int i = 0; i < fields.Length; i++)
            {
                string n = fields[i].Name;
                if (n.IndexOf("click", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
                object val = null;
                try
                {
                    val = fields[i].GetValue(elements[index]);
                }
                catch
                {
                }
                if (!first)
                {
                    sb.Append(",");
                }
                first = false;
                sb.Append("{\"name\":\"").Append(Esc(n))
                  .Append("\",\"type\":\"").Append(Esc(fields[i].FieldType.FullName))
                  .Append("\",\"null\":").Append(val == null ? "true" : "false")
                  .Append("}");
            }
            sb.Append("]}");
            return sb.ToString();
        }

        /// <summary>
        /// 游戏内点击。按可靠性依次尝试三条路径，全程不触碰系统鼠标：
        ///   ① 直接 Invoke 按钮的 clickEvent（UnityEvent&lt;OnInputEndData&gt;）
        ///   ② 调用 I_Touchable.OnInputEnd（显式接口实现）
        ///   ③ 调用 onClick 委托
        /// </summary>
        public static bool ClickIndex(int index)
        {
            LastError = "";
            try
            {
                if (index < 0 || index >= elements.Count)
                {
                    LastError = "index out of range (capture first)";
                    return false;
                }

                object button = elements[index];
                double px = pixelX[index] >= 0 ? pixelX[index] : 0;
                double py = pixelY[index] >= 0 ? pixelY[index] : 0;
                List<string> tried = new List<string>();

                // ---- ① clickEvent：直接触发按钮的 UnityEvent（实测唯一可用路径）----
                // 字段读取在运行时正常，是游戏内唯一稳定的点击方式。
                object clickEvent = BridgeState.Get(button, "clickEvent");
                if (clickEvent != null)
                {
                    MethodInfo invoke = null;
                    MethodInfo[] ms = clickEvent.GetType().GetMethods(
                        BindingFlags.Public | BindingFlags.Instance);
                    for (int i = 0; i < ms.Length; i++)
                    {
                        if (ms[i].Name == "Invoke" && ms[i].GetParameters().Length == 1)
                        {
                            invoke = ms[i];
                            break;
                        }
                    }
                    if (invoke != null)
                    {
                        Type argType = invoke.GetParameters()[0].ParameterType;
                        object data = BuildInputData(argType, px, py, true);
                        if (data != null)
                        {
                            invoke.Invoke(clickEvent, new object[] { data });
                            LastResult = "clicked #" + index + " (" + labels[index]
                                + ") via clickEvent.Invoke";
                            return true;
                        }
                        tried.Add("clickEvent: cannot build " + argType.Name);
                    }
                    else
                    {
                        tried.Add("clickEvent: no Invoke(1)");
                    }
                }
                else
                {
                    tried.Add("clickEvent: field missing");
                }

                // ---- ② OnInputEnd：显式接口实现（运行时 GetMethods 通常枚举不到，兜底）----
                MethodInfo endMethod = FindTouchMethod(
                    button.GetType(), "OnInputEnd", "OnInputEndData");
                if (endMethod != null)
                {
                    Type et = endMethod.GetParameters()[0].ParameterType;
                    object endData = BuildInputData(et, px, py, true);
                    if (endData != null)
                    {
                        MethodInfo startMethod = FindTouchMethod(
                            button.GetType(), "OnInputStart", "OnInputStartData");
                        if (startMethod != null)
                        {
                            Type st = startMethod.GetParameters()[0].ParameterType;
                            object startData = BuildInputData(st, px, py, false);
                            if (startData != null)
                            {
                                startMethod.Invoke(button, new object[] { startData });
                            }
                        }
                        endMethod.Invoke(button, new object[] { endData });
                        LastResult = "clicked #" + index + " (" + labels[index]
                            + ") via OnInputEnd";
                        return true;
                    }
                    tried.Add("OnInputEnd: cannot build " + et.Name);
                }
                else
                {
                    tried.Add("OnInputEnd: no suffix match on " + button.GetType().FullName);
                }

                // ---- ③ onClick 委托 ----
                object onClick = BridgeState.Get(button, "onClick");
                if (onClick != null)
                {
                    MethodInfo inv = onClick.GetType().GetMethod("Invoke");
                    if (inv != null && inv.GetParameters().Length == 1)
                    {
                        Type at = inv.GetParameters()[0].ParameterType;
                        object data = BuildInputData(at, px, py, true);
                        if (data != null)
                        {
                            inv.Invoke(onClick, new object[] { data });
                            LastResult = "clicked #" + index + " (" + labels[index]
                                + ") via onClick";
                            return true;
                        }
                        tried.Add("onClick: cannot build " + at.Name);
                    }
                    else
                    {
                        tried.Add("onClick: unsupported signature");
                    }
                }
                else
                {
                    tried.Add("onClick: field missing");
                }

                LastError = "all click paths failed -> " + string.Join("; ", tried.ToArray());
                return false;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return false;
            }
        }

        // -- 对外 -------------------------------------------------------------

        public static bool TryGetNormalized(int index, out double nx, out double ny)
        {
            nx = 0;
            ny = 0;
            if (index < 0 || index >= normX.Count)
            {
                return false;
            }
            if (normX[index] < 0 || normY[index] < 0)
            {
                return false;
            }
            nx = normX[index];
            ny = normY[index];
            return true;
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

        public static string ToJson()
        {
            StringBuilder sb = new StringBuilder(512);
            sb.Append("{\"ok\":true,\"count\":").Append(labels.Count);
            sb.Append(",\"elements\":[");
            for (int i = 0; i < labels.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(",");
                }
                sb.Append("{\"index\":").Append(i);
                sb.Append(",\"label\":\"").Append(Esc(labels[i])).Append("\"");
                if (normX[i] >= 0)
                {
                    sb.Append(",\"x\":").Append(normX[i].ToString("0.####", CultureInfo.InvariantCulture));
                    sb.Append(",\"y\":").Append(normY[i].ToString("0.####", CultureInfo.InvariantCulture));
                }
                sb.Append("}");
            }
            sb.Append("]");
            if (lastError.Length > 0)
            {
                sb.Append(",\"error\":\"").Append(Esc(lastError)).Append("\"");
            }
            sb.Append("}");
            return sb.ToString();
        }

        /// <summary>点击结果（服务线程读取）。</summary>
        public static string ClickResultJson()
        {
            StringBuilder sb = new StringBuilder(160);
            sb.Append("{\"ok\":").Append(LastResult.Length > 0 && LastError.Length == 0 ? "true" : "false");
            if (LastResult.Length > 0)
            {
                sb.Append(",\"result\":\"").Append(Esc(LastResult)).Append("\"");
            }
            if (LastError.Length > 0)
            {
                sb.Append(",\"error\":\"").Append(Esc(LastError)).Append("\"");
            }
            sb.Append(",\"mode\":\"in_game\"}");
            return sb.ToString();
        }

        public static void ResetClickResult()
        {
            LastResult = "";
            LastError = "";
        }

        public static int Count
        {
            get { return labels.Count; }
        }
    }
}
