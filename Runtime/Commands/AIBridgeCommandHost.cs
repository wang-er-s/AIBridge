namespace AIBridge.Runtime
{
    /// <summary>
    /// Supplies environment-specific services to shared command implementations.
    /// </summary>
    public sealed class AIBridgeCommandHost
    {
        private static readonly AIBridgeCommandHost PlayerHost = new AIBridgeCommandHost(
            new AIBridgePathInputTargetResolver(),
            AIBridgePlayerScreenshotBackend.Instance,
            AIBridgePlayerScreenshotStorage.Instance,
            null,
            false);

        private static AIBridgeCommandHost _current = PlayerHost;

        public AIBridgeCommandHost(
            IAIBridgeInputTargetResolver inputResolver,
            IAIBridgeScreenshotBackend screenshotBackend,
            IAIBridgeScreenshotStorage screenshotStorage,
            IAIBridgeScreenshotProgress screenshotProgress,
            bool useEditorLogHistory)
        {
            InputResolver = inputResolver;
            ScreenshotBackend = screenshotBackend;
            ScreenshotStorage = screenshotStorage;
            ScreenshotProgress = screenshotProgress;
            UseEditorLogHistory = useEditorLogHistory;
        }

        public IAIBridgeInputTargetResolver InputResolver { get; private set; }
        public IAIBridgeScreenshotBackend ScreenshotBackend { get; private set; }
        public IAIBridgeScreenshotStorage ScreenshotStorage { get; private set; }
        public IAIBridgeScreenshotProgress ScreenshotProgress { get; private set; }
        public bool UseEditorLogHistory { get; private set; }

        public static AIBridgeCommandHost Player
        {
            get { return PlayerHost; }
        }

        public static AIBridgeCommandHost Current
        {
            get { return _current; }
        }

        public static void ConfigureCurrent(AIBridgeCommandHost host)
        {
            _current = host ?? PlayerHost;
        }
    }
}
