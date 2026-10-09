// Renderer-neutral, data-only contract. Never import network or factual writers here.
export const FORMAT='workoutcalc.renders.v1', KIND='GeometryWarp', VERSION='geometry-warp-1';
// Match Utf8JsonWriter's default safe escaping for the ASCII request/hash contract.
const scalar=v=>JSON.stringify(v)?.replace(/[+<>&'\u0080-\uffff]/g,c=>'\\u'+c.charCodeAt(0).toString(16).toUpperCase().padStart(4,'0'));
export const canonical=v=>v===null||typeof v!=='object'?scalar(v):Array.isArray(v)?'['+v.map(canonical).join(',')+']':'{'+Object.keys(v).sort().map(k=>scalar(k)+':'+canonical(v[k])).join(',')+'}';
export const hashBytes=async bytes=>Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256',bytes)),b=>b.toString(16).padStart(2,'0')).join('').toUpperCase();
export const hashObject=v=>hashBytes(new TextEncoder().encode(canonical(v)));
export const synthetic=v=>v?.synthetic===true||v?.sourceKind==='Synthetic'||v?.sourceKind==='Generated'||v?.kind===KIND||v?.format===FORMAT||String(v?.id||v?.key||'').startsWith('render:');
export const MARKER='workoutcalc.synthetic=GeometryWarp';
export async function rejectSyntheticImage(blob){
    if(!(blob instanceof Blob)||blob.size>32*1024*1024)throw Error('Слишком большой или неподдерживаемый снимок.');
    if(new TextDecoder('latin1').decode(await blob.arrayBuffer()).includes(MARKER))throw Error('Синтетическая визуализация не является фактическим фото.');
}
export async function markPng(blob){
    const bytes=new Uint8Array(await blob.arrayBuffer()),payload=new TextEncoder().encode('Software\0'+MARKER),chunk=new Uint8Array(payload.length+12),v=new DataView(chunk.buffer);
    v.setUint32(0,payload.length);chunk.set(new TextEncoder().encode('tEXt'),4);chunk.set(payload,8);
    let crc=0xffffffff;for(const b of chunk.subarray(4,-4)){crc^=b;for(let i=0;i<8;i++)crc=crc&1?0xedb88320^(crc>>>1):crc>>>1;}v.setUint32(chunk.length-4,(crc^0xffffffff)>>>0);
    return new Blob([bytes.subarray(0,-12),chunk,bytes.subarray(-12)],{type:'image/png'});
}
export async function markJpeg(blob){
    const bytes=new Uint8Array(await blob.arrayBuffer()),text=new TextEncoder().encode(MARKER),chunk=new Uint8Array(text.length+4);
    chunk.set([255,239,0,text.length+2]);chunk.set(text,4);
    if(bytes[0]!==255||bytes[1]!==216)throw Error('Invalid synthetic JPEG');return new Blob([bytes.subarray(0,2),chunk,bytes.subarray(2)],{type:'image/jpeg'});
}
export function quality(m){
    if(!m?.finiteDecodable||Object.values(m).some(v=>typeof v==='number'&&(!Number.isFinite(v)||v<0||v>1))||
        m.targetSilhouetteIoU<.98||m.contourDistance>.006||m.rowWidthMae>.008||m.landmarkDrift>.025||m.invalidBodyFraction>0||
        m.repairedBackgroundFraction>.08||m.stretchedBodyFraction>.015||m.backgroundChangedOutsideBand>0||m.sourceMaskIoU<.90||
        m.heightResidual>.03||m.alignmentWidthMae>.02||m.centerlineError>.015||m.alignmentLandmarkError>.045)return 'Rejected';
    return m.stretchedBodyFraction>.002||m.repairedBackgroundFraction>.03||m.sourceMaskIoU<.95?'Limited':'Accepted';
}
const metricKeys=['targetSilhouetteIoU','contourDistance','rowWidthMae','landmarkDrift','invalidBodyFraction','repairedBackgroundFraction','stretchedBodyFraction','backgroundChangedOutsideBand','sourceMaskIoU','heightResidual','alignmentWidthMae','centerlineError','alignmentLandmarkError','finiteDecodable'];
export async function validateArtifact(row,local,sessions){
    const a=row?.metadata,r=a?.request,o=a?.result;
    const hex=s=>typeof s==='string'&&/^[A-F0-9]{64}$/.test(s);
    if(a?.format!==FORMAT||a.synthetic!==true||a.kind!==KIND||r?.rendererKind!==KIND||r.rendererVersion!==VERSION||
        r.alignmentVersion!=='orthographic-neutral-1'||r.structuralConditionVersion!=='structural-condition-1'||r.sourcePolicyVersion!=='exact-revision-photo-1'||
        !['Front','Side'].includes(r.view)||!hex(r.requestHash)||row.key!==r.id||r.id!=='render:'+r.requestHash||
        o?.requestHash!==r.requestHash||o.photorealistic!==false||o.rendererKind!==KIND||o.rendererVersion!==VERSION||o.artifactId!==r.id||!hex(o.outputHash)||
        !o.metrics||metricKeys.some(k=>!(k in o.metrics))||!['Accepted','Limited'].includes(o.quality)||quality(o.metrics)!==o.quality||
        !Number.isInteger(o.width)||!Number.isInteger(o.height)||o.width<1||o.height<1||o.width>1024||o.height>1024||
        !o.timings||Object.values(o.timings).some(n=>!Number.isFinite(n)||n<0)||!Array.isArray(o.reasons)||o.reasons.some(s=>typeof s!=='string'||s.length>500)||
        !Number.isFinite(Date.parse(r.createdAt))||!Number.isFinite(Date.parse(o.createdAt)))throw Error('Повреждены метаданные синтетического рендера.');
    if(await hashObject({...r,id:'',requestHash:'',createdAt:'0001-01-01T00:00:00+00:00'})!==r.requestHash)throw Error('Изменены входы рендера.');
    const env=JSON.parse(local['workoutcalc.observedHypotheses.v1']||'null'),h=env&&JSON.parse(env.payload).items.find(h=>h.core.id===r.hypothesisId),c=h?.core;
    if(!c||h.coreHash!==r.hypothesisCoreHash||c.profileId!==r.profileId||c.avatarId!==r.avatarId||c.trackingCycleId!==r.trackingCycleId||
        c.currentAvatarRevisionAtIssue.id!==r.sourceAvatarRevisionId||c.exactEndpoint.geometry.sha256!==r.targetGeometryHash||
        !c.currentAvatarRevisionAtIssue.inputs.photos.some(p=>p.sessionId===r.sourcePhotoSessionId&&p.analysisHash===r.sourcePhotoAnalysisHash)||
        !sessions.some(s=>s.id===r.sourcePhotoSessionId&&!synthetic(s)))throw Error('Нарушена связь рендера с гипотезой или исходным фото.');
    if(row.blob&&await hashBytes(await row.blob.arrayBuffer())!==o.outputHash)throw Error('Изменены байты рендера.');
}
