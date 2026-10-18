#!/usr/bin/env python3
"""Execute the actual bundled-only lifecycle/resolver without Unity or network."""
import argparse
from pathlib import Path
import subprocess
import tempfile
from test_local_agents import block, MONO, MCS, SCRIPTS

FIXTURE = r'''
using System;
using System.Collections;
public class OfflineCatalog {
 bool initializationComplete,destroying;public int bundledLoads;
 string status;System.Collections.Generic.Dictionary<string,object> bundledPosts=new System.Collections.Generic.Dictionary<string,object>();
 IEnumerator LoadBundledCatalog(){bundledLoads++;yield break;}
 string BundledUri(object post,string kind,int index)=>post==null?null:"file:///sealed/TikTok/media/12345.mp4";
 static void Complete(Action<string> callback,string value){callback(value);}
 // PRODUCTION
 static void Run(IEnumerator e){while(e.MoveNext())if(e.Current is IEnumerator nested)Run(nested);}
 public static void Main(){
  var c=new OfflineCatalog();Run(c.Initialize());if(!c.initializationComplete||c.bundledLoads!=1)throw new Exception("Only bundled initialization must run");
  c.RequestRefresh();if(!c.status.Contains("unavailable"))throw new Exception("Missing offline bundle must stay unavailable");
  c.bundledPosts.Add("1984drh/12345",new object());c.RequestRefresh();if(!c.status.Contains("offline"))throw new Exception("Refresh must describe sealed offline content");
  string value="unexpected";c.Resolve(new object(),"video","12345.mp4",-1,v=>value=v,()=>true);if(value!="file:///sealed/TikTok/media/12345.mp4")throw new Exception("Bundled media resolves locally");
  c.Resolve(null,"video","99999.mp4",-1,v=>value=v,()=>true);if(value!=null)throw new Exception("Missing media cannot fall back to remote acquisition");
  c.destroying=true;c.Resolve(new object(),"video","12345.mp4",-1,v=>value=v,()=>true);if(value!=null)throw new Exception("Destroyed component cannot return content");
  Console.WriteLine("PASS 6 actual offline catalogue initialization/refresh/resolve checks");
 }
}
public class SlopCityTikTokVideo {}
'''


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--mono', type=Path, default=MONO)
    parser.add_argument('--mcs', type=Path, default=MCS)
    args = parser.parse_args()
    source = (SCRIPTS / 'SlopCityTikTokSync.cs').read_text()
    methods = '\n'.join(block(source, token) for token in ['        private IEnumerator Initialize()', '        public void RequestRefresh()', '        private void Resolve('])
    # The fake post type only supplies a non-null identity to the resolver stub.
    fixture = FIXTURE.replace('string BundledUri(object post', 'string BundledUri(SlopCityTikTokVideo post').replace('c.Resolve(new object()', 'c.Resolve(new SlopCityTikTokVideo()').replace('// PRODUCTION', methods)
    with tempfile.TemporaryDirectory(prefix='slop-offline-catalog-') as directory:
        path = Path(directory)
        (path / 'Checks.cs').write_text(fixture)
        for name, defines in [('native', []), ('web', ['-define:UNITY_WEBGL'])]:
            executable = path / (name + '.exe')
            subprocess.run([str(args.mono), str(args.mcs), '-langversion:latest', *defines, '-out:' + str(executable), str(path / 'Checks.cs')], check=True)
            subprocess.run([str(args.mono), str(executable)], check=True)


if __name__ == '__main__':
    main()
