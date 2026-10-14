// Display-only local photo projection. Never writes analysis, measurements, avatars or forecasts.
import * as THREE from '../lib/three/three.module.js';
import { getImage, getSession } from './photos.js';
import { landmarker } from './pose.js';

const clamp = (n, a, b) => Math.max(a, Math.min(b, n));
const mix = (a, b, t) => a + (b - a) * t;

async function prepare(id, view, detector) {
    const bitmap = await createImageBitmap(await getImage(id, view));
    let result;
    try {
        result = detector.detect(bitmap);
        const landmarks = result.landmarks?.[0], mask = result.segmentationMasks?.[0];
        if (!landmarks || !mask || [11,23,27].some(i => Math.max(landmarks[i].visibility,landmarks[i+1].visibility)<.45)) return null;
        const canvas = document.createElement('canvas');
        canvas.width = bitmap.width; canvas.height = bitmap.height;
        const context = canvas.getContext('2d', { willReadFrequently: true });
        context.drawImage(bitmap, 0, 0);
        const pixels = context.getImageData(0, 0, canvas.width, canvas.height);
        const confidence = mask.getAsFloat32Array();
        const rows = Array.from({ length: mask.height }, () => [Infinity, -Infinity]);
        const runs = Array.from({ length: mask.height }, () => []);
        let top = mask.height, bottom = 0;
        for (let y = 0; y < mask.height; y++) for (let x = 0; x < mask.width; x++) {
            if (confidence[y * mask.width + x] < .85) continue;
            rows[y][0] = Math.min(rows[y][0], x / mask.width);
            rows[y][1] = Math.max(rows[y][1], x / mask.width);
            top = Math.min(top, y); bottom = Math.max(bottom, y);
        }
        for(let y=0;y<mask.height;y++) {
            let start=-1;
            for(let x=0;x<=mask.width;x++) {
                const inside=x<mask.width&&confidence[y*mask.width+x]>.9;
                if(inside&&start<0)start=x;
                if(!inside&&start>=0){if(x-start>2)runs[y].push([start/mask.width,(x-1)/mask.width]);start=-1;}
            }
        }
        for (let y = 0; y < canvas.height; y++) for (let x = 0; x < canvas.width; x++) {
            const m = confidence[Math.min(mask.height-1, Math.floor(y / canvas.height * mask.height)) * mask.width + Math.min(mask.width-1, Math.floor(x / canvas.width * mask.width))];
            pixels.data[(y * canvas.width + x) * 4 + 3] = Math.round(clamp((m - .7) / .25, 0, 1) * 255);
        }
        context.putImageData(pixels, 0, 0);
        const texture = new THREE.CanvasTexture(canvas);
        texture.colorSpace = THREE.SRGBColorSpace;
        texture.anisotropy = 4;
        return { texture, canvas, landmarks, rows, runs, top: top / mask.height, bottom: bottom / mask.height };
    } finally { bitmap.close(); result?.segmentationMasks?.forEach(m => m.close()); }
}

function bone(rig, name) {
    const index = rig?.byName.get(name);
    return index === undefined ? null : rig.restWorldPositions[index];
}

function mapping(geometry, rig, photo, view) {
    const positions = geometry.attributes.position, skin = geometry.attributes.skinIndex, weights = geometry.attributes.skinWeight;
    const result = new Float32Array(positions.count * 2);
    if (!photo) { result.fill(-1); return result; }
    const points = photo.landmarks, bounds = geometry.boundingBox;
    const h = bounds.max.y - bounds.min.y;
    const shoulder = bone(rig, 'upperarm01.L')?.y ?? h * .81;
    const hip = bone(rig, 'upperleg01.L')?.y ?? h * .52;
    const ankle = bone(rig, 'foot.L')?.y ?? h * .055;
    const center = (a,b) => ({x:(points[a].x+points[b].x)/2,y:(points[a].y+points[b].y)/2});
    const shoulderPhoto = center(11,12), hipPhoto = center(23,24), anklePhoto = center(27,28);
    const anchors = [[bounds.min.y,photo.bottom],[ankle,anklePhoto.y],[hip,hipPhoto.y],[shoulder,shoulderPhoto.y],[bounds.max.y,photo.top]];
    const yPhoto = y => { for(let j=1;j<anchors.length;j++) if(y<=anchors[j][0])return mix(anchors[j-1][1],anchors[j][1],clamp((y-anchors[j-1][0])/(anchors[j][0]-anchors[j-1][0]),0,1));return photo.top; };
    const segments = {};
    for (const [suffix, offset] of [['L',0],['R',1]]) {
        for(const [prefix,start,end,a,b] of [
            ['upperarm','upperarm01','lowerarm01',11,13],['lowerarm','lowerarm01','wrist',13,15],
            ['wrist','wrist','finger3-3',15,19],['upperleg','upperleg01','lowerleg01',23,25],['lowerleg','lowerleg01','foot',25,27]]) {
            const from=bone(rig,`${start}.${suffix}`),to=bone(rig,`${end}.${suffix}`);
            const photoOffset=view==='side'?(points[11].visibility>points[12].visibility?0:1):offset;
            if(from&&to)segments[`${prefix}.${suffix}`]={from,to,a:points[a+photoOffset],b:points[b+photoOffset]};
        }
    }
    // Torso width by rest-height, excluding limbs. This preserves the photo's center and
    // avoids projecting the background between hanging arms and the waist onto the abdomen.
    const slices=Array.from({length:256},()=>[Infinity,-Infinity]);
    const names=rig?.mesh.skeleton.bones.map(b=>b.name)??[];
    const nameAt=i=>{if(!skin)return '';let best=0;for(let k=1;k<4;k++)if(weights.array[i*4+k]>weights.array[i*4+best])best=k;return names[skin.array[i*4+best]]??'';};
    for(let i=0;i<positions.count;i++) {
        if(/arm|wrist|finger/.test(nameAt(i)))continue;
        const row=clamp(Math.floor((positions.getY(i)-bounds.min.y)/h*255),0,255);
        const x=view==='side'?positions.getZ(i):positions.getX(i);
        slices[row][0]=Math.min(slices[row][0],x);slices[row][1]=Math.max(slices[row][1],x);
    }
    for(let i=0;i<positions.count;i++) {
        const x=positions.getX(i),y=positions.getY(i),z=positions.getZ(i),name=nameAt(i);
        let u,v=yPhoto(y);
        const match=name.match(/^(upperarm|lowerarm|wrist|finger|upperleg|lowerleg).*\.([LR])$/);
        const segment=match&&segments[`${match[1]==='finger'?'wrist':match[1]}.${match[2]}`];
        if(segment) {
            const {from,to,a,b}=segment;
            const axis=p=>view==='side'?-p.z:view==='back'?-p.x:p.x;
            const dx=axis(to)-axis(from),dy=-(to.y-from.y), length=Math.hypot(dx,dy);
            const localX=axis({x,z})-axis(from),localY=-(y-from.y);
            const along=clamp((localX*dx+localY*dy)/(length*length),-.18,1.15);
            const across=(localX*(-dy)+localY*dx)/length;
            const aspect=photo.canvas.height/photo.canvas.width;
            const px=b.x-a.x,py=(b.y-a.y)*aspect,pl=Math.hypot(px,py);
            u=a.x+along*px+across*(-py)/length;
            v=a.y+along*(b.y-a.y)+across*px/(length*aspect);
        } else {
            let row=clamp(Math.floor((y-bounds.min.y)/h*255),0,255);
            let limits=slices[row];
            if(!Number.isFinite(limits[0])) {for(let d=1;d<256;d++){const a=slices[Math.max(0,row-d)],b=slices[Math.min(255,row+d)];if(Number.isFinite(a[0])){limits=a;break;}if(Number.isFinite(b[0])){limits=b;break;}}}
            const photoRow=photo.rows[clamp(Math.floor(v*photo.rows.length),0,photo.rows.length-1)];
            let left=photoRow[0],right=photoRow[1];
            // Below shoulders and above hips, use anatomical torso edges rather than arms.
            if(view!=='side'&&y<shoulder&&y>hip) {
                const t=clamp((y-hip)/(shoulder-hip),0,1);
                const centerX=mix(hipPhoto.x,shoulderPhoto.x,t);
                const runs=photo.runs[clamp(Math.floor(v*photo.rows.length),0,photo.rows.length-1)];
                const central=runs.find(([a,b])=>a<=centerX&&b>=centerX);
                if(central){left=central[0];right=central[1];}
            }
            let fraction=((view==='side'?z:x)-limits[0])/Math.max(.001,limits[1]-limits[0]);
            if(view==='back') fraction=1-fraction;
            // Side capture convention: the person faces image-left.
            if(view==='side') fraction=1-fraction;
            u=mix(left,right,clamp(fraction,.02,.98));
        }
        // Keep sampling inside the observed silhouette. This is an edge extension of
        // existing pixels, not generated skin or a change to the body's geometry.
        const runs=photo.runs[clamp(Math.floor(v*photo.rows.length),0,photo.rows.length-1)];
        if(runs.length) {
            const nearest=runs.reduce((a,b)=>Math.abs(u-clamp(u,...a))<Math.abs(u-clamp(u,...b))?a:b);
            u=clamp(u,mix(nearest[0],nearest[1],.04),mix(nearest[0],nearest[1],.96));
        }
        // An arm in a side photograph can occlude the waist/shorts. Do not stamp that
        // arm onto the torso as well: use the adjacent front/back observations there.
        if(view==='side'&&!/arm|wrist|finger/.test(name)) {
            const side=points[11].visibility>points[12].visibility?0:1;
            const aspect=photo.canvas.height/photo.canvas.width;
            const near=(a,b,r)=>{
                const dx=b.x-a.x,dy=(b.y-a.y)*aspect;
                const t=clamp(((u-a.x)*dx+(v-a.y)*aspect*dy)/Math.max(.0001,dx*dx+dy*dy),0,1);
                return Math.hypot(u-mix(a.x,b.x,t),(v-mix(a.y,b.y,t))*aspect)<r;
            };
            if(near(points[11+side],points[13+side],.065)||near(points[13+side],points[15+side],.05)||near(points[15+side],points[19+side],.045)){u=-1;v=2;}
        }
        result[i*2]=Number.isFinite(u)?u:-1;result[i*2+1]=Number.isFinite(v)?1-v:-1;
    }
    return result;
}

export async function createPhotoSurface(id) {
    const session=await getSession(id);
    if(!session?.views?.includes('front'))return null;
    const detector=await landmarker('image');
    const photos={};
    try {
        for(const view of ['front','back','side']) if(session.views.includes(view))photos[view]=await prepare(id,view,detector);
        if(!photos.front) {Object.values(photos).forEach(p=>p?.texture.dispose());return null;}
        const blank=new THREE.DataTexture(new Uint8Array([0,0,0,0]),1,1);blank.needsUpdate=true;
        const material=new THREE.MeshBasicMaterial({color:0xc9cfd8});
        material.toneMapped=false;
        material.onBeforeCompile=shader=>{
            for(const view of ['front','back','side'])shader.uniforms[`photo_${view}`]={value:photos[view]?.texture??blank};
            shader.vertexShader='attribute vec2 photoFront;attribute vec2 photoBack;attribute vec2 photoSide;varying vec2 vPhotoFront;varying vec2 vPhotoBack;varying vec2 vPhotoSide;varying vec3 vPhotoNormal;\n'+shader.vertexShader;
            shader.vertexShader=shader.vertexShader.replace('#include <begin_vertex>','#include <begin_vertex>\nvPhotoFront=photoFront;vPhotoBack=photoBack;vPhotoSide=photoSide;vPhotoNormal=normal;');
            shader.fragmentShader='uniform sampler2D photo_front;uniform sampler2D photo_back;uniform sampler2D photo_side;varying vec2 vPhotoFront;varying vec2 vPhotoBack;varying vec2 vPhotoSide;varying vec3 vPhotoNormal;\n'+shader.fragmentShader;
            shader.fragmentShader=shader.fragmentShader.replace('#include <color_fragment>',`#include <color_fragment>
                vec3 n=normalize(vPhotoNormal);
                vec4 f=texture2D(photo_front,vPhotoFront), b=texture2D(photo_back,vPhotoBack), s=texture2D(photo_side,vPhotoSide);
                float sideWeight=pow(abs(n.x),2.0);
                float wf=(pow(max(n.z,0.0),2.0)+sideWeight*(1.0-s.a)*.5)*f.a;
                float wb=(pow(max(-n.z,0.0),2.0)+sideWeight*(1.0-s.a)*.5)*b.a;
                float ws=sideWeight*s.a;
                float total=wf+wb+ws;
                vec3 photoColor=(f.rgb*wf+b.rgb*wb+s.rgb*ws)/max(total,.001);
                diffuseColor.rgb=mix(diffuseColor.rgb,photoColor,smoothstep(.02,.18,total));`);
        };
        material.customProgramCacheKey=()=> 'local-photo-surface-v1';
        let reference=null;
        return {
            id,material,
            attach(geometry,rig,slot) {
                if(!rig)return false;
                if(slot==='current'||!reference||reference.front.length!==geometry.attributes.position.count*2) {
                    reference={};for(const view of ['front','back','side'])reference[view]=mapping(geometry,rig,photos[view],view);
                }
                for(const view of ['front','back','side'])geometry.setAttribute(`photo${view[0].toUpperCase()+view.slice(1)}`,new THREE.BufferAttribute(reference[view],2));
                return true;
            },
            dispose(){material.dispose();blank.dispose();for(const p of Object.values(photos))if(p){p.texture.dispose();p.canvas.width=p.canvas.height=1;}reference=null;}
        };
    }catch(error){for(const p of Object.values(photos))if(p){p.texture.dispose();p.canvas.width=p.canvas.height=1;}throw error;}
}
