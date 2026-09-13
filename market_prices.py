
import time,json,urllib.request,threading
_cache=None
_stamp=0
_lock=threading.Lock()
def prices():
 global _cache,_stamp
 with _lock:
  if _cache is not None and time.time()-_stamp<3600:return {'prices':_cache,'updatedAt':_stamp}
  request=urllib.request.Request('https://esi.evetech.net/latest/markets/prices/?datasource=tranquility',headers={'User-Agent':'EVE-FitLab local fitting reference'})
  with urllib.request.urlopen(request,timeout=20) as r:data=json.load(r)
  _cache={str(e['type_id']):e['average_price'] for e in data if e.get('average_price',0)>0};_stamp=time.time()
  return {'prices':_cache,'updatedAt':_stamp}
