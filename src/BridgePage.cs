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
            sb.Append("<div class='sub' id='sub' data-i18n='connecting'>\u2014</div>");

            sb.Append("<div class='grid'>");

            // 实时状态
            sb.Append("<div class='card'><h2 data-i18n='h_state'>\u72b6\u6001</h2>");
            sb.Append("<div id='state'>\u2014</div></div>");
            // 火箭
            sb.Append("<div class='card'><h2 data-i18n='h_rocket'>\u706b\u7bad</h2>");
            sb.Append("<div id='build'>\u2014</div></div>");
            // 轨道（SFS.World.Orbit）
            sb.Append("<div class='card'><h2 data-i18n='h_orbit'>\u8f68\u9053</h2>");
            sb.Append("<div id='orbit'>\u2014</div></div>");
            // 界面（完整列表，可滚动 + 可搜索）
            sb.Append("<div class='card'><h2 data-i18n='h_ui'>\u754c\u9762</h2>");
            sb.Append("<input id='uiSearch' spellcheck='false' placeholder='\u641c\u7d22\u6807\u7b7e\u2026' ");
            sb.Append("style='width:100%;margin-bottom:8px'>");
            sb.Append("<div id='uiCount' class='sub' style='margin:0 0 8px'></div>");
            sb.Append("<div id='ui' style='max-height:320px;overflow:auto;font-size:12.5px;line-height:1.7'></div></div>");

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
            sb.Append("<div class='sub' style='margin:0 0 10px' data-i18n='excl_desc'>\u2014</div>");
            sb.Append("<div style='display:flex;gap:8px;align-items:center;flex-wrap:wrap'>");
            sb.Append("<button id='exclBtn' class='primary'>\u2026</button>");
            sb.Append("<span id='exclState' class='sub' style='margin:0'></span>");
            sb.Append("</div></div>");

            // 配置（可编辑：动态列出 ini 里所有键，不写死字段）
            sb.Append("<div class='card' style='grid-column:1/-1'><h2 data-i18n='h_cfg'>\u914d\u7f6e</h2>");
            sb.Append("<div class='sub' style='margin:0 0 12px' data-i18n='cfg_desc'>\u2014</div>");
            sb.Append("<div id='cfg'>\u2014</div>");
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

            // 游戏设置（读写 SFS 自身的音量 / 画面 / 帧率）
            sb.Append("<div class='card' style='grid-column:1/-1'><h2 data-i18n='h_game'>\u6e38\u620f\u8bbe\u7f6e</h2>");
            sb.Append("<div class='sub' style='margin:0 0 12px' data-i18n='game_desc'>\u2014</div>");
            sb.Append("<div id='gameSet'>\u2014</div></div>");

            // 日志（实时）
            sb.Append("<div class='card' style='grid-column:1/-1'><h2 data-i18n='h_log'>\u65e5\u5fd7</h2>");
            sb.Append("<div style='display:flex;gap:8px;margin-bottom:10px;flex-wrap:wrap;align-items:center'>");
            sb.Append("<button id='logPause' data-i18n='pause'>\u6682\u505c</button>");
            sb.Append("<button id='logClear' data-i18n='clear'>\u6e05\u7a7a</button>");
            sb.Append("<button id='logOps'>\u53ea\u770b\u64cd\u4f5c</button>");
            sb.Append("<span id='logInfo' class='sub' style='margin:0'></span>");
            sb.Append("</div>");
            sb.Append("<pre id='logBox' style='max-height:300px;overflow:auto;margin:0;");
            sb.Append("font-size:12px;line-height:1.6'></pre></div>");

            // 命令行（直接调任意接口）
            sb.Append("<div class='card' style='grid-column:1/-1'><h2 data-i18n='h_cmd'>\u547d\u4ee4\u884c</h2>");
            sb.Append("<div class='sub' style='margin:0 0 10px' data-i18n='cmd_desc'>\u2014</div>");
            sb.Append("<div style='display:flex;gap:8px'>");
            sb.Append("<input id='cmdIn' spellcheck='false' autocomplete='off' ");
            sb.Append("style='flex:1;font-family:ui-monospace,Consolas,monospace' placeholder='GET /state'>");
            sb.Append("<button id='cmdRun' class='primary' data-i18n='run'>\u6267\u884c</button>");
            sb.Append("</div>");
            sb.Append("<div style='margin-top:8px;display:flex;gap:6px;flex-wrap:wrap'>");
            sb.Append("<button class='quick' data-cmd='GET /state' data-i18n='q_state'>\u9065\u6d4b</button>");
            sb.Append("<button class='quick' data-cmd='GET /ui' data-i18n='q_ui'>\u754c\u9762</button>");
            sb.Append("<button class='quick' data-cmd='GET /build' data-i18n='q_build'>\u706b\u7bad</button>");
            sb.Append("<button class='quick' data-cmd='GET /blueprints' data-i18n='q_bp'>\u84dd\u56fe</button>");
            sb.Append("<button class='quick' data-cmd='GET /settings' data-i18n='q_set'>\u8bbe\u7f6e</button>");
            sb.Append("<button class='quick' data-cmd='GET /ping' data-i18n='q_ping'>\u5b58\u6d3b</button>");
            sb.Append("</div>");
            sb.Append("<pre id='cmdOut' style='max-height:320px;overflow:auto;margin:10px 0 0;");
            sb.Append("font-size:12px;line-height:1.6'></pre></div>");

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
            sb.Append("excl_desc:'\u5f00\u542f\u540e\u53ea\u63a5\u53d7 agent \u7684\u64cd\u4f5c\uff0c\u5ffd\u7565\u7528\u6237\u7684\u9f20\u6807\u4e0e\u952e\u76d8\u3002\u89e3\u9664\uff1a\u5728\u672c\u9875\u518d\u70b9\u4e00\u6b21\uff08\u6216\u6309 F10 \u5e94\u6025\uff09\u3002',");
            sb.Append("excl_on:'\u5f00\u542f\u72ec\u5360',excl_off:'\u5173\u95ed\u72ec\u5360',");
            sb.Append("excl_state_on:'\u5f53\u524d\uff1a\u72ec\u5360\u4e2d',excl_state_off:'\u5f53\u524d\uff1a\u666e\u901a',");
            sb.Append("h_cfg:'\u914d\u7f6e',");
            sb.Append("cfg_desc:'\u76f4\u63a5\u7f16\u8f91\u4e0b\u9762\u7684\u952e\u503c\u540e\u70b9\u4fdd\u5b58\u3002port \u4e0e open_browser \u9700\u91cd\u542f\u6e38\u620f\u751f\u6548\uff0c\u5176\u4f59\u7acb\u5373\u751f\u6548\u3002\u4e5f\u53ef\u4ee5\u81ea\u5df1\u52a0\u65b0\u952e\u3002',");
            sb.Append("save:'\u4fdd\u5b58',add:'\u65b0\u589e\u4e00\u9879',del:'\u5220',file:'\u6587\u4ef6',");
            sb.Append("saved:'\u5df2\u4fdd\u5b58',changed:'\u9879\u53d8\u66f4',saving:'\u4fdd\u5b58\u4e2d\u2026',failed:'\u5931\u8d25',empty:'\uff08\u7a7a\uff09',");
            sb.Append("h_notes:'\u8bf4\u660e',");
            sb.Append("ui_none:'\u6ca1\u6709\u5339\u914d\u7684\u5143\u7d20',");
            sb.Append("h_orbit:'\u8f68\u9053',");
            sb.Append("o_apo_t:'\u5230\u8fdc\u70b9',o_peri_t:'\u5230\u8fd1\u70b9',o_ground:'\u5728\u5730\u9762',");
            sb.Append("o_alt:'\u5f53\u524d\u9ad8\u5ea6',o_speed:'\u901f\u5ea6',o_heading:'\u706b\u7bad\u671d\u5411',o_fpa:'\u901f\u5ea6\u65b9\u5411',o_pitch:'\u653b\u89d2',o_target:'\u76ee\u6807\u89d2',");
            sb.Append("o_apo:'\u8fdc\u70b9',o_peri:'\u8fd1\u70b9',o_ecc:'\u79bb\u5fc3\u7387',o_period:'\u5468\u671f',o_stage:'\u72b6\u6001',");
            sb.Append("o_none:'\u5c1a\u672a\u5165\u8f68\uff08\u8fdb\u5165\u592a\u7a7a\u540e\u8fd9\u91cc\u4f1a\u663e\u793a\u8f68\u9053\u6839\u6570\uff09',");
            sb.Append("o_sub:'\u4e9a\u8f68\u9053\uff08\u4f1a\u518d\u5165\uff09',o_orbit:'\u5df2\u5165\u8f68',o_circ:'\u8fd1\u5706\u8f68\u9053',");
            sb.Append("h_game:'\u6e38\u620f\u8bbe\u7f6e',");
            sb.Append("game_desc:'\u76f4\u63a5\u8bfb\u5199\u6e38\u620f\u81ea\u5df1\u7684\u8bbe\u7f6e\uff08\u97f3\u91cf\u3001\u753b\u9762\u3001\u5e27\u7387\uff09\u3002\u6539\u5b8c\u7acb\u5373\u751f\u6548\uff0c\u4e0d\u7528\u91cd\u542f\u6e38\u620f\u3002',");
            sb.Append("game_unavailable:'\u8bfb\u4e0d\u5230\u6e38\u620f\u8bbe\u7f6e\uff08\u6e38\u620f\u53ef\u80fd\u6ca1\u5728\u8fd0\u884c\uff09',");
            sb.Append("game_apply:'\u5e94\u7528',");
            sb.Append("h_log:'\u65e5\u5fd7',pause:'\u6682\u505c',resume:'\u7ee7\u7eed',clear:'\u6e05\u7a7a',");
            sb.Append("log_paused:'\u5df2\u6682\u505c',log_live:'\u5b9e\u65f6',log_empty:'\uff08\u8fd8\u6ca1\u6709\u65e5\u5fd7\uff09',");
            sb.Append("h_cmd:'\u547d\u4ee4\u884c',run:'\u6267\u884c',");
            sb.Append("cmd_desc:'\u76f4\u63a5\u8c03\u63a5\u53e3\uff1a\u8f93\u5165\u50cf GET /state \u6216 POST /key {\\\"vk\\\":32} \u8fd9\u6837\u7684\u547d\u4ee4\u3002\u4e0d\u7528\u5207\u5230\u7ec8\u7aef\u3002\u4e0a\u4e0b\u952e\u53ef\u7ffb\u5386\u53f2\u3002',");
            sb.Append("q_state:'\u9065\u6d4b',q_ui:'\u754c\u9762',q_build:'\u706b\u7bad',q_bp:'\u84dd\u56fe',q_set:'\u8bbe\u7f6e',q_ping:'\u5b58\u6d3b',");
            sb.Append("cmd_usage:'\u7528\u6cd5\uff1aGET /state\u3001POST /key {\\\"vk\\\":32}\u3001POST /command {\\\"name\\\":\\\"stage\\\"}',");
            sb.Append("cmd_running:'\u6267\u884c\u4e2d\u2026',cmd_bad:'\u65e0\u6cd5\u89e3\u6790\u8fd9\u6761\u547d\u4ee4',");
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
            sb.Append("excl_desc:'When on, only the agent can control the game \u2014 your mouse and keyboard are ignored. Turn it off from this page (or press F10 as an emergency escape).',");
            sb.Append("excl_on:'Enable exclusive',excl_off:'Disable exclusive',");
            sb.Append("excl_state_on:'now: exclusive',excl_state_off:'now: normal',");
            sb.Append("h_cfg:'Configuration',");
            sb.Append("cfg_desc:'Edit the key/value pairs below and hit save. port and open_browser need a game restart; everything else applies immediately. You may add your own keys.',");
            sb.Append("save:'Save',add:'Add entry',del:'del',file:'File',");
            sb.Append("saved:'saved',changed:'changed',saving:'saving\u2026',failed:'failed',empty:'(empty)',");
            sb.Append("h_notes:'Notes',");
            sb.Append("ui_none:'no matching elements',");
            sb.Append("h_orbit:'Orbit',");
            sb.Append("o_apo_t:'To apoapsis',o_peri_t:'To periapsis',o_ground:'On ground',");
            sb.Append("o_alt:'Altitude',o_speed:'Speed',o_heading:'Heading',o_fpa:'Flight path',o_pitch:'Angle of attack',o_target:'Target',");
            sb.Append("o_apo:'Apoapsis',o_peri:'Periapsis',o_ecc:'Eccentricity',o_period:'Period',o_stage:'Status',");
            sb.Append("o_none:'Not in orbit yet (orbital elements appear once in space)',");
            sb.Append("o_sub:'Suborbital (will re-enter)',o_orbit:'In orbit',o_circ:'Near-circular',");
            sb.Append("h_game:'Game settings',");
            sb.Append("game_desc:'Read and write the game\u2019s own settings (volume, video, fps). Applied immediately, no restart needed.',");
            sb.Append("game_unavailable:'Game settings unavailable (is the game running?)',");
            sb.Append("game_apply:'Apply',");
            sb.Append("h_log:'Log',pause:'Pause',resume:'Resume',clear:'Clear',");
            sb.Append("log_paused:'paused',log_live:'live',log_empty:'(no entries yet)',");
            sb.Append("h_cmd:'Command line',run:'Run',");
            sb.Append("cmd_desc:'Call the API directly: type something like GET /state or POST /key {\\\"vk\\\":32}. No need to switch to a terminal. Up/down arrows recall history.',");
            sb.Append("q_state:'state',q_ui:'ui',q_build:'rocket',q_bp:'blueprints',q_set:'settings',q_ping:'ping',");
            sb.Append("cmd_usage:'Usage: GET /state, POST /key {\\\"vk\\\":32}, POST /command {\\\"name\\\":\\\"stage\\\"}',");
            sb.Append("cmd_running:'running\u2026',cmd_bad:'could not parse that command',");
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
            // 这两个状态要在 applyLang 之前就存在（applyLang 会读它们）
            sb.Append("var EXCL=false, LOG_PAUSED=false;");
            sb.Append("var LOG_OPS_ONLY=true;");
            sb.Append("var LOG_LAST=0;");
            sb.Append("var CMD_HIST=[], CMD_HI=-1;");
            sb.Append("var UI_LIST=[];");
            sb.Append("function t(k){return (S[LANG]&&S[LANG][k])||(S.zh[k])||k;}");
            sb.Append("function applyLang(){");
            sb.Append("document.documentElement.lang=(LANG==='zh'?'zh-CN':'en');");
            sb.Append("var els=document.querySelectorAll('[data-i18n]');");
            sb.Append("for(var i=0;i<els.length;i++){var k=els[i].getAttribute('data-i18n');");
            sb.Append("if(S[LANG][k]!==undefined)els[i].textContent=t(k);}");
            sb.Append("q('#exclBtn').textContent=EXCL?t('excl_off'):t('excl_on');");
            sb.Append("q('#exclState').textContent=EXCL?t('excl_state_on'):t('excl_state_off');");
            // 日志暂停按钮的文案由状态决定，不能只靠 data-i18n
            sb.Append("var lp=q('#logPause');if(lp)lp.textContent=LOG_PAUSED?t('resume'):t('pause');");
            sb.Append("var oc=q('#cmdOut');if(oc&&!oc.textContent.trim())oc.textContent=t('cmd_usage');");
            sb.Append("}");
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

            // ── 轨道卡片用的工具函数 ──
            sb.Append("function fmtM(v){");
            sb.Append("if(v===null||v===undefined||isNaN(v))return '\u2014';");
            sb.Append("var a=Math.abs(v);");
            sb.Append("if(a>=1e6)return (v/1e6).toFixed(2)+' Mm';");
            sb.Append("if(a>=1000)return (v/1000).toFixed(2)+' km';");
            sb.Append("return v.toFixed(1)+' m';}");
            sb.Append("function fmtSpeed(v){");
            sb.Append("if(v===null||v===undefined||isNaN(v))return '\u2014';");
            sb.Append("if(Math.abs(v)>=1000)return (v/1000).toFixed(2)+' km/s';");
            sb.Append("return v.toFixed(1)+' m/s';}");
            sb.Append("function fmtT(v){");
            sb.Append("if(v===null||v===undefined||isNaN(v)||v<=0)return '\u2014';");
            sb.Append("if(v>=3600)return (v/3600).toFixed(2)+' \u5c0f\u65f6';");
            sb.Append("if(v>=60)return (v/60).toFixed(1)+' \u5206\u949f';");
            sb.Append("return v.toFixed(0)+' \u79d2';}");
            sb.Append("function orbStage(o){");
            sb.Append("var R=6371000,apo=(o.apoapsis||0)+R,pe=(o.periapsis||0)+R;");
            sb.Append("if(pe<=R)return t('o_sub');");
            sb.Append("if(pe>70000&&apo>70000)return ((o.eccentricity||0)<0.1)?t('o_circ'):t('o_orbit');");
            sb.Append("return t('o_sub');}");
            sb.Append("function renderOrbit(s){");
            sb.Append("function deg(v){return (v===null||v===undefined||isNaN(v))?'\u2014':v.toFixed(2)+'\u00b0';}");
            sb.Append("var rows=[[t('o_alt'),fmtM(s.height)],[t('o_speed'),fmtSpeed(s.speed)]];");
            // 四个角度全部列出：朝向 / 速度方向 / 攻角 / 目标角
            sb.Append("rows.push([t('o_heading'),deg(s.angle)]);");
            sb.Append("rows.push([t('o_fpa'),deg(s.flight_path_angle)]);");
            sb.Append("rows.push([t('o_pitch'),deg(s.pitch_angle)]);");
            sb.Append("if(s.target_angle!==null&&s.target_angle!==undefined){");
            sb.Append("rows.push([t('o_target'),deg(s.target_angle)]);}");
            sb.Append("var o=s.orbit||{};");
            // 轨道根数始终显示：拿不到就显示 —，不用提示文字替代
            sb.Append("rows.push([t('o_apo'),fmtM(o.apoapsis)]);");
            sb.Append("rows.push([t('o_peri'),fmtM(o.periapsis)]);");
            sb.Append("rows.push([t('o_ecc'),(o.eccentricity===null||o.eccentricity===undefined)?'\u2014':o.eccentricity.toFixed(4)]);");
            sb.Append("rows.push([t('o_period'),fmtT(o.period)]);");
            sb.Append("rows.push([t('o_apo_t'),fmtT(s.time_to_apo)]);");
            sb.Append("rows.push([t('o_peri_t'),fmtT(s.time_to_peri)]);");
            sb.Append("rows.push([t('o_stage'),s.has_orbit?orbStage(o):t('o_ground')]);");
            sb.Append("var h='<table>'+rows.map(function(r){return row(r[0],r[1]);}).join('')+'</table>';");
            sb.Append("if(s.orbit_error){h+=\"<div class='sub' style='margin-top:8px;color:#e8a33d'>\"+esc(s.orbit_error)+\"</div>\";}");
            sb.Append("q('#orbit').innerHTML=h;}");

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
            // 轨道卡片：姿态角 + 轨道根数（数据来自 SFS.World.Orbit）
            sb.Append("renderOrbit(s);");
            sb.Append("var b=await (await fetch('/build')).json();");
            sb.Append("var ks=(b.part_kinds||[]).map(function(x){return esc(x.name)+' \u00d7 '+x.count}).join('<br>');");
            sb.Append("q('#build').innerHTML='<table>'+");
            sb.Append("row(t('mode'),esc(b.mode))+");
            sb.Append("row(t('parts'),b.part_count)+");
            sb.Append("row(t('mass'),(b.total_mass||0).toFixed(2)+' t')+");
            sb.Append("row(t('stages'),b.stage_count)+'</table><div style=\"margin-top:8px\">'+(ks||'\u2014')+'</div>';");
            sb.Append("var u=await (await fetch('/ui')).json();");
            sb.Append("UI_LIST=u.elements||[];");
            sb.Append("q('#uiCount').textContent=t('total')+' '+u.count+' '+t('items');");
            sb.Append("renderUi();");
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
            sb.Append("gsLoad();");

            // ── 游戏设置：读 /settings，每项可改并应用 ──
            sb.Append("function gsRow(it){");
            sb.Append("var v=(typeof it.value==='boolean')?(it.value?'1':'0'):it.value;");
            sb.Append("return \"<div class='cfgrow'>\"+");
            sb.Append("\"<span style='flex:0 0 210px;color:var(--muted)'>\"+esc(it.desc)+\"</span>\"+");
            sb.Append("\"<code style='flex:0 0 120px'>\"+esc(it.key)+\"</code>\"+");
            sb.Append("\"<input data-gk='\"+esc(it.key)+\"' value='\"+esc(v)+\"' style='flex:1'>\"+");
            sb.Append("\"<span style='flex:0 0 88px;color:var(--muted);font-size:12px'>\"+esc(it.range)+\"</span>\"+");
            sb.Append("\"<button data-gapply='\"+esc(it.key)+\"'>\"+t('game_apply')+\"</button></div>\";}");
            sb.Append("async function gsLoad(){");
            sb.Append("try{");
            sb.Append("var r=await (await fetch('/settings')).json();");
            sb.Append("if(!r.ok||!r.settings||!r.settings.length){");
            sb.Append("q('#gameSet').innerHTML=\"<span class='sub'>\"+t('game_unavailable')+\"</span>\";return;}");
            sb.Append("q('#gameSet').innerHTML=r.settings.map(gsRow).join('');");
            sb.Append("}catch(e){q('#gameSet').innerHTML=\"<span class='sub'>\"+t('game_unavailable')+\"</span>\";}}");
            sb.Append("function gsSet(k,v){return fetch('/settings',{method:'POST',");
            sb.Append("headers:{'Content-Type':'application/json'},body:JSON.stringify({key:k,value:v})})");
            sb.Append(".then(function(r){return r.json();});}");
            sb.Append("q('#gameSet').addEventListener('click',async function(e){");
            sb.Append("var k=e.target&&e.target.getAttribute&&e.target.getAttribute('data-gapply');");
            sb.Append("if(!k)return;");
            sb.Append("var inp=q(\"input[data-gk='\"+k+\"']\");");
            sb.Append("if(!inp)return;");
            sb.Append("var box=q('#gameSet');");
            sb.Append("try{var r=await gsSet(k,parseFloat(inp.value));");
            sb.Append("if(!r.ok){alert(r.error||'failed');}else{logTick();}}");
            sb.Append("catch(ex){alert(ex);}});");
            // ── 界面元素完整列表：显示全部，可搜索 ──
            sb.Append("function renderUi(){");
            sb.Append("var kw=(q('#uiSearch').value||'').trim().toLowerCase();");
            sb.Append("var list=UI_LIST.filter(function(e){");
            sb.Append("if(!kw)return true;");
            sb.Append("var s=(e.label||'')+' '+e.index;");
            sb.Append("return s.toLowerCase().indexOf(kw)>=0;});");
            sb.Append("if(!list.length){q('#ui').innerHTML=\"<span class='sub'>\"+t('ui_none')+\"</span>\";return;}");
            sb.Append("var html=list.map(function(e){");
            sb.Append("var lab=esc(e.label||t('none'));");
            sb.Append("var pos=(e.x!==undefined&&e.x>=0)?' @ '+e.x.toFixed(3)+', '+e.y.toFixed(3):'';");
            sb.Append("return \"<div style='display:flex;gap:8px'>\"+");
            sb.Append("\"<code style='flex:0 0 46px;text-align:right'>#\"+e.index+\"</code>\"+");
            sb.Append("\"<span style='flex:1'>\"+lab+\"</span>\"+");
            sb.Append("\"<span style='color:var(--muted);flex:0 0 118px;text-align:right'>\"+pos+\"</span>\"+");
            sb.Append("\"</div>\";}).join('');");
            sb.Append("q('#ui').innerHTML=html;}");
            sb.Append("q('#uiSearch').addEventListener('input',renderUi);");
            // ── 日志：增量拉取，可暂停 ──
            sb.Append("function logLine(e){");
            // 状态标签：成功绿 / 失败红 / 执行中蓝；none 不显示
            sb.Append("var tag='';");
            sb.Append("if(e.status==='ok'){tag=\" <span style='color:#3fb950'>&#10003;</span>\";}");
            sb.Append("else if(e.status==='fail'){tag=\" <span style='color:#f85149'>&#10007;</span>\";}");
            sb.Append("else if(e.status==='busy'){tag=\" <span style='color:#58a6ff'>&#9679;</span>\";}");
            sb.Append("var col=(e.status==='fail'||e.level==='\u9519\u8bef')?'#f85149':");
            sb.Append("((e.status==='ok')?'#3fb950':((e.level==='\u8b66\u544a')?'#e8a33d':'var(--fg)'));");
            sb.Append("return \"<span style='color:var(--muted)'>[\"+esc(e.time)+\"]</span> \"+");
            sb.Append("\"<span style='color:\"+col+\"'>\"+esc(e.text)+tag+\"</span>\";}");
            sb.Append("function logEmpty(){");
            sb.Append("var box=q('#logBox');");
            sb.Append("if(box.childNodes.length===0){");
            sb.Append("box.innerHTML=\"<span class='sub'>\"+t('log_empty')+\"</span>\";}}");
            sb.Append("function logAppend(list){");
            sb.Append("var box=q('#logBox');");
            sb.Append("if(box.querySelector('.sub'))box.innerHTML='';");
            sb.Append("var atBottom=(box.scrollTop+box.clientHeight>=box.scrollHeight-24);");
            sb.Append("for(var i=0;i<list.length;i++){");
            sb.Append("if(LOG_OPS_ONLY&&list[i].poll){LOG_LAST=list[i].seq;continue;}");
            sb.Append("box.insertAdjacentHTML('beforeend',logLine(list[i])+'<br>');");
            sb.Append("LOG_LAST=list[i].seq;}");
            sb.Append("while(box.childNodes.length>800){box.removeChild(box.firstChild);}");
            sb.Append("if(atBottom)box.scrollTop=box.scrollHeight;}");
            sb.Append("async function logTick(){");
            sb.Append("if(LOG_PAUSED)return;");
            sb.Append("try{");
            sb.Append("var r=await (await fetch('/log?since='+LOG_LAST)).json();");
            sb.Append("if(r.entries&&r.entries.length){logAppend(r.entries);}");
            sb.Append("else{logEmpty();}");
            sb.Append("q('#logInfo').textContent=t('log_live')+' \u00b7 '+r.total;");
            sb.Append("}catch(e){q('#logInfo').textContent=esc(e);}}");
            sb.Append("q('#logPause').onclick=function(e){e.preventDefault();");
            sb.Append("LOG_PAUSED=!LOG_PAUSED;");
            sb.Append("this.textContent=LOG_PAUSED?t('resume'):t('pause');");
            sb.Append("q('#logInfo').textContent=LOG_PAUSED?t('log_paused'):t('log_live');};");
            sb.Append("q('#logClear').onclick=async function(e){e.preventDefault();");
            sb.Append("await fetch('/log?clear=1');");
            sb.Append("q('#logBox').innerHTML='';LOG_LAST=0;logEmpty();};");
            sb.Append("function syncOpsBtn(){");
            sb.Append("var b=q('#logOps');if(!b)return;");
            sb.Append("b.textContent=LOG_OPS_ONLY?'\u53ea\u770b\u64cd\u4f5c':'\u5168\u90e8\u65e5\u5fd7';");
            sb.Append("b.className=LOG_OPS_ONLY?'primary':'';}");
            sb.Append("q('#logOps').onclick=function(e){e.preventDefault();");
            sb.Append("LOG_OPS_ONLY=!LOG_OPS_ONLY;");
            sb.Append("q('#logBox').innerHTML='';LOG_LAST=0;syncOpsBtn();logTick();};");
            sb.Append("syncOpsBtn();");
            sb.Append("logTick();setInterval(logTick,1500);");

            // ── 命令行 ──
            sb.Append("function parseCmd(s){");
            sb.Append("s=s.trim();if(!s)return null;");
            sb.Append("var m=s.match(/^(GET|POST|PUT|DELETE)?\\s*(\\/[^\\s]*)\\s*([\\s\\S]*)$/i);");
            sb.Append("if(!m)return null;");
            sb.Append("var method=(m[1]||'GET').toUpperCase();");
            sb.Append("var url=m[2];var rest=(m[3]||'').trim();");
            sb.Append("if(!url||url.charAt(0)!=='/')return null;");
            sb.Append("return {method:method,url:url,body:rest};}");
            sb.Append("async function runCmd(){");
            sb.Append("var raw=q('#cmdIn').value;var c=parseCmd(raw);");
            sb.Append("var out=q('#cmdOut');");
            sb.Append("if(!c){out.textContent=t('cmd_usage');return;}");
            sb.Append("if(CMD_HIST[0]!==raw){CMD_HIST.unshift(raw);if(CMD_HIST.length>50)CMD_HIST.pop();}");
            sb.Append("CMD_HI=-1;");
            sb.Append("var head='> '+c.method+' '+c.url+(c.body?' '+c.body:'')+'\\n';");
            sb.Append("out.textContent=head+t('cmd_running');");
            sb.Append("var t0=Date.now();");
            sb.Append("try{");
            sb.Append("var opt={method:c.method};");
            sb.Append("if(c.method!=='GET'&&c.body){opt.headers={'Content-Type':'application/json'};opt.body=c.body;}");
            sb.Append("var r=await fetch(c.url,opt);");
            sb.Append("var txt=await r.text();");
            sb.Append("try{txt=JSON.stringify(JSON.parse(txt),null,2);}catch(e2){}");
            sb.Append("out.textContent=head+'HTTP '+r.status+'  ('+(Date.now()-t0)+'ms)\\n\\n'+txt;");
            sb.Append("}catch(e){out.textContent=head+'ERROR: '+e;}");
            sb.Append("logTick();}");
            sb.Append("q('#cmdRun').onclick=function(e){e.preventDefault();runCmd();};");
            sb.Append("q('#cmdIn').addEventListener('keydown',function(e){");
            sb.Append("if(e.key==='Enter'){e.preventDefault();runCmd();return;}");
            sb.Append("if(e.key==='ArrowUp'){e.preventDefault();");
            sb.Append("if(CMD_HI<CMD_HIST.length-1){CMD_HI++;this.value=CMD_HIST[CMD_HI];}return;}");
            sb.Append("if(e.key==='ArrowDown'){e.preventDefault();");
            sb.Append("if(CMD_HI>0){CMD_HI--;this.value=CMD_HIST[CMD_HI];}");
            sb.Append("else{CMD_HI=-1;this.value='';}return;}});");
            sb.Append("var qs=document.querySelectorAll('.quick');");
            sb.Append("for(var qi=0;qi<qs.length;qi++){qs[qi].onclick=function(e){e.preventDefault();");
            sb.Append("q('#cmdIn').value=this.getAttribute('data-cmd');runCmd();};}");
            sb.Append("q('#cmdOut').textContent=t('cmd_usage');");

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
