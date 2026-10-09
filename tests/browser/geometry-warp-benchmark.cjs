const {chromium}=require('playwright'),assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path');
const {sourceFixture}=require('./geometry-fixture.cjs');
(async()=>{
 const fixture=JSON.parse(await fs.readFile(process.env.GEOMETRY_FIXTURES||'work/geometry-fixtures.json','utf8')),out=process.env.GEOMETRY_OUTPUT||'work/geometry-evidence';await fs.mkdir(out,{recursive:true});
 const browser=await chromium.launch({headless:true,channel:process.env.BROWSER_CHANNEL||undefined,args:['--enable-unsafe-swiftshader','--use-angle=swiftshader']});
 try{const page=await browser.newPage();await page.goto(process.env.APP_URL||'http://127.0.0.1:5256');await page.waitForSelector('[data-model-ready="true"]',{timeout:60000});
 await page.evaluate('window.sourceFixture='+sourceFixture.toString());const report=[];
 for(const f of fixture.cases)for(const view of ['Front','Side']){
  const rows=await page.evaluate(async({f,view})=>{
   const {renderPixels}=await import(new URL('js/geometry-warp.js',document.baseURI)),core=await import(new URL('js/geometry-warp-core.js',document.baseURI)),{WarpGL}=await import(new URL('js/geometry-warp-gl.js',document.baseURI)),{warpRows}=await import(new URL('js/warp.js',document.baseURI));
   const s=await window.sourceFixture(f,view),results=[];for(const target of f.targets){const gl=new WarpGL(512,768),at=performance.now();let r;
    try{r=await renderPixels({current:s.current,target:new Float32Array(target.positions),indices:s.indices,landmarks:f.landmarks,targetLandmarks:target.landmarks},s.photo,view,s.pixels,512,768,gl);}finally{gl.dispose();}
    const runtime=performance.now()-at,legacy=new Uint8ClampedArray(512*768*4),maskPixels=new Uint8ClampedArray(legacy.length);for(let i=0;i<s.mask.length;i++)maskPixels[i*4+3]=s.mask[i]*255;
    legacy.set(maskPixels);const legacyRows=(view==='Front'?target.legacyFront:target.legacySide).sort((a,b)=>a.y-b.y);if(legacyRows.length)warpRows(maskPixels,legacy,512,768,legacyRows);
    const lm=core.structuralMetrics(core.alphaMask(legacy),r.bundle.targetBodyMask,512,768,r.bundle.alignment.bodyHeight);
    let identityMax=0;if(target.name==='identity')for(let i=0;i<s.pixels.length;i++)identityMax=Math.max(identityMax,Math.abs(s.pixels[i]-r.pixels[i]));
    results.push({profile:f.name,view,change:target.name,noWarp:r.noWarp,legacyRowWarp:lm,geometryWarp:r.metrics,state:r.quality,runtimeMs:runtime,identityMaxByteError:identityMax,workingBytes:r.workingBytes});
   }return results;
  },{f,view});report.push(...rows);console.log(f.name,view,rows.map(r=>r.change+':'+r.state).join(', '));
 }
 await fs.writeFile(path.join(out,'benchmark.json'),JSON.stringify({fixture:'MakeHuman synthetic controlled; not real-human accuracy',renderer:'geometry-warp-1',cases:report},null,2));
 for(const r of report){if(r.change==='identity'){assert.equal(r.identityMaxByteError,0);assert.equal(r.geometryWarp.repairedBackgroundFraction,0);assert.equal(r.state,'Accepted');}
  else if(r.change==='unsafe-growth')assert.ok(['Rejected','Limited'].includes(r.state));
  else {assert.notEqual(r.state,'Rejected',JSON.stringify(r));assert.ok(r.geometryWarp.targetSilhouetteIoU>r.noWarp.targetSilhouetteIoU||r.noWarp.targetSilhouetteIoU===1,JSON.stringify(r));}
  assert.equal(r.geometryWarp.backgroundChangedOutsideBand,0);
 }
 console.log('Geometry benchmark PASS',report.length);
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exit(1);});
