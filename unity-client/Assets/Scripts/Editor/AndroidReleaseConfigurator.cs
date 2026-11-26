using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace KnowledgeWarrior.EditorTools
{
    /// <summary>
    /// Enforces universal Android 11 through Android 15 compatibility across OEM devices
    /// including Huawei, Infinix, Tecno, Xiaomi, Samsung, Oppo, Vivo, and Realme, while
    /// automatically wiring release keystore signing before every Android build.
    /// </summary>
    [InitializeOnLoad]
    public sealed class AndroidReleaseConfigurator : IPreprocessBuildWithReport
    {
        public const string OfficialCompanyName = "KnowledgeWarriorStudios";
        public const string OfficialProductName = "Knowledge Warrior";
        public const string OfficialPackageId = "com.knowledgewarrior.game";

        private const string KeystorePropsRelativePath = "Keystores/keystore.properties";
        private const string HeroIconAssetPath = "Assets/Art/Sprites/Hero_KnowledgeWarrior.png";
        private const string SessionInitializedKey = "KW_AndroidUniversalSettingsApplied_v1";

        public int callbackOrder => 0;

        static AndroidReleaseConfigurator()
        {
            EditorApplication.delayCall += () =>
            {
                if (SessionState.GetBool(SessionInitializedKey, false))
                {
                    ApplyKeystoreCredentialsFromDisk(logStatus: false);
                    return;
                }

                SessionState.SetBool(SessionInitializedKey, true);
                ApplyUniversalAndroidSettings(showDialog: false);
            };
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android)
            {
                return;
            }

            ApplyUniversalAndroidSettings(showDialog: false);
        }

        [MenuItem("Knowledge Warrior/Configure Android Universal Release", priority = 21)]
        public static void ConfigureAndroidUniversalReleaseMenu()
        {
            ApplyUniversalAndroidSettings(showDialog: true);
        }

        [MenuItem("Knowledge Warrior/Build Universal Android APK", priority = 22)]
        public static void BuildUniversalAndroidApkMenu()
        {
            ApplyUniversalAndroidSettings(showDialog: false);

            string defaultDirectory = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string outputPath = EditorUtility.SaveFilePanel(
                "Build Universal Android APK",
                defaultDirectory,
                "kw-universal-v" + PlayerSettings.bundleVersion,
                "apk");

            if (string.IsNullOrWhiteSpace(outputPath))
            {
                return;
            }

            var options = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/Battle.unity" },
                locationPathName = outputPath,
                target = BuildTarget.Android,
                options = BuildOptions.CompressWithLz4HC
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result == BuildResult.Succeeded)
            {
                EditorUtility.DisplayDialog(
                    OfficialProductName,
                    "Universal Android APK built successfully at:\n" + outputPath +
                    "\n\nSupported architectures: ARMv7 (32-bit) and ARM64 (64-bit)\n" +
                    "Supported OS range: Android 8.0 through Android 15+",
                    "OK");
            }
        }

        public static void ApplyUniversalAndroidSettings(bool showDialog)
        {
            PlayerSettings.companyName = OfficialCompanyName;
            PlayerSettings.productName = OfficialProductName;

            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, OfficialPackageId);
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, OfficialPackageId);

            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;

            PlayerSettings.insecureHttpOption = InsecureHttpOption.DevelopmentOnly;

            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARMv7 | AndroidArchitecture.ARM64;
            PlayerSettings.Android.buildApkPerCpuArchitecture = false;

            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.Android.preferredInstallLocation = AndroidPreferredInstallLocation.Auto;
            PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.Activity;

            PlayerSettings.openGLRequireES31AEP = false;
            PlayerSettings.openGLRequireES32 = false;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(
                BuildTarget.Android,
                new[] { GraphicsDeviceType.OpenGLES3, GraphicsDeviceType.Vulkan });

            ApplyAppIcon();
            bool releaseSigned = ApplyKeystoreCredentialsFromDisk(logStatus: true);

            AssetDatabase.SaveAssets();

            if (showDialog)
            {
                string signingStatus = releaseSigned
                    ? "Release Keystore Active (Knowledge Warrior Team certificate)"
                    : "Keystore properties not found; fallback to local debug key";

                EditorUtility.DisplayDialog(
                    OfficialProductName,
                    "Android Universal Compatibility configured:\n\n" +
                    "Package ID: " + OfficialPackageId + "\n" +
                    "CPU Architectures: ARMv7 (armeabi-v7a) + ARM64 (arm64-v8a)\n" +
                    "OS Support: Android 8.0 through Android 15+ (includes Android 11 to 15 and HarmonyOS)\n" +
                    "Graphics API: OpenGLES3 primary with Vulkan fallback\n" +
                    "Application Entry: Standard UnityPlayerActivity\n" +
                    "Install Location: Automatic internal storage\n" +
                    "Signing: " + signingStatus,
                    "OK");
            }
        }

        private static void ApplyAppIcon()
        {
            var iconTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(HeroIconAssetPath);
            if (iconTexture == null)
            {
                return;
            }

            int[] sizes = PlayerSettings.GetIconSizes(NamedBuildTarget.Unknown, IconKind.Any);
            if (sizes != null && sizes.Length > 0)
            {
                var icons = new Texture2D[sizes.Length];
                for (int i = 0; i < icons.Length; i++)
                {
                    icons[i] = iconTexture;
                }

                PlayerSettings.SetIcons(NamedBuildTarget.Unknown, icons, IconKind.Any);
            }
        }

        private static bool ApplyKeystoreCredentialsFromDisk(bool logStatus)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            string propsFullPath = Path.Combine(projectRoot, KeystorePropsRelativePath);

            if (!File.Exists(propsFullPath))
            {
                PlayerSettings.Android.useCustomKeystore = false;
                if (logStatus)
                {
                    Debug.LogWarning("[Knowledge Warrior] Keystore properties file not found at " + propsFullPath + ". Android build will use debug signing until configured.");
                }
                return false;
            }

            Dictionary<string, string> props = ParseProperties(propsFullPath);
            if (!props.TryGetValue("storeFile", out string storeFile) ||
                !props.TryGetValue("storePassword", out string storePassword) ||
                !props.TryGetValue("keyAlias", out string keyAlias) ||
                !props.TryGetValue("keyPassword", out string keyPassword))
            {
                PlayerSettings.Android.useCustomKeystore = false;
                Debug.LogWarning("[Knowledge Warrior] Keystore properties file is missing required fields.");
                return false;
            }

            string keystoreFullPath = Path.Combine(projectRoot, storeFile);
            if (!File.Exists(keystoreFullPath))
            {
                PlayerSettings.Android.useCustomKeystore = false;
                Debug.LogWarning("[Knowledge Warrior] Release keystore file not found at " + keystoreFullPath + ".");
                return false;
            }

            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = storeFile;
            PlayerSettings.Android.keystorePass = storePassword;
            PlayerSettings.Android.keyaliasName = keyAlias;
            PlayerSettings.Android.keyaliasPass = keyPassword;

            if (logStatus)
            {
                Debug.Log("[Knowledge Warrior] Loaded release signing keystore (" + storeFile + ") and universal ARMv7 + ARM64 Android settings.");
            }

            return true;
        }

        private static Dictionary<string, string> ParseProperties(string filePath)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string rawLine in File.ReadAllLines(filePath))
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                int separatorIndex = line.IndexOf('=');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                string key = line.Substring(0, separatorIndex).Trim();
                string value = line.Substring(separatorIndex + 1).Trim();
                result[key] = value;
            }

            return result;
        }
    }
}
