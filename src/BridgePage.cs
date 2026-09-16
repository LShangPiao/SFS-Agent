// SFS-Agent — 内置配置页
//
// 模组启动后会在默认浏览器里打开 http://127.0.0.1:<port>/
// 让人也能直接看到桥接服务的状态与全部接口，而不只是给程序用。
//
// 页面是自包含的（不引任何外部资源），离线也能用；
// 数据靠页面里的 JS 轮询本地接口拿。

using System;
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

            // 说明
            sb.Append("<div class='card'><h2>说明</h2><ul>");
            sb.Append("<li>只监听 <code>127.0.0.1</code>，不对局域网开放。</li>");
            sb.Append("<li>点击与按键都是**游戏内注入**，不会抢走你的鼠标或焦点。</li>");
            sb.Append("<li>配置项在 <code>Mods/SFS-Agent/sfs-agent.ini</code>。</li>");
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

        private static void WriteDefault(string iniPath)
        {
            try
            {
                File.WriteAllText(
                    iniPath,
                    "# SFS-Agent 配置\r\n"
                    + "# 桥接服务端口\r\n"
                    + "port=21578\r\n"
                    + "# 游戏启动后是否自动打开这个配置页\r\n"
                    + "open_browser=true\r\n",
                    Encoding.UTF8);
            }
            catch
            {
                // 写不了就算了，用默认值继续
            }
        }
    }
}
