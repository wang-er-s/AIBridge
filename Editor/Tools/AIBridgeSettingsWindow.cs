using System.IO;
using System.Linq;
using System.Text;
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
        private Toggle _autoScan;
        private TextField _scanAssemblies;

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

            // Commands tab
            _autoScan = rootVisualElement.Q<Toggle>("auto-scan");
            _scanAssemblies = rootVisualElement.Q<TextField>("scan-assemblies");
            
            // Setup auto-scan toggle listener
            _autoScan.RegisterValueChangedCallback(evt => UpdateRefreshButtonVisibility());
            
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

            _scanAssemblies.value = EditorPrefs.GetString(
                CommandRegistry.PrefKeyScanAssemblies, 
                "Assembly-CSharp-Editor-firstpass;Assembly-CSharp");

            if (!CommandRegistry.IsEditablePackage)
            {
                _autoScan.value = true;
                _autoScan.SetEnabled(false);
            }
            else
            {
                _autoScan.value = EditorPrefs.GetBool(CommandRegistry.PrefKeyAutoScan, false);
            }
            
            UpdateRefreshButtonVisibility();
        }

        private void SetupTabButtons()
        {
            var tabGeneral = rootVisualElement.Q<Button>("tab-general");
            var tabGif = rootVisualElement.Q<Button>("tab-gif");
            var tabCommands = rootVisualElement.Q<Button>("tab-commands");
            var tabTools = rootVisualElement.Q<Button>("tab-tools");

            tabGeneral.clicked += () => SwitchTab("content-general", tabGeneral);
            tabGif.clicked += () => SwitchTab("content-gif", tabGif);
            tabCommands.clicked += () => SwitchTab("content-commands", tabCommands);
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
            rootVisualElement.Q<VisualElement>("content-tools").style.display = DisplayStyle.None;

            // Remove active class from all buttons
            rootVisualElement.Q<Button>("tab-general").RemoveFromClassList("tab-active");
            rootVisualElement.Q<Button>("tab-gif").RemoveFromClassList("tab-active");
            rootVisualElement.Q<Button>("tab-commands").RemoveFromClassList("tab-active");
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

            // Command buttons
            rootVisualElement.Q<Button>("refresh-commands").clicked += () =>
            {
                CommandRegistry.Scan();
                UpdateCommandCount();
                SkillInstaller.GenerateSkillFile();
                SkillInstaller.OverrideSkill();
            };

            // Tools buttons
            rootVisualElement.Q<Button>("generate-skill").clicked += ()=>
            {
                SkillInstaller.GenerateSkillFile();
                SkillInstaller.OverrideSkill();
            };
            rootVisualElement.Q<Button>("install-skill-agent").clicked += SkillInstaller.CopyToAgent;
            rootVisualElement.Q<Button>("clear-cache").clicked += ClearCache;
            rootVisualElement.Q<Button>("reset-settings").clicked += ResetSettings;
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

            EditorPrefs.SetBool(CommandRegistry.PrefKeyAutoScan, _autoScan.value);
            EditorPrefs.SetString(CommandRegistry.PrefKeyScanAssemblies, _scanAssemblies.value);

            if (_autoScan.value)
            {
                CommandRegistry.Scan();
            }

            Debug.Log("[AIBridge] Settings saved.");
        }

        private void UpdateCommandCount()
        {
            var count = CommandRegistry.GetAll().Count();
            var label = rootVisualElement.Q<Label>("command-count");
            label.text = $"Total registered commands: {count}";
        }

        private void UpdateRefreshButtonVisibility()
        {
            var refreshButton = rootVisualElement.Q<Button>("refresh-commands");
            if (refreshButton != null)
            {
                refreshButton.style.display = _autoScan.value ? DisplayStyle.None : DisplayStyle.Flex;
            }
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
                EditorPrefs.DeleteKey(CommandRegistry.PrefKeyAutoScan);
                EditorPrefs.DeleteKey(CommandRegistry.PrefKeyScanAssemblies);
                
                LoadSettings();
                Debug.Log("[AIBridge] Settings reset to default.");
                EditorUtility.DisplayDialog("Success", "Settings reset to default.", "OK");
            }
        }
    }
}
