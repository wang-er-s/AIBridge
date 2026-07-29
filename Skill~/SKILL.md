---
name: aibridge
description: 通过 AI Bridge CLI 自动化 Unity Editor 操作 - 管理 GameObject、场景、资源、预制体、组件，执行 C# 代码，截图和控制播放模式。当用户需要以编程方式与 Unity 项目交互时使用。
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
- CLI 位置：`AIBridgeCache\CLI\AIBridgeCLI.exe`
- 始终添加 `--raw` 标志以获取 JSON 输出
- `E:\path\to\AIBridgeCLI.exe Compile --help` 查看全局帮助

常用全局参数：

- `--timeout <ms>` - 超时时间，单位毫秒，默认 `5000`
- `--no-wait` - 不等待结果，适合只触发命令的场景
- `--raw` - 输出原始 JSON
- `--quiet` - 安静模式，减少额外输出
- `--json <json>` - 透传并合并 JSON 参数，同名字段会覆盖
- `--stdin` - 从标准输入读取 JSON 参数
- `--help` - 显示帮助

当命令执行时间可能超过默认 5 秒时，必须显式增加 `--timeout`，例如：

- 编译：`AIBridgeCLI.exe Compile --raw --timeout 300000`
- 跑测试：`AIBridgeCLI.exe CodeExecuteCommand_Execute --code '...' --raw --timeout 300000`
- 截图/GIF 等较慢操作：按实际情况设置更大的超时

在 Windows PowerShell 中调用 `AIBridgeCLI.exe` 时注意命令写法：

- 如果可执行文件路径 **不包含空格**，优先直接写：
  `E:\path\to\AIBridgeCLI.exe Compile --raw`
- 如果路径 **包含空格**，必须写成：
  `& "E:\path with spaces\AIBridgeCLI.exe" Compile --raw`

不要写成：

`"E:\path\to\AIBridgeCLI.exe" Compile --raw`

## 常见工作流

### 工作流 1：测试 UI 交互

1. 创建 UI 元素（Canvas、Button、EventSystem）
2. 进入播放模式：`EditorCommand_Play`
3. 模拟点击：`InputSimulationCommand_Click --path "Canvas/Button1"`
4. 使用代码执行验证结果
5. 退出播放模式：`EditorCommand_Stop`

**注意：** UI 点击需要场景中有 EventSystem。

### 工作流 2：调试场景对象

1. 获取场景层级：`SceneCommand_GetHierarchy`
2. 查找特定对象：`GameObjectCommand_Find --name "Player"`
3. 获取组件信息：`InspectorCommand_GetComponents --path "Player"`
4. 执行代码检查/修改：`CodeExecuteCommand_Execute --code '...'`

### 工作流 3：快速代码原型

1. 编写 C# 代码片段（仅 using 语句 + 逻辑）
2. 执行：`CodeExecuteCommand_Execute --code 'using UnityEngine; Debug.Log("Test");'`
3. 在 Unity 控制台查看输出
4. 快速迭代，无需创建脚本文件

**对于较长代码：** 保存到 `AIBridgeCache/code/` 并使用 `--file` 参数。

### 工作流 4：资源管理

1. 搜索资源：`AssetDatabaseCommand_Search --mode prefab --keyword "Player"`
2. 加载资源信息：`AssetDatabaseCommand_Load --assetPath "Assets/Prefabs/Player.prefab"`
3. 根据需要实例化或修改

### 工作流 5：编译验证代码是否有报错

1. 编译unity：`Compile`
2. 查看返回值是否有报错

## 如何查询命令详情

### 查看命令详细用法

```bash
AIBridgeCLI GameObjectCommand_Find --help
```

返回包含：

- 命令描述
- 参数列表（名称、类型、是否必需、描述、默认值）
- 使用示例

通常来说使用一个命令前，你都要查询一下该命令的详细用法（除非你之前查询过）

<!-- AUTO-GENERATED-COMMANDS-START -->
## 命令分类

-- **Compile** - 编译代码，并返回编译结果，如果有报错是会直接返回，不需要再查看Log

### CodeExecute

- **CodeExecuteCommand_Execute** - 执行C#代码片段或脚本文件。不传 url 时在 Editor 执行，传入手机 Runtime URL 时编译并下发到 Player 执行。

### Editor

- **EditorCommand_GetState** - 获取当前编辑器状态（播放/暂停/编译状态）
- **EditorCommand_Log** - 向 Unity 控制台输出日志消息
- **EditorCommand_Pause** - 切换或设置暂停状态
- **EditorCommand_Play** - 进入播放模式
- **EditorCommand_Stop** - 退出播放模式

### GetLogs

- **GetLogsCommand_StartCapture** - 开始捕获日志到缓冲区（精准模式），捕获的日志带毫秒级时间戳
- **GetLogsCommand_StopCapture** - 停止捕获日志，返回捕获的日志总数
- **Log** - 从 Unity 编辑器获取控制台日志

### InputSimulation

- **InputSimulationCommand_Click** - 通过路径模拟点击 GameObject (Only Runtime)
- **InputSimulationCommand_ClickAt** - 在屏幕坐标处模拟点击 (Only Runtime)
- **InputSimulationCommand_ClickByInstanceId** - 通过实例 ID 模拟点击 GameObject (Only Runtime)
- **InputSimulationCommand_Drag** - 通过路径模拟从一个对象拖动到另一个对象 (Only Runtime)
- **InputSimulationCommand_DragByInstanceId** - 通过实例 ID 模拟从一个对象拖动到另一个对象 (Only Runtime)
- **InputSimulationCommand_LongPress** - 通过路径模拟长按 GameObject (Only Runtime)
- **InputSimulationCommand_LongPressByInstanceId** - 通过实例 ID 模拟长按 GameObject (Only Runtime)

### Screenshot

- **ScreenshotCommand_Gif** - 捕获多个截图并合成 GIF，至少需要 15 秒超时
- **ScreenshotCommand_Image** - 捕获 Game 视图的截图

<!-- AUTO-GENERATED-COMMANDS-END -->

## 边界情况和故障排除

- **"NoEventSystem"** - 为 UI 交互添加 EventSystem
- **长时间操作** - 使用 `--timeout` 参数（例如 GIF 需要 15 秒以上）
- **路径未找到** - 使用 `SceneCommand_GetHierarchy` 验证路径
- **代码执行错误** - 检查 using 语句和语法

---
