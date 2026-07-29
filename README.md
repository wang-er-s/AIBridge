# AI Bridge

[English](./README_EN.md) | 中文

AI 编码助手与 Unity Editor 之间的文件通信框架。

## 功能特性

- **GameObject** - 创建、删除、查找、重命名、复制、切换激活状态
- **Transform** - 位置、旋转、缩放、父子层级、LookAt
- **Component/Inspector** - 获取/设置属性、添加/移除组件
- **Scene** - 加载、保存、获取层级、创建新场景
- **Prefab** - 实例化、保存、解包、应用覆盖
- **Asset** - 搜索、导入、刷新、按过滤器查找
- **编辑器控制** - 编译、撤销/重做、播放模式、聚焦窗口
- **截图 & GIF** - 捕获游戏视图、录制动画 GIF
- **批量命令** - 高效执行多个命令
- **代码执行** - 在编辑器或运行时动态执行 C# 代码

## 为什么选择 AI Bridge？（对比 Unity MCP）

| 特性         | AI Bridge    | Unity MCP        |
| ------------ | ------------ | ---------------- |
| 通信方式     | 文件通信     | WebSocket 长连接 |
| Unity 编译时 | **正常工作** | 连接断开         |
| 端口冲突     | 无           | 可能导致重连失败 |
| 多工程支持   | **支持**     | 不支持           |
| 稳定性       | **高**       | 受编译/重启影响  |
| 上下文消耗   | **低**       | 较高             |
| 扩展性       | 简单接口     | 需了解 MCP 协议  |

**MCP 的问题**：Unity MCP 使用 WebSocket 长连接。当 Unity 重新编译时（开发过程中频繁发生），连接会断开。端口冲突还可能导致无法重连，使用体验较差。

**AI Bridge 方案**：通过文件通信，AI Bridge 从根源上完美解决了这些问题。命令以 JSON 文件写入，结果以文件读取——简单、稳定、可靠，不受 Unity 状态影响。

## 安装

### 通过 Unity Package Manager

1. 打开 Unity Package Manager（Window > Package Manager）
2. 点击 "+" > "Add package from git URL"
3. 输入：`https://github.com/wang-er-s/AIBridge.git`

### 手动安装

1. 下载或克隆此仓库
2. 将整个文件夹复制到 Unity 项目的 `Packages` 目录

## 系统要求

- Unity 2021.3 或更高版本
- .NET 9.0 Runtime（用于 CLI 工具）
- Newtonsoft.Json (com.unity.nuget.newtonsoft-json)

## 快速开始

### 0. 首次安装配置

安装 AI Bridge 后，需要进行以下初始化步骤：

1. **打开设置窗口**：`Window > AIBridge`
2. **安装 Skill 到 Agent**：切换到 `Tools` 标签，点击 **"Copy To Agent"** 按钮，将 Skill 文档安装到 agent的skills 目录
3. **确认命令**：保持 Unity Editor 打开，运行 `AIBridgeCLI Commands` 查看当前已注册命令。

### 1. 添加自定义命令

创建一个静态类，使用 `[AIBridge]` 特性标记方法：

```csharp
using AIBridge.Editor;
using System.Collections;
using System.ComponentModel;

public static class MyCustomCommand
{
    [AIBridge("创建一个具有特定设置的自定义立方体")]
    public static IEnumerator CreateCustomCube(
        [Description("立方体名称")] string name = "CustomCube",
        [Description("立方体大小")] float size = 1.0f)
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.transform.localScale = Vector3.one * size;

        yield return CommandResult.Success($"创建了 {name}，大小为 {size}");
    }
}
```

**关键要点：**

- 方法必须是 `static` 并返回 `IEnumerator`
- 可以使用yield return new WaitForSeconds或者yield return new WaitUntil
- 使用 `[AIBridge]` 特性添加描述
- 使用 `[Description]` 为参数添加文档（可选，不写则的字段名）
- 返回 `CommandResult.Success()` 或 `CommandResult.Failure()`

### 2. 刷新命令列表

添加自定义命令后，不需要重新生成 Skill 文档。打开 Unity Editor 后，使用
`AIBridgeCLI Commands` 查看当前注册命令，使用 `AIBridgeCLI <CommandName> --help`
查看参数详情。

### 3. 使用命令

使用 CLI 工具或让 AI 助手调用你的命令：

```bash
AIBridgeCLI MyCustomCommand_CreateCustomCube --name "MyCube" --size 2.0
```

## 命令注册

## Skill 文档

`Skill~/SKILL.md` 文件是为 AI 助手（如 Droid、Claude、GPT 等）维护的使用指南。
命令列表不再写入 Skill 文件，使用 CLI 实时查询：

```bash
AIBridgeCLI Commands
AIBridgeCLI <CommandName> --help
```

### 安装 Skill 到 Agent 目录

**首次安装（必需）：**

1. 打开 `Window > AIBridge` 窗口
2. 切换到 `Tools` 标签
3. 点击 **"Copy To Agent"** 按钮

**复制逻辑：**
- 系统会先扫描项目根目录中已存在的 AI 编辑器目录（`.cursor`、`.agent`、`.factory`、`.claude`、`.codex` 等）
- 如果找到任何已存在的目录，会将 Skill 文档复制到这些目录的 `skills/aibridge/` 子目录中
- 如果没有找到任何 AI 编辑器目录，会自动创建 `.agent` 目录并复制 Skill 文档

**示例：**
- 如果项目中已有 `.factory` 目录，Skill 会被复制到 `.factory/skills/aibridge/SKILL.md`
- 如果项目中同时有 `.factory` 和 `.cursor` 目录，两个目录都会被更新
- 如果项目中没有任何 AI 编辑器目录，会创建 `.agent/skills/aibridge/SKILL.md`

### 更新 Skill 文档

修改固定工作流或使用说明后，直接编辑 `Skill~/SKILL.md`，然后在 `Tools` 标签点击
**"Copy To Agent"** 更新 Agent 目录。命令元数据通过 `AIBridgeCLI Commands` 和
`AIBridgeCLI <CommandName> --help` 实时获取。

## 许可证

MIT License

## 贡献

欢迎贡献！请随时提交 Pull Request。
