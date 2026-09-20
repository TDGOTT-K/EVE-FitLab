export function sample(time, launch=19, impact=24.2) {
 const age=time-launch, hitAge=time-impact;
 return {time,phase:age<0?'ready':hitAge<0?'flight':hitAge<1.8?'impact':'complete',
  progress:Math.max(0,Math.min(1,age/(impact-launch))),
  missileVisible:age>=0&&hitAge<0,
  flash:age>=0&&age<.35?Math.pow(1-age/.35,2):0,
  shield:hitAge>=0&&hitAge<1.8?Math.pow(1-hitAge/1.8,2):0,
  ring:Math.max(0,hitAge)*2.3, hitAge};
}
