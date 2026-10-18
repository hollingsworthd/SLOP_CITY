import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import vm from 'node:vm';
const page=readFileSync(new URL('../Unity/Assets/WebGLTemplates/SlopCity/index.html',import.meta.url),'utf8');
const start=page.indexOf('    function resizeGame() {');
const source=page.slice(start,page.indexOf('    try { if (typeof ResizeObserver',start));
function resize(width,height,density,touch=true,maximum=16384){
 const canvas={getBoundingClientRect:()=>({width,height}),dataset:{}};
 const context={canvas,window:{devicePixelRatio:density,slopCityTouchControls:touch},maxCanvasDimension:maximum,graphicsLost:false,sessionFailed:false,resolution:{value:'540'}};
 vm.runInNewContext(source+';resizeGame();',context);
 return {canvas,window:context.window};
}
test('touch canvas caps total backing pixels while preserving aspect ratio and scene budget',()=>{
 for(const [width,height,density] of [[1024,1366,2],[1366,1024,2],[430,932,3],[932,430,3],[2048,2732,4],[1,1,3],[800,600,1]]){
  const {canvas,window}=resize(width,height,density);
  assert.ok(canvas.width*canvas.height<=1920*1080);
  assert.ok(canvas.width>=1&&canvas.height>=1);
  assert.ok(Math.abs(canvas.width-window.slopCityDisplayPixelRatio*width)<1.001);
  assert.ok(Math.abs(canvas.height-window.slopCityDisplayPixelRatio*height)<1.001);
  assert.equal(window.slopCitySceneRenderHeight,540);
 }
});
test('desktop density and graphics device axis limit remain independent',()=>{
 let result=resize(1800,1000,2,false);assert.equal(result.canvas.width,3600);assert.equal(result.canvas.height,2000);
 result=resize(1366,1024,2,true,1024);assert.equal(result.canvas.width,1024);assert.ok(result.canvas.height<=1024);
 for(const density of [NaN,0,-3,Infinity])assert.equal(resize(800,600,density).canvas.width,800);
});
