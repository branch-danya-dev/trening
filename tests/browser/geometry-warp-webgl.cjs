const {chromium}=require('playwright'),assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path');
const {sourceFixture}=require('./geometry-fixture.cjs');
(async()=>{
 const browser=await chromium.launch({headless:true,channel:process.env.BROWSER_CHANNEL||undefined,args:['--enable-unsafe-swiftshader']});
 try{const p=await browser.newPage();await p.goto(process.env.APP_URL||'http://127.0.0.1:5256');await p.waitForSelector('[data-model-ready="true"]',{timeout:60000});
 const result=await p.evaluate(async()=>{
  const {WarpGL}=await import(new URL('js/geometry-warp-gl.js',document.baseURI));
  const w=128,h=128,gl=new WarpGL(w,h),indices=new Uint32Array([0,1,2,0,2,3,4,5,6,4,6,7]);
  const quad=(left,right,z)=>[left,-.6,z,right,-.6,z,right,.6,z,left,.6,z];
  const current=new Float32Array([...quad(-.5,.5,-.4),...quad(-.5,.5,.4)]),target=new Float32Array([...quad(-.7,.1,-.4),...quad(-.1,.7,.4)]);
  const a={scale:100},bundle=gl.conditions(current,target,indices,a),pixels=new Uint8ClampedArray(w*h*4).fill(255),mask=new Uint8Array(w*h).fill(1);
  const out=gl.warp(current,target,indices,bundle,pixels,mask),at=(x,y)=>(y*w+x)*4;
  const sourceOcclusion=out[at(90,64)+3]===0; // New target surface was behind another source triangle.
  const targetOcclusion=bundle.targetDepth[64*w+64]<.5; // Near target face wins at overlap.
  const frontVisible=out[at(40,64)+3]===255,finite=bundle.currentDepth.every(Number.isFinite)&&bundle.targetDepth.every(Number.isFinite);
  const normalsCorrect=bundle.targetNormalMask[at(40,64)+2]>240;
  const ext=gl.gl.getExtension('WEBGL_lose_context');ext.loseContext();await new Promise(r=>setTimeout(r,30));let lost=false;try{gl.check();}catch{lost=true;}ext.restoreContext();await new Promise(r=>setTimeout(r,30));gl.dispose();
  const fresh=new WarpGL(32,32);fresh.check();fresh.gl.getShaderParameter=()=>false;let shaderFailure=false;try{fresh.conditions(current,target,indices,a);}catch{shaderFailure=true;}finally{fresh.dispose();}
  let unavailable=false;const canvas=document.createElement('canvas');canvas.getContext=()=>null;try{new WarpGL(32,32,canvas);}catch{unavailable=true;}
  return {sourceOcclusion,targetOcclusion,frontVisible,finite,normalsCorrect,lost,shaderFailure,unavailable};
 });for(const [k,v]of Object.entries(result))assert.equal(v,true,k);
 const fixture=JSON.parse(await fs.readFile(process.env.GEOMETRY_FIXTURES||'work/geometry-fixtures.json','utf8'));await p.evaluate('window.sourceFixture='+sourceFixture.toString());
 const repeat=await p.evaluate(async f=>{
  const {WarpGL}=await import(new URL('js/geometry-warp-gl.js',document.baseURI)),core=await import(new URL('js/geometry-warp-core.js',document.baseURI)),{renderPixels}=await import(new URL('js/geometry-warp.js',document.baseURI));
  const s=await window.sourceFixture(f,'Front'),projected=core.project(s.current,s.a),gl=new WarpGL(512,768),maps=gl.conditions(projected,projected,s.indices,s.a),raw=gl.warp(projected,projected,s.indices,maps,s.pixels,s.mask);gl.dispose();
  let holes=0,max=0;for(let i=0;i<s.mask.length;i++)if(s.mask[i]){if(!raw[i*4+3])holes++;else for(let c=0;c<4;c++)max=Math.max(max,Math.abs(raw[i*4+c]-s.pixels[i*4+c]));}
  const mesh={current:s.current,target:new Float32Array(f.targets[1].positions),indices:s.indices,landmarks:f.landmarks,targetLandmarks:f.targets[1].landmarks};
  const render=async()=>{const g=new WarpGL(512,768);try{return await renderPixels(mesh,s.photo,'Front',s.pixels,512,768,g);}finally{g.dispose();}};
  const first=await render(),second=await render();let repeatError=0;for(let i=0;i<first.pixels.length;i++)repeatError=Math.max(repeatError,Math.abs(first.pixels[i]-second.pixels[i]));
  return {identityShaderHoles:holes,identityShaderMaxByteError:max,repeatMaxByteError:repeatError};
 },fixture.cases[0]);assert.equal(repeat.identityShaderHoles,0);assert.equal(repeat.identityShaderMaxByteError,0);assert.equal(repeat.repeatMaxByteError,0);
 const out=process.env.GEOMETRY_OUTPUT||'work/geometry-evidence';await fs.mkdir(out,{recursive:true});await fs.writeFile(path.join(out,'webgl.json'),JSON.stringify({...result,...repeat},null,2));console.log('WebGL gates PASS',result,repeat);
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exit(1);});
