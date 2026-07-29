---
name: aibridge
description: 通过 AI Bridge CLI 自动化 Unity Editor 操作，执行 C# 代码、查询状态、测试输入、读取日志和捕获截图。当用户需要以编程方式与 Unity 项目交互时使用。
---

# AI Bridge Unity Skill

## 概述

通过 AI Bridge CLI 以编程方式控制 Unity Editor，用于快速原型开发、测试和自动化。

## 何时使用此 Skill

- 测试 UI 交互（按钮、滑块、输入框）
- 调试场景对象和组件
- 使用代码执行进行快速原型开发
- 自动化重复的 Editor 任务
- 为文档捕获截图或 GIF

## 前置条件

- Unity 项目已安装 AI Bridge 包
- CLI 位置：`AIBridgeCache/CLI/`，Windows 使用 `AIBridgeCLI.exe`，macOS/Linux 使用 `AIBridgeCLI`
- 执行命令并由脚本解析结果时添加 `--raw`；查看帮助时可省略
- `AIBridgeCLI --help` 查看全局帮助
- `AIBridgeCLI Commands` 查看当前 Editor 已注册命令
- `AIBridgeCLI <CommandName> --help` 查看命令详情

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

- **"NoEventSystem"** - 为 UI 交互添加 EventSystem
- **长时间操作** - 使用 `--timeout` 参数（例如 GIF 需要 15 秒以上）
- **"Unknown command"** - 先运行 `AIBridgeCLI Commands`，不要使用 Skill 中未列出的旧命令
- **代码执行错误** - 检查 using 语句和语法
- **"Timeout waiting for result"** - 确认 Unity Editor 已打开、AI Bridge 已启用，并检查 `AIBridgeCache/CLI/.platform`
- **CLI 平台不匹配** - 在 `Window > AIBridge` 设置中点击 `Replace CLI`
- **无日志** - `Log --raw` 无日志时可能返回提示字符串，不要假设所有命令的 `data` 都是数组

---
