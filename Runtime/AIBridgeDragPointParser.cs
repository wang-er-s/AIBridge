using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AIBridge.Runtime
{
    public sealed class AIBridgeDragPoint
    {
        private AIBridgeDragPoint(string path, Vector2? position, int? instanceId)
        {
            Path = path;
            Position = position;
            InstanceId = instanceId;
        }

        public string Path { get; private set; }
        public Vector2? Position { get; private set; }
        public int? InstanceId { get; private set; }
        public bool IsPath { get { return Path != null; } }

        public static AIBridgeDragPoint FromPath(string path)
        {
            return new AIBridgeDragPoint(path, null, null);
        }

        public static AIBridgeDragPoint FromPath(string path, int instanceId)
        {
            return new AIBridgeDragPoint(path, null, instanceId);
        }

        public static AIBridgeDragPoint FromPosition(Vector2 position)
        {
            return new AIBridgeDragPoint(null, position, null);
        }

        public static AIBridgeDragPoint FromInstanceId(int instanceId)
        {
            return new AIBridgeDragPoint(null, null, instanceId);
        }
    }

    public static class AIBridgeDragPointParser
    {
        public static bool TryParse(
            object rawPoints,
            out List<AIBridgeDragPoint> points,
            out string error)
        {
            points = new List<AIBridgeDragPoint>();
            error = null;

            JArray array;
            try
            {
                array = rawPoints as JArray ?? JToken.FromObject(rawPoints) as JArray;
            }
            catch
            {
                error = "Parameter 'points' must be an array.";
                return false;
            }

            if (array == null || array.Count < 2)
            {
                error = "Parameter 'points' must contain at least two points.";
                return false;
            }

            for (var index = 0; index < array.Count; index++)
            {
                var point = array[index] as JObject;
                if (point == null)
                {
                    error = $"Point at index {index} must be an object.";
                    return false;
                }

                JToken pathToken;
                var hasPath = point.TryGetValue("path", System.StringComparison.OrdinalIgnoreCase, out pathToken);
                JToken xToken;
                var hasX = point.TryGetValue("x", System.StringComparison.OrdinalIgnoreCase, out xToken);
                JToken yToken;
                var hasY = point.TryGetValue("y", System.StringComparison.OrdinalIgnoreCase, out yToken);
                JToken instanceIdToken;
                var hasInstanceId = point.TryGetValue(
                    "instanceId",
                    System.StringComparison.OrdinalIgnoreCase,
                    out instanceIdToken);

                if (hasPath && !hasX && !hasY &&
                    ((!hasInstanceId && point.Count == 1) || (hasInstanceId && point.Count == 2)))
                {
                    var path = pathToken.Type == JTokenType.String ? pathToken.Value<string>() : null;
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        error = $"Path at index {index} must be a non-empty string.";
                        return false;
                    }

                    if (hasInstanceId)
                    {
                        int instanceId;
                        if (!AIBridgeInputTargetParser.TryParseInstanceId(instanceIdToken, out instanceId))
                        {
                            error = $"Instance ID at index {index} must be a 32-bit integer.";
                            return false;
                        }

                        points.Add(AIBridgeDragPoint.FromPath(path, instanceId));
                    }
                    else
                    {
                        points.Add(AIBridgeDragPoint.FromPath(path));
                    }
                    continue;
                }

                if (!hasPath && !hasInstanceId && hasX && hasY && point.Count == 2)
                {
                    float x;
                    float y;
                    try
                    {
                        x = xToken.Value<float>();
                        y = yToken.Value<float>();
                    }
                    catch
                    {
                        error = $"Coordinates at index {index} must be numbers.";
                        return false;
                    }

                    if (float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(y) || float.IsInfinity(y) ||
                        x < 0 || y < 0)
                    {
                        error = $"Coordinates at index {index} must be finite and non-negative.";
                        return false;
                    }

                    points.Add(AIBridgeDragPoint.FromPosition(new Vector2(x, y)));
                    continue;
                }

                if (!hasPath && !hasX && !hasY && hasInstanceId && point.Count == 1)
                {
                    int instanceId;
                    if (!AIBridgeInputTargetParser.TryParseInstanceId(instanceIdToken, out instanceId))
                    {
                        error = $"Instance ID at index {index} must be a 32-bit integer.";
                        return false;
                    }

                    points.Add(AIBridgeDragPoint.FromInstanceId(instanceId));
                    continue;
                }

                error = $"Point at index {index} must be exactly {{\"path\":\"...\"}}, " +
                    "{\"path\":\"...\",\"instanceId\":integer}, {\"instanceId\":integer}, " +
                    "or {\"x\":number,\"y\":number}.";
                return false;
            }

            return true;
        }
    }

    public sealed class AIBridgeInputTarget
    {
        private AIBridgeInputTarget(string path, Vector2? position, int? instanceId)
        {
            Path = path;
            Position = position;
            InstanceId = instanceId;
        }

        public string Path { get; private set; }
        public Vector2? Position { get; private set; }
        public int? InstanceId { get; private set; }

        public static AIBridgeInputTarget FromPath(string path)
        {
            return new AIBridgeInputTarget(path, null, null);
        }

        public static AIBridgeInputTarget FromPath(string path, int instanceId)
        {
            return new AIBridgeInputTarget(path, null, instanceId);
        }

        public static AIBridgeInputTarget FromPosition(Vector2 position)
        {
            return new AIBridgeInputTarget(null, position, null);
        }

        public static AIBridgeInputTarget FromInstanceId(int instanceId)
        {
            return new AIBridgeInputTarget(null, null, instanceId);
        }
    }

    public static class AIBridgeInputTargetParser
    {
        public static bool TryParse(
            object pathValue,
            object pointValue,
            object instanceIdValue,
            out AIBridgeInputTarget target,
            out string error)
        {
            target = null;
            error = null;
            var hasPath = pathValue != null;
            var hasPoint = pointValue != null;
            var hasInstanceId = instanceIdValue != null;
            if (hasPoint && (hasPath || hasInstanceId) ||
                !hasPoint && !hasPath && !hasInstanceId)
            {
                error = "Provide 'point', 'path', 'instanceId', or 'path' with 'instanceId'.";
                return false;
            }

            if (hasPath)
            {
                var path = pathValue as string;
                if (string.IsNullOrWhiteSpace(path))
                {
                    error = "Parameter 'path' must be a non-empty string.";
                    return false;
                }

                if (hasInstanceId)
                {
                    int instanceId;
                    if (!TryParseInstanceIdValue(instanceIdValue, out instanceId))
                    {
                        error = "Parameter 'instanceId' must be a 32-bit integer.";
                        return false;
                    }

                    target = AIBridgeInputTarget.FromPath(path, instanceId);
                }
                else
                {
                    target = AIBridgeInputTarget.FromPath(path);
                }
                return true;
            }

            if (hasInstanceId)
            {
                int instanceId;
                if (!TryParseInstanceIdValue(instanceIdValue, out instanceId))
                {
                    error = "Parameter 'instanceId' must be a 32-bit integer.";
                    return false;
                }

                target = AIBridgeInputTarget.FromInstanceId(instanceId);
                return true;
            }

            JToken pointToken;
            try
            {
                pointToken = pointValue as JToken ?? JToken.FromObject(pointValue);
            }
            catch
            {
                error = "Parameter 'point' must be an object with numeric 'x' and 'y'.";
                return false;
            }

            var point = pointToken as JObject;
            JToken xToken;
            JToken yToken;
            if (point == null || point.Count != 2 ||
                !point.TryGetValue("x", System.StringComparison.OrdinalIgnoreCase, out xToken) ||
                !point.TryGetValue("y", System.StringComparison.OrdinalIgnoreCase, out yToken))
            {
                error = "Parameter 'point' must be exactly {\"x\":number,\"y\":number}.";
                return false;
            }

            float x;
            float y;
            try
            {
                x = xToken.Value<float>();
                y = yToken.Value<float>();
            }
            catch
            {
                error = "Parameter 'point' coordinates must be numbers.";
                return false;
            }

            if (float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(y) || float.IsInfinity(y) ||
                x < 0 || y < 0)
            {
                error = "Parameter 'point' coordinates must be finite and non-negative.";
                return false;
            }

            target = AIBridgeInputTarget.FromPosition(new Vector2(x, y));
            return true;
        }

        public static bool TryParseInstanceId(JToken token, out int instanceId)
        {
            instanceId = 0;
            if (token == null || token.Type != JTokenType.Integer)
            {
                return false;
            }

            try
            {
                var value = token.Value<long>();
                if (value < int.MinValue || value > int.MaxValue)
                {
                    return false;
                }

                instanceId = (int)value;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryParseInstanceIdValue(object value, out int instanceId)
        {
            instanceId = 0;
            JToken token;
            try
            {
                token = value as JToken ?? JToken.FromObject(value);
            }
            catch
            {
                return false;
            }

            return TryParseInstanceId(token, out instanceId);
        }
    }
}
