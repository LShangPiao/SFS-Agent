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
            sb.Append("<title>SFS-Agent \u00b7 航天模拟器桥接</title><style>");
            sb.Append("*{box-sizing:border-box}");
            sb.Append("body{margin:0;padding:32px;background:#0f1420;color:#dbe3f0;");
            sb.Append("font:14px/1.6 ui-sans-serif,system-ui,'Segoe UI',sans-serif}");
            sb.Append("h1{margin:0 0 4px;font-size:20px}");
            sb.Append(".sub{color:#8b98ad;margin-bottom:24px}");
            sb.Append(".grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(240px,1fr));gap:16px}");
            sb.Append(".card{background:#182031;border:1px solid #232e44;border-radius:10px;padding:16px}");
            sb.Append(".card h2{margin:0 0 10px;font-size:13px;color:#8b98ad;font-weight:600;");
            sb.Append("text-transform:uppercase;letter-spacing:.06em}");
            sb.Append("table{width:100%;border-collapse:collapse;font-size:13px}");
            sb.Append("td{padding:3px 0;vertical-align:top}");
            sb.Append("td:first-child{color:#8b98ad;width:44%}");
            sb.Append("code{background:#0b1120;border:1px solid #232e44;border-radius:4px;");
            sb.Append("padding:1px 5px;font-size:12px}");
            sb.Append("a{color:#6ea8fe;text-decoration:none}a:hover{text-decoration:underline}");
            sb.Append(".ok{color:#4ade80}.bad{color:#f87171}");
            sb.Append("ul{margin:6px 0;padding-left:18px}li{margin:2px 0}");
            sb.Append("</style></head><body>");

            sb.Append("<h1>SFS-Agent</h1>");
            sb.Append("<div class='sub' id='sub'>正在连接 Spaceflight Simulator\u2026</div>");

            sb.Append("<div class='grid'>");

            // 实时状态
            sb.Append("<div class='card'><h2>实时状态</h2><div id='state'>\u2026</div></div>");
            // 火箭
            sb.Append("<div class='card'><h2>火箭</h2><div id='build'>\u2026</div></div>");
            // 界面
            sb.Append("<div class='card'><h2>可点击界面元素</h2><div id='ui'>\u2026</div></div>");

            // 接口清单
            sb.Append("<div class='card'><h2>HTTP 接口</h2><table>");
            sb.Append("<tr><td><code>GET /ping</code></td><td>存活与按键注入状态</td></tr>");
            sb.Append("<tr><td><code>GET /state</code></td><td>飞行遥测</td></tr>");
            sb.Append("<tr><td><code>GET /build</code></td><td>火箭零件构成</td></tr>");
            sb.Append("<tr><td><code>GET /build_catalog</code></td><td>可用零件名</td></tr>");
            sb.Append("<tr><td><code>GET /ui</code></td><td>当前可点击元素</td></tr>");
            sb.Append("<tr><td><code>GET /screenshot</code></td><td>当前画面（PNG）</td></tr>");
            sb.Append("<tr><td><code>POST /ui_click</code></td><td>按索引点击</td></tr>");
            sb.Append("<tr><td><code>POST /click</code></td><td>按坐标点击</td></tr>");
            sb.Append("<tr><td><code>POST /key</code></td><td>发送按键</td></tr>");
            sb.Append("<tr><td><code>POST /command</code></td><td>飞行指令</td></tr>");
            sb.Append("<tr><td><code>POST /build_place</code></td><td>定点放置零件</td></tr>");
            sb.Append("<tr><td><code>GET /debug_parts</code></td><td>零件名来源诊断</td></tr>");
            sb.Append("</table></div>");

            // 操作方式
            sb.Append("<div class='card'><h2>SFS 默认操作</h2><table>");
            sb.Append("<tr><td>转向</td><td>Q / E</td></tr>");
            sb.Append("<tr><td>平移与俯仰</td><td>W / A / S / D（需开 RCS）</td></tr>");
            sb.Append("<tr><td>油门</td><td>Shift 加大 / Ctrl 减小</td></tr>");
            sb.Append("<tr><td>RCS 开关</td><td>R</td></tr>");
            sb.Append("<tr><td>点火 / 下一级</td><td>空格</td></tr>");
            sb.Append("<tr><td>分级控制程序</td><td>回车</td></tr>");
            sb.Append("</table></div>");

            // 配置（可编辑：动态列出 ini 里所有键，不写死字段）
            sb.Append("<div class='card' style='grid-column:1/-1'><h2>配置</h2>");
            sb.Append("<div class='sub' style='margin:0 0 12px'>");
            sb.Append("直接编辑下面的键值后点保存。<b>port</b> 与 <b>open_browser</b> 需要重启游戏生效；");
            sb.Append("其余项立即生效。也可以自己加新键 —— 页面会把 ini 里所有内容原样列出。");
            sb.Append("</div>");
            sb.Append("<div id='cfg'>\u2026</div>");
            sb.Append("<div style='margin-top:12px;display:flex;gap:8px;flex-wrap:wrap'>");
            sb.Append("<button id='save' style='padding:6px 16px;border-radius:6px;border:1px solid #2f6fd0;");
            sb.Append("background:#1d4ed8;color:#fff;cursor:pointer'>保存</button>");
            sb.Append("<button id='add' style='padding:6px 16px;border-radius:6px;border:1px solid #232e44;");
            sb.Append("background:#111827;color:#dbe3f0;cursor:pointer'>新增一项</button>");
            sb.Append("<span id='msg' style='align-self:center'></span>");
            sb.Append("</div>");
            sb.Append("<div class='sub' style='margin:10px 0 0'>文件：<code>");
            sb.Append(Esc(Path.Combine("Mods", "SFS-Agent", "sfs-agent.ini")));
            sb.Append("</code></div></div>");

            // 说明
            sb.Append("<div class='card'><h2>说明</h2><ul>");
            sb.Append("<li>只监听 <code>127.0.0.1</code>，不对局域网开放。</li>");
            sb.Append("<li>点击与按键都是**游戏内注入**，不会抢走你的鼠标或焦点。</li>");
            sb.Append("<li>本页数据全部来自本地桥接服务，不联网。</li>");
            sb.Append("</ul></div>");

            sb.Append("</div>");

            sb.Append("<script>");
            sb.Append("var P='" + p + "';");
            sb.Append("function q(s){return document.querySelector(s);}");
            sb.Append("function row(k,v){return '<tr><td>'+k+'</td><td>'+v+'</td></tr>';}");
            sb.Append("function esc(s){return String(s).replace(/[&<>]/g,function(c){");
            sb.Append("return {'&':'&amp;','<':'&lt;','>':'&gt;'}[c];});}");
            sb.Append("async function tick(){");
            sb.Append("try{");
            sb.Append("var p=await (await fetch('/ping')).json();");
            sb.Append("q('#sub').innerHTML='已连接 \u00b7 按键注入 <b class=\"'+(p.key_injection==='on'?'ok':'bad')+'\">'+");
            sb.Append("esc(p.key_injection)+'</b> \u00b7 '+esc(p.key_injection_info||'');");
            sb.Append("var s=await (await fetch('/state')).json();");
            sb.Append("q('#state').innerHTML='<table>'+");
            sb.Append("row('在世界中',s.in_world?'是':'否')+");
            sb.Append("row('飞行中',s.flying?'是':'否')+");
            sb.Append("row('火箭',esc(s.rocket||'\u2014'))+");
            sb.Append("row('天体',esc(s.planet||'\u2014'))+");
            sb.Append("row('高度',(s.height||0).toFixed(1)+' m')+");
            sb.Append("row('速度',(s.speed||0).toFixed(1)+' m/s')+");
            sb.Append("row('油门',((s.throttle||0)*100).toFixed(0)+'%')+");
            sb.Append("row('分级',s.stage)+");
            sb.Append("row('质量',(s.mass||0).toFixed(1)+' t')+'</table>';");
            sb.Append("var b=await (await fetch('/build')).json();");
            sb.Append("var ks=(b.part_kinds||[]).map(function(x){return esc(x.name)+' \u00d7 '+x.count}).join('<br>');");
            sb.Append("q('#build').innerHTML='<table>'+");
            sb.Append("row('场景',esc(b.mode))+");
            sb.Append("row('零件数',b.part_count)+");
            sb.Append("row('总质量',(b.total_mass||0).toFixed(2)+' t')+");
            sb.Append("row('分级数',b.stage_count)+'</table><div style=\"margin-top:8px\">'+(ks||'\u2014')+'</div>';");
            sb.Append("var u=await (await fetch('/ui')).json();");
            sb.Append("var us=(u.elements||[]).slice(0,12).map(function(e){");
            sb.Append("return '#'+e.index+' '+esc(e.label||'(\u65e0\u6807\u7b7e)')+");
            sb.Append("(e.x!==undefined?' @ '+e.x.toFixed(2)+','+e.y.toFixed(2):'');}).join('<br>');");
            sb.Append("q('#ui').innerHTML='共 '+u.count+' \u4e2a<div style=\"margin-top:8px\">'+(us||'\u2014')+'</div>';");
            sb.Append("}catch(e){q('#sub').innerHTML='<span class=\"bad\">连不上桥接服务</span> \u00b7 '+esc(e);}");
            sb.Append("}");
            sb.Append("tick();setInterval(tick,1500);");

            // 配置加载 / 保存
            sb.Append("var CKEY='cfgkeys';");
            sb.Append("function cfgRow(k,v){");
            sb.Append("return \"<div style='display:flex;gap:8px;margin-bottom:6px'>\"+");
            sb.Append("\"<input value='\"+esc(k)+\"' data-k='1' style='flex:0 0 240px;padding:5px 8px;border-radius:5px;\"+");
            sb.Append("\"border:1px solid #232e44;background:#0b1120;color:#dbe3f0'>\"+");
            sb.Append("\"<input value='\"+esc(v)+\"' data-v='1' style='flex:1;padding:5px 8px;border-radius:5px;\"+");
            sb.Append("\"border:1px solid #232e44;background:#0b1120;color:#dbe3f0'>\"+");
            sb.Append("\"<button data-del='1' style='padding:5px 10px;border-radius:5px;border:1px solid #232e44;\"+");
            sb.Append("\"background:#111827;color:#f87171;cursor:pointer'>删</button></div>\";}");
            sb.Append("function cfgCollect(){");
            sb.Append("var rows=q('#cfg').querySelectorAll('div[style*=flex]');var o={};");
            sb.Append("for(var i=0;i<rows.length;i++){");
            sb.Append("var k=rows[i].querySelector('input[data-k]').value.trim();");
            sb.Append("var v=rows[i].querySelector('input[data-v]').value;");
            sb.Append("if(k)o[k]=v;}return o;}");
            sb.Append("async function cfgLoad(){");
            sb.Append("var c=await (await fetch('/config')).json();");
            sb.Append("var html='';for(var k in c){html+=cfgRow(k,c[k]);}");
            sb.Append("q('#cfg').innerHTML=html||\"<span class='sub'>（空）</span>\";}");
            sb.Append("q('#add').onclick=function(e){e.preventDefault();");
            sb.Append("q('#cfg').insertAdjacentHTML('beforeend',cfgRow('',''));};");
            sb.Append("q('#cfg').addEventListener('click',function(e){");
            sb.Append("if(e.target.hasAttribute&&e.target.hasAttribute('data-del')){");
            sb.Append("e.target.parentNode.parentNode.removeChild(e.target.parentNode);}});");
            sb.Append("q('#save').onclick=async function(e){e.preventDefault();");
            sb.Append("q('#msg').textContent='保存中\u2026';");
            sb.Append("try{var r=await (await fetch('/config',{method:'POST',");
            sb.Append("headers:{'Content-Type':'application/json'},body:JSON.stringify(cfgCollect())})).json();");
            sb.Append("q('#msg').innerHTML=r.ok?\"<span class='ok'>已保存（\"+r.changed+\" 项变更）</span>\":");
            sb.Append("\"<span class='bad'>\"+esc(r.error||'失败')+\"</span>\";");
            sb.Append("if(r.ok)cfgLoad();}catch(ex){q('#msg').innerHTML=\"<span class='bad'>\"+esc(ex)+\"</span>\";}};");
            sb.Append("cfgLoad();");
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
                    + "overlay=true\r\n",
                    new UTF8Encoding(false));
            }
            catch
            {
                // 写不了就算了，用默认值继续
            }
        }
    }
}
