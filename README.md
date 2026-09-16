# SFS-Agent

<p align="center">
  由 <b>星河拓航工作室</b>（Galaxy Exploration Studio）开发与维护
</p>

**Spaceflight Simulator（航天模拟器）的游戏内模组**，在 `127.0.0.1:21578` 上开一个
只读为主的 HTTP 接口，把游戏状态暴露给外部程序（例如 N.E.K.O. 的
[航天模拟器助手](https://github.com/LShangPiao/n.e.k.o_plugin_sfs_bridge) 插件）。

它让 AI 助手可以：

- 读取**飞行遥测**（高度、速度、油门、分级、质量……）
- **截图**当前画面
- 读取**火箭零件构成**
- **列出并点击界面按钮**（主菜单、载入存档、建造菜单、设置）
- 发送少量**飞行控制指令**（油门、分级）

## 安装

从 [Releases](https://github.com/LShangPiao/SFS-Agent/releases) 下载
`SFS-Agent.dll`，按 SFS 的「一目录一模组」规范放入：

```
<Steam>\steamapps\common\Spaceflight Simulator\Spaceflight Simulator Game\Mods\SFS-Agent\SFS-Agent.dll
```

> ⚠️ 必须放在 `Mods\SFS-Agent\` 这个**子目录**里，文件名与目录名一致。
> 不要平铺到 `Mods\` 根目录，也不要同时保留其他旧模组目录 ——
> 两个模组会抢 21578 端口，导致外部连上的不是你期望的那个。

**然后重启游戏** —— 模组只在游戏启动时加载。

验证是否装好：

```powershell
curl http://127.0.0.1:21578/ping
# {"ok":true,"mod":"sfs_agent","version":"0.2.0"}
```

## 从源码编译

需要 Windows 自带的 `csc.exe`（.NET Framework 4.x）与已安装的游戏本体。

```powershell
pwsh -File build.ps1
```

脚本会：

1. 从游戏安装目录复制 `Assembly-CSharp.dll`、`0Harmony.dll` 到本地 `refs/`
2. 用 `csc` 编译 `src/*.cs` 为 `dist/SFS-Agent.dll`
3. 自动部署到游戏的 `Mods\SFS-Agent\`，并清理冲突的旧模组目录

> SFS 的 Unity 模块引用 **netstandard 2.1**，而 .NET Framework 的 `csc` 不支持，
> 因此本模组**不引用任何 UnityEngine 程序集**，全部通过反射 + Harmony 动态补丁
> 与游戏交互。只有 C# 5 语法可用，请勿使用 `=>` 表达式成员、字符串插值、`?.`。

## HTTP 接口

只监听 `127.0.0.1`，不对局域网或公网开放。

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| GET | `/ping` | 存活检测，返回模组名与版本 |
| GET | `/health` | 健康检查 |
| GET | `/state` | 飞行遥测 |
| GET | `/build` | 火箭零件构成 |
| GET | `/ui` | 当前界面可点击元素清单 |
| GET | `/screenshot` | 抓取画面，返回 PNG |
| POST | `/command` | 飞行指令：`set_throttle` / `throttle_on` / `throttle_off` / `stage` |
| POST | `/ui_click` | 按索引点击界面元素 |
| POST | `/click` | 按归一化坐标点击 |
| POST | `/key` | 发送按键 |
| POST | `/scroll` | 滚动 |
| POST | `/debug_methods` | 诊断：打印某个元素的反射信息 |

### `/state` 返回示例

```json
{"ok":true,"in_world":false,"flying":false,"rocket":"","planet":"",
 "height":0,"speed":0,"velocity_x":0,"velocity_y":0,
 "throttle":0,"throttle_on":false,"stage":-1,"has_control":false,"mass":0}
```

### `/ui` 返回示例

```json
{"ok":true,"count":9,"elements":[
  {"index":4,"label":"Play","x":0.5,"y":0.481},
  {"index":3,"label":"Settings","x":0.5,"y":0.5843}
]}
```

`x`/`y` 是相对游戏窗口的**归一化坐标**（0-1，左上角为原点）。
置灰的按钮（`buttonEnabled == false`）会被自动过滤掉 —— 所以未选中存档时，
存档界面上的 `Play` / `Rename` / `Delete` 不会出现在清单里。

## 界面点击是「游戏内事件注入」

`POST /ui_click` **不会移动系统鼠标**：模组直接触发按钮自己的
`clickEvent`（`UnityEvent<OnInputEndData>`），因此：

- ✅ 点击期间鼠标指针**不会移动**，你可以同时用电脑做别的事
- ✅ 不需要把游戏窗口切到前台
- ⚠️ 但点击**会真实改变游戏状态**（开始游戏、载入存档等）

> 实现注记（踩过的坑）：`SFS.UI.Button` 以**显式接口实现**提供
> `SFS.Input.I_Touchable.OnInputEnd`，方法名为 `SFS.Input.I_Touchable.OnInputEnd`
> 且是 private。这些方法**在游戏运行时无法通过 `GetMethods()` 枚举到**
> （离线反射同一个 `Assembly-CSharp.dll` 却可以），因此走
> `OnInputEnd` 的路径在游戏内不可用。`GetFields()` 运行时工作正常，
> 所以实际生效的是字段路径 `clickEvent.Invoke(...)`，并以
> `OnInputEnd`、`onClick` 作为兜底。

其他已知事实（游戏 v1.6.00.16）：

- 菜单按钮的运行时类型是 `SFS.UI.ButtonPC : SFS.UI.Button : MonoBehaviour`
- `SFS.UI.Button` 的字段：`clickEvent`(`SFS.UI.ClickUnityEvent` : `UnityEvent<OnInputEndData>`)、
  `onClick` / `onUp` / `onRightClick`(`OptionalDelegate<OnInputEndData>`)、`buttonEnabled`(`bool`)
- `OnInputEndData(InputType, TouchPosition, bool click)`；`InputType`: `Touch=0, MouseLeft=1, MouseRight=2`

## 说明

- 模组只做只读遥测采集与少量指令，**不会修改存档文件**。
- Spaceflight Simulator 为 Team Curiosity 开发的商业游戏，本项目与官方无关，
  分发的是自制的第三方模组；仓库中**不包含**任何游戏本体文件。

## 关于星河拓航工作室

本模组由 **星河拓航工作室**（**Galaxy Exploration Studio**）开发与维护。

星河拓航工作室是一个由来自五湖四海的航天爱好者组成的非正式线上航天科普组织，
成员多为在校学生。我们希望通过有趣、可靠的方式，让更多人了解真实的航天。

- 官方网站：<https://xhth.top/>
- B 站主页：<https://space.bilibili.com/3546949529635067>

欢迎航天爱好者加入交流，也欢迎反馈问题与建议。

## 许可证

本项目采用 [GNU General Public License v3.0](LICENSE) 许可。

Copyright (C) 2026 星河拓航工作室 (Galaxy Exploration Studio)
