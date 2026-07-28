using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;

namespace AIBridge.Editor
{
    [InitializeOnLoad]
    public static class CommandRegistry
    {
        private static readonly Dictionary<string, CommandEntry> _registry =
            new Dictionary<string, CommandEntry>(32);

        static CommandRegistry()
        {
            RegisterBuiltInCommands();
        }

        public static CommandEntry Register(Type commandType, string methodName)
        {
            if (commandType == null)
            {
                throw new ArgumentNullException(nameof(commandType));
            }

            if (string.IsNullOrWhiteSpace(methodName))
            {
                throw new ArgumentException("Method name cannot be null or empty.", nameof(methodName));
            }

            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var method = commandType.GetMethod(methodName, flags);
            if (method == null)
            {
                throw new MissingMethodException(commandType.FullName, methodName);
            }

            return Register(method);
        }

        public static CommandEntry Register(MethodInfo method)
        {
            if (method == null)
            {
                throw new ArgumentNullException(nameof(method));
            }

            var type = method.DeclaringType;
            var attr = method.GetCustomAttribute<AIBridgeAttribute>();
            if (type == null)
            {
                throw new ArgumentException("Command method must have a declaring type.", nameof(method));
            }
            if (!method.IsStatic)
            {
                throw new ArgumentException($"{type.FullName}.{method.Name} must be static.", nameof(method));
            }
            if (method.ReturnType != typeof(IEnumerator))
            {
                throw new ArgumentException($"{type.FullName}.{method.Name} must return IEnumerator.", nameof(method));
            }
            if (attr == null)
            {
                throw new ArgumentException(
                    $"{type.FullName}.{method.Name} must declare AIBridgeAttribute.",
                    nameof(method));
            }

            var commandName = attr.Name ?? $"{type.Name}_{method.Name}";
            if (_registry.TryGetValue(commandName, out var existing))
            {
                if (existing.Method == method)
                {
                    return existing;
                }

                throw new InvalidOperationException(
                    $"Command '{commandName}' is already registered by " +
                    $"{existing.Method.DeclaringType?.FullName}.{existing.Method.Name}.");
            }

            var entry = new CommandEntry
            {
                Name = commandName,
                Description = attr.Description,
                Example = attr.Example,
                Method = method,
                Parameters = method.GetParameters(),
                Attribute = attr,
            };
            _registry.Add(commandName, entry);
            return entry;
        }

        public static bool TryGetCommand(string name, out CommandEntry entry)
            => _registry.TryGetValue(name, out entry);

        public static IEnumerable<CommandEntry> GetAll() => _registry.Values;

        private static void RegisterBuiltInCommands()
        {
            Register(typeof(CompileCommand), nameof(CompileCommand.Start));
            Register(typeof(CompileCommand), nameof(CompileCommand.Status));

            Register(typeof(EditorCommand), nameof(EditorCommand.Play));
            Register(typeof(EditorCommand), nameof(EditorCommand.Stop));
            Register(typeof(EditorCommand), nameof(EditorCommand.Pause));
            Register(typeof(EditorCommand), nameof(EditorCommand.GetState));
            Register(typeof(EditorCommand), nameof(EditorCommand.Log));

            Register(typeof(GetLogsCommand), nameof(GetLogsCommand.Log));
            Register(typeof(GetLogsCommand), nameof(GetLogsCommand.StartCapture));
            Register(typeof(GetLogsCommand), nameof(GetLogsCommand.StopCapture));

            Register(typeof(InputSimulationCommand), nameof(InputSimulationCommand.Click));
            Register(typeof(InputSimulationCommand), nameof(InputSimulationCommand.ClickByInstanceId));
            Register(typeof(InputSimulationCommand), nameof(InputSimulationCommand.ClickAt));
            Register(typeof(InputSimulationCommand), nameof(InputSimulationCommand.Drag));
            Register(typeof(InputSimulationCommand), nameof(InputSimulationCommand.DragByInstanceId));
            Register(typeof(InputSimulationCommand), nameof(InputSimulationCommand.LongPress));
            Register(typeof(InputSimulationCommand), nameof(InputSimulationCommand.LongPressByInstanceId));

            Register(typeof(ScreenshotCommand), nameof(ScreenshotCommand.Image));
            Register(typeof(ScreenshotCommand), nameof(ScreenshotCommand.Gif));
            Register(typeof(global::CodeExecuteCommand), nameof(global::CodeExecuteCommand.Execute));

            AIBridgeLogger.LogInfo($"[CommandRegistry] Registered {_registry.Count} commands.");
        }
    }
}
