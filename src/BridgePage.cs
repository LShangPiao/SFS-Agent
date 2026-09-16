// SFS-Agent — 内置配置页
//
// 模组启动后会在默认浏览器里打开 http://127.0.0.1:<port>/
// 让人也能直接看到桥接服务的状态与全部接口，而不只是给程序用。
//
// 页面是自包含的（不引任何外部资源），离线也能用；
// 数据靠页面里的 JS 轮询本地接口拿。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace SfsAgent
{
    public static class BridgePage
    {
        public static string Html(int port)
        {
            string p = port.ToString(CultureInfo.InvariantCulture);
            StringBuilder sb = new StringBuilder(8192);

            sb.Append("<!doctype html><html lang='zh-CN'><head><meta charset='utf-8'>");
            sb.Append("<meta name='viewport' content='width=device-width,initial-scale=1'>");
            sb.Append("<title>SFS-Agent</title><style>");
            sb.Append("*{box-sizing:border-box}");
            // ── 亮色（默认）：白底 + 淡粉 ──
            sb.Append(":root{");
            sb.Append("--bg:#fdfbff;--bg2:#ffffff;--fg:#3a2b33;--muted:#8d7480;");
            sb.Append("--line:#f2d9e6;--accent:#e0508c;--accent2:#ff8fbe;");
            sb.Append("--code-bg:#fff4f9;--shadow:0 2px 10px rgba(224,80,140,.07);");
            sb.Append("--shadow-hover:0 6px 20px rgba(224,80,140,.16);");
            sb.Append("}");
            // ── 暗色：黑底 + 浅蓝 ──
            sb.Append("@media (prefers-color-scheme:dark){:root{");
            sb.Append("--bg:#0d1117;--bg2:#151c26;--fg:#d6e4f7;--muted:#8aa0bd;");
            sb.Append("--line:#233043;--accent:#5aa9ff;--accent2:#8fc7ff;");
            sb.Append("--code-bg:#0f1620;--shadow:0 2px 10px rgba(0,0,0,.35);");
            sb.Append("--shadow-hover:0 6px 22px rgba(90,169,255,.22);");
            sb.Append("}}");
            sb.Append("body{margin:0;padding:32px;background:var(--bg);color:var(--fg);");
            sb.Append("font:14px/1.65 ui-sans-serif,system-ui,'Segoe UI',sans-serif;");
            sb.Append("-webkit-font-smoothing:antialiased;transition:background .2s,color .2s}");
            sb.Append("h1{margin:0 0 4px;font-size:21px;letter-spacing:-.01em;color:var(--accent)}");
            sb.Append(".sub{color:var(--muted);margin-bottom:24px}");
            sb.Append(".grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(290px,1fr));gap:26px}");
            // 卡片：更大圆角 + 柔和阴影 + 悬停微微上浮
            sb.Append(".card{background:var(--bg2);border:1px solid var(--line);border-radius:18px;");
            sb.Append("padding:24px 26px;box-shadow:var(--shadow);");
            sb.Append("transition:transform .18s ease,box-shadow .18s ease,border-color .18s ease}");
            sb.Append(".card:hover{transform:translateY(-3px);box-shadow:var(--shadow-hover);");
            sb.Append("border-color:var(--accent2)}");
            sb.Append(".card h2{margin:0 0 16px;font-size:12px;color:var(--accent);font-weight:650;");
            sb.Append("text-transform:uppercase;letter-spacing:.08em}");
            sb.Append("table{width:100%;border-collapse:collapse;font-size:13px}");
            sb.Append("td{padding:6px 0;vertical-align:top}");
            sb.Append("td:first-child{color:var(--muted);width:46%}");
            sb.Append("pre{background:var(--code-bg);border:1px solid var(--line);border-radius:12px;");
            sb.Append("padding:14px 16px;margin:10px 0;overflow:auto;font-size:12.5px;line-height:1.6}");
            sb.Append("pre code{background:none;border:none;padding:0;color:var(--fg)}");
            sb.Append("code{background:var(--code-bg);border:1px solid var(--line);border-radius:6px;");
            sb.Append("padding:1px 7px;font-size:12px;color:var(--accent)}");
            sb.Append("a{color:var(--accent);text-decoration:none;border-bottom:1px solid var(--line)}");
            sb.Append("a:hover{border-bottom-color:var(--accent)}");
            sb.Append(".ok{color:#2fae6b}.bad{color:#e0574f}");
            sb.Append("@media (prefers-color-scheme:dark){.ok{color:#5fd39a}.bad{color:#ff7b72}}");
            sb.Append("ul{margin:8px 0;padding-left:20px}li{margin:5px 0}");
            sb.Append("input{background:var(--code-bg);border:1px solid var(--line);border-radius:9px;");
            sb.Append("color:var(--fg);padding:7px 11px;font:inherit;font-size:13px;transition:border-color .15s}");
            sb.Append("input:focus{outline:none;border-color:var(--accent2)}");
            sb.Append("button{border-radius:10px;border:1px solid var(--line);background:var(--bg2);");
            sb.Append("color:var(--fg);cursor:pointer;padding:8px 16px;font:inherit;font-size:13px;");
            sb.Append("transition:transform .12s,box-shadow .15s,background .15s,border-color .15s}");
            sb.Append("button:hover{transform:translateY(-1px);border-color:var(--accent2)}");
            sb.Append("button.primary{background:var(--accent);border-color:var(--accent);color:#fff}");
            sb.Append("button.primary:hover{filter:brightness(1.06)}");
            sb.Append("h1{letter-spacing:-.01em}");
            sb.Append(".steps{counter-reset:s;list-style:none;padding-left:0}");
            sb.Append(".steps li{counter-increment:s;position:relative;padding-left:34px;margin:12px 0}");
            sb.Append(".steps li::before{content:counter(s);position:absolute;left:0;top:1px;");
            sb.Append("width:23px;height:23px;border-radius:50%;background:var(--accent);color:#fff;");
            sb.Append("font-size:12px;font-weight:600;display:flex;align-items:center;justify-content:center}");
            sb.Append("button.copy{padding:4px 10px;font-size:12px;margin-left:8px}");
            sb.Append("#lang{position:fixed;top:22px;right:26px;border-radius:999px}");
            sb.Append(".cfgrow{display:flex;gap:9px;margin-bottom:8px;align-items:center}");
            sb.Append(".cfgrow .ck{flex:0 0 240px}");
            sb.Append(".cfgrow .cv{flex:1;min-width:0}");
            sb.Append(".cfgrow .del{color:var(--muted);padding:7px 12px}");
            sb.Append(".cfgrow .del:hover{color:var(--bad,#e0574f)}");
            sb.Append("</style></head><body>");

            sb.Append("<button id='lang' onclick='toggleLang()'>\u4e2d\u6587 / EN</button>");
            sb.Append("<h1>SFS-Agent</h1>");
            sb.Append("<div class='sub' id='sub' data-i18n='connecting'>\u2026</div>");

            sb.Append("<div class='grid'>");

            // 实时状态
            sb.Append("<div class='card'><h2 data-i18n='h_state'>\u72b6\u6001</h2>");
            sb.Append("<div id='state'>\u2026</div></div>");
            // 火箭
            sb.Append("<div class='card'><h2 data-i18n='h_rocket'>\u706b\u7bad</h2>");
            sb.Append("<div id='build'>\u2026</div></div>");
            // 界面
            sb.Append("<div class='card'><h2 data-i18n='h_ui'>\u754c\u9762</h2>");
            sb.Append("<div id='ui'>\u2026</div></div>");

            // 接口清单
            sb.Append("<div class='card'><h2>HTTP API</h2><table>");
            sb.Append("<tr><td><code>GET /ping</code></td><td data-i18n='api_ping'>\u5b58\u6d3b</td></tr>");
            sb.Append("<tr><td><code>GET /state</code></td><td data-i18n='api_state'>\u9065\u6d4b</td></tr>");
            sb.Append("<tr><td><code>GET /build</code></td><td data-i18n='api_build'>\u706b\u7bad\u6784\u6210</td></tr>");
            sb.Append("<tr><td><code>GET /build_catalog</code></td><td data-i18n='api_catalog'>\u53ef\u7528\u96f6\u4ef6</td></tr>");
            sb.Append("<tr><td><code>GET /blueprints</code></td><td data-i18n='api_bp'>\u84dd\u56fe\u5217\u8868</td></tr>");
            sb.Append("<tr><td><code>POST /blueprint_load</code></td><td data-i18n='api_bpl'>\u52a0\u8f7d\u84dd\u56fe</td></tr>");
            sb.Append("<tr><td><code>GET /ui</code></td><td data-i18n='api_ui'>\u53ef\u70b9\u51fb\u5143\u7d20</td></tr>");
            sb.Append("<tr><td><code>GET /screenshot</code></td><td data-i18n='api_shot'>\u622a\u56fe</td></tr>");
            sb.Append("<tr><td><code>POST /ui_click</code></td><td data-i18n='api_click'>\u70b9\u51fb</td></tr>");
            sb.Append("<tr><td><code>POST /key</code></td><td data-i18n='api_key'>\u6309\u952e</td></tr>");
            sb.Append("<tr><td><code>POST /command</code></td><td data-i18n='api_cmd'>\u98de\u884c\u6307\u4ee4</td></tr>");
            sb.Append("<tr><td><code>POST /camera</code></td><td data-i18n='api_cam'>\u89c6\u89d2\u63a7\u5236</td></tr>");
            sb.Append("<tr><td><code>POST /exclusive</code></td><td data-i18n='api_excl'>\u72ec\u5360\u6a21\u5f0f</td></tr>");
            sb.Append("<tr><td><code>GET/POST /config</code></td><td data-i18n='api_cfg'>\u8bfb\u5199\u914d\u7f6e</td></tr>");
            sb.Append("</table></div>");

            // 操作方式
            sb.Append("<div class='card'><h2 data-i18n='h_controls'>SFS \u64cd\u4f5c</h2><table>");
            sb.Append("<tr><td data-i18n='c_turn'>\u8f6c\u5411</td><td>Q / E</td></tr>");
            sb.Append("<tr><td data-i18n='c_move'>\u5e73\u79fb\u4e0e\u4fef\u4ef0</td><td>W / A / S / D</td></tr>");
            sb.Append("<tr><td data-i18n='c_thr'>\u6cb9\u95e8</td><td>Shift / Ctrl</td></tr>");
            sb.Append("<tr><td data-i18n='c_rcs'>RCS</td><td>R</td></tr>");
            sb.Append("<tr><td data-i18n='c_ign'>\u70b9\u706b / \u4e0b\u4e00\u7ea7</td><td data-i18n='c_space'>\u7a7a\u683c</td></tr>");
            sb.Append("<tr><td data-i18n='c_prog'>\u5206\u7ea7\u7a0b\u5e8f</td><td data-i18n='c_enter'>\u56de\u8f66</td></tr>");
            sb.Append("</table></div>");

            // 独占模式
            sb.Append("<div class='card'><h2 data-i18n='h_excl'>Agent Exclusive</h2>");
            sb.Append("<div class='sub' style='margin:0 0 10px' data-i18n='excl_desc'>\u2026</div>");
            sb.Append("<div style='display:flex;gap:8px;align-items:center;flex-wrap:wrap'>");
            sb.Append("<button id='exclBtn' class='primary'>\u2026</button>");
            sb.Append("<span id='exclState' class='sub' style='margin:0'></span>");
            sb.Append("</div></div>");

            // 配置（可编辑：动态列出 ini 里所有键，不写死字段）
            sb.Append("<div class='card' style='grid-column:1/-1'><h2 data-i18n='h_cfg'>\u914d\u7f6e</h2>");
            sb.Append("<div class='sub' style='margin:0 0 12px' data-i18n='cfg_desc'>\u2026</div>");
            sb.Append("<div id='cfg'>\u2026</div>");
            sb.Append("<div style='margin-top:12px;display:flex;gap:8px;flex-wrap:wrap'>");
            sb.Append("<button id='save' class='primary' data-i18n='save'>\u4fdd\u5b58</button>");
            sb.Append("<button id='add' data-i18n='add'>\u65b0\u589e\u4e00\u9879</button>");
            sb.Append("<span id='msg' style='align-self:center'></span>");
            sb.Append("</div>");
            sb.Append("<div class='sub' style='margin:10px 0 0'>");
            sb.Append("<span data-i18n='file'>\u6587\u4ef6</span>\uff1a<code>");
            sb.Append(Esc(Path.Combine("Mods", "SFS-Agent", "sfs-agent.ini")));
            sb.Append("</code></div></div>");

            // 如何接入（把对接点、步骤、示例都摊开）
            sb.Append("<div class='card' style='grid-column:1/-1'><h2 data-i18n='h_how'>\u5982\u4f55\u63a5\u5165</h2>");

            sb.Append("<p data-i18n='how_intro' style='margin:0 0 6px'>\u2026</p>");
            sb.Append("<p style='margin:0'><code>");
            sb.Append("http://127.0.0.1:");
            sb.Append(p);
            sb.Append("</code></p>");

            sb.Append("<ol class='steps'>");
            sb.Append("<li data-i18n='how_s1'>\u2026</li>");
            sb.Append("<li data-i18n='how_s2'>\u2026</li>");
            sb.Append("<li data-i18n='how_s3'>\u2026</li>");
            sb.Append("</ol>");

            sb.Append("<div style='margin-top:6px'><b data-i18n='how_examples'>\u793a\u4f8b</b>");
            sb.Append("<button class='copy' onclick='copyCode(this)'>copy</button></div>");
            sb.Append("<pre><code id='code1'>");
            sb.Append("curl http://127.0.0.1:" + p + "/state\n");
            sb.Append("curl http://127.0.0.1:" + p + "/ui\n");
            sb.Append("curl -X POST -H \"Content-Type: application/json\" \\\n");
            sb.Append("     -d '{\"index\":4}' http://127.0.0.1:" + p + "/ui_click\n");
            sb.Append("curl -X POST -H \"Content-Type: application/json\" \\\n");
            sb.Append("     -d '{\"vk\":32}' http://127.0.0.1:" + p + "/key");
            sb.Append("</code></pre>");

            sb.Append("<pre><code id='code2'>");
            sb.Append("# Python\n");
            sb.Append("import requests\n");
            sb.Append("B = \"http://127.0.0.1:" + p + "\"\n\n");
            sb.Append("state = requests.get(f\"{B}/state\").json()\n");
            sb.Append("ui    = requests.get(f\"{B}/ui\").json()\n");
            sb.Append("for e in ui[\"elements\"]:\n");
            sb.Append("    print(e[\"index\"], e[\"label\"], e.get(\"x\"), e.get(\"y\"))\n\n");
            sb.Append("requests.post(f\"{B}/ui_click\", json={\"index\": 4})   # 点 Play\n");
            sb.Append("requests.post(f\"{B}/key\", json={\"vk\": 32})           # 按空格（点火）\n");
            sb.Append("requests.post(f\"{B}/command\", json={\"name\": \"set_throttle\", \"value\": 1.0})");
            sb.Append("</code></pre>");

            sb.Append("<ul>");
            sb.Append("<li data-i18n='how_wrap1'>\u2026</li>");
            sb.Append("<li data-i18n='how_wrap2'>\u2026</li>");
            sb.Append("</ul></div>");

            // 需要配置什么
            sb.Append("<div class='card' style='grid-column:1/-1'><h2 data-i18n='h_need'>\u9700\u8981\u914d\u7f6e\u4ec0\u4e48</h2>");
            sb.Append("<table>");
            sb.Append("<tr><td data-i18n='need_port_l'>\u7aef\u53e3</td><td data-i18n='need_port_v'>\u2026</td></tr>");
            sb.Append("<tr><td data-i18n='need_vision_l'>\u89c6\u89c9\u6a21\u578b</td><td data-i18n='need_vision_v'>\u2026</td></tr>");
            sb.Append("<tr><td data-i18n='need_excl_l'>\u72ec\u5360\u6a21\u5f0f</td><td data-i18n='need_excl_v'>\u2026</td></tr>");
            sb.Append("<tr><td data-i18n='need_lang_l'>\u754c\u9762\u8bed\u8a00</td><td data-i18n='need_lang_v'>\u2026</td></tr>");
            sb.Append("</table></div>");

            // 说明
            sb.Append("<div class='card'><h2 data-i18n='h_notes'>\u8bf4\u660e</h2><ul>");
            sb.Append("<li data-i18n='n1'>\u2026</li>");
            sb.Append("<li data-i18n='n2'>\u2026</li>");
            sb.Append("<li data-i18n='n3'>\u2026</li>");
            sb.Append("</ul></div>");

            sb.Append("</div>");

            sb.Append("<script>");
            sb.Append("var P='" + p + "';");
            // 中英文字符串表（data-i18n 对应的键）
            sb.Append("var S={zh:{");
            sb.Append("connecting:'\u6b63\u5728\u8fde\u63a5 Spaceflight Simulator\u2026',");
            sb.Append("connected:'\u5df2\u8fde\u63a5',noconn:'\u8fde\u4e0d\u4e0a\u6865\u63a5\u670d\u52a1',");
            sb.Append("h_state:'\u5b9e\u65f6\u72b6\u6001',h_rocket:'\u706b\u7bad',h_ui:'\u53ef\u70b9\u51fb\u754c\u9762\u5143\u7d20',");
            sb.Append("in_world:'\u5728\u4e16\u754c\u4e2d',flying:'\u98de\u884c\u4e2d',rocket:'\u706b\u7bad',planet:'\u5929\u4f53',");
            sb.Append("height:'\u9ad8\u5ea6',speed:'\u901f\u5ea6',throttle:'\u6cb9\u95e8',stage:'\u5206\u7ea7',mass:'\u8d28\u91cf',");
            sb.Append("mode:'\u573a\u666f',parts:'\u96f6\u4ef6\u6570',stages:'\u5206\u7ea7\u6570',yes:'\u662f',no:'\u5426',");
            sb.Append("total:'\u5171',items:'\u4e2a',none:'\u65e0',");
            sb.Append("h_controls:'SFS \u64cd\u4f5c',c_turn:'\u8f6c\u5411',c_move:'\u5e73\u79fb\u4e0e\u4fef\u4ef0',");
            sb.Append("c_thr:'\u6cb9\u95e8',c_rcs:'RCS \u5f00\u5173',c_ign:'\u70b9\u706b / \u4e0b\u4e00\u7ea7',c_space:'\u7a7a\u683c',");
            sb.Append("c_prog:'\u5206\u7ea7\u7a0b\u5e8f',c_enter:'\u56de\u8f66',");
            sb.Append("api_ping:'\u5b58\u6d3b\u4e0e\u6309\u952e\u6ce8\u5165',api_state:'\u98de\u884c\u9065\u6d4b',");
            sb.Append("api_build:'\u706b\u7bad\u96f6\u4ef6\u6784\u6210',api_catalog:'\u53ef\u7528\u96f6\u4ef6\u540d',");
            sb.Append("api_bp:'\u84dd\u56fe\u5217\u8868',api_bpl:'\u52a0\u8f7d\u84dd\u56fe',");
            sb.Append("api_ui:'\u5f53\u524d\u53ef\u70b9\u51fb\u5143\u7d20',api_shot:'\u5f53\u524d\u753b\u9762',");
            sb.Append("api_click:'\u6309\u7d22\u5f15 / \u5750\u6807\u70b9\u51fb',api_key:'\u53d1\u9001\u6309\u952e',");
            sb.Append("api_cmd:'\u98de\u884c\u6307\u4ee4',api_cam:'\u89c6\u89d2\u63a7\u5236',");
            sb.Append("api_excl:'Agent \u72ec\u5360\u6a21\u5f0f',api_cfg:'\u8bfb\u5199\u914d\u7f6e',");
            sb.Append("h_excl:'Agent \u72ec\u5360\u6a21\u5f0f',");
            sb.Append("excl_desc:'\u5f00\u542f\u540e\u53ea\u63a5\u53d7 agent \u7684\u64cd\u4f5c\uff0c\u5ffd\u7565\u7528\u6237\u7684\u9f20\u6807\u4e0e\u952e\u76d8\u3002\u89e3\u9664\uff1a\u70b9\u5c4f\u5e55\u4e0a\u7684\u300c\u89e3\u9664\u72ec\u5360\u300d\u6216\u6309 F10\u3002',");
            sb.Append("excl_on:'\u5f00\u542f\u72ec\u5360',excl_off:'\u5173\u95ed\u72ec\u5360',");
            sb.Append("excl_state_on:'\u5f53\u524d\uff1a\u72ec\u5360\u4e2d',excl_state_off:'\u5f53\u524d\uff1a\u666e\u901a',");
            sb.Append("h_cfg:'\u914d\u7f6e',");
            sb.Append("cfg_desc:'\u76f4\u63a5\u7f16\u8f91\u4e0b\u9762\u7684\u952e\u503c\u540e\u70b9\u4fdd\u5b58\u3002port \u4e0e open_browser \u9700\u91cd\u542f\u6e38\u620f\u751f\u6548\uff0c\u5176\u4f59\u7acb\u5373\u751f\u6548\u3002\u4e5f\u53ef\u4ee5\u81ea\u5df1\u52a0\u65b0\u952e\u3002',");
            sb.Append("save:'\u4fdd\u5b58',add:'\u65b0\u589e\u4e00\u9879',del:'\u5220',file:'\u6587\u4ef6',");
            sb.Append("saved:'\u5df2\u4fdd\u5b58',changed:'\u9879\u53d8\u66f4',saving:'\u4fdd\u5b58\u4e2d\u2026',failed:'\u5931\u8d25',empty:'\uff08\u7a7a\uff09',");
            sb.Append("h_notes:'\u8bf4\u660e',");
            sb.Append("n1:'\u53ea\u76d1\u542c 127.0.0.1\uff0c\u4e0d\u5bf9\u5c40\u57df\u7f51\u5f00\u653e\u3002',");
            sb.Append("n2:'\u70b9\u51fb\u4e0e\u6309\u952e\u90fd\u662f\u6e38\u620f\u5185\u6ce8\u5165\uff0c\u4e0d\u62a2\u9f20\u6807\u4e0e\u7126\u70b9\u3002',");
            sb.Append("n3:'\u672c\u9875\u6570\u636e\u5168\u90e8\u6765\u81ea\u672c\u5730\u6865\u63a5\u670d\u52a1\uff0c\u4e0d\u8054\u7f51\u3002',");
            sb.Append("h_how:'\u5982\u4f55\u63a5\u5165',");
            sb.Append("how_intro:'\u672c\u6a21\u7ec4\u5c31\u662f\u4e00\u4e2a\u672c\u5730 HTTP \u63a5\u53e3\u3002\u4efb\u4f55\u80fd\u8bbf\u95ee\u672c\u673a\u7684\u7a0b\u5e8f\uff08\u811a\u672c\u3001AI \u52a9\u624b\u3001\u8c03\u8bd5\u5de5\u5177\uff09\u90fd\u80fd\u76f4\u63a5\u63a5\u5165\uff0c\u65e0\u9700\u5bc6\u94a5\u3001\u65e0\u9700\u6ce8\u518c\u3002\u5730\u5740\uff1a',");
            sb.Append("how_s1:'\u5148\u8c03 <code>GET /ui</code> \u62ff\u5230\u5f53\u524d\u754c\u9762\u7684\u53ef\u70b9\u51fb\u5143\u7d20\uff08\u5e26\u7d22\u5f15\u4e0e\u5750\u6807\uff09\u3002',");
            sb.Append("how_s2:'\u7528 <code>POST /ui_click</code> \u4f20 index \u70b9\u51fb\uff0c<code>POST /key</code> \u53d1\u6309\u952e\uff0c<code>POST /command</code> \u53d1\u98de\u884c\u6307\u4ee4\u3002',");
            sb.Append("how_s3:'\u70b9\u51fb\u4f1a\u81ea\u52a8\u7b49\u7ea6 3 \u79d2\u518d\u56de\u8bfb\u754c\u9762\uff1b\u6ca1\u53d8\u5316\u65f6\u5148\u522b\u5f53\u6210\u300c\u6ca1\u53cd\u5e94\u300d\u3002',");
            sb.Append("how_examples:'\u793a\u4f8b',");
            sb.Append("how_wrap1:'\ud83d\udc31 <b>N.E.K.O. \u732b\u5a18</b>\uff1a\u88c5\u5bf9\u5e94\u7684\u63d2\u4ef6\u5373\u53ef\uff0c\u63d2\u4ef6\u4f1a\u81ea\u5df1\u8c03\u8fd9\u4e9b\u63a5\u53e3\uff1a<a href=\"https://github.com/LShangPiao/n.e.k.o_plugin_sfs_bridge\" target=\"_blank\">n.e.k.o_plugin_sfs_bridge</a>',");
            sb.Append("how_wrap2:'\ud83e\udd16 <b>\u5176\u4ed6 AI / \u81ea\u5df1\u7684\u811a\u672c</b>\uff1a\u76f4\u63a5 HTTP \u8c03\u7528\u5373\u53ef\u3002\u628a <code>/ui</code> \u7684\u6e05\u5355\u4ea4\u7ed9\u6a21\u578b\uff0c\u8ba9\u5b83\u9009 index\uff0c\u518d\u8c03 <code>/ui_click</code> \u5c31\u80fd\u8ba9\u5b83\u73a9\u6e38\u620f\u3002',");
            sb.Append("h_need:'\u9700\u8981\u914d\u7f6e\u4ec0\u4e48',");
            sb.Append("need_port_l:'\u7aef\u53e3',");
            sb.Append("need_port_v:'\u9ed8\u8ba4 21578\uff0c\u5728\u4e0b\u9762\u300c\u914d\u7f6e\u300d\u91cc\u6539\uff0c\u6539\u5b8c\u9700\u91cd\u542f\u6e38\u620f\u3002',");
            sb.Append("need_vision_l:'\u89c6\u89c9\u6a21\u578b\uff08\u770b\u753b\u9762\uff09',");
            sb.Append("need_vision_v:'N.E.K.O. \u8bbe\u7f6e \u2192 API \u2192 \u89c6\u89c9\u804a\u5929\u6a21\u578b\u3002\u4e0d\u914d\u7684\u8bdd\u732b\u5a18\u770b\u4e0d\u5230\u6e38\u620f\u753b\u9762\uff08\u4f1a\u5982\u5b9e\u544a\u8bc9\u4f60\uff0c\u4e0d\u4f1a\u80e1\u7f16\uff09\u3002',");
            sb.Append("need_excl_l:'Agent \u72ec\u5360\u6a21\u5f0f',");
            sb.Append("need_excl_v:'\u9ed8\u8ba4\u5173\u3002\u5f00\u542f\u540e\u53ea\u6709 agent \u80fd\u64cd\u4f5c\u6e38\u620f\uff1b\u4e0a\u9762\u90a3\u5f20\u5361\u7247\u53ef\u4ee5\u4e00\u952e\u5f00\u5173\u3002',");
            sb.Append("need_lang_l:'\u754c\u9762\u8bed\u8a00',");
            sb.Append("need_lang_v:'\u4e0b\u9762\u914d\u7f6e\u91cc\u7684 lang\uff08zh / en\uff09\u3002\u6539\u5b8c\u6e38\u620f\u5185\u63d0\u793a\u4e5f\u4f1a\u8ddf\u7740\u53d8\u3002'");
            sb.Append("},en:{");            sb.Append("connecting:'Connecting to Spaceflight Simulator\u2026',");
            sb.Append("connected:'Connected',noconn:'Bridge unreachable',");
            sb.Append("h_state:'Live status',h_rocket:'Rocket',h_ui:'Clickable UI elements',");
            sb.Append("in_world:'In world',flying:'Flying',rocket:'Rocket',planet:'Body',");
            sb.Append("height:'Height',speed:'Speed',throttle:'Throttle',stage:'Stage',mass:'Mass',");
            sb.Append("mode:'Scene',parts:'Parts',stages:'Stages',yes:'yes',no:'no',");
            sb.Append("total:'total',items:'',none:'none',");
            sb.Append("h_controls:'SFS controls',c_turn:'Turn',c_move:'Translate / pitch',");
            sb.Append("c_thr:'Throttle',c_rcs:'RCS toggle',c_ign:'Ignite / next stage',c_space:'Space',");
            sb.Append("c_prog:'Staging program',c_enter:'Enter',");
            sb.Append("api_ping:'Liveness & key injection',api_state:'Flight telemetry',");
            sb.Append("api_build:'Rocket part breakdown',api_catalog:'Available part names',");
            sb.Append("api_bp:'Blueprint list',api_bpl:'Load a blueprint',");
            sb.Append("api_ui:'Current clickable elements',api_shot:'Current frame',");
            sb.Append("api_click:'Click by index / coords',api_key:'Send a key',");
            sb.Append("api_cmd:'Flight commands',api_cam:'Camera control',");
            sb.Append("api_excl:'Agent exclusive mode',api_cfg:'Read / write config',");
            sb.Append("h_excl:'Agent Exclusive Mode',");
            sb.Append("excl_desc:'When on, only the agent can control the game \u2014 your mouse and keyboard are ignored. Unlock via the on-screen button or F10.',");
            sb.Append("excl_on:'Enable exclusive',excl_off:'Disable exclusive',");
            sb.Append("excl_state_on:'now: exclusive',excl_state_off:'now: normal',");
            sb.Append("h_cfg:'Configuration',");
            sb.Append("cfg_desc:'Edit the key/value pairs below and hit save. port and open_browser need a game restart; everything else applies immediately. You may add your own keys.',");
            sb.Append("save:'Save',add:'Add entry',del:'del',file:'File',");
            sb.Append("saved:'saved',changed:'changed',saving:'saving\u2026',failed:'failed',empty:'(empty)',");
            sb.Append("h_notes:'Notes',");
            sb.Append("n1:'Listens on 127.0.0.1 only \u2014 not exposed to the LAN.',");
            sb.Append("n2:'Clicks and keys are injected in-game; your mouse and focus are untouched.',");
            sb.Append("n3:'Everything on this page comes from the local bridge; nothing goes online.',");
            sb.Append("h_how:'How to integrate',");
            sb.Append("how_intro:'This mod is just a local HTTP API. Any program that can reach localhost (scripts, AI agents, debug tools) can talk to it \u2014 no key, no signup. Address:',");
            sb.Append("how_s1:'Call <code>GET /ui</code> to get the clickable elements of the current screen (with index and coordinates).',");
            sb.Append("how_s2:'Use <code>POST /ui_click</code> with an index to click, <code>POST /key</code> to send a key, <code>POST /command</code> for flight commands.',");
            sb.Append("how_s3:'Clicks wait about 3 seconds and then re-read the screen \u2014 if nothing changed, do not jump to \\'no response\\'.',");
            sb.Append("how_examples:'Examples',");
            sb.Append("how_wrap1:'\ud83d\udc31 <b>N.E.K.O. assistant</b>: just install the matching plugin \u2014 it calls these endpoints for you: <a href=\"https://github.com/LShangPiao/n.e.k.o_plugin_sfs_bridge\" target=\"_blank\">n.e.k.o_plugin_sfs_bridge</a>',");
            sb.Append("how_wrap2:'\ud83e\udd16 <b>Other agents / your own script</b>: plain HTTP. Feed the <code>/ui</code> list to your model, let it pick an index, then call <code>/ui_click</code> \u2014 that is enough to let it play.',");
            sb.Append("h_need:'What to configure',");
            sb.Append("need_port_l:'Port',");
            sb.Append("need_port_v:'Defaults to 21578. Change it in Configuration below; a game restart is required.',");
            sb.Append("need_vision_l:'Vision model (to see the screen)',");
            sb.Append("need_vision_v:'N.E.K.O. Settings \u2192 API \u2192 vision chat model. Without it the assistant cannot see the game screen (it will tell you honestly instead of guessing).',");
            sb.Append("need_excl_l:'Agent exclusive mode',");
            sb.Append("need_excl_v:'Off by default. When on, only the agent can control the game; toggle it with the card above.',");
            sb.Append("need_lang_l:'UI language',");
            sb.Append("need_lang_v:'The lang key in Configuration below (zh / en). The in-game overlay follows it too.'");
            sb.Append("}};");
            sb.Append("var LANG='zh';");
            sb.Append("function t(k){return (S[LANG]&&S[LANG][k])||(S.zh[k])||k;}");
            sb.Append("function applyLang(){");
            sb.Append("document.documentElement.lang=(LANG==='zh'?'zh-CN':'en');");
            sb.Append("var els=document.querySelectorAll('[data-i18n]');");
            sb.Append("for(var i=0;i<els.length;i++){var k=els[i].getAttribute('data-i18n');");
            sb.Append("if(S[LANG][k]!==undefined)els[i].textContent=t(k);}");
            sb.Append("q('#exclBtn').textContent=EXCL?t('excl_off'):t('excl_on');");
            sb.Append("q('#exclState').textContent=EXCL?t('excl_state_on'):t('excl_state_off');}");
            sb.Append("function toggleLang(){LANG=(LANG==='zh'?'en':'zh');");
            sb.Append("try{localStorage.setItem('sfsagent_lang',LANG);}catch(e){}");
            sb.Append("applyLang();cfgLoad();tick();}");
            sb.Append("function copyCode(btn){");
            sb.Append("var pre=btn.parentNode.parentNode.querySelector('pre code');");
            sb.Append("if(!pre)return;");
            sb.Append("var txt=pre.textContent;");
            sb.Append("if(navigator.clipboard){navigator.clipboard.writeText(txt);}");
            sb.Append("else{var a=document.createElement('textarea');a.value=txt;document.body.appendChild(a);");
            sb.Append("a.select();document.execCommand('copy');document.body.removeChild(a);}");
            sb.Append("btn.textContent='copied';setTimeout(function(){btn.textContent='copy';},1200);}");
            sb.Append("try{var sl=localStorage.getItem('sfsagent_lang');if(sl==='en'||sl==='zh')LANG=sl;}catch(e){}");
            sb.Append("function q(s){return document.querySelector(s);}");
            sb.Append("function row(k,v){return '<tr><td>'+k+'</td><td>'+v+'</td></tr>';}");
            sb.Append("function esc(s){return String(s).replace(/[&<>]/g,function(c){");
            sb.Append("return {'&':'&amp;','<':'&lt;','>':'&gt;'}[c];});}");
            sb.Append("var EXCL=false;");
            sb.Append("async function tick(){");
            sb.Append("try{");
            sb.Append("var p=await (await fetch('/ping')).json();");
            sb.Append("q('#sub').innerHTML=t('connected')+' \u00b7 <b class=\"'+(p.key_injection==='on'?'ok':'bad')+'\">'+");
            sb.Append("esc(p.key_injection)+'</b> '+esc(p.key_injection_info||'');");
            sb.Append("var s=await (await fetch('/state')).json();");
            sb.Append("q('#state').innerHTML='<table>'+");
            sb.Append("row(t('in_world'),s.in_world?t('yes'):t('no'))+");
            sb.Append("row(t('flying'),s.flying?t('yes'):t('no'))+");
            sb.Append("row(t('rocket'),esc(s.rocket||'\u2014'))+");
            sb.Append("row(t('planet'),esc(s.planet||'\u2014'))+");
            sb.Append("row(t('height'),(s.height||0).toFixed(1)+' m')+");
            sb.Append("row(t('speed'),(s.speed||0).toFixed(1)+' m/s')+");
            sb.Append("row(t('throttle'),((s.throttle||0)*100).toFixed(0)+'%')+");
            sb.Append("row(t('stage'),s.stage)+");
            sb.Append("row(t('mass'),(s.mass||0).toFixed(1)+' t')+'</table>';");
            sb.Append("var b=await (await fetch('/build')).json();");
            sb.Append("var ks=(b.part_kinds||[]).map(function(x){return esc(x.name)+' \u00d7 '+x.count}).join('<br>');");
            sb.Append("q('#build').innerHTML='<table>'+");
            sb.Append("row(t('mode'),esc(b.mode))+");
            sb.Append("row(t('parts'),b.part_count)+");
            sb.Append("row(t('mass'),(b.total_mass||0).toFixed(2)+' t')+");
            sb.Append("row(t('stages'),b.stage_count)+'</table><div style=\"margin-top:8px\">'+(ks||'\u2014')+'</div>';");
            sb.Append("var u=await (await fetch('/ui')).json();");
            sb.Append("var us=(u.elements||[]).slice(0,12).map(function(e){");
            sb.Append("return '#'+e.index+' '+esc(e.label||t('none'))+");
            sb.Append("(e.x!==undefined?' @ '+e.x.toFixed(2)+','+e.y.toFixed(2):'');}).join('<br>');");
            sb.Append("q('#ui').innerHTML=t('total')+' '+u.count+' '+t('items')+'<div style=\"margin-top:8px\">'+(us||'\u2014')+'</div>';");
            sb.Append("}catch(e){q('#sub').innerHTML='<span class=\"bad\">'+t('noconn')+'</span> \u00b7 '+esc(e);}");
            sb.Append("}");
            sb.Append("tick();setInterval(tick,1500);");

            // 独占模式：由页面直接开关（游戏里那条提示会同步跟着变）
            sb.Append("function exclBtnLabel(){q('#exclBtn').textContent=EXCL?t('excl_off'):t('excl_on');");
            sb.Append("q('#exclState').textContent=EXCL?t('excl_state_on'):t('excl_state_off');}");
            sb.Append("q('#exclBtn').onclick=async function(e){e.preventDefault();");
            sb.Append("try{var r=await (await fetch('/exclusive',{method:'POST',");
            sb.Append("headers:{'Content-Type':'application/json'},body:JSON.stringify({on:!EXCL})})).json();");
            sb.Append("EXCL=!!r.exclusive;exclBtnLabel();}catch(ex){q('#exclState').textContent=esc(ex);}};");
            sb.Append("(async function(){try{var c=await (await fetch('/config')).json();");
            sb.Append("var v=String(c.exclusive_input||'').toLowerCase();");
            sb.Append("EXCL=(v==='1'||v==='true'||v==='on');}catch(e){}exclBtnLabel();})();");

            // 配置加载 / 保存
            sb.Append("var CKEY='cfgkeys';");
            sb.Append("function cfgRow(k,v){");
            sb.Append("return \"<div class='cfgrow'>\"+");
            sb.Append("\"<input value='\"+esc(k)+\"' data-k='1' class='ck'>\"+");
            sb.Append("\"<input value='\"+esc(v)+\"' data-v='1' class='cv'>\"+");
            sb.Append("\"<button data-del='1' class='del'>\"+t('del')+\"</button></div>\";}");
            sb.Append("function cfgCollect(){");
            sb.Append("var rows=q('#cfg').querySelectorAll('div[style*=flex]');var o={};");
            sb.Append("for(var i=0;i<rows.length;i++){");
            sb.Append("var k=rows[i].querySelector('input[data-k]').value.trim();");
            sb.Append("var v=rows[i].querySelector('input[data-v]').value;");
            sb.Append("if(k)o[k]=v;}return o;}");
            sb.Append("async function cfgLoad(){");
            sb.Append("var c=await (await fetch('/config')).json();");
            sb.Append("var html='';for(var k in c){html+=cfgRow(k,c[k]);}");
            sb.Append("q('#cfg').innerHTML=html||\"<span class='sub'>\"+t('empty')+\"</span>\";}");
            sb.Append("q('#add').onclick=function(e){e.preventDefault();");
            sb.Append("q('#cfg').insertAdjacentHTML('beforeend',cfgRow('',''));};");
            sb.Append("q('#cfg').addEventListener('click',function(e){");
            sb.Append("if(e.target.hasAttribute&&e.target.hasAttribute('data-del')){");
            sb.Append("e.target.parentNode.parentNode.removeChild(e.target.parentNode);}});");
            sb.Append("q('#save').onclick=async function(e){e.preventDefault();");
            sb.Append("q('#msg').textContent=t('saving');");
            sb.Append("try{var r=await (await fetch('/config',{method:'POST',");
            sb.Append("headers:{'Content-Type':'application/json'},body:JSON.stringify(cfgCollect())})).json();");
            sb.Append("q('#msg').innerHTML=r.ok?\"<span class='ok'>\"+t('saved')+\" (\"+r.changed+\" \"+t('changed')+\")</span>\":");
            sb.Append("\"<span class='bad'>\"+esc(r.error||t('failed'))+\"</span>\";");
            sb.Append("if(r.ok)cfgLoad();}catch(ex){q('#msg').innerHTML=\"<span class='bad'>\"+esc(ex)+\"</span>\";}};");
            sb.Append("cfgLoad();");
            sb.Append("applyLang();");
            sb.Append("</script></body></html>");

            return sb.ToString();
        }

        /// <summary>用默认浏览器打开配置页。失败静默（不影响游戏）。</summary>
        public static void OpenInBrowser(int port)
        {
            try
            {
                string url = "http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture) + "/";
                System.Diagnostics.Process.Start(url);
            }
            catch (Exception ex)
            {
                Main.Log("open browser failed: " + ex.Message);
            }
        }

        /// <summary>
        /// 读取 Mods/SFS-Agent/sfs-agent.ini（简单的 key=value）。
        /// 不存在就用默认值 —— 绝不因为缺配置而阻止模组工作。
        /// </summary>
        public static void LoadConfig(string iniPath, out int port, out bool openBrowser)
        {
            port = 21578;
            openBrowser = true;
            try
            {
                if (!File.Exists(iniPath))
                {
                    WriteDefault(iniPath);
                    return;
                }
                string[] lines = File.ReadAllLines(iniPath);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0 || line[0] == '#' || line[0] == ';')
                    {
                        continue;
                    }
                    int eq = line.IndexOf('=');
                    if (eq <= 0)
                    {
                        continue;
                    }
                    string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string val = line.Substring(eq + 1).Trim();
                    if (key == "port")
                    {
                        int parsed;
                        if (int.TryParse(val, NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out parsed)
                            && parsed > 1024 && parsed < 65536)
                        {
                            port = parsed;
                        }
                    }
                    else if (key == "open_browser")
                    {
                        openBrowser = val == "1" || val.ToLowerInvariant() == "true";
                    }
                }
            }
            catch (Exception ex)
            {
                Main.Log("read config failed: " + ex.Message);
            }
        }

        // -- 通用配置读写（页面用）------------------------------------------
        //
        // 故意做成**任意 key=value**，而不是固定几个字段：
        // 以后加配置项不用改页面，用户也能自己塞自定义项。

        public static string ReadAllJson(string iniPath)
        {
            StringBuilder sb = new StringBuilder(512);
            sb.Append("{");
            bool first = true;
            try
            {
                if (File.Exists(iniPath))
                {
                    string[] lines = File.ReadAllLines(iniPath);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string line = lines[i].Trim();
                        if (line.Length == 0 || line[0] == '#' || line[0] == ';')
                        {
                            continue;
                        }
                        int eq = line.IndexOf('=');
                        if (eq <= 0)
                        {
                            continue;
                        }
                        string k = line.Substring(0, eq).Trim();
                        string v = line.Substring(eq + 1).Trim();
                        if (k.Length == 0)
                        {
                            continue;
                        }
                        if (!first)
                        {
                            sb.Append(",");
                        }
                        first = false;
                        sb.Append("\"").Append(Esc(k)).Append("\":\"")
                          .Append(Esc(v)).Append("\"");
                    }
                }
            }
            catch (Exception ex)
            {
                return "{\"ok\":false,\"error\":\"" + Esc(ex.Message) + "\"}";
            }
            sb.Append("}");
            return sb.ToString();
        }

        /// <summary>
        /// 合并写入配置：只覆盖请求里出现的键，其余保留。
        /// 这样页面提交一个字段也不会把别的配置抹掉。
        /// </summary>
        public static string MergeWriteJson(string iniPath, string body)
        {
            try
            {
                Dictionary<string, string> cfg = ReadAllMap(iniPath);
                List<string> order = new List<string>();
                foreach (KeyValuePair<string, string> kv in cfg)
                {
                    order.Add(kv.Key);
                }

                Dictionary<string, string> incoming = ParseFlatJson(body);
                int changed = 0;
                foreach (KeyValuePair<string, string> kv in incoming)
                {
                    if (!order.Contains(kv.Key))
                    {
                        order.Add(kv.Key);
                    }
                    if (!cfg.ContainsKey(kv.Key) || cfg[kv.Key] != kv.Value)
                    {
                        changed++;
                    }
                    cfg[kv.Key] = kv.Value;
                }

                StringBuilder sb = new StringBuilder(512);
                sb.Append("# SFS-Agent 配置\r\n");
                sb.Append("# 端口与「自动打开本页」需要重启游戏才生效\r\n");
                for (int i = 0; i < order.Count; i++)
                {
                    string k = order[i];
                    if (cfg.ContainsKey(k))
                    {
                        sb.Append(k).Append("=").Append(cfg[k]).Append("\r\n");
                    }
                }
                File.WriteAllText(iniPath, sb.ToString(), new UTF8Encoding(false));

                return "{\"ok\":true,\"changed\":" + changed + "}";
            }
            catch (Exception ex)
            {
                return "{\"ok\":false,\"error\":\"" + Esc(ex.Message) + "\"}";
            }
        }

        private static Dictionary<string, string> ReadAllMap(string iniPath)
        {
            Dictionary<string, string> map = new Dictionary<string, string>();
            if (!File.Exists(iniPath))
            {
                map["port"] = "21578";
                map["open_browser"] = "true";
                map["exclusive_input"] = "false";
                map["overlay"] = "true";
                return map;
            }
            string[] lines = File.ReadAllLines(iniPath);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line[0] == '#' || line[0] == ';')
                {
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }
                map[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            return map;
        }

        /// <summary>极简的扁平 JSON 解析：{"k":"v", ...}。</summary>
        private static Dictionary<string, string> ParseFlatJson(string json)
        {
            Dictionary<string, string> map = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(json))
            {
                return map;
            }
            int i = 0;
            int n = json.Length;
            while (i < n)
            {
                int k1 = json.IndexOf('"', i);
                if (k1 < 0)
                {
                    break;
                }
                int k2 = json.IndexOf('"', k1 + 1);
                if (k2 < 0)
                {
                    break;
                }
                string key = json.Substring(k1 + 1, k2 - k1 - 1);
                int colon = json.IndexOf(':', k2);
                if (colon < 0)
                {
                    break;
                }
                int v1 = json.IndexOf('"', colon);
                if (v1 < 0)
                {
                    break;
                }
                int v2 = json.IndexOf('"', v1 + 1);
                if (v2 < 0)
                {
                    break;
                }
                string val = json.Substring(v1 + 1, v2 - v1 - 1);
                if (key.Length > 0)
                {
                    map[key] = val;
                }
                i = v2 + 1;
            }
            return map;
        }

        /// <summary>读一个字符串配置项。</summary>
        public static string ReadValue(string iniPath, string key, string fallback)
        {
            try
            {
                Dictionary<string, string> map = ReadAllMap(iniPath);
                string v;
                if (map.TryGetValue(key, out v) && v != null)
                {
                    return v;
                }
            }
            catch
            {
            }
            return fallback;
        }

        /// <summary>读一个开关型配置项，缺省返回 fallback。</summary>
        public static bool ReadFlag(string iniPath, string key, bool fallback)
        {
            try
            {
                Dictionary<string, string> map = ReadAllMap(iniPath);
                string v;
                if (map.TryGetValue(key, out v))
                {
                    v = v.Trim().ToLowerInvariant();
                    if (v == "1" || v == "true" || v == "on")
                    {
                        return true;
                    }
                    if (v == "0" || v == "false" || v == "off")
                    {
                        return false;
                    }
                }
            }
            catch
            {
            }
            return fallback;
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

        private static void WriteDefault(string iniPath)
        {
            try
            {
                File.WriteAllText(
                    iniPath,
                    "# SFS-Agent 配置\r\n"
                    + "# 桥接服务端口（改完要重启游戏）\r\n"
                    + "port=21578\r\n"
                    + "# 游戏启动后是否自动打开配置页\r\n"
                    + "open_browser=true\r\n"
                    + "# 是否进入 Agent 独占模式：只接受 agent 的操作，忽略用户的鼠标与键盘\r\n"
                    + "exclusive_input=false\r\n"
                    + "# 独占模式下是否显示屏幕提示（上下淡蓝渐变 + 文字）\r\n"
                    + "overlay=true\r\n"
                    + "# 界面语言：zh 或 en（配置页右上角可一键切换）\r\n"
                    + "lang=zh\r\n",
                    new UTF8Encoding(false));
            }
            catch
            {
                // 写不了就算了，用默认值继续
            }
        }
    }
}
