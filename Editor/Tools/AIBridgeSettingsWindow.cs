using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AIBridge.Editor
{
    /// <summary>
    /// AI Bridge settings window using UI Toolkit.
    /// </summary>
    public class AIBridgeSettingsWindow : EditorWindow
    {
        private VisualElement _currentTab;
        
        // Settings fields
        private Toggle _bridgeEnabled;
        private Toggle _debugLogging;
        private SliderInt _gifFrameCount;
        private SliderInt _gifFps;
        private Slider _gifScale;
        private SliderInt _gifColorCount;
        private Toggle _runtimeBridgeEnabled;
        private Toggle _runtimeCodeExecution;
        private Toggle _allowRuntimeInRelease;
        private IntegerField _runtimeHttpPort;
        private Label _hybridClrStatus;
        private Button _installHybridClrButton;

        [MenuItem("Window/AIBridge")]
        private static void OpenWindow()
        {
            var window = GetWindow<AIBridgeSettingsWindow>();
            window.titleContent = new GUIContent("AI Bridge Settings");
            window.minSize = new Vector2(600, 500);
            window.Show();
        }

        public void CreateGUI()
        {
            // Load UXML - try multiple possible paths
            var paths = new[]
            {
                "Packages/cn.lys.aibridge/Editor/UI/AIBridgeSettingsWindow.uxml",
                "Packages/AIBridge/Editor/UI/AIBridgeSettingsWindow.uxml"
            };

            VisualTreeAsset visualTree = null;
            foreach (var path in paths)
            {
                visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
                if (visualTree != null)
                    break;
            }
            
            if (visualTree == null)
            {
                var label = new Label("Error: Could not load UXML file. Tried paths:\n" + string.Join("\n", paths));
                label.style.color = Color.red;
                label.style.whiteSpace = WhiteSpace.Normal;
                rootVisualElement.Add(label);
                return;
            }

            visualTree.CloneTree(rootVisualElement);

            // Initialize UI
            InitializeFields();
            LoadSettings();
            SetupTabButtons();
            SetupActionButtons();
        }

        private void InitializeFields()
        {
            // General tab
            _bridgeEnabled = rootVisualElement.Q<Toggle>("bridge-enabled");
            _debugLogging = rootVisualElement.Q<Toggle>("debug-logging");
            
            // Directory info
            var queueDir = rootVisualElement.Q<TextField>("queue-dir");
            var screenshotDir = rootVisualElement.Q<TextField>("screenshot-dir");
            var cliPath = rootVisualElement.Q<TextField>("cli-path");
            
            queueDir.value = AIBridge.BridgeDirectory;
            screenshotDir.value = ScreenshotHelper.ScreenshotsDir;
            cliPath.value = AIBridge.BridgeCLI;

            // GIF tab
            _gifFrameCount = rootVisualElement.Q<SliderInt>("gif-frame-count");
            _gifFps = rootVisualElement.Q<SliderInt>("gif-fps");
            _gifScale = rootVisualElement.Q<Slider>("gif-scale");
            _gifColorCount = rootVisualElement.Q<SliderInt>("gif-color-count");

            _runtimeBridgeEnabled = rootVisualElement.Q<Toggle>("runtime-bridge-enabled");
            _runtimeCodeExecution = rootVisualElement.Q<Toggle>("runtime-code-execution");
            _allowRuntimeInRelease = rootVisualElement.Q<Toggle>("runtime-allow-release");
            _runtimeHttpPort = rootVisualElement.Q<IntegerField>("runtime-http-port");
            _hybridClrStatus = rootVisualElement.Q<Label>("hybridclr-status");
            _installHybridClrButton = rootVisualElement.Q<Button>("install-hybridclr");
            _runtimeBridgeEnabled.RegisterValueChangedCallback(_ => SaveRuntimeSettings());
            _runtimeCodeExecution.RegisterValueChangedCallback(_ => SaveRuntimeSettings());
            _allowRuntimeInRelease.RegisterValueChangedCallback(_ => SaveRuntimeSettings());
            _runtimeHttpPort.RegisterValueChangedCallback(_ => SaveRuntimeSettings());

            UpdateCommandCount();
        }

        private void LoadSettings()
        {
            _bridgeEnabled.value = AIBridge.Enabled;
            _debugLogging.value = AIBridgeLogger.DebugEnabled;

            _gifFrameCount.value = GifRecorderSettings.DefaultFrameCount;
            _gifFps.value = GifRecorderSettings.DefaultFps;
            _gifScale.value = GifRecorderSettings.DefaultScale;
            _gifColorCount.value = GifRecorderSettings.DefaultColorCount;

            _runtimeBridgeEnabled.SetValueWithoutNotify(AIBridgeRuntimeEditorSettings.EnableRuntimeBridge);
            _runtimeCodeExecution.SetValueWithoutNotify(AIBridgeRuntimeEditorSettings.EnableRuntimeCodeExecution);
            _allowRuntimeInRelease.SetValueWithoutNotify(AIBridgeRuntimeEditorSettings.AllowReleaseBuild);
            _runtimeHttpPort.SetValueWithoutNotify(AIBridgeRuntimeEditorSettings.HttpPort);
            UpdateHybridClrStatus();

        }

        private void SetupTabButtons()
        {
            var tabGeneral = rootVisualElement.Q<Button>("tab-general");
            var tabGif = rootVisualElement.Q<Button>("tab-gif");
            var tabCommands = rootVisualElement.Q<Button>("tab-commands");
            var tabRuntime = rootVisualElement.Q<Button>("tab-runtime");
            var tabTools = rootVisualElement.Q<Button>("tab-tools");

            tabGeneral.clicked += () => SwitchTab("content-general", tabGeneral);
            tabGif.clicked += () => SwitchTab("content-gif", tabGif);
            tabCommands.clicked += () => SwitchTab("content-commands", tabCommands);
            tabRuntime.clicked += () => SwitchTab("content-runtime", tabRuntime);
            tabTools.clicked += () => SwitchTab("content-tools", tabTools);

            // Set initial tab
            _currentTab = rootVisualElement.Q<VisualElement>("content-general");
        }

        private void SwitchTab(string contentName, Button activeButton)
        {
            // Hide all tabs
            rootVisualElement.Q<VisualElement>("content-general").style.display = DisplayStyle.None;
            rootVisualElement.Q<VisualElement>("content-gif").style.display = DisplayStyle.None;
            rootVisualElement.Q<VisualElement>("content-commands").style.display = DisplayStyle.None;
            rootVisualElement.Q<VisualElement>("content-runtime").style.display = DisplayStyle.None;
            rootVisualElement.Q<VisualElement>("content-tools").style.display = DisplayStyle.None;

            // Remove active class from all buttons
            rootVisualElement.Q<Button>("tab-general").RemoveFromClassList("tab-active");
            rootVisualElement.Q<Button>("tab-gif").RemoveFromClassList("tab-active");
            rootVisualElement.Q<Button>("tab-commands").RemoveFromClassList("tab-active");
            rootVisualElement.Q<Button>("tab-runtime").RemoveFromClassList("tab-active");
            rootVisualElement.Q<Button>("tab-tools").RemoveFromClassList("tab-active");

            // Show selected tab
            _currentTab = rootVisualElement.Q<VisualElement>(contentName);
            _currentTab.style.display = DisplayStyle.Flex;
            activeButton.AddToClassList("tab-active");
        }

        private void SetupActionButtons()
        {
            // Directory buttons
            rootVisualElement.Q<Button>("open-queue-dir").clicked += () => 
                EditorUtility.RevealInFinder(AIBridge.BridgeDirectory);
            rootVisualElement.Q<Button>("open-screenshot-dir").clicked += () => 
                EditorUtility.RevealInFinder(ScreenshotHelper.ScreenshotsDir);
            rootVisualElement.Q<Button>("open-cli-dir").clicked += () => 
                EditorUtility.RevealInFinder(Path.GetDirectoryName(AIBridge.BridgeCLI));
            rootVisualElement.Q<Button>("refresh-cli").clicked += RefreshCLI;

            _installHybridClrButton.clicked += InstallHybridClr;

            // Tools buttons
            rootVisualElement.Q<Button>("install-skill-agent").clicked += SkillInstaller.CopyToAgent;
            rootVisualElement.Q<Button>("clear-cache").clicked += ClearCache;
            rootVisualElement.Q<Button>("reset-settings").clicked += ResetSettings;
        }

        private void RefreshCLI()
        {
            if (AIBridge.RefreshCLI())
            {
                ShowNotification(new GUIContent("CLI replaced"));
                return;
            }

            EditorUtility.DisplayDialog(
                "AI Bridge",
                "Failed to replace CLI. Check the Unity Console for details.",
                "OK");
        }

        private void OnDestroy()
        {
            SaveSettings();
        }

        private void SaveSettings()
        {
            AIBridge.Enabled = _bridgeEnabled.value;
            AIBridgeLogger.DebugEnabled = _debugLogging.value;

            GifRecorderSettings.DefaultFrameCount = _gifFrameCount.value;
            GifRecorderSettings.DefaultFps = _gifFps.value;
            GifRecorderSettings.DefaultScale = _gifScale.value;
            GifRecorderSettings.DefaultColorCount = _gifColorCount.value;

            SaveRuntimeSettings();

            Debug.Log("[AIBridge] Settings saved.");
        }

        private void SaveRuntimeSettings()
        {
            if (_runtimeBridgeEnabled == null
                || _runtimeCodeExecution == null
                || _allowRuntimeInRelease == null
                || _runtimeHttpPort == null)
            {
                return;
            }

            AIBridgeRuntimeEditorSettings.EnableRuntimeBridge = _runtimeBridgeEnabled.value;
            AIBridgeRuntimeEditorSettings.EnableRuntimeCodeExecution = _runtimeCodeExecution.value;
            AIBridgeRuntimeEditorSettings.AllowReleaseBuild = _allowRuntimeInRelease.value;
            AIBridgeRuntimeEditorSettings.HttpPort = _runtimeHttpPort.value;
            AIBridgeSelfRuntimeBuildProcessor.SyncDefinesForActiveTarget();
        }

        private void UpdateHybridClrStatus()
        {
            if (_hybridClrStatus == null)
            {
                return;
            }

            var installed = AIBridgeHybridClrUtility.IsInstalled();
            var declared = AIBridgeHybridClrUtility.IsDeclaredInManifest();
            _hybridClrStatus.text = installed
                ? "HybridCLR installed"
                : declared
                    ? "HybridCLR added to manifest. Package Manager is resolving it."
                    : "HybridCLR not installed. Player code execution is unavailable.";
            _hybridClrStatus.style.color = installed
                ? new StyleColor(new Color(0.45f, 0.8f, 0.45f))
                : new StyleColor(new Color(0.9f, 0.5f, 0.3f));
            _installHybridClrButton.SetEnabled(!installed);
            _installHybridClrButton.text = installed
                ? "HybridCLR Installed"
                : declared
                    ? "Resolve HybridCLR Again"
                    : "Install HybridCLR From Git";
            _runtimeCodeExecution.SetEnabled(installed);
        }

        private void InstallHybridClr()
        {
            _installHybridClrButton.SetEnabled(false);
            bool changed;
            string error;
            if (!AIBridgeHybridClrUtility.Install(out changed, out error))
            {
                UpdateHybridClrStatus();
                EditorUtility.DisplayDialog(
                    "HybridCLR Installation Failed",
                    error ?? "Failed to update Packages/manifest.json.",
                    "OK");
                return;
            }

            if (changed)
            {
                Debug.Log("[AIBridge] Added HybridCLR Git dependency to Packages/manifest.json.");
            }

            UnityEditor.PackageManager.Client.Resolve();
            UpdateHybridClrStatus();
        }

        private void UpdateCommandCount()
        {
            var count = CommandRegistry.GetAll().Count();
            var label = rootVisualElement.Q<Label>("command-count");
            label.text = $"Total registered commands: {count}";
        }

        private void ClearCache()
        {
            if (EditorUtility.DisplayDialog("Clear Cache", 
                "Are you sure you want to clear the screenshot cache?", "Yes", "No"))
            {
                ScreenshotCacheManager.CleanupOldScreenshots();
                Debug.Log("[AIBridge] Screenshot cache cleared.");
                EditorUtility.DisplayDialog("Success", "Screenshot cache cleared.", "OK");
            }
        }

        private void ResetSettings()
        {
            if (EditorUtility.DisplayDialog("Reset Settings", 
                "Are you sure you want to reset all settings to default?", "Yes", "No"))
            {
                AIBridgeRuntimeEditorSettings.Reset();
                
                LoadSettings();
                SaveRuntimeSettings();
                Debug.Log("[AIBridge] Settings reset to default.");
                EditorUtility.DisplayDialog("Success", "Settings reset to default.", "OK");
            }
        }
    }
}
