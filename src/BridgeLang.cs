// SFS-Agent — 日志文案的中英切换
//
// 游戏内提示、日志内容都要跟着界面语言走。
// 模组不能引 Unity 程序集，所以不能用游戏自带的翻译表，
// 这里自己维护一份最小集合。
//
// 用法：L.T("中文", "English")

using System.Collections.Generic;

namespace SfsAgent
{
    public static class BridgeLang
    {
        private static readonly Dictionary<string, string> En =
            new Dictionary<string, string>();

        static BridgeLang()
        {
            // 这里只放日志里真正会出现的句子
            En["当前高度"] = "altitude";
            En["、速度"] = ", speed ";
            En["当前姿态角"] = "attitude";
            En["姿态角"] = "attitude";
            En["爬升到"] = "climbed to";
            En["下降到"] = "descended to";
            En["速度"] = "speed";
        }

        /// <summary>根据当前界面语言选中英文。</summary>
        public static string T(string zh, string en)
        {
            return BridgeConfig.Lang == "en" ? en : zh;
        }

        /// <summary>把一个中文词换成当前语言的说法（查不到就原样返回）。</summary>
        public static string W(string zh)
        {
            if (BridgeConfig.Lang != "en")
            {
                return zh;
            }
            string v;
            return En.TryGetValue(zh, out v) ? v : zh;
        }
    }
}
