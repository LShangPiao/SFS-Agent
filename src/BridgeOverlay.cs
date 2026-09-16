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

        /// <summary>界面语言：zh / en。跟随配置里的 lang。</summary>
        public static volatile string Lang = "zh";

        private static string _lastLang;

        private static string TextWorking()
        {
            return Lang == "en" ? "Agent controlling" : "Agent \u64cd\u4f5c\u4e2d";
        }

        private static string TextUnlock()
        {
            return Lang == "en" ? "Release control" : "\u89e3\u9664\u72ec\u5360";
        }

        // 解除按钮的屏幕区域（像素，左上角原点）
        private const double BtnW = 148;
        private const double BtnH = 30;
        private const double BtnTop = 62;
        private const double HitInset = 3;   // 判定往内缩一点，边缘不误触

        private static double unlockX;
        private static double unlockY;
        private static double unlockW = BtnW;
        private static double unlockH = BtnH;

        private static object root;          // Canvas 所在的 GameObject
        private static object unlockLabel;   // 「解除独占」的文字组件
        private static object workLabel;     // 「Agent 操作中」的文字组件
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
            // 判定区域与按钮视觉严格对齐，并往内缩 HitInset，避免边缘误触
            return screenX >= unlockX + HitInset
                && screenX <= unlockX + unlockW - HitInset
                && screenY >= unlockY + HitInset
                && screenY <= unlockY + unlockH - HitInset;
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

                // 语言切换时刷新文字
                if (_lastLang != Lang)
                {
                    _lastLang = Lang;
                    SetMember(workLabel, "text", TextWorking());
                    SetMember(unlockLabel, "text", TextUnlock());
                }
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
                    // 偏扁、偏方的无衬线字体优先（用户觉得原来那个不合适）
                    string[] names = new string[]
                    {
                        "Microsoft YaHei UI", "Microsoft YaHei", "SimHei",
                        "Segoe UI Semibold", "Segoe UI", "Arial",
                    };
                    for (int i = 0; i < names.Length; i++)
                    {
                        object f = create.Invoke(null, new object[] { names[i], 24 });
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

        /// <summary>Unity 内置的九宫格圆角 sprite，用来让按钮有圆角。</summary>
        private static object GetRoundedSprite()
        {
            try
            {
                Type res = T("UnityEngine.Resources");
                Type objType = T("UnityEngine.Object");
                MethodInfo m = res.GetMethod(
                    "GetBuiltinResource", BindingFlags.Public | BindingFlags.Static,
                    null, new Type[] { typeof(Type), typeof(string) }, null);
                if (m == null)
                {
                    return null;
                }
                return m.Invoke(null, new object[] { T("UnityEngine.Sprite"), "UI/Skin/UISprite.psd" });
            }
            catch
            {
                return null;
            }
        }

        /// <summary>把文字节点压扁一点，视觉上更宽更扁。</summary>
        private static void Squash(object rt, double sx, double sy)
        {
            SetMember(rt, "localScale", MakeVector3(sx, sy, 1.0));
        }

        private static object MakeVector3(double x, double y, double z)
        {
            Type t = T("UnityEngine.Vector3");
            if (t == null)
            {
                return null;
            }
            ConstructorInfo c = t.GetConstructor(
                new Type[] { typeof(float), typeof(float), typeof(float) });
            return c == null ? null : c.Invoke(new object[] { (float)x, (float)y, (float)z });
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

                // ---- 文字：Agent 操作中 / Agent controlling ----
                object textRt = NewUiElement("label", canvas, textType);
                SetRect(textRt, 0, 1, 1, 1, 28, 34);
                object text = GetComponent(textRt, textType);
                SetMember(text, "text", TextWorking());
                if (font != null)
                {
                    SetMember(text, "font", font);
                }
                SetMember(text, "fontSize", 22);
                SetMember(text, "alignment", Enum.ToObject(
                    T("UnityEngine.TextAnchor"), 1)); // UpperCenter
                SetMember(text, "color", blue);
                SetMember(text, "raycastTarget", false);
                Squash(textRt, 1.08, 0.90);   // 压扁一点
                workLabel = text;

                // ---- 解除按钮（用九宫格圆角 sprite，pill 感）----
                object btnRt = NewUiElement("unlock", canvas, imageType);
                unlockW = BtnW;
                unlockH = BtnH;
                SetMember(btnRt, "anchorMin", MakeVector2(0.5, 1));
                SetMember(btnRt, "anchorMax", MakeVector2(0.5, 1));
                SetMember(btnRt, "pivot", MakeVector2(0.5, 1));
                SetMember(btnRt, "sizeDelta", MakeVector2(BtnW, BtnH));
                unlockY = BtnTop;
                SetMember(btnRt, "anchoredPosition", MakeVector2(0, -BtnTop));
                object btnImg = GetComponent(btnRt, imageType);
                SetMember(btnImg, "color", MakeColor(0.12, 0.20, 0.36, 0.94));
                object sprite = GetRoundedSprite();
                if (sprite != null)
                {
                    SetMember(btnImg, "sprite", sprite);
                    // Image.Type.Sliced = 1
                    SetMember(btnImg, "type", Enum.ToObject(T("UnityEngine.UI.Image+Type"), 1));
                    // pixelsPerUnitMultiplier 调小 => sprite 九宫格被放大 => 圆角更明显。
                    // 不设的话 30px 高的按钮上圆角只有几个像素，肉眼看还是直角。
                    SetMember(btnImg, "pixelsPerUnitMultiplier", 0.35f);
                }

                object btnTextRt = NewUiElement("unlockText", canvas, textType);
                SetMember(btnTextRt, "anchorMin", MakeVector2(0.5, 1));
                SetMember(btnTextRt, "anchorMax", MakeVector2(0.5, 1));
                SetMember(btnTextRt, "pivot", MakeVector2(0.5, 1));
                SetMember(btnTextRt, "sizeDelta", MakeVector2(BtnW, BtnH));
                SetMember(btnTextRt, "anchoredPosition", MakeVector2(0, -BtnTop));
                unlockLabel = GetComponent(btnTextRt, textType);
                SetMember(unlockLabel, "text", TextUnlock());
                if (font != null)
                {
                    SetMember(unlockLabel, "font", font);
                }
                SetMember(unlockLabel, "fontSize", 15);
                SetMember(unlockLabel, "alignment", Enum.ToObject(
                    T("UnityEngine.TextAnchor"), 4)); // MiddleCenter
                SetMember(unlockLabel, "color", MakeColor(1.0, 0.80, 0.90, 1.0));
                SetMember(unlockLabel, "raycastTarget", false);
                Squash(btnTextRt, 1.10, 0.90);

                _lastLang = Lang;
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
