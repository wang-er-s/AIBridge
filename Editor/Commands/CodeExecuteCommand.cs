using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using AIBridge.Runtime;
using AIBridge.Editor;
using UnityEngine;

public static class CodeExecuteCommand
{
    [AIBridge("执行C#代码片段或脚本文件。不传 url 时在 Editor 执行，传入手机 Runtime URL 时编译并下发到 Player 执行。",
        example:@"
Windows CMD 必须使用单引号包裹代码：
AIBridgeCLI CodeExecuteCommand_Execute --code 'using UnityEngine; Debug.Log(""Hello"");' --raw

PowerShell 或 Bash 可以使用双引号（需要转义）：
AIBridgeCLI CodeExecuteCommand_Execute --code ""using UnityEngine; Debug.Log(\""Hello\"");"" --raw

// 上边代码是你需要提供的逻辑，不需要写方法，只需要写using和逻辑
// 以上的代码会被编译成下边的
using UnityEngine;

public static class CodeExecutor
{{
    public static object Execute()
    {{
        Debug.Log(""Hello"");
        return null;
    }}
}}
")]
    public static IEnumerator Execute(
        [Description("要执行的代码")] string code = null,
        [Description("要执行的文件，需要完整路径")] string file = null,
        [Description("手机 Runtime URL；为空时在 Editor 执行")] string url = null,
        [Description("手机 Runtime 执行超时，单位毫秒")]
        int runtimeTimeout = AIBridgeProtocol.DefaultExecutionTimeoutMs)
    {
        if (!string.IsNullOrEmpty(file))
        {
            if (File.Exists(file))
            {
                code = File.ReadAllText(file);
                if (string.IsNullOrWhiteSpace(code))
                {
                    yield return CommandResult.Failure("File is empty.");
                    yield break;
                }
            }
            else
            {
                yield return CommandResult.Failure("File is not exist.");
                yield break;
            }
        }
        if (string.IsNullOrWhiteSpace(code))
        {
            yield return CommandResult.Failure("Code is null or empty.");
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(url))
        {
            yield return ExecuteInPlayer(code, url, runtimeTimeout);
            yield break;
        }

        var codeRunner = new CSharpCodeRunner();
        // Capture logs during execution
        var logMessages = new List<string>();
        var logHandler = new Application.LogCallback((logString, stackTrace, type) =>
        {
            var prefix = type switch
            {
                LogType.Error or LogType.Exception => "[ERROR] ",
                LogType.Warning => "[WARNING] ",
                _ => "[INFO] "
            };
            logMessages.Add(prefix + logString);
        });

        EvaluationResult result = null;
        try
        {
            Application.logMessageReceived += logHandler;
            result = codeRunner.CompileAndExecute(code);
            while (result != null && result.IsPending)
            {
                result = codeRunner.ContinuePendingTask(result);
                yield return null;
            }
        }
        finally
        {
            Application.logMessageReceived -= logHandler;
        }

        var output = string.Join("\n", logMessages);
        
        if (result == null || !result.Success)
        {
            yield return CommandResult.Failure($"Execution Failed:\n{result.ErrorMessage}\nOutput:\n{output}");
        }
        else
        {
            var returnValue = result.ReturnValue == null ? "null" : result.ReturnValue.ToString();
            yield return CommandResult.Success($"ReturnValue:\n{returnValue}\nOutput:\n{output}");
        }
    }

    private static IEnumerator ExecuteInPlayer(string code, string url, int timeout)
    {
        if (!AIBridgeHybridClrUtility.IsInstalled())
        {
            yield return CommandResult.Failure(
                "Player code execution requires HybridCLR. Install it first: "
                + AIBridgeHybridClrUtility.GitUrl);
            yield break;
        }

        if (!AIBridgeRuntimeEditorSettings.EnableRuntimeBridge
            || !AIBridgeRuntimeEditorSettings.EnableRuntimeCodeExecution)
        {
            yield return CommandResult.Failure(
                "Runtime code execution is disabled in Window/AIBridge Runtime settings.");
            yield break;
        }

        var codeRunner = new CSharpCodeRunner(true);
        var compileResult = codeRunner.CompileForRuntime(code);
        if (!compileResult.Success)
        {
            yield return CommandResult.Failure("Runtime compilation failed:\n" + compileResult.ErrorMessage);
            yield break;
        }

        var success = false;
        object returnValue = null;
        string error = null;
        yield return RuntimeCodeExecuteClient.Execute(
            url,
            compileResult.CompiledAssemblyBytes,
            compileResult.EntryTypeName,
            compileResult.EntryMethodName,
            timeout,
            (completedSuccessfully, result, executeError) =>
            {
                success = completedSuccessfully;
                returnValue = result;
                error = executeError;
            });

        if (!success)
        {
            yield return CommandResult.Failure("Player execution failed:\n" + (error ?? "Unknown Runtime error."));
            yield break;
        }

        yield return CommandResult.Success(new
        {
            target = "player",
            runtimeUrl = url,
            returnValue = returnValue
        });
    }
}