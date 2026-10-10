#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SlopCity.Editor
{
    /// <summary>
    /// Command-line entry points. Use -batchmode -quit -buildTarget StandaloneOSX.
    /// Exceptions deliberately propagate to Unity for a non-zero batch exit code;
    /// these scripts never exit an interactive Editor or remove user files.
    /// </summary>
    public static class SlopCityBatchBuild
    {
        public const string EditorVersion = "6000.3.26f1";
        public const string ProductName = "SLOP CITY";
        public const string BundleIdentifier = "com.machinepublishers.slopcity";
        public const string DefaultMacOutput = "Builds/macOS/SLOP CITY.app";
        public const string ValidationReportPath = "Builds/Reports/scene-validation.json";
        public const string BuildReportPath = "Builds/Reports/mac-build.json";

        // Unity -executeMethod SlopCity.Editor.SlopCityBatchBuild.BatchValidation
        public static void BatchValidation()
        {
            var report = new BuildReceipt { operation = "validate", startedAtUtc = DateTime.UtcNow.ToString("O") };
            bool validationWritten = false;
            try
            {
                WriteIncompleteValidation("Scene validation is in progress.");
                ConfigureProjectForMac();
                Scene scene = SlopCitySceneBuilder.BuildSceneForAutomation();
                // Save/reopen catches missing serialized references and prefab edits
                // that only happened to exist in the first in-memory scene.
                scene = EditorSceneManager.OpenScene(SlopCitySceneBuilder.ScenePath, OpenSceneMode.Single);
                SlopCityValidationReport validation = SlopCitySceneValidation.Validate(scene);
                WriteJson(ValidationReportPath, validation);
                validationWritten = true;
                validation.ThrowIfInvalid();
                report.result = "Validated";
                report.validationChecks = validation.checks;
                FinishReceipt(report);
                Debug.Log("SLOP_CITY_VALIDATION_SUCCEEDED " + Path.Combine(ProjectRoot, ValidationReportPath));
            }
            catch (Exception error)
            {
                if (!validationWritten) WriteIncompleteValidation(error.ToString());
                report.result = "Failed";
                report.errors.Add(error.ToString());
                FinishReceipt(report);
                throw;
            }
        }

        // Unity -executeMethod SlopCity.Editor.SlopCityBatchBuild.BuildMacAppleSilicon
        [MenuItem("SLOP CITY/Build Mac Apple Silicon")]
        public static void BuildMacAppleSilicon()
        {
            var receipt = new BuildReceipt { operation = "macOS-arm64-build", startedAtUtc = DateTime.UtcNow.ToString("O") };
            bool validationWritten = false;
            try
            {
                WriteIncompleteValidation("Scene validation is in progress.");
                ConfigureProjectForMac();
                if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneOSX))
                    throw new BuildFailedException("The macOS standalone player module is not installed for this Unity Editor.");
                if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneOSX)
                    throw new BuildFailedException("Start this build with -buildTarget StandaloneOSX, or select macOS in the Editor before invoking the menu.");

                Scene generated = SlopCitySceneBuilder.BuildSceneForPlayerBuild();
                if (!generated.IsValid())
                {
                    WriteIncompleteValidation("The interactive scene rebuild was cancelled.");
                    receipt.result = "Cancelled";
                    FinishReceipt(receipt);
                    return;
                }
                Scene scene = EditorSceneManager.OpenScene(SlopCitySceneBuilder.ScenePath, OpenSceneMode.Single);
                SlopCityValidationReport validation = SlopCitySceneValidation.Validate(scene);
                WriteJson(ValidationReportPath, validation);
                validationWritten = true;
                validation.ThrowIfInvalid();
                receipt.validationChecks = validation.checks;

                string output = ResolveMacOutput();
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                receipt.outputPath = output;
                receipt.architecture = GetMacArchitecture();
                BuildReport build = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { SlopCitySceneBuilder.ScenePath },
                    locationPathName = output,
                    target = BuildTarget.StandaloneOSX,
                    targetGroup = BuildTargetGroup.Standalone,
                    options = BuildOptions.StrictMode | BuildOptions.DetailedBuildReport
                });
                if (build == null) throw new BuildFailedException("Unity returned no BuildReport.");
                receipt.result = build.summary.result.ToString();
                receipt.totalBytes = build.summary.totalSize.ToString();
                receipt.totalSeconds = build.summary.totalTime.TotalSeconds;
                receipt.totalErrors = build.summary.totalErrors;
                receipt.totalWarnings = build.summary.totalWarnings;
                foreach (BuildStep step in build.steps)
                    foreach (BuildStepMessage message in step.messages)
                    {
                        if (message.type == LogType.Error || message.type == LogType.Exception || message.type == LogType.Assert)
                            receipt.errors.Add(step.name + ": " + message.content);
                        else if (message.type == LogType.Warning && receipt.warnings.Count < 100)
                            receipt.warnings.Add(step.name + ": " + message.content);
                    }
                if (build.summary.result != BuildResult.Succeeded)
                    throw new BuildFailedException("Mac build failed: " + build.SummarizeErrors());
                if (!Directory.Exists(Path.Combine(output, "Contents", "MacOS")) || !File.Exists(Path.Combine(output, "Contents", "Info.plist")))
                    throw new BuildFailedException("Build reported success but the .app bundle is incomplete.");
                FinishReceipt(receipt);
                Debug.Log("SLOP_CITY_MAC_BUILD_SUCCEEDED " + output);
            }
            catch (Exception error)
            {
                if (!validationWritten) WriteIncompleteValidation(error.ToString());
                receipt.result = "Failed";
                receipt.errors.Add(error.ToString());
                FinishReceipt(receipt);
                throw;
            }
        }

        public static void ConfigureProjectForMac()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new BuildFailedException("Exit Play Mode before validation or building.");
            if (!Application.unityVersion.StartsWith("6000.3.", StringComparison.Ordinal))
                throw new BuildFailedException("Open this project with Unity " + EditorVersion + " (Unity 6.3 LTS).");
            if (Application.unityVersion != EditorVersion)
                Debug.LogWarning("SLOP CITY: project is pinned to " + EditorVersion + ", current editor is " + Application.unityVersion + ".");
#if !ENABLE_LEGACY_INPUT_MANAGER
            throw new BuildFailedException("Legacy Input Manager is disabled. Set activeInputHandler to 0 or 2 in ProjectSettings and restart Unity before building.");
#endif
            PlayerSettings.companyName = "Machine Publishers";
            PlayerSettings.productName = ProductName;
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, BundleIdentifier);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.defaultScreenWidth = 1440;
            PlayerSettings.defaultScreenHeight = 990;
            PlayerSettings.defaultIsNativeResolution = false;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = false;
            PlayerSettings.macRetinaSupport = true;
            PlayerSettings.usePlayerLog = true;
            QualitySettings.antiAliasing = 4;
            QualitySettings.pixelLightCount = 6;
            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowResolution = ShadowResolution.Medium;
            QualitySettings.shadowDistance = 35f;

            string platformName = BuildPipeline.GetBuildTargetName(BuildTarget.StandaloneOSX);
            // Match Unity's DesktopStandaloneBuildWindowExtension serialization.
            string architecture = OSArchitecture.ARM64.ToString().ToLowerInvariant();
            EditorUserBuildSettings.SetPlatformSettings(platformName, "Architecture", architecture);
            if (!string.Equals(GetMacArchitecture(), architecture, StringComparison.OrdinalIgnoreCase))
                throw new BuildFailedException("The macOS ARM64 architecture setting did not persist.");
            AssetDatabase.SaveAssets();
        }

        private static string GetMacArchitecture()
        {
            return EditorUserBuildSettings.GetPlatformSettings(BuildPipeline.GetBuildTargetName(BuildTarget.StandaloneOSX), "Architecture");
        }

        private static string ResolveMacOutput()
        {
            string output = DefaultMacOutput;
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] != "-slopCityOutput") continue;
                if (i + 1 >= args.Length) throw new BuildFailedException("-slopCityOutput requires a path within this project's Builds directory.");
                output = args[i + 1];
            }
            string path = Path.GetFullPath(Path.IsPathRooted(output) ? output : Path.Combine(ProjectRoot, output));
            string generatedRoot = Path.Combine(ProjectRoot, "Builds") + Path.DirectorySeparatorChar;
            if (!path.StartsWith(generatedRoot, StringComparison.Ordinal) || !path.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                throw new BuildFailedException("Mac output must be an .app inside this project's Builds directory. No external path will be overwritten.");
            return path;
        }

        private static void FinishReceipt(BuildReceipt receipt)
        {
            receipt.unityVersion = Application.unityVersion;
            receipt.finishedAtUtc = DateTime.UtcNow.ToString("O");
            receipt.scenePath = SlopCitySceneBuilder.ScenePath;
            string path = receipt.operation == "validate" ? "Builds/Reports/validation-run.json" : BuildReportPath;
            WriteJson(path, receipt);
        }

        private static void WriteJson(string relativePath, object value)
        {
            string path = Path.Combine(ProjectRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(value, true));
        }

        private static void WriteIncompleteValidation(string reason)
        {
            WriteJson(ValidationReportPath, new SlopCityValidationReport
            {
                passed = false,
                unityVersion = Application.unityVersion,
                checkedAtUtc = DateTime.UtcNow.ToString("O"),
                scenePath = SlopCitySceneBuilder.ScenePath,
                errors = new List<string> { reason }
            });
        }

        private static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;

        [Serializable]
        private sealed class BuildReceipt
        {
            public string operation;
            public string result;
            public string unityVersion;
            public string architecture = "ARM64";
            public string scriptingBackend = "Mono";
            public string startedAtUtc;
            public string finishedAtUtc;
            public string scenePath;
            public string outputPath;
            public string totalBytes;
            public double totalSeconds;
            public int totalErrors;
            public int totalWarnings;
            public int validationChecks;
            public List<string> errors = new List<string>();
            public List<string> warnings = new List<string>();
        }
    }
}
#endif
