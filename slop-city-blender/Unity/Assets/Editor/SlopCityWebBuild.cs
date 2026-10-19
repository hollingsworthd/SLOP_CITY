#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace SlopCity.Editor
{
    /// <summary>Exports the saved city without rebuilding it or reconfiguring the Mac player.</summary>
    public static class SlopCityWebBuild
    {
        public const string DefaultOutput = "Builds/WebGL";
        public const string ReportPath = "Builds/Reports/web-build.json";

        // Unity -batchmode -quit -buildTarget WebGL -executeMethod SlopCity.Editor.SlopCityWebBuild.BuildWeb
        // Add -slopCityCleanBuild after changing build-scene processing to invalidate cached scene data.
        [MenuItem("SLOP CITY/Build Webpage")]
        public static void BuildWeb()
        {
            var receipt = new Receipt { startedAtUtc = DateTime.UtcNow.ToString("O") };
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    throw new BuildFailedException("Exit Play Mode before exporting the webpage.");
                if (!Application.unityVersion.StartsWith("6000.3.", StringComparison.Ordinal))
                    throw new BuildFailedException("Use Unity " + SlopCityBatchBuild.EditorVersion + " to export this project.");
                if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL))
                    throw new BuildFailedException("Install Web Build Support for Unity " + SlopCityBatchBuild.EditorVersion + ".");
                if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
                    throw new BuildFailedException("Start Unity with -buildTarget WebGL, or switch to Web before exporting.");
                if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(SlopCitySceneBuilder.ScenePath))
                    throw new BuildFailedException("The saved city scene is missing: " + SlopCitySceneBuilder.ScenePath);
#if !ENABLE_LEGACY_INPUT_MANAGER
                throw new BuildFailedException("Enable the legacy Input Manager and restart Unity before exporting.");
#endif
                string output = ResolveOutput();
                receipt.outputPath = output;
                receipt.cleanBuild = Array.IndexOf(Environment.GetCommandLineArgs(), "-slopCityCleanBuild") >= 0;
                SlopCityWebGeometryBuildProcessor.ResetCaptureCount();
                Directory.CreateDirectory(output);
                using (new WebSettingsScope())
                using (var textures = new SlopCityWebTextureBudgetScope())
                using (var details = new SlopCityWebDetailExport.Scope(output))
                {
                    receipt.applicationVersion = PlayerSettings.bundleVersion;
                    receipt.textureCompression = EditorUserBuildSettings.webGLBuildSubtarget.ToString();
                    receipt.initialMemoryMiB = PlayerSettings.WebGL.initialMemorySize;
                    receipt.maximumMemoryMiB = PlayerSettings.WebGL.maximumMemorySize;
                    receipt.memoryGrowthMode = PlayerSettings.WebGL.memoryGrowthMode.ToString();
                    receipt.exceptionSupport = PlayerSettings.WebGL.exceptionSupport.ToString();
                    receipt.memoryLinearGrowthStepMiB = PlayerSettings.WebGL.linearMemoryGrowthStep;
                    receipt.staticBatching = PlayerSettings.GetStaticBatchingForPlatform(BuildTarget.WebGL);
                    receipt.powerPreference = PlayerSettings.WebGL.powerPreference.ToString();
                    receipt.worldTextureImports = textures.entries;
                    BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                    {
                        scenes = new[] { SlopCitySceneBuilder.ScenePath },
                        locationPathName = output,
                        target = BuildTarget.WebGL,
                        targetGroup = BuildTargetGroup.WebGL,
                        subtarget = (int)EditorUserBuildSettings.webGLBuildSubtarget,
                        options = BuildOptions.StrictMode | BuildOptions.DetailedBuildReport
                            | (receipt.cleanBuild ? BuildOptions.CleanBuildCache : BuildOptions.None)
                    });
                    if (report == null) throw new BuildFailedException("Unity returned no Web build report.");
                    receipt.result = report.summary.result.ToString();
                    receipt.totalBytes = report.summary.totalSize.ToString();
                    receipt.totalSeconds = report.summary.totalTime.TotalSeconds;
                    receipt.totalErrors = report.summary.totalErrors;
                    receipt.totalWarnings = report.summary.totalWarnings;
                    receipt.capturedGeometryBounds = SlopCityWebGeometryBuildProcessor.CapturedRendererCount;
                    foreach (BuildStep step in report.steps)
                        foreach (BuildStepMessage message in step.messages)
                        {
                            if (message.type == LogType.Error || message.type == LogType.Exception || message.type == LogType.Assert)
                                receipt.errors.Add(step.name + ": " + message.content);
                            else if (message.type == LogType.Warning && receipt.warnings.Count < 100)
                                receipt.warnings.Add(step.name + ": " + message.content);
                        }
                    if (report.summary.result != BuildResult.Succeeded)
                        throw new BuildFailedException("Web build failed: " + report.SummarizeErrors());
                    if (receipt.cleanBuild && receipt.capturedGeometryBounds == 0)
                        throw new BuildFailedException("The clean Web build did not capture browser interaction geometry. Do not publish this export.");
                    details.Complete();
                }
                if (!File.Exists(Path.Combine(output, "index.html")))
                    throw new BuildFailedException("The Web build did not produce index.html.");
                Debug.Log("SLOP_CITY_WEB_BUILD_SUCCEEDED " + output);
            }
            catch (Exception error)
            {
                receipt.result = "Failed";
                receipt.errors.Add(error.ToString());
                throw;
            }
            finally
            {
                receipt.finishedAtUtc = DateTime.UtcNow.ToString("O");
                string path = Path.Combine(ProjectRoot, ReportPath);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonUtility.ToJson(receipt, true));
            }
        }

        private static string ResolveOutput()
        {
            string output = DefaultOutput;
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] != "-slopCityOutput") continue;
                if (i + 1 >= args.Length) throw new BuildFailedException("-slopCityOutput requires a directory inside Builds.");
                output = args[++i];
            }
            string path = Path.GetFullPath(Path.IsPathRooted(output) ? output : Path.Combine(ProjectRoot, output));
            string root = Path.Combine(ProjectRoot, "Builds") + Path.DirectorySeparatorChar;
            if (!path.StartsWith(root, StringComparison.Ordinal) || path.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                throw new BuildFailedException("Web output must be a directory inside this project's Builds directory.");
            return path;
        }

        private static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;

        // Every setting touched below is restored even when a build fails. In particular,
        // this does not call ConfigureProjectForMac or touch its native codec importer.
        private sealed class WebSettingsScope : IDisposable
        {
            private readonly string product = PlayerSettings.productName;
            private readonly string version = PlayerSettings.bundleVersion;
            private readonly int width = PlayerSettings.defaultWebScreenWidth;
            private readonly int height = PlayerSettings.defaultWebScreenHeight;
            private readonly bool background = PlayerSettings.runInBackground;
            private readonly ScriptingImplementation backend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.WebGL);
            private readonly Il2CppCodeGeneration generation = PlayerSettings.GetIl2CppCodeGeneration(NamedBuildTarget.WebGL);
            private readonly ManagedStrippingLevel stripping = PlayerSettings.GetManagedStrippingLevel(NamedBuildTarget.WebGL);
            private readonly bool defaultGraphics = PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.WebGL);
            private readonly GraphicsDeviceType[] graphics = PlayerSettings.GetGraphicsAPIs(BuildTarget.WebGL);
            private readonly bool staticBatching = PlayerSettings.GetStaticBatchingForPlatform(BuildTarget.WebGL);
            private readonly bool dynamicBatching = PlayerSettings.GetDynamicBatchingForPlatform(BuildTarget.WebGL);
            private readonly string template = PlayerSettings.WebGL.template;
            private readonly WebGLCompressionFormat compression = PlayerSettings.WebGL.compressionFormat;
            private readonly bool fallback = PlayerSettings.WebGL.decompressionFallback;
            private readonly bool caching = PlayerSettings.WebGL.dataCaching;
            private readonly bool threads = PlayerSettings.WebGL.threadsSupport;
            private readonly WebGLExceptionSupport exceptions = PlayerSettings.WebGL.exceptionSupport;
            private readonly int initialMemory = PlayerSettings.WebGL.initialMemorySize;
            private readonly int maximumMemory = PlayerSettings.WebGL.maximumMemorySize;
            private readonly WebGLMemoryGrowthMode growth = PlayerSettings.WebGL.memoryGrowthMode;
            private readonly int linearGrowthStep = PlayerSettings.WebGL.linearMemoryGrowthStep;
            private readonly WebGLPowerPreference power = PlayerSettings.WebGL.powerPreference;
            private readonly WebGLTextureSubtarget textureSubtarget = EditorUserBuildSettings.webGLBuildSubtarget;

            public WebSettingsScope()
            {
                try
                {
                    PlayerSettings.productName = SlopCityBatchBuild.ProductName;
                    PlayerSettings.bundleVersion = "1.0.1";
                    PlayerSettings.defaultWebScreenWidth = 1440;
                    PlayerSettings.defaultWebScreenHeight = 900;
                    PlayerSettings.runInBackground = false;
                    PlayerSettings.SetScriptingBackend(NamedBuildTarget.WebGL, ScriptingImplementation.IL2CPP);
                    PlayerSettings.SetIl2CppCodeGeneration(NamedBuildTarget.WebGL, Il2CppCodeGeneration.OptimizeSpeed);
                    PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, ManagedStrippingLevel.Low);
                    PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.WebGL, false);
                    PlayerSettings.SetGraphicsAPIs(BuildTarget.WebGL, new[] { GraphicsDeviceType.OpenGLES3 });
                    // DXT expands to RGBA on mobile GPUs without S3TC. ETC2 keeps
                    // imported textures compressed on the WebGL 2 mobile target.
                    EditorUserBuildSettings.webGLBuildSubtarget = WebGLTextureSubtarget.ETC2;
                    // Keep immutable source meshes shared. Static batching adds
                    // combined vertex/index copies across the resident city; the
                    // Web geometry pass already preserves instancing and releases
                    // render-only CPU streams without those additional copies.
                    PlayerSettings.SetStaticBatchingForPlatform(BuildTarget.WebGL, false);
                    PlayerSettings.SetDynamicBatchingForPlatform(BuildTarget.WebGL, false);
                    PlayerSettings.WebGL.template = "PROJECT:SlopCity";
                    PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
                    PlayerSettings.WebGL.decompressionFallback = true;
                    PlayerSettings.WebGL.dataCaching = true;
                    // A single-threaded build works on ordinary static hosts without COOP/COEP.
                    PlayerSettings.WebGL.threadsSupport = false;
                    // Keep null and array-bounds checks in the shipped IL2CPP player.
                    // Explicit-throw-only builds can turn those faults into Wasm traps.
                    PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.FullWithoutStacktrace;
                    // Start smaller; real allocations may still grow this heap.
                    // This reduces the initial reservation, not the city's
                    // measured working set or its eventual memory requirement.
                    PlayerSettings.WebGL.initialMemorySize = 128;
                    PlayerSettings.WebGL.maximumMemorySize = 1024;
                    // Preserve headroom for the full city rather than substituting
                    // a smaller unmeasured maximum that can turn pressure into an
                    // allocation trap. Linear growth bounds unused heap headroom;
                    // these settings establish neither process RAM nor an iOS limit.
                    PlayerSettings.WebGL.memoryGrowthMode = WebGLMemoryGrowthMode.Linear;
                    PlayerSettings.WebGL.linearMemoryGrowthStep = 16;
                    PlayerSettings.WebGL.powerPreference = WebGLPowerPreference.Default;
                    AssetDatabase.SaveAssets();
                }
                catch { Dispose(); throw; }
            }

            public void Dispose()
            {
                PlayerSettings.productName = product;
                PlayerSettings.bundleVersion = version;
                PlayerSettings.defaultWebScreenWidth = width;
                PlayerSettings.defaultWebScreenHeight = height;
                PlayerSettings.runInBackground = background;
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.WebGL, backend);
                PlayerSettings.SetIl2CppCodeGeneration(NamedBuildTarget.WebGL, generation);
                PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, stripping);
                PlayerSettings.SetGraphicsAPIs(BuildTarget.WebGL, graphics);
                PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.WebGL, defaultGraphics);
                PlayerSettings.SetStaticBatchingForPlatform(BuildTarget.WebGL, staticBatching);
                PlayerSettings.SetDynamicBatchingForPlatform(BuildTarget.WebGL, dynamicBatching);
                PlayerSettings.WebGL.template = template;
                PlayerSettings.WebGL.compressionFormat = compression;
                PlayerSettings.WebGL.decompressionFallback = fallback;
                PlayerSettings.WebGL.dataCaching = caching;
                PlayerSettings.WebGL.threadsSupport = threads;
                PlayerSettings.WebGL.exceptionSupport = exceptions;
                PlayerSettings.WebGL.initialMemorySize = initialMemory;
                PlayerSettings.WebGL.maximumMemorySize = maximumMemory;
                PlayerSettings.WebGL.memoryGrowthMode = growth;
                PlayerSettings.WebGL.linearMemoryGrowthStep = linearGrowthStep;
                PlayerSettings.WebGL.powerPreference = power;
                EditorUserBuildSettings.webGLBuildSubtarget = textureSubtarget;
                AssetDatabase.SaveAssets();
            }
        }

        [Serializable]
        private sealed class Receipt
        {
            public string operation = "webgl-build";
            public string result;
            public string unityVersion = Application.unityVersion;
            public string applicationVersion;
            public string architecture = "WebAssembly / WebGL 2";
            public string scriptingBackend = "IL2CPP";
            public string compression = "gzip with decompression fallback";
            public string textureCompression;
            public int initialMemoryMiB;
            public int maximumMemoryMiB;
            public string memoryGrowthMode;
            public string exceptionSupport;
            public int memoryLinearGrowthStepMiB;
            public bool staticBatching;
            public string powerPreference;
            public List<SlopCityWebTextureBudgetScope.Entry> worldTextureImports = new List<SlopCityWebTextureBudgetScope.Entry>();
            public string startedAtUtc;
            public string finishedAtUtc;
            public string scenePath = SlopCitySceneBuilder.ScenePath;
            public string outputPath;
            public string totalBytes;
            public double totalSeconds;
            public int totalErrors;
            public int totalWarnings;
            public bool cleanBuild;
            public int capturedGeometryBounds;
            public List<string> errors = new List<string>();
            public List<string> warnings = new List<string>();
        }
    }
}
#endif
