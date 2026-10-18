#!/usr/bin/env node
// Execute the actual pinned request library and game adapter. Native callbacks,
// transport completion and byte allocations are controlled, never networked.
// Late-completion probes do not establish Safari/iPhone occurrence or memory use.
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import crypto from 'node:crypto';
import zlib from 'node:zlib';
import {fileURLToPath} from 'node:url';
const here=path.dirname(fileURLToPath(import.meta.url));
const relative='.tools/Unity-6000.3.26f1/Unity.app/Contents/PlaybackEngines/WebGLSupport/BuildTools/lib/WebRequest.js';
let root=path.resolve(here,'..');
while(!fs.existsSync(path.join(root,relative))){const parent=path.dirname(root);assert.notEqual(parent,root,'Install the pinned independent Unity tools');root=parent;}
const engine=fs.readFileSync(path.join(root,relative),'utf8');
assert.equal(crypto.createHash('sha256').update(engine).digest('hex'),'d7feaa5a192567c05406247b5379cbdbb11638ee609f8d07947d5c6641f43c7c','Unity request ABI changed; review before upgrading');
const plugin=fs.readFileSync(path.resolve(here,'../Unity/Assets/Plugins/WebGL/SlopCityWebRequestLifecycle.jslib'),'utf8');
const flag=process.argv.indexOf('--framework');
let framework;
if(flag>=0){assert.equal(flag,2);assert.equal(process.argv.length,4);const raw=fs.readFileSync(process.argv[3]);framework=(raw[0]===31&&raw[1]===139?zlib.gunzipSync(raw):raw).toString('utf8');}else assert.equal(process.argv.length,2);
// Expand only pinned Emscripten callback/pointer expressions in the test VM.
// No Unity library implementation is copied into this repository.
const prepared=engine.replace(/#if USE_PTHREADS[\s\S]*?#endif/g,'')
 .replace(/\{\{\{ ptrToIdx\('([^']+)'(?:, 2)?\) \}\}\}/g,'$1')
 .replace(/\{\{\{ makeDynCall\('[^']+', '([^']+)'\) \}\}\}/g,'$1');
assert.ok(!prepared.includes('{{{')&&!/^#/m.test(prepared));
function helper(source,name){
 const marker='function '+name+'(',start=source.indexOf(marker);assert.notEqual(start,-1,'Missing compiled helper '+name);
 const open=source.indexOf('{',start);let depth=0,quote='',comment='';
 for(let i=open;i<source.length;i++){const c=source[i],n=source[i+1];
  if(comment==='line'){if(c==='\n')comment='';continue;}if(comment==='block'){if(c==='*'&&n==='/'){comment='';i++;}continue;}
  if(quote){if(c==='\\')i++;else if(c===quote)quote='';continue;}
  if(c==='/'&&n==='/'){comment='line';i++;continue;}if(c==='/'&&n==='*'){comment='block';i++;continue;}
  if(c==='"'||c==="'"||c==='`'){quote=c;continue;}if(c==='{')depth++;else if(c==='}'&&--depth===0)return source.slice(start,i+1);
 }throw Error('Unclosed compiled helper '+name);
}
let checks=0;
function check(value,label){assert.ok(value,label);checks++;}
const settled=async()=>{for(let i=0;i<8;i++)await Promise.resolve();};
function environment({patched=true,cached=false,streaming=true,fetcher}={}){
 const pending=[],responses=[],progress=[],freed=[],allocated=new Map(),timers=new Map();let nextPointer=16,nextTimer=1;
 const heap=new Uint8Array(4*1024*1024);
 const nativeFetch=function(url,init){
  if(fetcher)return fetcher(url,init);
  let resolve,reject;const promise=new Promise((yes,no)=>{resolve=yes;reject=no;});pending.push({url,init,resolve,reject,promise,receiver:this});return promise;
 };
 const module={fetchWithProgress:nativeFetch,cacheControl:()=> 'no-store'};
 if(cached){module.companyName='Game';module.productName='Game';module.cachedFetch=function(url,init){return module.fetchWithProgress(url,init);};}
 const originalFetch=module.fetchWithProgress,originalCached=module.cachedFetch;
 const context=vm.createContext({AbortController,Uint8Array,Blob,Error,Promise,console,
  setTimeout(fn){const id=nextTimer++;timers.set(id,fn);return id;},clearTimeout(id){timers.delete(id);},
  UTF8ToString:x=>x,lengthBytesUTF8:x=>String(x).length,stringToUTF8(text,ptr,size){heap.set(new TextEncoder().encode(text).subarray(0,size-1),ptr);heap[ptr+size-1]=0;},
  _malloc(bytes){const pointer=nextPointer;nextPointer+=Math.max(bytes,1);assert.ok(nextPointer<heap.byteLength);allocated.set(pointer,bytes);return pointer;},
  _free(pointer){assert.ok(allocated.has(pointer),'Invalid/double free '+pointer);allocated.delete(pointer);freed.push(pointer);},
  HEAPU8:heap,Module:module,LibraryManager:{library:{}},
  autoAddDeps(library,dependency){for(const[key,value]of Object.entries(library))if(typeof value==='function')library[key+'__deps']=[...(library[key+'__deps']??[]),dependency];},
  mergeInto(target,fields){Object.assign(target,fields);},getWasmTableEntry:value=>value,dynCall_viiiiii:(fn,...args)=>fn(...args),
 });
 vm.runInContext(prepared,context);context.wr=context.LibraryWebRequestWebGL.$wr;
 Object.assign(context.LibraryManager.library,context.LibraryWebRequestWebGL);
 if(patched){vm.runInContext(plugin,context);for(const[key,value]of Object.entries(context.LibraryManager.library))if(key.startsWith('$')&&!key.includes('__'))context[key.slice(1)]=value;
  if(framework){for(const[key,value]of Object.entries(context.LibraryManager.library)){if(key.includes('__')||typeof value!=='function')continue;const name=key.startsWith('$')?key.slice(1):key==='JS_WebRequest_Send'||key==='JS_WebRequest_Release'?'_'+key:null;if(name)context[name]=vm.runInContext('('+helper(framework,name)+')',context);}}
 }
 for(const name of ['Create','Send','Abort','Release'])if(!framework||!patched||!['Send','Release'].includes(name))context['_JS_WebRequest_'+name]=context.LibraryManager.library['JS_WebRequest_'+name];
 const wr=context.wr;
 function create(id){if(id!==undefined)wr.nextRequestId=id;const value=context._JS_WebRequest_Create('StreamingAssets/poster.jpg','GET');wr.requests[value].init.enableStreamingDownload=streaming;return value;}
 function send(id,{onResponse,onProgress}={}){context._JS_WebRequest_Send(id,0,0,11,(...args)=>{responses.push(args);onResponse?.(...args);},(...args)=>{progress.push(args);onProgress?.(...args);});check(module.fetchWithProgress===originalFetch&&module.cachedFetch===originalCached,'Module transport hooks restored synchronously');}
 function chunk(request,bytes=16){const body=new Uint8Array(bytes).fill(23);const response={status:200,parsedBody:body};request.init.onProgress({response,chunk:request.init.enableStreamingDownload?body:null,loaded:bytes,total:bytes});return response;}
 return{context,wr,module,pending,responses,progress,freed,allocated,timers,heap,create,send,chunk,abort:id=>context._JS_WebRequest_Abort(id),release:id=>context._JS_WebRequest_Release(id),tokens:()=>Object.keys(context.slopCityWebRequestTokens??{}).length};
}
// Before/after reproduction using the same installed engine implementation.
for(const patched of [false,true]){
 const e=environment({patched});
 for(let i=0;i<64;i++){const id=e.create();e.send(id);const p=e.pending.shift();e.abort(id);e.release(id);p.resolve({status:200,parsedBody:new Uint8Array(1024)});await settled();}
 check(Object.keys(e.wr.requests).length===0,'All native request entries released');
 check(Object.keys(e.wr.responses).length===(patched?0:64),'Late completion '+(patched?'does not resurrect':'reproduces orphan')+' response bodies');
 check(e.responses.length===(patched?0:64),'Released native callbacks '+(patched?'suppressed':'reproduced'));
 check(e.tokens()===0&&e.allocated.size===0,'No adapter token or allocator owner left after release');
}
for(const cached of [false,true])for(const streaming of [false,true]){
 const e=environment({cached,streaming});const id=e.create();e.wr.requests[id].timeout=100;e.send(id);const p=e.pending.shift();
 const response=e.chunk(p,96);p.resolve(response);await settled();
 check(e.progress.length===1&&e.responses.length===1,'Live progress/completion preserved');
 check(e.responses[0][1]===200&&e.responses[0][3]===96&&e.responses[0][5]===0,'Live native status/length/error ABI preserved');
 check((e.responses[0][2]===0)===streaming,'Streaming and full-body pointer semantics preserved');
 check(e.wr.responses[id]===response,'Live response metadata remains available until release');
 check(e.allocated.size===0&&e.wr.requests[id].tempBuffer===null&&e.timers.size===0,'Terminal success frees and clears streaming scratch exactly once');
 e.release(id);check(e.allocated.size===0&&Object.keys(e.wr.responses).length===0&&e.tokens()===0,'Completed release does not double-free');
}
for(const terminal of ['success','error']){
 const e=environment(),id=e.create();e.send(id);const p=e.pending.shift();e.chunk(p,2048);check(e.allocated.size===1,'Live progress owns one scratch buffer');
 e.abort(id);e.release(id);check(e.allocated.size===0,'Release frees existing scratch before a terminal callback');
 e.chunk(p,4096);if(terminal==='success')p.resolve({status:200,parsedBody:new Uint8Array(4096)});else p.reject(Error('late failure'));await settled();
 check(e.progress.length===1&&e.responses.length===0&&Object.keys(e.wr.responses).length===0&&e.allocated.size===0,'Every late progress/terminal path remains fenced');
}
for(const terminal of ['success','error']){
 const e=environment(),id=e.create();e.send(id);const p=e.pending.shift();e.chunk(p);e.abort(id);
 e.chunk(p);if(terminal==='success')p.resolve({status:200,parsedBody:new Uint8Array(16)});else p.reject(Error('aborted'));await settled();
 check(e.progress.length===1&&e.responses.length===1&&e.responses[0][5]===17,'Still-owned abort preserves native aborted completion exactly once');
 check(e.allocated.size===0,'Aborted terminal frees byte owners');e.release(id);
}
{
 const e=environment(),id=e.create();e.send(id);const old=e.pending.shift();e.chunk(old);e.release(id);const next=e.create(id);e.send(next);const live=e.pending.shift();
 e.chunk(old);old.resolve({status:200,parsedBody:new Uint8Array(16)});await settled();
 check(e.progress.length===1&&e.responses.length===0&&!e.wr.responses[id],'Recycled request ID rejects the old object/generation');
 const response=e.chunk(live,32);live.resolve(response);await settled();check(e.responses.length===1&&e.wr.responses[id]===response,'Recycled ID new request remains usable');e.release(id);check(e.tokens()===0&&e.allocated.size===0,'Recycled generations leave no owners');
}
{
 const e=environment(),id=e.create();let ownedInside=false;
 e.send(id,{onProgress(){e.release(id);ownedInside=e.allocated.size===1;}});const p=e.pending.shift();e.chunk(p);check(ownedInside,'Reentrant release defers free while native progress borrows pointer');check(e.allocated.size===0,'Reentrant free completes after native progress unwinds');p.resolve({status:200,parsedBody:new Uint8Array(16)});await settled();check(e.responses.length===0&&Object.keys(e.wr.responses).length===0,'Reentrant progress release suppresses terminal publication');
}
{
 const e=environment(),id=e.create();e.send(id,{onResponse(){e.release(id);}});const p=e.pending.shift();e.chunk(p);p.resolve({status:200,parsedBody:new Uint8Array(16)});await settled();check(e.responses.length===1&&e.allocated.size===0&&e.tokens()===0,'Reentrant completion release neither leaks nor double-frees');
}
for(const cached of [false,true]){
 const error=Error('transport failed'),e=environment({cached,fetcher(){throw error;}}),id=e.create();e.send(id);check(e.responses.length===1&&e.responses[0][5]===2&&e.allocated.size===0,'Synchronous native error behavior preserved');e.release(id);
}
{
 const e=environment(),id=e.create();e.send(id);const p=e.pending.shift();e.chunk(p);e.wr.requests[id].isTimedOut=true;p.reject(Error('timeout'));await settled();check(e.responses.length===1&&e.responses[0][5]===14&&e.allocated.size===0,'Native timeout error mapping and scratch cleanup preserved');e.release(id);
}
{
 const e=environment();const outer=e.create(),inner=e.create();let nested=false;
 e.module.cachedFetch=function(url,init){if(!nested){nested=true;e.context._JS_WebRequest_Send(inner,0,0,11,()=>e.responses.push(['inner']),()=>{});}return e.module.fetchWithProgress(url,init);};e.module.companyName='Game';e.module.productName='Game';
 // This case deliberately installs a user transport after factory snapshots;
 // invoke native wrapper directly and verify exact stack restoration manually.
 const before=e.module.cachedFetch;
 e.context._JS_WebRequest_Send(outer,0,0,11,()=>e.responses.push(['outer']),()=>{});
 check(e.module.cachedFetch===before,'Nested Send preserves enclosing transport wrapper');
 for(const p of e.pending)p.resolve({status:200,parsedBody:new Uint8Array(0)});await settled();
 check(e.responses.length===2,'Nested/reentrant independent request completions survive');e.release(outer);e.release(inner);check(e.tokens()===0&&e.allocated.size===0,'Nested Send tokens/byte owners retire');
}

{
 const e=environment(),id=e.create();e.send(id);const old=e.pending.shift();e.chunk(old);
 const replacement=e.create(id);e.send(replacement);const live=e.pending.shift();e.chunk(old);old.reject(Error('superseded'));await settled();
 check(e.responses.length===0&&e.progress.length===1,'New Send token fences prior generation even without an old Release');
 live.resolve({status:200,parsedBody:new Uint8Array(0)});await settled();check(e.responses.length===1,'Superseding Send remains live');e.release(id);check(e.allocated.size===0&&e.tokens()===0,'Superseded scratch and tokens freed');
}
{
 const e=environment(),id=e.create();e.send(id);const old=e.pending.shift();
 old.init.signal.addEventListener('abort',()=>{e.create(id);e.send(id);});
 e.release(id);const live=e.pending.shift();check(!!e.wr.requests[id],'Abort-listener replacement survives old Release');
 old.resolve({status:200,parsedBody:new Uint8Array(0)});const response=e.chunk(live,48);live.resolve(response);await settled();
 check(e.responses.length===1&&e.wr.responses[id]===response,'Reentrant recycled generation alone publishes its response');e.release(id);check(e.tokens()===0&&e.allocated.size===0,'Abort-listener reentrancy retires all byte owners');
}
{
 const e=environment(),id=e.create();e.send(id);const p=e.pending.shift();e.chunk(p,16);e.chunk(p,4096);
 check(e.allocated.size===1&&e.freed.length===1,'Pinned progressive scratch resize frees exactly its prior allocation');e.release(id);p.reject(Error('released'));await settled();
 check(e.allocated.size===0&&e.freed.length===2&&e.responses.length===0,'Release frees resized scratch exactly once');
}
{
 const e=environment(),id=e.create();e.send(id);const p=e.pending.shift();e.chunk(p,64);p.reject(Error('offline'));await settled();
 check(e.responses.length===1&&e.responses[0][5]===2&&e.allocated.size===0,'Still-owned asynchronous error keeps native error behavior');e.release(id);
}
{
 const e=environment({streaming:false}),id=e.create();let copied;
 e.send(id,{onResponse(arg,status,pointer,length){copied=Array.from(e.heap.subarray(pointer,pointer+length));}});const p=e.pending.shift();p.resolve({status:206,parsedBody:new Uint8Array([10,20,30,40])});await settled();
 check(JSON.stringify(copied)==='[10,20,30,40]'&&e.responses[0][1]===206,'Native full-body borrowed bytes/status remain exact');
 check(e.allocated.size===0,'Native full-body allocation is freed by its original helper');e.release(id);
}
{
 const e=environment({cached:true}),id=e.create(),options=e.wr.requests[id];options.init.method='POST';options.init.headers={'X-Game':'exact'};e.heap.set([4,3,2,1],128);
 e.context._JS_WebRequest_Send(id,128,4,11,()=>e.responses.push(['posted']),()=>{});const p=e.pending.shift();
 check(p.init===options.init&&p.init.method==='POST'&&p.init.headers['X-Game']==='exact'&&p.init.control==='no-store','Exact native init/cache/body selection preserved');
 check(JSON.stringify(Array.from(new Uint8Array(await p.init.body.arrayBuffer())))==='[4,3,2,1]','Native POST Blob payload unchanged');
 p.resolve({status:200,parsedBody:new Uint8Array(0)});await settled();e.release(id);check(e.tokens()===0,'POST token released');
}

console.log(JSON.stringify({checks,status:'passed',actualPinnedLibrary:true,compiledFramework:!!framework,networkCalls:0,scope:'controlled late callbacks and allocator ownership; not browser/iPhone memory acceptance'}));
