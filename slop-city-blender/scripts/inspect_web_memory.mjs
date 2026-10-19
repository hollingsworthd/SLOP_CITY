#!/usr/bin/env node
// Reports addressable build buffers, never physical RAM. Does not run the game.
import {readFile,readdir,lstat} from 'node:fs/promises';
import {resolve,join} from 'node:path';
import {gunzipSync} from 'node:zlib';
import {createHash} from 'node:crypto';
const root=resolve(process.argv[2]??'');
if(!process.argv[2])throw Error('Supply a completed Unity Web export directory.');
const names=await readdir(join(root,'Build'));
async function one(suffix){
 const matches=names.filter(name=>name.endsWith(suffix));if(matches.length!==1)throw Error('Expected exactly one '+suffix);
 const path=join(root,'Build',matches[0]),stat=await lstat(path);if(!stat.isFile()||stat.isSymbolicLink())throw Error('Expected ordinary build file.');
 const compressed=await readFile(path),bytes=compressed[0]===31&&compressed[1]===139?gunzipSync(compressed):compressed;
 return {path,bytes,sha256:createHash('sha256').update(compressed).digest('hex'),compressedBytes:compressed.length};
}
const wasm=await one('.wasm.unityweb'),data=await one('.data.unityweb');
if(wasm.bytes.subarray(0,8).toString('hex')!=='0061736d01000000')throw Error('Invalid Wasm header.');
let cursor=8;
function leb(){let value=0,multiplier=1;for(let n=0;n<5;n++){if(cursor>=wasm.bytes.length)throw Error('Truncated LEB');const b=wasm.bytes[cursor++];value+=(b&127)*multiplier;if(!(b&128))return value;multiplier*=128;}throw Error('Oversized LEB');}
let memories=[];
while(cursor<wasm.bytes.length){const id=wasm.bytes[cursor++],size=leb(),end=cursor+size;if(end>wasm.bytes.length)throw Error('Truncated Wasm section');
 if(id===5){const count=leb();for(let n=0;n<count;n++){const flags=leb();if(flags&~3)throw Error('Unsupported memory flags');const initialPages=leb(),maximumPages=flags&1?leb():null;memories.push({initialBytes:initialPages*65536,maximumBytes:maximumPages===null?null:maximumPages*65536,shared:!!(flags&2)});}}
 cursor=end;
}
if(memories.length!==1)throw Error('Expected one defined Unity Wasm memory.');
const limits={initialHeapBytes:128*1024**2,maximumHeapBytes:1024*1024**2,packagedDataBytes:112*1024**2};
const passed=memories[0].initialBytes<=limits.initialHeapBytes&&memories[0].maximumBytes<=limits.maximumHeapBytes&&data.bytes.length<=limits.packagedDataBytes;
console.log(JSON.stringify({checkedAtUtc:new Date().toISOString(),exportPath:root,passed,limits,wasm:{sha256:wasm.sha256,compressedBytes:wasm.compressedBytes,uncompressedBytes:wasm.bytes.length,...memories[0]},packagedData:{sha256:data.sha256,compressedBytes:data.compressedBytes,uncompressedBytes:data.bytes.length},interpretation:'Build buffer sizes only; not physical device RAM, resident memory, GPU allocations or a leak measurement. Packaged backing and live Unity objects must not be counted twice.'},null,2));
if(!passed)process.exitCode=1;
