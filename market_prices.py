"""ESI average-price snapshots. No valuation math; public CLI exports exact inputs."""
import argparse,copy,datetime,hashlib,json,math,threading,time,urllib.request
URL='https://esi.evetech.net/latest/markets/prices/?datasource=tranquility'
_cache=None
_stamp=0
_attempt=0
_failure=None
_lock=threading.Lock()

def _utc(stamp):return datetime.datetime.fromtimestamp(stamp,datetime.timezone.utc).isoformat()

def snapshot():
 global _cache,_stamp,_attempt,_failure
 with _lock:
  now=time.time()
  if _cache is not None and now-_stamp<3600:
   result=copy.deepcopy(_cache);result['source']['cacheState']='cached';return result
  if now-_attempt>=30:
   _attempt=now
   try:
    request=urllib.request.Request(URL,headers={'User-Agent':'EVE-FitLab local fitting reference'})
    with urllib.request.urlopen(request,timeout=15) as response:
     raw=response.read(8*1024*1024+1)
     if len(raw)>8*1024*1024:raise ValueError('ESI price response exceeds limit')
     rows=json.loads(raw)
     if not isinstance(rows,list):raise ValueError('Invalid ESI price response')
     quotes={}
     for row in rows:
      ident=row.get('type_id');value=row.get('average_price')
      if type(ident) is not int or ident<=0 or str(ident) in quotes:raise ValueError('Invalid or duplicate ESI type identifier')
      if value is not None and (type(value) not in (int,float) or not math.isfinite(value) or value<0):raise ValueError('Invalid ESI price')
      quotes[str(ident)]=value
     _stamp=now;_failure=None
     _cache={'source':{'provider':'ESI Tranquility','url':URL,'currency':'ISK','priceKind':'average_price','fetchedAt':_utc(now),'providerDate':response.headers.get('Date'),'expiresAt':response.headers.get('Expires'),'contentSha256':hashlib.sha256(raw).hexdigest(),'cacheState':'fresh','reason':None},'prices':quotes}
     return copy.deepcopy(_cache)
   except (OSError,ValueError,TypeError,AttributeError) as error:_failure=str(error)
  if _cache is not None:
   result=copy.deepcopy(_cache);result['source'].update(cacheState='stale',reason=_failure);return result
  return {'source':{'provider':'ESI Tranquility','url':URL,'currency':'ISK','priceKind':'average_price','fetchedAt':None,'providerDate':None,'expiresAt':None,'contentSha256':None,'cacheState':'unavailable','reason':_failure or 'Awaiting price source'},'prices':{}}

def prices():
 data=snapshot()
 return {**data,'updatedAt':_stamp or None}

if __name__=='__main__':
 parser=argparse.ArgumentParser(description='Export an ESI average-price snapshot for fit_valuation / sde-fit-valuation')
 parser.add_argument('--snapshot',action='store_true',help='Export the full source-bound snapshot')
 parser.add_argument('--out',help='Output JSON file; stdout otherwise')
 args=parser.parse_args();data=json.dumps(snapshot(),ensure_ascii=False,allow_nan=False,indent=2)
 if args.out:
  from pathlib import Path
  Path(args.out).write_text(data,encoding='utf-8')
 else:print(data)
