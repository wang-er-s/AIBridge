using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using AIBridge.Runtime;
using UnityEditor;

namespace AIBridge.Editor
{
    public static class CompileCommand
    {
        private static readonly Regex MsBuildErrorRegex = new Regex(
            @"^\s*(?<file>.+?)\((?<line>\d+),(?<column>\d+)\):\s*(?<type>error|warning)\s+(?<code>\w+):\s*(?<message>.+)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        [AIBridge("内部调用的", exposeToSkill: false)]
        public static IEnumerator Start()
        {
            if (EditorApplication.isCompiling)
            {
                yield return CommandResult.Success(new
                {
                    compilationStarted = false,
                    alreadyCompiling = true,
                    message = "Compilation already in progress. Use CompileCommand_Status to poll."
                });
                yield break;
            }

            if (EditorApplication.isPlaying | EditorApplication.isPaused)
            {
                yield return CommandResult.Failure("Unity is playing, compile need stop play");
            }

            AssetDatabase.Refresh();
            yield return CommandResult.Success(new
            {
                compilationStarted = true,
                message = "Compilation started. Use CompileCommand_Status to poll for results."
            });
        }

        [AIBridge("内部调用的", exposeToSkill: false)]
        public static IEnumerator Status(bool includeDetails = true)
        {
            if (EditorApplication.isCompiling)
            {
                yield return CommandResult.Success(new { status = "compiling", isCompiling = true });
                yield break;
            }

            var result = CompilationTracker.GetResult();
            string statusStr;
            switch (result.status)
            {
                case CompilationTracker.CompilationStatus.Success: statusStr = "success"; break;
                case CompilationTracker.CompilationStatus.Failed: statusStr = "failed"; break;
                case CompilationTracker.CompilationStatus.Compiling: statusStr = "compiling"; break;
                default: statusStr = "idle"; break;
            }

            if (includeDetails)
            {
                yield return CommandResult.Success(new
                {
                    status = statusStr,
                    isCompiling = false,
                    errorCount = result.errorCount,
                    duration = result.durationSeconds,
                    errors = ConvertErrors(result.errors),
                });
            }
            else
            {
                yield return CommandResult.Success(new
                {
                    status = statusStr,
                    isCompiling = false,
                    errorCount = result.errorCount,
                    duration = result.durationSeconds
                });
            }
        }

        private static List<object> ConvertErrors(List<CompilationTracker.CompilerError> errorList)
        {
            var result = new List<object>();
            if (errorList == null) return result;
            foreach (var e in errorList)
            {
                result.Add(new { file = e.file, line = e.line, column = e.column, message = e.message, code = e.errorCode, assembly = e.assemblyName });
            }
            return result;
        }
    }
}
