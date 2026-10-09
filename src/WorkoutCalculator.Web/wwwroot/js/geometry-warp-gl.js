// Isolated WebGL2 offscreen context. No viewer globals, network, WebGPU, or inference.
const vertex=`#version 300 es
precision highp float;
layout(location=0) in vec3 position;
layout(location=1) in vec3 sourcePosition;
out vec3 sourceClip;
out vec3 targetClip;
void main(){gl_Position=vec4(position,1.0);sourceClip=sourcePosition;targetClip=position;}`;
const structural=`#version 300 es
precision highp float;
in vec3 targetClip;
uniform vec2 dimensions;
uniform float scale;
layout(location=0) out vec4 normalMask;
layout(location=1) out vec4 packedDepth;
void main(){
 vec3 p=vec3(targetClip.x*dimensions.x/(2.0*scale),targetClip.y*dimensions.y/(2.0*scale),-targetClip.z*2.0);
 vec3 n=normalize(cross(dFdx(p),dFdy(p)));
 normalMask=vec4(n*0.5+0.5,1.0);
 uint d=uint(round(gl_FragCoord.z*16777215.0));
 packedDepth=vec4(float(d&255u),float((d>>8)&255u),float((d>>16)&255u),255.0)/255.0;
}`;
const warp=`#version 300 es
precision highp float;
in vec3 sourceClip;
uniform sampler2D sourceImage;
uniform sampler2D sourceDepth;
uniform sampler2D observedMask;
out vec4 color;
void main(){
 vec2 uv=sourceClip.xy*0.5+0.5;
 float facing=dFdx(sourceClip.x)*dFdy(sourceClip.y)-dFdx(sourceClip.y)*dFdy(sourceClip.x);
 if(facing<=0.0||any(lessThan(uv,vec2(0.0)))||any(greaterThan(uv,vec2(1.0))))discard;
 float expected=sourceClip.z*0.5+0.5;
 vec2 dx=dFdx(sourceClip.xy),dy=dFdy(sourceClip.xy);
 float zx=dFdx(sourceClip.z*0.5),zy=dFdy(sourceClip.z*0.5);
 vec2 slope=2.0*vec2(zx*dy.y-zy*dx.y,zy*dx.x-zx*dy.x)/facing;
 ivec2 size=textureSize(sourceDepth,0),base=ivec2(floor(uv*vec2(size)-0.5));
 float best=1e20;ivec2 chosen=ivec2(-1);
 // Plane-corrected pixel-center depth test avoids false occlusion at subpixel UVs.
 // Only the four raster samples surrounding the projected point are eligible.
 for(int y=0;y<2;y++)for(int x=0;x<2;x++){
  ivec2 p=base+ivec2(x,y);if(any(lessThan(p,ivec2(0)))||any(greaterThanEqual(p,size)))continue;
  vec2 center=(vec2(p)+0.5)/vec2(size),delta=center-uv;
  float visible=texelFetch(sourceDepth,p,0).r;
  if(visible>=1.0||abs(expected+dot(slope,delta)-visible)>0.0005||texelFetch(observedMask,p,0).r<0.5)continue;
  float d=dot(delta,delta);if(d<best){best=d;chosen=p;}
 }
 if(chosen.x<0)discard;
 color=vec4(texelFetch(sourceImage,chosen,0).rgb,1.0);
}`;
export function flip(data,w,h,channels=4){const out=new data.constructor(data.length);for(let y=0;y<h;y++)out.set(data.subarray(y*w*channels,(y+1)*w*channels),(h-1-y)*w*channels);return out;}
export class WarpGL {
    constructor(width,height,canvas=document.createElement('canvas')){
        this.canvas=canvas;canvas.width=width;canvas.height=height;this.width=width;this.height=height;this.resources=[];this.lost=false;
        this.onLost=e=>{e.preventDefault();this.lost=true;};canvas.addEventListener('webglcontextlost',this.onLost);
        this.gl=canvas.getContext('webgl2',{antialias:false,alpha:true,preserveDrawingBuffer:false,depth:true,premultipliedAlpha:false});
        if(!this.gl)throw Error('WebGL2 unavailable');this.maxTexture=this.gl.getParameter(this.gl.MAX_TEXTURE_SIZE);
        if(Math.max(width,height)>this.maxTexture)throw Error('Texture capability too small');
    }
    keep(kind,value){this.resources.push([kind,value]);return value;}
    check(){if(this.lost||this.gl.isContextLost())throw Error('WebGL context lost; retry with a fresh context');const e=this.gl.getError();if(e!==this.gl.NO_ERROR)throw Error('WebGL resource failure '+e);}
    program(fragment){const gl=this.gl,shaders=[gl.VERTEX_SHADER,gl.FRAGMENT_SHADER].map((type,i)=>{const s=this.keep('Shader',gl.createShader(type));gl.shaderSource(s,i?fragment:vertex);gl.compileShader(s);if(!gl.getShaderParameter(s,gl.COMPILE_STATUS))throw Error('Shader compile failure');return s;});const p=this.keep('Program',gl.createProgram());for(const s of shaders)gl.attachShader(p,s);gl.linkProgram(p);if(!gl.getProgramParameter(p,gl.LINK_STATUS))throw Error('Shader link failure');return p;}
    texture(data,channels=4,linear=false){const gl=this.gl,t=this.keep('Texture',gl.createTexture());gl.bindTexture(gl.TEXTURE_2D,t);gl.pixelStorei(gl.UNPACK_ALIGNMENT,1);gl.pixelStorei(gl.UNPACK_COLORSPACE_CONVERSION_WEBGL,gl.NONE);gl.texImage2D(gl.TEXTURE_2D,0,channels===1?gl.R8:gl.RGBA8,this.width,this.height,0,channels===1?gl.RED:gl.RGBA,gl.UNSIGNED_BYTE,data?flip(data,this.width,this.height,channels):null);gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_MIN_FILTER,linear?gl.LINEAR:gl.NEAREST);gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_MAG_FILTER,linear?gl.LINEAR:gl.NEAREST);gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_WRAP_S,gl.CLAMP_TO_EDGE);gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_WRAP_T,gl.CLAMP_TO_EDGE);return t;}
    target(two=false){const gl=this.gl,fbo=this.keep('Framebuffer',gl.createFramebuffer());gl.bindFramebuffer(gl.FRAMEBUFFER,fbo);const color=this.texture(null);gl.framebufferTexture2D(gl.FRAMEBUFFER,gl.COLOR_ATTACHMENT0,gl.TEXTURE_2D,color,0);let packed;
        if(two){packed=this.texture(null);gl.framebufferTexture2D(gl.FRAMEBUFFER,gl.COLOR_ATTACHMENT1,gl.TEXTURE_2D,packed,0);}
        const depth=this.keep('Texture',gl.createTexture());gl.bindTexture(gl.TEXTURE_2D,depth);gl.texImage2D(gl.TEXTURE_2D,0,gl.DEPTH_COMPONENT24,this.width,this.height,0,gl.DEPTH_COMPONENT,gl.UNSIGNED_INT,null);
        for(const param of [gl.TEXTURE_MIN_FILTER,gl.TEXTURE_MAG_FILTER])gl.texParameteri(gl.TEXTURE_2D,param,gl.NEAREST);
        for(const param of [gl.TEXTURE_WRAP_S,gl.TEXTURE_WRAP_T])gl.texParameteri(gl.TEXTURE_2D,param,gl.CLAMP_TO_EDGE);
        gl.framebufferTexture2D(gl.FRAMEBUFFER,gl.DEPTH_ATTACHMENT,gl.TEXTURE_2D,depth,0);gl.drawBuffers(two?[gl.COLOR_ATTACHMENT0,gl.COLOR_ATTACHMENT1]:[gl.COLOR_ATTACHMENT0]);
        if(gl.checkFramebufferStatus(gl.FRAMEBUFFER)!==gl.FRAMEBUFFER_COMPLETE)throw Error('Framebuffer unsupported');return {fbo,color,depth,two};
    }
    draw(target,program,positions,source,indices,textures={},scale=1){
        const gl=this.gl;this.check();gl.bindFramebuffer(gl.FRAMEBUFFER,target.fbo);gl.drawBuffers(target.two?[gl.COLOR_ATTACHMENT0,gl.COLOR_ATTACHMENT1]:[gl.COLOR_ATTACHMENT0]);gl.viewport(0,0,this.width,this.height);gl.clearColor(0,0,0,0);gl.clearDepth(1);gl.clear(gl.COLOR_BUFFER_BIT|gl.DEPTH_BUFFER_BIT);gl.disable(gl.BLEND);gl.disable(gl.DITHER);gl.enable(gl.DEPTH_TEST);gl.depthFunc(gl.LESS);gl.enable(gl.CULL_FACE);gl.cullFace(gl.BACK);gl.frontFace(gl.CCW);gl.useProgram(program);
        const vao=this.keep('VertexArray',gl.createVertexArray());gl.bindVertexArray(vao);
        for(const [location,data]of [[0,positions],[1,source]]){const buffer=this.keep('Buffer',gl.createBuffer());gl.bindBuffer(gl.ARRAY_BUFFER,buffer);gl.bufferData(gl.ARRAY_BUFFER,data,gl.STATIC_DRAW);gl.enableVertexAttribArray(location);gl.vertexAttribPointer(location,3,gl.FLOAT,false,0,0);}
        const ib=this.keep('Buffer',gl.createBuffer());gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER,ib);gl.bufferData(gl.ELEMENT_ARRAY_BUFFER,indices,gl.STATIC_DRAW);
        let unit=0;for(const [name,texture]of Object.entries(textures)){gl.activeTexture(gl.TEXTURE0+unit);gl.bindTexture(gl.TEXTURE_2D,texture);gl.uniform1i(gl.getUniformLocation(program,name),unit++);}
        gl.uniform2f(gl.getUniformLocation(program,'dimensions'),this.width,this.height);gl.uniform1f(gl.getUniformLocation(program,'scale'),scale);gl.drawElements(gl.TRIANGLES,indices.length,gl.UNSIGNED_INT,0);this.check();
    }
    read(target,attachment=0){const gl=this.gl,data=new Uint8Array(this.width*this.height*4);gl.bindFramebuffer(gl.FRAMEBUFFER,target.fbo);gl.readBuffer(gl.COLOR_ATTACHMENT0+attachment);gl.readPixels(0,0,this.width,this.height,gl.RGBA,gl.UNSIGNED_BYTE,data);this.check();return flip(data,this.width,this.height);}
    conditions(current,target,indices,alignment){
        const program=this.program(structural),c=this.target(true),t=this.target(true);this.draw(c,program,current,current,indices,{},alignment.scale);this.draw(t,program,target,current,indices,{},alignment.scale);
        const depth=rt=>{const rgba=this.read(rt,1);return Float32Array.from({length:this.width*this.height},(_,i)=>rgba[i*4+3]?(rgba[i*4]+rgba[i*4+1]*256+rgba[i*4+2]*65536)/16777215:1);};
        return {version:'structural-condition-1',dimensions:[this.width,this.height],alignment,currentDepth:depth(c),targetDepth:depth(t),currentNormalMask:this.read(c),targetNormalMask:this.read(t),currentTarget:c,targetTarget:t};
    }
    warp(current,target,indices,conditions,pixels,mask){const rt=this.target(),observed=Uint8Array.from(mask,v=>v?255:0);this.draw(rt,this.program(warp),target,current,indices,{sourceImage:this.texture(pixels,4,true),sourceDepth:conditions.currentTarget.depth,observedMask:this.texture(observed,1)});return this.read(rt);}
    dispose(){const gl=this.gl;if(gl)for(const [kind,r]of this.resources.reverse())gl['delete'+kind](r);this.resources=[];this.canvas.removeEventListener('webglcontextlost',this.onLost);gl?.getExtension('WEBGL_lose_context')?.loseContext();}
}
