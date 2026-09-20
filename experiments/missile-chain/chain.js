import * as T from 'three';
import {OrbitControls} from 'three/addons/controls/OrbitControls.js';
import {GLTFLoader} from 'three/addons/loaders/GLTFLoader.js';
import {KTX2Loader} from 'three/addons/loaders/KTX2Loader.js';
import {MeshoptDecoder} from 'three/addons/libs/meshopt_decoder.module.js';
import {RoomEnvironment} from 'three/addons/environments/RoomEnvironment.js';
import {sample} from './timeline.js';
const $=s=>document.querySelector(s),view=$('#view');
const renderer=new T.WebGLRenderer({antialias:true});renderer.setPixelRatio(Math.min(devicePixelRatio,1.5));view.append(renderer.domElement);renderer.toneMapping=T.AgXToneMapping;renderer.toneMappingExposure=.85;
const scene=new T.Scene();scene.background=new T.Color('#080f19');const camera=new T.PerspectiveCamera(38,1,.02,300);const controls=new OrbitControls(camera,renderer.domElement);controls.enableDamping=true;
const pm=new T.PMREMGenerator(renderer),room=new RoomEnvironment();scene.environment=pm.fromScene(room,.04).texture;room.dispose();pm.dispose();scene.add(new T.HemisphereLight(0xb8d5ff,0x17212c,.65));const sun=new T.DirectionalLight(0xe5f2ff,1.6);sun.position.set(4,7,3);scene.add(sun);
const loader=new GLTFLoader().setMeshoptDecoder(MeshoptDecoder).setKTX2Loader(new KTX2Loader().setTranscoderPath('../tools/node/node_modules/three/examples/jsm/libs/basis/').detectSupport(renderer));
let time=17.5,playing=true,speed=1,last=performance.now(),ready=false;
function aim(pos,target){camera.position.copy(pos);controls.target.copy(target);controls.update()}
try{
const [pg,bg,lg,source,event]=await Promise.all([loader.loadAsync('../pack/phoenix-lod0.glb'),loader.loadAsync('../pack/bhaalgorn-lod0.glb'),loader.loadAsync('launcher-textured.glb?v=restored1'),fetch('launcher-source.json').then(r=>r.json()),fetch('event.json').then(r=>r.json())]);
const phoenix=new T.Group(),target=new T.Group();scene.add(phoenix,target);
const pb=new T.Box3().setFromObject(pg.scene),pc=pb.getCenter(new T.Vector3()),ps=pb.getSize(new T.Vector3()),scale=5/Math.max(ps.x,ps.y,ps.z);pg.scene.position.copy(pc).negate();phoenix.add(pg.scene);phoenix.scale.setScalar(scale);phoenix.position.set(-5,0,0);
const bb=new T.Box3().setFromObject(bg.scene),bc=bb.getCenter(new T.Vector3()),bs=bb.getSize(new T.Vector3());bg.scene.position.copy(bc).negate();target.add(bg.scene);target.scale.setScalar(3/Math.max(bs.x,bs.y,bs.z));target.position.set(5,0,0);
const launcher=lg.scene;const mountMatrix=new T.Matrix4().fromArray(source.mountMatrix);const mountPosition=new T.Vector3(),mountQuaternion=new T.Quaternion(),mountScale=new T.Vector3();mountMatrix.decompose(mountPosition,mountQuaternion,mountScale);launcher.position.copy(mountPosition);launcher.quaternion.copy(mountQuaternion);pg.scene.add(launcher);scene.updateMatrixWorld(true);const rawSize=new T.Box3().setFromObject(launcher).getSize(new T.Vector3());// Preserve imported weapon units; normalize only the authored mount scale.
scene.updateMatrixWorld(true);
const restQuaternion=launcher.quaternion.clone();
const mixer=new T.AnimationMixer(launcher);
const clips=new Map(lg.animations.map(c=>[c.name,c]));
const actions=new Map(lg.animations.map(c=>[c.name,mixer.clipAction(c)]));
function mechanical(t){
 const name=t<19?'Deploy':t<25?'Fire':'Pack';
 const progress=t<19?(t-17.5)/1.5:t<25?(t-19)/6:(t-25)/2;
 mixer.stopAllAction();const action=actions.get(name);action.reset().setLoop(T.LoopOnce,1);action.clampWhenFinished=true;action.play();action.time=T.MathUtils.clamp(progress,0,1)*clips.get(name).duration;mixer.update(0);launcher.updateMatrixWorld(true);return {clip:name,clipTime:action.time};
}
mechanical(19);
const muzzleBone=launcher.getObjectByName('Pos_Fire01');
if(!muzzleBone)throw Error('Original muzzle socket missing');
const launch=muzzleBone.getWorldPosition(new T.Vector3());
mechanical(17.5);
const targetBox=new T.Box3().setFromObject(target),targetSize=targetBox.getSize(new T.Vector3()),hit=new T.Vector3(5-targetSize.x*.64,.18,.1);
const c1=launch.clone().add(new T.Vector3(.5,2.0,0)),c2=hit.clone().add(new T.Vector3(-2,1,0));const curve=new T.CubicBezierCurve3(launch,c1,c2,hit);
const missile=new T.Group();const body=new T.Mesh(new T.CylinderGeometry(.045,.06,.32,10),new T.MeshStandardMaterial({color:0xb2c4cd,metalness:.7,roughness:.3}));body.rotation.z=-Math.PI/2;missile.add(body);const nose=new T.Mesh(new T.ConeGeometry(.05,.15,10),new T.MeshStandardMaterial({color:0xe9d5a8,metalness:.5,roughness:.25}));nose.rotation.z=-Math.PI/2;nose.position.x=.23;missile.add(nose);scene.add(missile);
const glowMat=new T.MeshBasicMaterial({color:0x8deaff,transparent:true,depthWrite:false,blending:T.AdditiveBlending});const glow=new T.Mesh(new T.SphereGeometry(.10,12,8),glowMat);glow.position.x=-.22;missile.add(glow);
const trailGeo=new T.BufferGeometry(),positions=new Float32Array(61*3);trailGeo.setAttribute('position',new T.BufferAttribute(positions,3));const trail=new T.Line(trailGeo,new T.LineBasicMaterial({color:0x78c8ff,transparent:true,opacity:.85,blending:T.AdditiveBlending,depthWrite:false}));scene.add(trail);
const flash=new T.Mesh(new T.SphereGeometry(.035,16,12),new T.MeshBasicMaterial({color:0xffdab0,transparent:true,depthWrite:false,blending:T.AdditiveBlending}));flash.position.copy(launch);scene.add(flash);const light=new T.PointLight(0xffc583,0,4,2);light.position.copy(launch);scene.add(light);
const shieldMat=new T.ShaderMaterial({transparent:true,depthWrite:false,side:T.DoubleSide,blending:T.AdditiveBlending,uniforms:{power:{value:0},radius:{value:0},hit:{value:new T.Vector3(-.95,.12,.08).normalize()}},vertexShader:`varying vec3 unitPos;void main(){unitPos=normalize(position);gl_Position=projectionMatrix*modelViewMatrix*vec4(position,1.);}`,fragmentShader:`varying vec3 unitPos;uniform float power;uniform float radius;uniform vec3 hit;void main(){float d=acos(clamp(dot(normalize(unitPos),hit),-1.,1.));float ring=exp(-pow((d-radius)*12.,2.));float core=exp(-d*5.);float grid=pow(abs(sin(unitPos.y*55.)),18.)*.12;float a=power*(ring*.75+core*.45+grid*core);gl_FragColor=vec4(.15,.65,1.,a);}`});
const shield=new T.Mesh(new T.SphereGeometry(1,64,40),shieldMat);shield.position.copy(targetBox.getCenter(new T.Vector3()));shield.scale.copy(targetSize).multiplyScalar(.68);scene.add(shield);
const impact=new T.Mesh(new T.SphereGeometry(.07,20,12),new T.MeshBasicMaterial({color:0xd8f4ff,transparent:true,depthWrite:false,blending:T.AdditiveBlending}));impact.position.copy(hit);scene.add(impact);const hitLight=new T.PointLight(0x57c5ff,0,6,2);hitLight.position.copy(hit);scene.add(hitLight);
const marker=new T.Mesh(new T.TorusGeometry(.06,.003,6,40),new T.MeshBasicMaterial({color:0xe5ae61}));marker.position.copy(launch);scene.add(marker);
const grid=new T.GridHelper(28,28,0x24445a,0x122539);grid.position.y=-2.4;scene.add(grid);
const launchTime=event.missile.launchedAtUs/1e6,impactTime=event.impact.timeUs/1e6;
function setTime(value){time=Math.max(17.5,Math.min(27,Number(value)));const s=sample(time,launchTime,impactTime);const animation=mechanical(time);marker.position.copy(muzzleBone.getWorldPosition(new T.Vector3()));missile.visible=s.missileVisible;trail.visible=s.missileVisible;missile.position.copy(curve.getPoint(s.progress));missile.quaternion.setFromUnitVectors(new T.Vector3(1,0,0),curve.getTangent(s.progress));glow.scale.setScalar(1+.12*Math.sin(time*49));for(let i=0;i<=60;i++){const p=curve.getPoint(Math.max(0,s.progress-.18*(1-i/60)));positions.set(p.toArray(),i*3)}trailGeo.attributes.position.needsUpdate=true;trailGeo.computeBoundingSphere();flash.visible=s.flash>0;flash.scale.setScalar(.3+s.flash*2);flash.material.opacity=s.flash;light.intensity=s.flash*2;shield.visible=s.shield>0;shieldMat.uniforms.power.value=s.shield;shieldMat.uniforms.radius.value=s.ring;impact.visible=s.shield>0;impact.scale.setScalar(1+Math.max(0,s.hitAge)*4);impact.material.opacity=s.shield*.35;hitLight.intensity=s.shield*20;marker.visible=s.phase==='ready';marker.quaternion.copy(camera.quaternion);$('#phase').textContent={ready:'原始机械动画 / 展开',flight:'鱼雷飞行',impact:'命中 / 护盾波纹',complete:'链路完成'}[s.phase];$('#event').textContent=`${event.missile.id} · ${s.phase==='impact'||s.phase==='complete'?'MissileImpacted #'+event.impact.sequence:'MissileLaunched'} · 凤凰 → 巴戈龙`;$('#clock').textContent=time.toFixed(2)+' s';$('#seek').value=time;view.dataset.phase=s.phase;view.dataset.time=time.toFixed(4);return {...s,position:missile.position.toArray(),launcherQuaternion:launcher.quaternion.toArray(),animation,muzzle:muzzleBone.getWorldPosition(new T.Vector3()).toArray(),loaderPosition:launcher.getObjectByName('LauncherLoader').position.toArray(),sourceId:event.missile.id};}
$('#wide').onclick=()=>aim(new T.Vector3(10,11,19),new T.Vector3(0,0,0));$('#mount').onclick=()=>aim(launcher.getWorldPosition(new T.Vector3()).add(new T.Vector3(.24,.22,.32)),launcher.getWorldPosition(new T.Vector3()));$('#target').onclick=()=>aim(new T.Vector3(1,3,5),new T.Vector3(5,0,0));if(new URLSearchParams(location.search).get('view')==='launcher')$('#mount').click();else $('#wide').click();
function playback(value){playing=value;$('#play').textContent=playing?'暂停':'播放';}
$('#play').onclick=()=>{if(time>=27)setTime(17.5);playback(!playing)};$('#restart').onclick=()=>{setTime(17.5);playback(true)};$('#seek').oninput=e=>{playback(false);setTime(e.target.value)};$('#speed').onchange=e=>speed=Number(e.target.value);
window.chainDemo={clips:lg.animations.map(c=>({name:c.name,duration:c.duration})),seek:t=>{playback(false);return setTime(t)},getState:()=>({...setTime(time),playing,speed}),play:()=>playback(true),pause:()=>playback(false)};
$('#loading').remove();$('#play').disabled=$('#restart').disabled=false;ready=true;setTime(time);
renderer.setAnimationLoop(now=>{const dt=Math.min(.1,(now-last)/1000);last=now;if(playing){setTime(time+dt*speed);if(time>=27)playback(false)}controls.update();renderer.render(scene,camera)});
}catch(error){$('#error').textContent=String(error);console.error(error);view.dataset.error=String(error)}
function resize(){renderer.setSize(view.clientWidth,view.clientHeight);camera.aspect=view.clientWidth/view.clientHeight;camera.updateProjectionMatrix()}new ResizeObserver(resize).observe(view);resize();
