using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using AIBridge.Runtime;
using UnityEditor;
using UnityEngine;

namespace AIBridge.Editor
{
    [InitializeOnLoad]
    public static class EditorCoroutineRunner
    {
        private class CoroutineHandle
        {
            public Action<CommandResult> OnComplete;
            public string RequestId;
            public Stopwatch Timer;
            public Stack<IEnumerator> Enumerators;

            private double _waitStartTime;
            private float _waitDuration;
            private CustomYieldInstruction _customYieldInstruction;

            public bool Step()
            {
                try
                {
                    while (true)
                    {
                        if (_waitDuration > 0)
                        {
                            double elapsed = EditorApplication.timeSinceStartup - _waitStartTime;
                            if (elapsed < _waitDuration)
                            {
                                return false;
                            }
                            _waitDuration = 0;
                        }

                        if (_customYieldInstruction != null)
                        {
                            if (_customYieldInstruction.keepWaiting)
                            {
                                return false;
                            }

                            _customYieldInstruction = null;
                        }

                        if (Enumerators.Count == 0)
                        {
                            var result = CommandResult.SuccessWithId(RequestId);
                            result.executionTime = Timer.ElapsedMilliseconds;
                            OnComplete?.Invoke(result);
                            return true;
                        }

                        var currentCoroutine = Enumerators.Peek();
                        if (!currentCoroutine.MoveNext())
                        {
                            Dispose(Enumerators.Pop());
                            continue;
                        }

                        var current = currentCoroutine.Current;

                        if (current is WaitForSeconds waitSeconds)
                        {
                            var seconds = GetWaitForSecondsValue(waitSeconds);
                            _waitStartTime = EditorApplication.timeSinceStartup;
                            _waitDuration = seconds;
                            if (_waitDuration > 0)
                            {
                                return false;
                            }
                            continue;
                        }

                        if (current is WaitForFixedUpdate || current is WaitForEndOfFrame)
                        {
                            continue;
                        }

                        if (current is CustomYieldInstruction instruction)
                        {
                            _customYieldInstruction = instruction;
                            if (instruction.keepWaiting)
                            {
                                return false;
                            }
                            _customYieldInstruction = null;
                            continue;
                        }

                        if (current is IEnumerator nestedCoroutine)
                        {
                            Enumerators.Push(nestedCoroutine);
                            continue;
                        }

                        if (current is AIBridgeCommandOutcome outcome)
                        {
                            var outcomeResult = outcome.Success
                                ? CommandResult.Success(outcome.Result)
                                : CommandResult.Failure(FormatError(outcome));
                            outcomeResult.id = RequestId;
                            outcomeResult.executionTime = Timer.ElapsedMilliseconds;
                            DisposeAll();
                            OnComplete?.Invoke(outcomeResult);
                            return true;
                        }

                        if (current is CommandResult commandResult)
                        {
                            commandResult.id = RequestId;
                            commandResult.executionTime = Timer.ElapsedMilliseconds;
                            DisposeAll();
                            OnComplete?.Invoke(commandResult);
                            return true;
                        }

                        return false;
                    }
                }
                catch (Exception ex)
                {
                    DisposeAll();
                    var result = CommandResult.FromException(RequestId, ex);
                    result.executionTime = Timer.ElapsedMilliseconds;
                    OnComplete?.Invoke(result);
                    return true;
                }
            }

            private static string FormatError(AIBridgeCommandOutcome outcome)
            {
                if (string.IsNullOrEmpty(outcome.ErrorCode) ||
                    outcome.ErrorCode == "command_failed")
                {
                    return outcome.ErrorMessage;
                }

                return outcome.ErrorCode + ": " + outcome.ErrorMessage;
            }

            private void DisposeAll()
            {
                while (Enumerators.Count > 0)
                {
                    Dispose(Enumerators.Pop());
                }
            }

            private static void Dispose(IEnumerator enumerator)
            {
                var disposable = enumerator as IDisposable;
                if (disposable != null)
                {
                    disposable.Dispose();
                }
            }
        }

        private static readonly List<CoroutineHandle> _running = new List<CoroutineHandle>();
        private static readonly FieldInfo _waitForSecondsField;

        static EditorCoroutineRunner()
        {
            EditorApplication.update += Tick;

            var waitForSecondsType = typeof(WaitForSeconds);
            _waitForSecondsField = waitForSecondsType.GetField("m_Seconds",
                BindingFlags.NonPublic | BindingFlags.Instance);
        }

        private static float GetWaitForSecondsValue(WaitForSeconds waitForSeconds)
        {
            if (_waitForSecondsField != null && _waitForSecondsField.GetValue(waitForSeconds) is float seconds)
            {
                return seconds;
            }
            return 0f;
        }

        public static void Start(IEnumerator coroutine, Action<CommandResult> onComplete, string requestId)
        {
            if (coroutine == null)
            {
                var result = CommandResult.SuccessWithId(requestId);
                result.executionTime = 0;
                onComplete?.Invoke(result);
                return;
            }

            _running.Add(new CoroutineHandle
            {
                OnComplete = onComplete,
                RequestId = requestId,
                Timer = Stopwatch.StartNew(),
                Enumerators = new Stack<IEnumerator>(new[] { coroutine })
            });
        }

        private static void Tick()
        {
            for (int i = _running.Count - 1; i >= 0; i--)
            {
                if (_running[i].Step())
                    _running.RemoveAt(i);
            }
        }
    }
}
