// Deterministic five-view research audit. No anatomy parsing or nearest-surface work in browser.
const {chromium}=require('playwright'),fs=require('node:fs/promises'),path=require('node:path'),assert=require('node:assert/strict');
(async()=>{
 const dir=process.env.ANATOMY_OUTPUT||'work/anatomical-evidence',fixture=JSON.parse(await fs.readFile(path.join(dir,'fixtures.json'),'utf8'));await fs.mkdir(path.join(dir,'screenshots'),{recursive:true});
 const browser=await chromium.launch({headless:true,channel:process.env.BROWSER_CHANNEL||undefined,args:['--enable-unsafe-swiftshader','--use-angle=swiftshader']});
 try{
  const page=await browser.newPage({viewport:{width:1600,height:960},deviceScaleFactor:1});
  const base=(process.env.APP_URL||'http://127.0.0.1:5256').replace(/\/?$/,'/');
  await page.route('**/__anatomy_audit',route=>route.fulfill({contentType:'text/html',body:`<!doctype html><meta charset="utf-8"><title>Anatomical muscle audit</title><style>body{margin:0;background:#eff3f7;color:#163047;font:16px system-ui}header{padding:18px 30px}h1{margin:0;font-size:25px}p{margin:7px 0}main{display:grid;grid-template-columns:repeat(5,1fr);gap:2px}section{text-align:center;background:#fff}h2{font-size:17px;margin:8px}canvas{display:block;width:320px;height:350px}.row{font-size:14px;color:#52687b}footer{padding:14px 30px}</style><header><h1 id="title"></h1><p>BodyParts3D-derived surface mapping • same modeled state, body, camera and lighting • actual displacement (no exaggeration)</p></header><main id="grid"></main><footer>Geometry R&amp;D evidence, not human forecast accuracy. Lats retain procedural fallback. Identity and per-case measurements are recorded separately.</footer>`}));
  await page.goto(base+'__anatomy_audit');
  const errors=[];page.on('pageerror',e=>errors.push(e.message));
  for(const f of fixture.visuals){
   for(const v of fixture.protectedVertices)for(let k=0;k<3;k++)assert.equal(f.identity[v*3+k],f.anatomical[v*3+k],`${f.name}: protected vertex`);
   await page.evaluate(async({f,indices,base})=>{
    const T=await import(base+'lib/three/three.module.js');document.querySelector('#title').textContent=f.name+(f.limited?' — anatomical safety fallback active':'');
    const grid=document.querySelector('#grid');grid.replaceChildren();
    for(const [name,angle] of [['Front',0],['Side',Math.PI/2],['Back',Math.PI],['3/4 front',Math.PI/4],['3/4 back',Math.PI*3/4]]){
     const section=document.createElement('section');section.innerHTML='<h2>'+name+'</h2>';grid.append(section);
     for(const [label,positions] of [['Procedural',f.procedural],['Anatomical',f.anatomical]]){
      const caption=document.createElement('div');caption.className='row';caption.textContent=label;section.append(caption);
      const renderer=new T.WebGLRenderer({antialias:true,preserveDrawingBuffer:true});renderer.setSize(320,350);renderer.setPixelRatio(1);renderer.setClearColor(0xf8fafc);section.append(renderer.domElement);
      const scene=new T.Scene(),geo=new T.BufferGeometry();geo.setAttribute('position',new T.Float32BufferAttribute(positions,3));geo.setIndex(indices);geo.computeVertexNormals();
      const mesh=new T.Mesh(geo,new T.MeshStandardMaterial({color:0x8fb2ba,roughness:.8,metalness:0}));scene.add(mesh);scene.add(new T.HemisphereLight(0xffffff,0x34465b,2.2));
      const light=new T.DirectionalLight(0xffffff,2.5);light.position.set(2,3,4);scene.add(light);
      const camera=new T.OrthographicCamera(-.82,.82,.9,-.9,.01,10);camera.position.set(Math.sin(angle)*3,.9,Math.cos(angle)*3);camera.lookAt(0,.9,0);renderer.render(scene,camera);
      // Pixels retained; release GPU resources between cases.
      geo.dispose();mesh.material.dispose();renderer.dispose();
     }
    }
   },{f,indices:fixture.indices,base});
   await page.screenshot({path:path.join(dir,'screenshots',f.name+'.png'),fullPage:true});
  }
  assert.deepEqual(errors,[]);console.log('Anatomical visual audit PASS',fixture.visuals.length,'cases × five views × two providers');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exit(1);});
