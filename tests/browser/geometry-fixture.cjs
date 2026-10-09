// A controlled, explicitly synthetic sensor fixture. This helper is never shipped in wwwroot.
async function sourceFixture(f,view){
 const core=await import(new URL('js/geometry-warp-core.js',document.baseURI)),{WarpGL}=await import(new URL('js/geometry-warp-gl.js',document.baseURI));
 const w=512,h=768,current=new Float32Array(f.current),indices=new Uint32Array(f.indices),b=core.bounds(current),scale=h*.9/(b.maxY-b.minY);
 const a={version:core.ALIGNMENT,projection:'orthographic',width:w,height:h,scale,offsetX:w/2,offsetY:h*.05+b.maxY*scale,view,facingLeft:true,depthSpan:4,bodyHeight:h*.9};
 const gl=new WarpGL(w,h),projected=core.project(current,a);let bundle;try{bundle=gl.conditions(projected,projected,indices,a);}finally{gl.dispose();}
 const mask=core.alphaMask(bundle.currentNormalMask),pixels=new Uint8ClampedArray(w*h*4);
 for(let y=0;y<h;y++)for(let x=0;x<w;x++){const i=y*w+x,j=i*4,check=(Math.floor(x/24)+Math.floor(y/24))%2;
  pixels[j]=mask[i]?110+Math.round(bundle.currentNormalMask[j]*.35)+check*12:210+check*20;
  pixels[j+1]=mask[i]?85+check*18:220;pixels[j+2]=mask[i]?100+Math.round(bundle.currentNormalMask[j+2]*.25):225;pixels[j+3]=255;
 }
 const pose=Array.from({length:33},()=>({x:w/2,y:h/2,visibility:1}));for(const p of f.landmarks){const q=core.projectPoint(p,a);pose[p.index]={x:q.x,y:q.y,visibility:1};}
 const levels=[];for(let n=0;n<113;n++){const fraction=Number((.3+n*.005).toFixed(4)),row=Math.round(h*.95-fraction*h*.9);if(view==='Front'&&fraction<.46)continue;
  const span=core.rowSpan(mask,w,row,w/2);if(span)levels.push({fraction,row,left:span[0],right:span[1],sizeCm:(span[1]-span[0])*100/scale,snapped:true,armOverlap:false});}
 const photo={view,width:w,height:h,top:Math.floor(h*.05),bottom:Math.ceil(h*.95),crown:h*.05,floor:h*.95,cmPerPixel:100/scale,scaleFromSide:false,facingLeft:true,levels,pose,warnings:[],bodyMask:core.maskRuns(mask)};
 return {photo,pixels,mask,current,indices,a};
}
module.exports={sourceFixture};
