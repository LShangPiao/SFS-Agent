// SFS-Agent — 独占模式覆盖层
//
// 独占模式下：用户自己的鼠标/键盘被吞掉，游戏只接受 agent 的注入。
// 屏幕上用 Canvas 画提示：上下淡蓝色渐变 + 一行字 + 一个「解除」按钮。
//
// 为什么用 Canvas 而不是 OnGUI：OnGUI 需要 MonoBehaviour 上有 OnGUI 方法，
// 而本模组的基类不是 MonoBehaviour，也拿不到合适的挂载点。
// Canvas + Image/Text 是普通游戏对象，全反射就能建出来。
//
// 所有创建与更新都必须在主线程。

using System;
using System.Reflection;
using System.Text;

namespace SfsAgent
{
    public static class BridgeOverlay
    {
        public static volatile bool WantVisible;
        public static volatile bool Exclusive;

        // 解除按钮的屏幕区域（像素，左上角原点），由 Build() 计算
        private static double unlockX;
        private static double unlockY;
        private static double unlockW;
        private static double unlockH;

        private static object root;          // Canvas 所在的 GameObject
        private static object unlockLabel;   // 用于改文字
        private static bool built;
        private static bool buildFailed;
        private static string buildError = "";

        public static string Status()
        {
            if (buildFailed)
            {
                return "overlay unavailable: " + buildError;
            }
            return built ? "overlay ready" : "overlay not built";
        }

        /// <summary>用户是否点在「解除」按钮上。</summary>
        public static bool IsUnlockHit(double screenX, double screenY)
        {
            if (!built)
            {
                return false;
            }
            return screenX >= unlockX && screenX <= unlockX + unlockW
                && screenY >= unlockY && screenY <= unlockY + unlockH;
        }

        // -- 生命周期 ---------------------------------------------------------

        public static void Tick()
        {
            if (!WantVisible)
            {
                if (built && !buildFailed)
                {
                    SetActive(false);
                }
                return;
            }

            if (!built && !buildFailed)
            {
                Build();
            }
            if (built)
            {
                SetActive(true);
            }
        }

        private static void SetActive(bool on)
        {
            try
            {
                object go = root;
                if (go == null)
                {
                    return;
                }
                MethodInfo m = go.GetType().GetMethod(
                    "SetActive", BindingFlags.Public | BindingFlags.Instance);
                if (m != null)
                {
                    m.Invoke(go, new object[] { on });
                }
            }
            catch
            {
            }
        }

        // -- 构建 -------------------------------------------------------------

        private static Type T(string name)
        {
            return BridgeState.FindType(name);
        }

        private static object NewGameObject(string name)
        {
            Type t = T("UnityEngine.GameObject");
            return Activator.CreateInstance(t, new object[] { name });
        }

        private static object AddComponent(object go, Type type)
        {
            MethodInfo m = go.GetType().GetMethod(
                "AddComponent", BindingFlags.Public | BindingFlags.Instance,
                null, new Type[] { typeof(Type) }, null);
            return m == null ? null : m.Invoke(go, new object[] { type });
        }

        private static void SetMember(object obj, string name, object value)
        {
            if (obj == null || value == null)
            {
                return;
            }
            const BindingFlags F =
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            Type t = obj.GetType();
            PropertyInfo p = t.GetProperty(name, F);
            if (p != null && p.CanWrite)
            {
                p.SetValue(obj, value, null);
                return;
            }
            FieldInfo f = t.GetField(name, F);
            if (f != null)
            {
                f.SetValue(obj, value);
            }
        }

        private static object MakeColor(double r, double g, double b, double a)
        {
            Type t = T("UnityEngine.Color");
            if (t == null)
            {
                return null;
            }
            ConstructorInfo c = t.GetConstructor(
                new Type[] { typeof(float), typeof(float), typeof(float), typeof(float) });
            return c == null ? null : c.Invoke(new object[] { (float)r, (float)g, (float)b, (float)a });
        }

        private static object MakeVector2(double x, double y)
        {
            Type t = T("UnityEngine.Vector2");
            ConstructorInfo c = t.GetConstructor(new Type[] { typeof(float), typeof(float) });
            return c == null ? null : c.Invoke(new object[] { (float)x, (float)y });
        }

        /// <summary>建一个带 RectTransform 的 UI 元素并挂到 parent 下。</summary>
        private static object NewUiElement(string name, object parent, Type componentType)
        {
            object go = NewGameObject(name);
            object rt = AddComponent(go, T("UnityEngine.RectTransform"));
            // 挂到父节点下（worldPositionStays = false）
            object tr = rt;
            MethodInfo setParent = tr.GetType().GetMethod(
                "SetParent", BindingFlags.Public | BindingFlags.Instance,
                null, new Type[] { T("UnityEngine.Transform"), typeof(bool) }, null);
            object parentTr = BridgeState.Get(parent, "transform");
            if (setParent != null && parentTr != null)
            {
                setParent.Invoke(tr, new object[] { parentTr, false });
            }
            if (componentType != null)
            {
                AddComponent(go, componentType);
            }
            return rt;
        }

        private static void SetRect(object rt, double anchorMinX, double anchorMinY,
            double anchorMaxX, double anchorMaxY, double offsetTop, double height)
        {
            SetMember(rt, "anchorMin", MakeVector2(anchorMinX, anchorMinY));
            SetMember(rt, "anchorMax", MakeVector2(anchorMaxX, anchorMaxY));
            SetMember(rt, "pivot", MakeVector2(0.5, 1.0));
            SetMember(rt, "sizeDelta", MakeVector2(0, height));
            SetMember(rt, "anchoredPosition", MakeVector2(0, -offsetTop));
        }

        private static object GetComponent(object rt, Type type)
        {
            MethodInfo m = rt.GetType().GetMethod(
                "GetComponent", BindingFlags.Public | BindingFlags.Instance,
                null, new Type[] { typeof(Type) }, null);
            return m == null ? null : m.Invoke(rt, new object[] { type });
        }

        private static object GetFont()
        {
            try
            {
                Type fontType = T("UnityEngine.Font");
                MethodInfo create = fontType.GetMethod(
                    "CreateDynamicFontFromOSFont",
                    BindingFlags.Public | BindingFlags.Static,
                    null, new Type[] { typeof(string), typeof(int) }, null);
                if (create != null)
                {
                    // 中文优先用系统里的中文字体
                    string[] names = new string[] { "Microsoft YaHei", "SimHei", "Arial" };
                    for (int i = 0; i < names.Length; i++)
                    {
                        object f = create.Invoke(null, new object[] { names[i], 22 });
                        if (f != null)
                        {
                            return f;
                        }
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        private static void Build()
        {
            built = false;
            buildFailed = false;
            try
            {
                Type canvasType = T("UnityEngine.Canvas");
                Type imageType = T("UnityEngine.UI.Image");
                Type textType = T("UnityEngine.UI.Text");
                if (canvasType == null || imageType == null || textType == null)
                {
                    buildFailed = true;
                    buildError = "缺少 Unity UI 类型";
                    return;
                }

                root = NewGameObject("SfsAgentOverlay");
                UnityEngine_DontDestroy(root);

                object canvas = AddComponent(root, canvasType);
                // renderMode = ScreenSpaceOverlay (0)
                SetMember(canvas, "renderMode", Enum.ToObject(
                    T("UnityEngine.RenderMode"), 0));
                SetMember(canvas, "sortingOrder", 32760);

                object font = GetFont();
                object blue = MakeColor(0.35, 0.62, 1.0, 1.0);

                // ---- 顶部渐变（条数多一些，肉眼才看不出分界）----
                int bands = 16;
                double bandH = 11;
                for (int i = 0; i < bands; i++)
                {
                    object rt = NewUiElement("bandTop" + i, canvas, imageType);
                    SetRect(rt, 0, 1, 1, 1, i * bandH, bandH + 1);
                    object img = GetComponent(rt, imageType);
                    // 从顶部最浓到往下几乎透明
                    double t = (double)i / (bands - 1);
                    double alpha = 0.46 * (1.0 - t) * (1.0 - t) + 0.03;
                    SetMember(img, "color", MakeColor(0.33, 0.60, 1.0, alpha));
                }

                // ---- 底部渐变 ----
                for (int i = 0; i < bands; i++)
                {
                    object rt = NewUiElement("bandBottom" + i, canvas, imageType);
                    SetMember(rt, "anchorMin", MakeVector2(0, 0));
                    SetMember(rt, "anchorMax", MakeVector2(1, 0));
                    SetMember(rt, "pivot", MakeVector2(0.5, 0));
                    SetMember(rt, "sizeDelta", MakeVector2(0, bandH + 1));
                    SetMember(rt, "anchoredPosition", MakeVector2(0, i * bandH));
                    object img = GetComponent(rt, imageType);
                    double t = (double)i / (bands - 1);
                    double alpha = 0.46 * (1.0 - t) * (1.0 - t) + 0.03;
                    SetMember(img, "color", MakeColor(0.33, 0.60, 1.0, alpha));
                }

                // ---- 文字：Agent 操作中 ----
                object textRt = NewUiElement("label", canvas, textType);
                SetRect(textRt, 0, 1, 1, 1, 28, 34);
                object text = GetComponent(textRt, textType);
                SetMember(text, "text", "Agent 操作中");
                if (font != null)
                {
                    SetMember(text, "font", font);
                }
                SetMember(text, "fontSize", 22);
                SetMember(text, "alignment", Enum.ToObject(
                    T("UnityEngine.TextAnchor"), 1)); // UpperCenter
                SetMember(text, "color", blue);
                SetMember(text, "raycastTarget", false);

                // ---- 解除按钮 ----
                object btnRt = NewUiElement("unlock", canvas, imageType);
                unlockW = 132;
                unlockH = 32;
                SetMember(btnRt, "anchorMin", MakeVector2(0.5, 1));
                SetMember(btnRt, "anchorMax", MakeVector2(0.5, 1));
                SetMember(btnRt, "pivot", MakeVector2(0.5, 1));
                SetMember(btnRt, "sizeDelta", MakeVector2(unlockW, unlockH));
                unlockX = 800 - unlockW / 2;   // 先用假值，Tick 里按实际屏幕更新
                unlockY = 66;
                SetMember(btnRt, "anchoredPosition", MakeVector2(0, -66));
                object btnImg = GetComponent(btnRt, imageType);
                SetMember(btnImg, "color", MakeColor(0.12, 0.20, 0.36, 0.94));

                object btnTextRt = NewUiElement("unlockText", canvas, textType);
                SetMember(btnTextRt, "anchorMin", MakeVector2(0.5, 1));
                SetMember(btnTextRt, "anchorMax", MakeVector2(0.5, 1));
                SetMember(btnTextRt, "pivot", MakeVector2(0.5, 1));
                SetMember(btnTextRt, "sizeDelta", MakeVector2(unlockW, unlockH));
                SetMember(btnTextRt, "anchoredPosition", MakeVector2(0, -66));
                unlockLabel = GetComponent(btnTextRt, textType);
                SetMember(unlockLabel, "text", "解除独占");
                if (font != null)
                {
                    SetMember(unlockLabel, "font", font);
                }
                SetMember(unlockLabel, "fontSize", 15);
                SetMember(unlockLabel, "alignment", Enum.ToObject(
                    T("UnityEngine.TextAnchor"), 4)); // MiddleCenter
                SetMember(unlockLabel, "color", MakeColor(0.76, 0.86, 1.0, 1.0));
                SetMember(unlockLabel, "raycastTarget", false);

                built = true;
            }
            catch (Exception ex)
            {
                buildFailed = true;
                buildError = ex.GetType().Name + ": " + ex.Message;
                Main.Log("overlay build failed: " + buildError);
            }
        }

        private static void UnityEngine_DontDestroy(object go)
        {
            try
            {
                MethodInfo m = T("UnityEngine.Object").GetMethod(
                    "DontDestroyOnLoad", BindingFlags.Public | BindingFlags.Static,
                    null, new Type[] { typeof(object) }, null);
                if (m != null)
                {
                    m.Invoke(null, new object[] { go });
                }
            }
            catch
            {
            }
        }

        /// <summary>按实际屏幕尺寸更新解除按钮的命中区域（每条 Tick 调）。</summary>
        public static void RefreshGeometry()
        {
            if (!built)
            {
                return;
            }
            try
            {
                Type st = T("UnityEngine.Screen");
                double w = BridgeState.ToDouble(BridgeState.GetStatic(st, "width"), 1600);
                unlockX = w / 2 - unlockW / 2;
            }
            catch
            {
            }
        }
    }
}
