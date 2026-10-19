#!/usr/bin/env python3
"""Exercise the actual build-settings scope without launching Unity or a player."""
import argparse
from pathlib import Path
import subprocess
import tempfile
from test_native_allocation_rollback import block

STUBS = r'''
using System;
public enum BuildTarget { WebGL }
public enum NamedBuildTarget { WebGL }
public enum ScriptingImplementation { Mono, IL2CPP }
public enum Il2CppCodeGeneration { OptimizeSize, OptimizeSpeed }
public enum ManagedStrippingLevel { High, Low }
public enum GraphicsDeviceType { Vulkan, OpenGLES3 }
public enum WebGLCompressionFormat { Brotli, Gzip }
public enum WebGLMemoryGrowthMode { Geometric, Linear }
public enum WebGLPowerPreference { Default, LowPower, HighPerformance }
public enum WebGLTextureSubtarget { DXT, ETC2 }
public enum WebGLExceptionSupport { None, ExplicitlyThrownExceptionsOnly, FullWithoutStacktrace, FullWithStacktrace }
public static class SlopCityBatchBuild { public const string ProductName="Game"; }
public static class EditorUserBuildSettings { public static WebGLTextureSubtarget webGLBuildSubtarget; }
public static class AssetDatabase {
 public static bool FailNextSave;
 public static void SaveAssets(){if(FailNextSave){FailNextSave=false;throw new Exception("Injected save failure");}}
}
public static class PlayerSettings {
 public static string productName="Original",bundleVersion="Original";
 public static int defaultWebScreenWidth=800,defaultWebScreenHeight=600;
 public static bool runInBackground=true;
 static ScriptingImplementation backend;static Il2CppCodeGeneration generation;static ManagedStrippingLevel stripping;
 static bool defaultGraphics=true,staticBatching=false,dynamicBatching=true;
 static GraphicsDeviceType[] graphics={GraphicsDeviceType.Vulkan};
 public static ScriptingImplementation GetScriptingBackend(NamedBuildTarget t)=>backend;
 public static void SetScriptingBackend(NamedBuildTarget t,ScriptingImplementation v){backend=v;}
 public static Il2CppCodeGeneration GetIl2CppCodeGeneration(NamedBuildTarget t)=>generation;
 public static void SetIl2CppCodeGeneration(NamedBuildTarget t,Il2CppCodeGeneration v){generation=v;}
 public static ManagedStrippingLevel GetManagedStrippingLevel(NamedBuildTarget t)=>stripping;
 public static void SetManagedStrippingLevel(NamedBuildTarget t,ManagedStrippingLevel v){stripping=v;}
 public static bool GetUseDefaultGraphicsAPIs(BuildTarget t)=>defaultGraphics;
 public static void SetUseDefaultGraphicsAPIs(BuildTarget t,bool v){defaultGraphics=v;}
 public static GraphicsDeviceType[] GetGraphicsAPIs(BuildTarget t)=>graphics;
 public static void SetGraphicsAPIs(BuildTarget t,GraphicsDeviceType[] v){graphics=v;}
 public static bool GetStaticBatchingForPlatform(BuildTarget t)=>staticBatching;
 public static void SetStaticBatchingForPlatform(BuildTarget t,bool v){staticBatching=v;}
 public static bool GetDynamicBatchingForPlatform(BuildTarget t)=>dynamicBatching;
 public static void SetDynamicBatchingForPlatform(BuildTarget t,bool v){dynamicBatching=v;}
 public static class WebGL {
  public static string template="Original";
  public static WebGLCompressionFormat compressionFormat;
  public static bool decompressionFallback=false,dataCaching=false,threadsSupport=true;
  public static int initialMemorySize=32,maximumMemorySize=2048,linearMemoryGrowthStep=7;
  public static WebGLMemoryGrowthMode memoryGrowthMode;
  public static WebGLPowerPreference powerPreference;
  static WebGLExceptionSupport support;
  public static int FailAssignment;
  public static WebGLExceptionSupport exceptionSupport {
   get=>support;
   set {int fail=FailAssignment;FailAssignment=0;if(fail==1)throw new Exception("Before write");support=value;if(fail==2)throw new Exception("After write");}
  }
 }
 public static string Snapshot()=>productName+bundleVersion+defaultWebScreenWidth+defaultWebScreenHeight+runInBackground
  +backend+generation+stripping+defaultGraphics+staticBatching+dynamicBatching+graphics[0]
  +WebGL.template+WebGL.compressionFormat+WebGL.decompressionFallback+WebGL.dataCaching+WebGL.threadsSupport
  +WebGL.initialMemorySize+WebGL.maximumMemorySize+WebGL.linearMemoryGrowthStep+WebGL.memoryGrowthMode
  +WebGL.powerPreference+WebGL.exceptionSupport+EditorUserBuildSettings.webGLBuildSubtarget;
}
public class Check {
__SCOPE__
 static int checks;
 static void Assert(bool value,string message){checks++;if(!value)throw new Exception(message);}
 public static void Main(){
  foreach(WebGLExceptionSupport prior in Enum.GetValues(typeof(WebGLExceptionSupport))){
   PlayerSettings.WebGL.exceptionSupport=prior;string before=PlayerSettings.Snapshot();
   using(new WebSettingsScope()){
    Assert(PlayerSettings.WebGL.exceptionSupport==WebGLExceptionSupport.FullWithoutStacktrace,"Every prior setting gets null/bounds checks");
    Assert(PlayerSettings.WebGL.initialMemorySize==128&&PlayerSettings.WebGL.maximumMemorySize==1024,"Memory budget remains intact");
   }
   Assert(PlayerSettings.Snapshot()==before,"Successful export restores all original settings");
   for(int failure=0;failure<3;failure++){
    AssetDatabase.FailNextSave=failure==0;PlayerSettings.WebGL.FailAssignment=failure;
    bool threw=false;try{using(new WebSettingsScope()){} }catch(Exception){threw=true;}
    Assert(threw,"Injected failure remains visible");
    Assert(PlayerSettings.Snapshot()==before,"Failed scope restores all original settings before/after mutation");
   }
  }
  Console.WriteLine(checks+" actual build-scope exception policy checks passed");
 }
}
'''


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mono', type=Path, required=True)
    parser.add_argument('--source', type=Path, default=Path(__file__).resolve().parents[1] / 'Unity/Assets/Editor/SlopCityWebBuild.cs')
    args = parser.parse_args()
    mono = args.mono.resolve()
    source = args.source.read_text()
    # The receipt exposes the effective policy for later package acceptance.
    assert 'receipt.exceptionSupport = PlayerSettings.WebGL.exceptionSupport.ToString();' in source
    with tempfile.TemporaryDirectory(prefix='slopcity-exceptions-') as folder:
        code = Path(folder) / 'Check.cs'
        code.write_text(STUBS.replace('__SCOPE__', block(source, 'private sealed class WebSettingsScope')))
        exe = code.with_suffix('.exe')
        subprocess.run([str(mono), str(mono.parents[1] / 'lib/mono/4.5/csc.exe'), '-nologo', '-out:' + str(exe), str(code)], check=True)
        subprocess.run([str(mono), str(exe)], check=True)


if __name__ == '__main__':
    main()
