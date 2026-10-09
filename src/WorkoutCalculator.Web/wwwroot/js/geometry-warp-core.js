export const ALIGNMENT='orthographic-neutral-1', CONDITION='structural-condition-1';
export function workingSize(width,height,maxTexture=1024){
    if(!Number.isInteger(width)||!Number.isInteger(height)||width<64||height<64||width>2048||height>2048||maxTexture<128)throw Error('Unsupported image dimensions or texture capability');
    const scale=Math.min(1,Math.min(1024,maxTexture)/Math.max(width,height));return {width:Math.max(1,Math.round(width*scale)),height:Math.max(1,Math.round(height*scale)),scale};
}
export function maskFromRuns(photo,w,h){
    if(photo.bodyMask?.version!=='photo-mask-rle-1')throw Error('Missing source mask');
    const raw=new Uint8Array(photo.width*photo.height),runs=photo.bodyMask.runs;let end=0;
    if(!Array.isArray(runs)||runs.length%2||runs.length>raw.length)throw Error('Invalid source mask');
    for(let i=0;i<runs.length;i+=2){if(!Number.isInteger(runs[i])||!Number.isInteger(runs[i+1])||runs[i]<end||runs[i+1]<=0||runs[i]+runs[i+1]>raw.length)throw Error('Invalid source mask');end=runs[i]+runs[i+1];raw.fill(1,runs[i],end);}
    const out=new Uint8Array(w*h);for(let y=0;y<h;y++)for(let x=0;x<w;x++)out[y*w+x]=raw[Math.min(photo.height-1,Math.floor((y+.5)*photo.height/h))*photo.width+Math.min(photo.width-1,Math.floor((x+.5)*photo.width/w))];return out;
}
export function maskRuns(mask){const runs=[];for(let i=0;i<mask.length;i++){if(!mask[i])continue;const start=i;while(i+1<mask.length&&mask[i+1])i++;runs.push(start,i-start+1);}return {version:'photo-mask-rle-1',runs};}
export function bounds(positions){let minY=Infinity,maxY=-Infinity;for(let i=1;i<positions.length;i+=3){minY=Math.min(minY,positions[i]);maxY=Math.max(maxY,positions[i]);}return {minY,maxY};}
const horizontal=(x,z,view,left)=>view==='Front'?x:left?-z:z;
export function alignment(current,landmarks,photo,view,w,h){
    const {minY,maxY}=bounds(current),sx=w/photo.width,sy=h/photo.height;
    const scale=(photo.floor-photo.crown)*sy/(maxY-minY),hip=landmarks.filter(p=>p.index===23||p.index===24);
    if(hip.length!==2||!Number.isFinite(scale)||scale<=0)throw Error('Missing alignment landmarks');
    const observedHip=(photo.pose[23].x+photo.pose[24].x)*sx/2;
    const meshHip=hip.reduce((n,p)=>n+horizontal(p.x,p.z,view,photo.facingLeft),0)/2;
    const a={version:ALIGNMENT,projection:'orthographic',width:w,height:h,scale,offsetX:observedHip-meshHip*scale,offsetY:photo.crown*sy+maxY*scale,view,facingLeft:photo.facingLeft,depthSpan:4,bodyHeight:(photo.floor-photo.crown)*sy};
    const points=landmarks.filter(p=>photo.pose[p.index]?.visibility>=.8);
    a.landmarkError=points.reduce((n,p)=>{const q=projectPoint(p,a),o=photo.pose[p.index];return n+Math.hypot(q.x-o.x*sx,q.y-o.y*sy);},0)/Math.max(1,points.length)/a.bodyHeight;
    a.heightResidual=Math.abs((photo.floor-photo.crown)*photo.cmPerPixel/100-(maxY-minY))/(maxY-minY);
    return a;
}
export function projectPoint(p,a){const x=horizontal(p.x,p.z,a.view,a.facingLeft),depth=a.view==='Front'?p.z:a.facingLeft?p.x:-p.x;return {x:x*a.scale+a.offsetX,y:a.offsetY-p.y*a.scale,depth:.5-depth/a.depthSpan};}
export function project(positions,a){
    const out=new Float32Array(positions.length);for(let i=0;i<positions.length;i+=3){const q=projectPoint({x:positions[i],y:positions[i+1],z:positions[i+2]},a);out[i]=q.x/a.width*2-1;out[i+1]=1-q.y/a.height*2;out[i+2]=q.depth*2-1;}return out;
}
export const alphaMask=rgba=>Uint8Array.from({length:rgba.length/4},(_,i)=>rgba[i*4+3]>0?1:0);
export function iou(a,b){let union=0,both=0;for(let i=0;i<a.length;i++){union+=!!(a[i]||b[i]);both+=!!(a[i]&&b[i]);}return union?both/union:1;}
export function rowSpan(mask,w,y,center=null){
    const spans=[];for(let x=0;x<w;x++){if(!mask[y*w+x])continue;const left=x;while(x+1<w&&mask[y*w+x+1])x++;spans.push([left,x+1]);}
    if(!spans.length)return null;if(center===null)return [spans[0][0],spans.at(-1)[1]];
    return spans.find(([l,r])=>l<=center&&r>=center)||spans.reduce((a,b)=>Math.abs((a[0]+a[1])/2-center)<Math.abs((b[0]+b[1])/2-center)?a:b);
}
export function distanceField(mask,w,h){
    const n=w*h,d=new Int32Array(n).fill(w+h),owner=new Int32Array(n).fill(-1);
    for(let i=0;i<n;i++)if(mask[i]){d[i]=0;owner[i]=i;}
    const relax=(i,j)=>{if(d[j]+1<d[i]){d[i]=d[j]+1;owner[i]=owner[j];}};
    for(let y=0;y<h;y++)for(let x=0;x<w;x++){const i=y*w+x;if(x)relax(i,i-1);if(y)relax(i,i-w);}
    for(let y=h-1;y>=0;y--)for(let x=w-1;x>=0;x--){const i=y*w+x;if(x+1<w)relax(i,i+1);if(y+1<h)relax(i,i+w);}
    return {distance:d,owner};
}
function contour(mask,w,h){const c=new Uint8Array(mask.length);for(let y=1;y<h-1;y++)for(let x=1;x<w-1;x++){const i=y*w+x;if(mask[i]&&(!mask[i-1]||!mask[i+1]||!mask[i-w]||!mask[i+w]))c[i]=1;}return c;}
export function structuralMetrics(mask,target,w,h,height){
    const ca=contour(mask,w,h),cb=contour(target,w,h),da=distanceField(ca,w,h).distance,db=distanceField(cb,w,h).distance;
    let distance=0,count=0,widthError=0,rows=0;for(let i=0;i<mask.length;i++){if(ca[i]){distance+=db[i];count++;}if(cb[i]){distance+=da[i];count++;}}
    for(let y=0;y<h;y++){const a=rowSpan(mask,w,y),b=rowSpan(target,w,y);if(a||b){widthError+=Math.abs((a?a[1]-a[0]:0)-(b?b[1]-b[0]:0));rows++;}}
    return {targetSilhouetteIoU:iou(mask,target),contourDistance:distance/Math.max(1,count)/height,rowWidthMae:widthError/Math.max(1,rows)/height};
}
export function alignmentMetrics(mask,photo,a){
    let widths=0,centers=0,count=0;for(const l of photo.levels){if(l.armOverlap||!l.snapped)continue;const y=Math.round(l.row*a.height/photo.height),sx=a.width/photo.width;if(y<0||y>=a.height)continue;const span=rowSpan(mask,a.width,y,(l.left+l.right)*sx/2);if(!span)continue;widths+=Math.abs(span[1]-span[0]-(l.right-l.left)*sx);centers+=Math.abs((span[0]+span[1]-(l.left+l.right)*sx)/2);count++;}
    return {alignmentWidthMae:count?widths/count/a.bodyHeight:1,centerlineError:count?centers/count/a.bodyHeight:1,heightResidual:a.heightResidual,alignmentLandmarkError:a.landmarkError};
}
export function composite(source,warped,sourceMask,targetMask,w,h,bodyHeight,identity=false){
    const out=new Uint8ClampedArray(source),outputMask=new Uint8Array(targetMask),area=targetMask.reduce((a,b)=>a+b,0),sourceArea=sourceMask.reduce((a,b)=>a+b,0);
    if(identity)return {pixels:out,mask:new Uint8Array(sourceMask),invalidBodyFraction:0,repairedBackgroundFraction:0,stretchedBodyFraction:0,backgroundChangedOutsideBand:0,repairPixels:0};
    const valid=new Uint8Array(w*h),background=new Uint8Array(w*h);let holes=0,repair=0;
    for(let i=0;i<valid.length;i++){valid[i]=targetMask[i]&&warped[i*4+3]>0?1:0;background[i]=!sourceMask[i]&&!targetMask[i]?1:0;if(targetMask[i]&&!valid[i])holes++;if(sourceMask[i]&&!targetMask[i])repair++;}
    const bodyNearest=distanceField(valid,w,h),bgNearest=distanceField(background,w,h);let invalid=0,outside=0;
    const maxBody=Math.max(1,Math.floor(bodyHeight*.008)),maxBackground=Math.max(1,Math.floor(bodyHeight*.025));
    for(let i=0;i<valid.length;i++){
        let donor=-1,from=warped;
        if(targetMask[i]){if(valid[i])donor=i;else if(bodyNearest.distance[i]<=maxBody)donor=bodyNearest.owner[i];else{invalid++;outputMask[i]=0;}}
        else if(sourceMask[i]){from=source;if(bgNearest.distance[i]<=maxBackground)donor=bgNearest.owner[i];else invalid++;}
        if(donor>=0){out.set(from.subarray(donor*4,donor*4+4),i*4);out[i*4+3]=255;}
        if(!sourceMask[i]&&!targetMask[i]&&out.subarray(i*4,i*4+4).some((v,k)=>v!==source[i*4+k]))outside++;
    }
    return {pixels:out,mask:outputMask,invalidBodyFraction:invalid/Math.max(1,area),repairedBackgroundFraction:repair/Math.max(1,sourceArea),stretchedBodyFraction:holes/Math.max(1,area),backgroundChangedOutsideBand:outside/(w*h),repairPixels:repair};
}
