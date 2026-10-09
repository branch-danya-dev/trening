import {WarpGL} from './geometry-warp-gl.js';
import {workingSize,maskFromRuns,alignment,project,projectPoint,alphaMask,iou,alignmentMetrics,structuralMetrics,composite} from './geometry-warp-core.js';
import {quality,hashBytes,hashObject,markPng,canonical,synthetic,VERSION,KIND} from './render-contract.js';
import {getImage,getSession} from './photos.js';
import {readArtifact,saveArtifact,artifactUrl,downloadArtifact,deleteArtifact} from './render-store.js';
export {artifactUrl,downloadArtifact,deleteArtifact};
export {savedResults} from './render-store.js';
const staged=new Map();
function copy(view,Type){const bytes=view.slice();return new Type(bytes.buffer.slice(bytes.byteOffset,bytes.byteOffset+bytes.byteLength));}
export function stageMeshes(id,current,target,indices,landmarksJson,targetLandmarksJson,currentMs=0,targetMs=0){
    if(staged.size>2)throw Error('Другой рендер ещё выполняется.');
    staged.set(id,{current:copy(current,Float32Array),target:copy(target,Float32Array),indices:copy(indices,Uint32Array),landmarks:JSON.parse(landmarksJson),targetLandmarks:JSON.parse(targetLandmarksJson),currentMs,targetMs});
}
export function validateMeshes(mesh){
    const {current,target,indices}=mesh;
    if(!current.length||current.length!==target.length||current.length%3||current.length>300000||!indices.length||indices.length%3||indices.length>1500000||
        current.some(v=>!Number.isFinite(v))||target.some(v=>!Number.isFinite(v))||indices.some(i=>i>=current.length/3))throw Error('Unsupported mesh topology');
}
export async function renderPixels(mesh,photo,view,pixels,w,h,gl=new WarpGL(w,h)){
    const times={},start=performance.now();validateMeshes(mesh);
    const a=alignment(mesh.current,mesh.landmarks,photo,view,w,h),current=project(mesh.current,a),target=project(mesh.target,a);times.alignment=performance.now()-start;
    const mask=maskFromRuns(photo,w,h),mapsAt=performance.now();
    const bundle=gl.conditions(current,target,mesh.indices,a),cm=alphaMask(bundle.currentNormalMask),tm=alphaMask(bundle.targetNormalMask);times.structuralMaps=performance.now()-mapsAt;
    bundle.currentBodyMask=cm;bundle.targetBodyMask=tm;
    bundle.sourceHash=await hashObject({geometry:await hashBytes(mesh.current),alignment:a});bundle.targetHash=await hashObject({geometry:await hashBytes(mesh.target),alignment:a});
    const identity=mesh.current.every((v,i)=>v===mesh.target[i]),warpAt=performance.now();
    const warped=identity?pixels:gl.warp(current,target,mesh.indices,bundle,pixels,mask);times.warp=performance.now()-warpAt;
    const repairAt=performance.now(),output=composite(pixels,warped,mask,tm,w,h,a.bodyHeight,identity);times.repairComposite=performance.now()-repairAt;
    const landmarks=mesh.landmarks.map((p,i)=>{const q=mesh.targetLandmarks[i]||p;return Math.hypot(projectPoint(p,a).x-projectPoint(q,a).x,projectPoint(p,a).y-projectPoint(q,a).y)/a.bodyHeight;});
    const metrics={...structuralMetrics(output.mask,tm,w,h,a.bodyHeight),...alignmentMetrics(cm,photo,a),sourceMaskIoU:iou(cm,mask),
        landmarkDrift:Math.max(0,...landmarks),invalidBodyFraction:output.invalidBodyFraction,repairedBackgroundFraction:output.repairedBackgroundFraction,
        stretchedBodyFraction:output.stretchedBodyFraction,backgroundChangedOutsideBand:output.backgroundChangedOutsideBand,finiteDecodable:output.pixels.every(Number.isFinite)};
    const state=quality(metrics),reasons=[];
    if(state==='Rejected')reasons.push('Форма, поза или невидимая текстура выходят за безопасные пределы. Используйте 3D-сравнение.');
    if(state==='Limited')reasons.push('Ограниченная визуализация: небольшая область фона восстановлена или текстура края растянута.');
    if(metrics.sourceMaskIoU<.9||metrics.alignmentLandmarkError>.045)reasons.push('Исходная форма не совмещается с фото по маске или позе.');
    const noWarp=structuralMetrics(mask,tm,w,h,a.bodyHeight);
    return {pixels:output.pixels,mask:output.mask,metrics,quality:state,reasons,timings:times,bundle,noWarp,identity,workingBytes:w*h*100+mesh.current.byteLength*6+mesh.indices.byteLength*4};
}
export async function render(requestJson,photoJson){
    const request=JSON.parse(requestJson),photo=JSON.parse(photoJson),mesh=staged.get(request.id);staged.delete(request.id);
    const result={requestHash:request.requestHash,rendererKind:KIND,rendererVersion:VERSION,photorealistic:false,artifactId:null,outputHash:null,
        sourceStructuralHash:request.sourceAvatarRevisionHash,targetStructuralHash:request.targetGeometryHash,quality:'Unsupported',metrics:null,reasons:[],timings:{},width:0,height:0,maxTextureSize:0,workingBytes:0,createdAt:new Date().toISOString()};
    let gl;
    try{
        if(mesh){result.timings.currentMesh=mesh.currentMs;result.timings.targetMesh=mesh.targetMs;}
        const cachedAt=performance.now(),cached=await readArtifact(request.id);if(cached){result.timings.repeatCached=performance.now()-cachedAt;return JSON.stringify({...cached.metadata.result,timings:{...cached.metadata.result.timings,...result.timings}});}
        if(!mesh)throw Error('Frozen meshes not prepared');
        const sourceAt=performance.now(),session=await getSession(request.sourcePhotoSessionId),view=request.view.toLowerCase();
        if(!session||synthetic(session)||session.sourceKind!=='OriginalObservation'||canonical(session.analysis?.[view])!==canonical(photo))throw Error('Source photo or analysis changed');
        const expectedSession=JSON.stringify(session),blob=await getImage(session.id,view);
        if(!blob||await hashBytes(await blob.arrayBuffer())!==request.sourceImageHash)throw Error('Source photo bytes changed');
        const probe=new WarpGL(1,1);result.maxTextureSize=probe.maxTexture;probe.dispose();
        const size=workingSize(photo.width,photo.height,result.maxTextureSize);result.width=size.width;result.height=size.height;
        const bitmap=await createImageBitmap(blob,{resizeWidth:size.width,resizeHeight:size.height,resizeQuality:'high'}),canvas=document.createElement('canvas');canvas.width=size.width;canvas.height=size.height;
        const ctx=canvas.getContext('2d',{willReadFrequently:true});ctx.drawImage(bitmap,0,0);bitmap.close();const pixels=ctx.getImageData(0,0,size.width,size.height).data;
        result.timings.sourceLoadDecrypt=performance.now()-sourceAt;gl=new WarpGL(size.width,size.height);
        const rendered=await renderPixels(mesh,photo,request.view,pixels,size.width,size.height,gl);
        Object.assign(result,{metrics:rendered.metrics,quality:rendered.quality,reasons:rendered.reasons,sourceStructuralHash:rendered.bundle.sourceHash,targetStructuralHash:rendered.bundle.targetHash,workingBytes:rendered.workingBytes});Object.assign(result.timings,rendered.timings);
        if(result.quality==='Rejected')return JSON.stringify(result);
        const encodeAt=performance.now();ctx.putImageData(new ImageData(rendered.pixels,size.width,size.height),0,0);
        const output=await markPng(await new Promise((resolve,reject)=>canvas.toBlob(b=>b?resolve(b):reject(Error('Image encode failed')),'image/png')));
        const check=await createImageBitmap(output);if(check.width!==size.width||check.height!==size.height)throw Error('Output not decodable');check.close();
        result.outputHash=await hashBytes(await output.arrayBuffer());result.artifactId=request.id;result.timings.encode=performance.now()-encodeAt;
        const saveAt=performance.now();await saveArtifact(request,result,output,expectedSession);result.timings.save=performance.now()-saveAt;
        return JSON.stringify(result);
    }catch(e){result.quality='Unsupported';result.artifactId=null;result.outputHash=null;result.reasons=['Локальная визуализация недоступна: '+String(e.message).slice(0,240)+'. 3D-сравнение сохранено.'];return JSON.stringify(result);}
    finally{gl?.dispose();}
}
