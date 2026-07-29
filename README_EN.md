# AI Bridge

English | [中文](./README.md)

File-based communication framework between AI Code assistants and Unity Editor.

## Features

- **GameObject** - Create, destroy, find, rename, duplicate, toggle active
- **Transform** - Position, rotation, scale, parent hierarchy, look at
- **Component/Inspector** - Get/set properties, add/remove components
- **Scene** - Load, save, get hierarchy, create new
- **Prefab** - Instantiate, save, unpack, apply overrides
- **Asset** - Search, import, refresh, find by filter
- **Editor Control** - Compile, undo/redo, play mode, focus window
- **Screenshot & GIF** - Capture game view, record animated GIFs
- **Batch Commands** - Execute multiple commands efficiently
- **Code Execution** - Execute C# code dynamically in Editor or Runtime

## Why AI Bridge? (vs Unity MCP)

| Feature               | AI Bridge          | Unity MCP                       |
| --------------------- | ------------------ | ------------------------------- |
| Communication         | File-based         | WebSocket                       |
| During Unity Compile  | **Works normally** | Connection lost                 |
| Port Conflicts        | None               | May cause reconnection failure  |
| Multi-Project Support | **Yes**            | No                              |
| Stability             | **High**           | Affected by compile/restart     |
| Context Usage         | **Low**            | Higher                          |
| Extensibility         | Simple interface   | Requires MCP protocol knowledge |

**The Problem with MCP**: Unity MCP uses persistent WebSocket connections. When Unity recompiles (which happens frequently during development), the connection breaks. Port conflicts can also prevent reconnection, leading to a frustrating experience.

**AI Bridge Solution**: By using file-based communication, AI Bridge completely avoids these issues. Commands are written as JSON files and results are read back - simple, stable, and reliable regardless of Unity's state.

## Installation

### Via Unity Package Manager

1. Open Unity Package Manager (Window > Package Manager)
2. Click "+" > "Add package from git URL"
3. Enter: `https://github.com/wang-er-s/AIBridge.git`

### Manual Installation

1. Download or clone this repository
2. Copy the entire folder to your Unity project's `Packages` folder

## Requirements

- Unity 2021.3 or later
- .NET 9.0 Runtime (for CLI tool)
- Newtonsoft.Json (com.unity.nuget.newtonsoft-json)

## Quick Start

### 0. Initial Setup

After installing AI Bridge, you need to complete the following initialization steps:

1. **Open Settings Window**: `Window > AIBridge`
2. **Install Skill to Agent**: Switch to the `Tools` tab and click the **"Copy To Agent"** button to install the Skill documentation to the agent's skills directory
3. **Verify Commands**: Keep Unity Editor open and run `AIBridgeCLI Commands` to list registered commands.

### 1. Add Custom Commands

Create a static class with methods marked with `[AIBridge]` attribute:

```csharp
using AIBridge.Editor;
using System.Collections;
using System.ComponentModel;

public static class MyCustomCommand
{
    [AIBridge("Create a custom cube with specific settings")]
    public static IEnumerator CreateCustomCube(
        [Description("Cube name")] string name = "CustomCube",
        [Description("Cube size")] float size = 1.0f)
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.transform.localScale = Vector3.one * size;

        yield return CommandResult.Success($"Created {name} with size {size}");
    }
}
```

**Key Points:**

- Method must be `static` and return `IEnumerator`
- You can use `yield return new WaitForSeconds` or `yield return new WaitUntil`
- Use `[AIBridge]` attribute with description
- Use `[Description]` for parameter documentation (optional, defaults to field name if not provided)
- Return `CommandResult.Success()` or `CommandResult.Failure()`

### 2. Discover Commands

After adding custom commands, you do not need to regenerate the Skill documentation.
With Unity Editor open, use `AIBridgeCLI Commands` to list registered commands and
`AIBridgeCLI <CommandName> --help` to inspect parameters.

### 3. Use Commands

Use the CLI tool or let AI assistants call your commands:

```bash
AIBridgeCLI MyCustomCommand_CreateCustomCube --name "MyCube" --size 2.0
```

## Command Registration

## Skill Documentation

The `Skill~/SKILL.md` file is maintained guidance for AI assistants (like Droid, Claude, and GPT).
The command list is no longer written into the Skill file. Query current metadata through the CLI:

```bash
AIBridgeCLI Commands
AIBridgeCLI <CommandName> --help
```

### Install Skill to Agent Directory

**Initial Installation (Required):**

1. Open the `Window > AIBridge` window
2. Switch to the `Tools` tab
3. Click the **"Copy To Agent"** button

**Copy Logic:**
- The system will first scan for existing AI editor directories in the project root (`.cursor`, `.agent`, `.factory`, `.claude`, `.codex`, etc.)
- If any existing directories are found, the Skill documentation will be copied to the `skills/aibridge/` subdirectory of these directories
- If no AI editor directories are found, it will automatically create a `.agent` directory and copy the Skill documentation

**Examples:**
- If the project already has a `.factory` directory, the Skill will be copied to `.factory/skills/aibridge/SKILL.md`
- If the project has both `.factory` and `.cursor` directories, both will be updated
- If the project has no AI editor directories, it will create `.agent/skills/aibridge/SKILL.md`

### Update Skill Documentation

When you change fixed workflows or usage guidance, edit `Skill~/SKILL.md` directly, then click
**"Copy To Agent"** in the `Tools` tab to update Agent directories. Query command metadata at
runtime with `AIBridgeCLI Commands` and `AIBridgeCLI <CommandName> --help`.

## License

MIT License

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.
