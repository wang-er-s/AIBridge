---
name: aibridge
description: 通过 AI Bridge CLI 自动化 Unity Editor 和手机 Player Runtime，执行 C# 代码、查询状态、测试输入、读取日志和捕获截图。当用户需要以编程方式与 Unity Editor 或手机 App 交互时使用。
---

# AI Bridge Unity Skill

## 概述

通过 AI Bridge CLI 以编程方式控制 Unity Editor 和手机 Player Runtime，用于快速原型开发、测试和自动化。

## 何时使用此 Skill

- 测试 UI 交互（按钮、滑块、输入框）
- 调试场景对象和组件
- 使用代码执行进行快速原型开发
- 自动化重复的 Editor 任务
- 为文档捕获截图或 GIF
- 通过局域网或 ADB 测试手机 App

## 前置条件

- Unity 项目已安装 AI Bridge 包
- CLI 位置：`AIBridgeCache/CLI/`，Windows 使用 `AIBridgeCLI.exe`，macOS/Linux 使用 `AIBridgeCLI`
- 执行命令并由脚本解析结果时添加 `--raw`；查看帮助时可省略
- `AIBridgeCLI --help` 查看全局帮助
- `AIBridgeCLI Commands` 查看当前 Editor 已注册命令
- `AIBridgeCLI <CommandName> --help` 查看命令详情
- 手机命令仍由本地 Editor 转发，Editor 和手机 Player 都必须保持运行

常用全局参数：

- `--timeout <ms>` - 超时时间，单位毫秒，默认 `5000`
- `--no-wait` - 不等待结果，适合只触发命令的场景
- `--raw` - 输出原始 JSON
- `--quiet` - 安静模式，减少额外输出
- `--json <json>` - 透传并合并 JSON 参数，同名字段会覆盖
- `--stdin` - 从标准输入读取 JSON 参数
- `--help` / `-h` - 显示帮助，不执行命令

对应的CLI位于：

- macOS/Linux：`./AIBridgeCache/CLI/AIBridgeCLI`
- Windows PowerShell：`& "$PWD/AIBridgeCache/CLI/AIBridgeCLI.exe"`

当命令执行时间可能超过默认 5 秒时，必须显式增加 `--timeout`，例如：

- 编译：`AIBridgeCLI Compile --raw --timeout 300000`
- 跑测试：`AIBridgeCLI CodeExecuteCommand_Execute --code '...' --raw --timeout 300000`
- 截图/GIF 等较慢操作：按实际情况设置更大的超时

在 Windows PowerShell 中调用 `AIBridgeCLI.exe` 时使用 `&`：

- 如果可执行文件路径 **不包含空格**，优先直接写：
  `E:\path\to\AIBridgeCLI.exe Compile --raw`
- 如果路径 **包含空格**，必须写成：
  `& "E:\path with spaces\AIBridgeCLI.exe" Compile --raw`

不要只用引号包裹可执行文件路径而不加 `&`。

## 常见工作流

### 工作流 1：测试 UI 交互

1. 创建 UI 元素（Canvas、Button、EventSystem）
2. 进入播放模式：`AIBridgeCLI EditorCommand_Play`
3. 模拟点击：`AIBridgeCLI InputSimulationCommand_Click --path "Canvas/Button1" --raw`
4. 使用代码执行验证结果
5. 退出播放模式：`AIBridgeCLI EditorCommand_Stop`

**注意：** UI 点击需要场景中有 EventSystem。

### 工作流 2：查询输入目标路径

需要查询 GameObject 的完整层级路径和实例 ID 时，使用
`CodeExecuteCommand_Execute` 遍历场景对象，并调用
`AIBridgeGameObjectResolver.GetHierarchyPath(gameObject)`：

```bash
AIBridgeCLI CodeExecuteCommand_Execute --code 'using System.Linq; using UnityEngine; using AIBridge.Runtime; return string.Join("\n", Resources.FindObjectsOfTypeAll<GameObject>().Where(go => go.scene.IsValid() && go.scene.isLoaded).Select(go => AIBridgeGameObjectResolver.GetHierarchyPath(go) + " | instanceId=" + go.GetInstanceID()));' --raw
```

Runtime 中存在重名路径时，输入命令必须同时提供 `path` 和 `instanceId`。

### 工作流 3：快速代码原型

1. 编写 C# 代码片段（仅 using 语句 + 逻辑）
2. 执行：`AIBridgeCLI CodeExecuteCommand_Execute --code 'using UnityEngine; Debug.Log("Test");' --raw`
3. 在 Unity 控制台查看输出
4. 快速迭代，无需创建脚本文件

**对于较长代码：** 保存到 `AIBridgeCache/code/` 并使用 `--file` 参数。
只读查询建议显式使用 `return`，结果通常包含返回值和执行输出。

### 工作流 4：编译验证代码是否有报错

1. 编译 Unity：`AIBridgeCLI Compile --raw --timeout 300000`
2. 查看返回值是否有报错

## 手机 Runtime

手机 Player 默认使用 Development Build 启用 Runtime Bridge。Release Build 只有在
Runtime 设置中显式允许后才能启用。测试期间保持 App 在前台，后台暂停可能导致请求超时。

### 连接手机

局域网连接不需要 ADB。电脑和手机位于同一可信局域网时，使用手机 IP：

```bash
curl http://192.168.1.20:27182/aibridge-self/health
AIBridgeCLI CodeExecuteCommand_Execute --code 'return "LAN_OK";' --url http://192.168.1.20:27182 --runtimeTimeout 30000 --raw --timeout 60000
```

如果出现 `Insecure connection not allowed`，在 Player Settings 中将
`InsecureHttpOption` 设为 `AlwaysAllowed`；测试结束后按项目安全要求恢复。

USB 调试可通过 ADB 把电脑本地端口映射到手机：

```bash
adb devices -l
adb forward tcp:27182 tcp:27182
curl http://127.0.0.1:27182/aibridge-self/health
```

ADB 模式使用 `--url http://127.0.0.1:27182`。局域网模式直接使用
`--url http://<手机IP>:27182`，不需要执行 `adb forward`。

远程命令同时受两个超时控制：

- `--runtimeTimeout`：Editor 等待手机 Runtime 的时间
- `--timeout`：CLI 等待 Editor 返回结果的时间，必须大于 `runtimeTimeout`

只有命令帮助中包含 `url` 参数的命令才能转发到手机。执行前使用
`AIBridgeCLI <CommandName> --help` 确认。

### 手机输入测试

- 场景必须存在 `EventSystem`，否则返回 `event_system_missing`
- UI 目标需要正常的 Canvas Raycaster 和 Pointer Handler
- 2D Sprite 目标需要相机上的 `Physics2DRaycaster`、目标上的 `Collider2D` 和 Pointer Handler
- Runtime 重名路径必须同时提供 `path` 和 `instanceId`
- `instanceId` 在 App 重启或重新安装后会变化，执行输入前重新查询
- 坐标必须是有限非负数，并位于 `Screen.width`、`Screen.height` 范围内
- 横竖屏切换会改变屏幕坐标，切换后重新查询目标位置
- Drag 的首个点使用目标 `path`，后续点可使用屏幕坐标

示例：

```bash
AIBridgeCLI InputSimulationCommand_Drag --points '[{"path":"SnakeDragSurface","instanceId":-1170},{"x":1588,"y":143}]' --url http://192.168.1.20:27182 --runtimeTimeout 30000 --raw --timeout 60000
```

### 下载手机截图和 GIF

远程截图结果中的 `imagePath` / `gifPath` 是手机文件路径，电脑不能直接读取。
使用返回的 `downloadUrl` 下载到当前工作区：

```bash
AIBridgeCLI ScreenshotCommand_Image --url http://192.168.1.20:27182 --runtimeTimeout 120000 --raw --timeout 180000
curl -o AIBridgeCache/phone-screenshot.png "http://192.168.1.20:27182/aibridge-self/artifacts/<filename>.png"
```

GIF 使用相同流程。Player 关闭或重新安装后，旧下载地址可能失效。

### 手机测试安全

Runtime Bridge 可执行代码。只在可信私有网络启用，不要将 `27182` 映射到公网。
App 重启或重新安装会清除临时对象、日志捕获状态和旧实例 ID。

## 命令发现和帮助

Editor 已打开即可查询命令，不需要进入 Play Mode。先列出当前命令：

```bash
AIBridgeCLI Commands
```

需要机器可读结果时：

```bash
AIBridgeCLI Commands --raw
```

查看某个命令详情：

```bash
AIBridgeCLI InputSimulationCommand_Click --help
AIBridgeCLI InputSimulationCommand_Click -h
```

命令详情包含描述、Usage、参数类型、必填状态、默认值和示例。`--help` / `-h` 会在执行前拦截，不会执行目标命令。

## 边界情况和故障排除

- **`event_system_missing`** - 为输入交互添加 EventSystem
- **`Insecure connection not allowed`** - 允许 Player Settings 中的 HTTP 连接
- **`Cannot connect to destination host`** - 检查 Player 是否运行、IP、端口、防火墙和 AP 隔离
- **长时间操作** - 使用 `--timeout` 参数（例如 GIF 需要 15 秒以上）
- **"Unknown command"** - 先运行 `AIBridgeCLI Commands`，不要使用 Skill 中未列出的旧命令
- **代码执行错误** - 检查 using 语句和语法
- **"Timeout waiting for result"** - 确认 Unity Editor 已打开、AI Bridge 已启用，并检查 `AIBridgeCache/CLI/.platform`
- **CLI 平台不匹配** - 在 `Window > AIBridge` 设置中点击 `Replace CLI`
- **无日志** - `Log --raw` 无日志时可能返回提示字符串，不要假设所有命令的 `data` 都是数组

---
