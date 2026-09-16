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
- **在指定坐标放置零件**（不需要拖动）
- 发送少量**飞行控制指令**（油门、分级、RCS）

## 定位：通用桥接，不绑定任何特定助手

这个模组的职责只有一件事：**把 Spaceflight Simulator 的能力用 HTTP 暴露出来**。
它不知道对面是谁，也不该知道 —— 任何程序都能接（自己的脚本、别的桌面助手、调试工具）。

因此这里**不包含**面向特定助手的提示词、工具命名或对话逻辑。
面向 N.E.K.O. 猫娘的适配层（工具描述、操作指南、防幻觉引导）在另一个仓库：

> <https://github.com/LShangPiao/n.e.k.o_plugin_sfs_bridge>

| | SFS-Agent（本仓库） | sfs_bridge（插件仓库） |
| --- | --- | --- |
| 形态 | 游戏内 C# 模组 | N.E.K.O. Python 插件 |
| 职责 | **公用**能力层：遥测 / 截图 / 输入 / 建造 | **专用**适配层：给猫娘的工具与提示 |
| 面向 | 任何程序 | N.E.K.O. |
| 依赖 | 只依赖游戏本体 | 依赖本模组 + N.E.K.O. |

## 内置配置页

模组启动后会把 <http://127.0.0.1:21578/> 在默认浏览器里打开：
实时状态、全部接口清单、SFS 默认操作方式，不写代码也能确认桥接是否正常。

自动打开可以关掉。配置文件是同目录下的 `sfs-agent.ini`（首次运行自动生成）：

```ini
# 桥接服务端口
port=21578
# 游戏启动后是否自动打开配置页
open_browser=true
```

配置读不到就用默认值 —— 绝不因为缺配置而让模组不工作。

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
| GET | `/` | **内置配置页**（HTML 仪表盘，游戏启动时自动打开） |
| GET | `/ping` | 存活检测，返回模组名、版本与按键注入状态 |
| GET | `/health` | 健康检查 |
| GET | `/state` | 飞行遥测 |
| GET | `/build` | 火箭零件构成 |
| GET | `/build_catalog` | 可用零件名（默认在主线程调用游戏自己的 `LoadParts()` 取全量） |
| GET | `/ui` | 当前界面可点击元素清单 |
| GET | `/screenshot` | 抓取画面，返回 PNG |
| POST | `/command` | 飞行指令：`set_throttle` / `throttle_on` / `throttle_off` / `stage` / `staging_program` / `rcs_on` / `rcs_off` / `rcs_toggle` |
| POST | `/ui_click` | 按索引点击界面元素（游戏内派发） |
| POST | `/click` | 按归一化坐标点击（游戏内派发） |
| POST | `/click_raw` | 同上，但用 Win32 模拟真实鼠标（**会抢鼠标与焦点**，仅排障用） |
| POST | `/key` | 发送按键（游戏内注入，游戏不必在前台） |
| POST | `/key_raw` | 同上，但用 Win32 `keybd_event`（**会抢焦点**，仅排障用） |
| POST | `/build_place` | 把零件直接放到建造网格坐标（不需要拖动） |
| POST | `/scroll` | 滚动（Win32，滚轮事件发给光标下的窗口） |
| POST | `/debug_methods` | 诊断：打印某个元素的反射信息 |
| POST | `/debug_parts` | 诊断：dump 零件名的各个来源 |

> 所有会碰 Unity API 的操作都在**主线程**执行（HTTP 线程只入队 / 置标志）。
> 这条约束是硬性的：曾经把 `Resources.LoadAll` 放在 HTTP 线程上调用，
> 直接把游戏打崩了（原生访问违例，托管 try/catch 兜不住）。

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

## 点击不会抢走你的鼠标

`POST /ui_click` 与 `POST /click` **不会移动系统鼠标**，也不需要游戏窗口在前台。

实现方式：调用 SFS 自己的输入派发入口

```
SFS.Input.InputManager.CheckMouseOverState(TouchPosition)
SFS.Input.InputManager.InputStart(index, InputType, TouchPosition)
SFS.Input.InputManager.TouchEnd (index, InputType, TouchPosition)
```

`InputManager` 内部会自己做命中判定并把结果写进 `mouseOverElement`，
所以：

- **必须先调 `CheckMouseOverState`** —— 不调的话 `InputStart` 用的会是上一次
  （通常是 null）的悬停元素，点了等于没点
- 命中判定由游戏自己做，比自己去比对按钮矩形准得多
- 走的是游戏原生的按钮派发，因此 `clickEvent` / `onClick` 各种接线方式都能正确触发

> 曾经的弯路：直接 `Invoke` 按钮的 `clickEvent`。它能点中一部分按钮，但
> **按钮把逻辑接在 `onClick`（`OptionalDelegate`）上时会返回成功却毫无效果** ——
> 实测主菜单 Esc 退出确认框的 Cancel 就是这种情况，属于「假成功」。
> 现已统一走 `InputManager`，`clickEvent` 只作为拿不到坐标时的最后兜底。
>
> 另一个坑：`CheckMouseOverState` 等方法在运行时**无法通过 `GetMethods()` 枚举到
> 显式接口实现**（离线反射同一个 `Assembly-CSharp.dll` 却可以），因此这里按
> 方法名 + 参数个数查找，并用 `GetFields()` 读字段（字段读取运行时正常）。

点击的按下与抬起**分帧执行**（一次真实点击本来就有按下-抬起过程），
所以 `/click` 会等状态机跑完再应答。

## 按键也不会抢焦点

`POST /key` 通过 Harmony 拦截 `UnityEngine.Input.GetKey / GetKeyDown / GetKeyUp`
来注入按键：

- 只对我们正在注入的那几个键覆盖返回值，其余键一律走原方法
- 游戏自己的输入逻辑完全不变，分级、转向、菜单都按原生行为工作
- **不触碰系统输入，游戏不需要在前台**（实测游戏在后台时按 Esc 依然弹出了退出确认）
- 出错时前缀一律放行原方法，绝不把游戏输入搞坏

`vk` 用虚拟键码（与 UnityEngine.KeyCode 数值一致），可传 `hold_ms` 控制按住时长。
`GET /ping` 的 `key_injection` 字段会如实报告拦截是否装上了。

## 在指定位置放置零件（不需要拖动）

建造界面里零件必须从左侧菜单**拖**到火箭上，纯点击放不上去。
`POST /build_place {"name": "...", "x": 0, "y": 0}` 直接构造游戏自己的数据结构：

```
SFS.Parts.PartSave { name, position, orientation }
  -> SFS.Builds.Blueprint(parts, stages, center, rotation, interiorView)
    -> SFS.Builds.BuildState.main.SpawnBlueprint(blueprint, applyUndo, logger)
```

零件名必须是合法的（先 `GET /build_catalog` 查询）：

```jsonc
// GET /build_catalog
{"ok":true,"ready":true,"count":14,"source":"scene_parts",
 "parts":["Fuel Tank","Fairing","Valiant Engine", ...]}
```

`source` 会说明零件名是从哪里拿到的，便于排障。
`POST /build_place` 会先校验零件名确实在目录里，**不确定的名字一律拒绝**，
绝不把未经验证的数据交给游戏内部。

其他已知事实（游戏 v1.6.00.16）：

- 菜单按钮的运行时类型是 `SFS.UI.ButtonPC : SFS.UI.Button : MonoBehaviour`
- `SFS.UI.Button` 的字段：`clickEvent`(`SFS.UI.ClickUnityEvent` : `UnityEvent<OnInputEndData>`)、
  `onClick` / `onUp` / `onRightClick`(`OptionalDelegate<OnInputEndData>`)、`buttonEnabled`(`bool`)
- `OnInputEndData(InputType, TouchPosition, bool click)`；`InputType`: `Touch=0, MouseLeft=1, MouseRight=2`
- 座位在非激活对象上时 `FindObjectsOfType` 找不到它（例如零件菜单 `PickGridUI`），
  此时经由持有者字段（`BuildManager.main.pickGrid`）取实例
- `PartsLoader.parts` / `partVariants` 在 v1.6.00.16 里**是 null**；
  零件名要走 `PartsLoader.LoadParts()` 的**返回值**（该方法不写静态字段）

## SFS 默认操作方法

给上层 AI 或用户对照用（`POST /key` 传对应的虚拟键码）：

| 操作 | 按键 | 键码 |
| --- | --- | --- |
| 向左 / 向右转向 | Q / E | 81 / 69 |
| 平移与俯仰（需先开 RCS） | W / A / S / D | 87 / 65 / 83 / 68 |
| 油门加大 / 减小 | Shift / Ctrl | 16 / 17 |
| RCS 开关 | R | 82 |
| 点火 / 执行下一级 | 空格 | 32 |
| 分级控制程序 | 回车 | 13 |
| 返回 | Esc | 27 |

> 油门与分级也可以完全不走按键：`POST /command` 的
> `set_throttle` / `throttle_on` / `throttle_off` / `stage` / `rcs_toggle`
> 是直接改游戏状态的，最可靠。

## 已验证的能力（实机测试）

| 能力 | 状态 |
| --- | --- |
| 飞行遥测（高度 / 速度 / 油门 / 分级 / 质量 / 是否可控） | ✅ |
| 菜单点击：主菜单 → 存档列表 → 世界 → 建造 | ✅ 含多级确认对话框 |
| 界面元素枚举（过滤屏外与未激活元素） | ✅ |
| 按键注入（Esc / 空格 / Q 等，**游戏不必在前台**） | ✅ |
| 飞行控制：点火、油门、分级、RCS | ✅ 实测速度随油门持续上升 |
| 建造：在指定坐标放置零件 + 镜头自动跟随 | ✅ 零件数与质量精确 +1 |
| 截图 | ✅ |
| 内置配置页 + 启动时自动开浏览器 | ✅ |
| **从零搭出一枚能正常飞的完整火箭** | ⚠️ 见下方「已知限制」 |

## 已知限制

- **摆放零件时必须给对间距，否则火箭连不成一体。**
  `build_place` 会把你给的坐标**原样**交给游戏（不做吸附），所以如果零件之间
  距离不对（实测：把引擎放在离燃料罐 6 米处），发射时结构会散架 ——
  整枚火箭只剩 1 个零件，直接钻到地下。
  摆放时应参考零件实际尺寸，或把新零件紧贴已有零件放置。
- `build_place` **不做吸附与碰撞检查**，可能产生重叠。
- 界面枚举会过滤掉**屏外**与**未激活**的元素；**被其它界面遮挡**的元素无法在
  枚举阶段识别 —— 识别它们需要调用游戏自己的命中判定
  (`InputManager.CheckMouseOverState`)，而那会把 `mouseOverElement`
  逐个写一遍，导致界面上所有按钮依次高亮闪烁（实测确认，用户肉眼可见）。
  因此遮挡改为在**点击时验证一次**，命中不符会在返回值里给出 `warning` 字段。
- 滚动条里的元素若被裁切，其矩形中心可能仍落在屏幕范围内，此时枚举会误报；
  点击时的 `warning` 会捕获这种情况。

## 说明

- 模组只做只读遥测采集与少量指令，**不会修改存档文件**。
- `build_place` 会真实往当前建造场景里生成零件；如果不想被改动，
  请在测试用的存档里操作。
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
