import copy,hashlib,io,json,subprocess,tempfile,unittest
from pathlib import Path
from unittest.mock import patch
import market_prices
from nengine_adapter import bridge
from nengine_valuation import value_fit

class MarketSnapshots(unittest.TestCase):
    def setUp(self):
        self.old=(market_prices._cache,market_prices._stamp,market_prices._attempt,market_prices._failure)
        market_prices._cache=None;market_prices._stamp=market_prices._attempt=0;market_prices._failure=None
    def tearDown(self):market_prices._cache,market_prices._stamp,market_prices._attempt,market_prices._failure=self.old
    def test_source_zero_missing_and_cache(self):
        raw=b'[{"type_id":587,"average_price":0},{"type_id":185}]'
        response=io.BytesIO(raw);response.headers={'Date':'Fri, 18 Sep 2026 00:00:00 GMT'}
        with patch('market_prices.time.time',return_value=10000),patch('market_prices.urllib.request.urlopen',return_value=response) as get:
            result=market_prices.snapshot();self.assertEqual(result['prices'],{'587':0,'185':None})
            self.assertEqual(result['source']['contentSha256'],hashlib.sha256(raw).hexdigest())
            result['prices']['587']=100
            cached=market_prices.snapshot();self.assertEqual(cached['source']['cacheState'],'cached');self.assertEqual(cached['prices']['587'],0);get.assert_called_once()
        with patch('market_prices.time.time',return_value=15000),patch('market_prices.urllib.request.urlopen',side_effect=OSError('offline')):
            stale=market_prices.snapshot();self.assertEqual(stale['source']['cacheState'],'stale');self.assertEqual(stale['source']['reason'],'offline')
    def test_failure_has_no_fabricated_prices(self):
        with patch('market_prices.time.time',return_value=10000),patch('market_prices.urllib.request.urlopen',side_effect=OSError('offline')):
            result=market_prices.snapshot();self.assertEqual(result['prices'],{});self.assertEqual(result['source']['cacheState'],'unavailable');self.assertIsNone(result['source']['fetchedAt'])

class Valuation(unittest.TestCase):
    @classmethod
    def tearDownClass(cls):bridge().close()
    def fit(self):return {'name':'Valuation','shipId':587,'skills':[],'slots':[{'key':'high-0','kind':'high','item':2881,'ammo':185,'loadedCharges':2}],'cargo':[{'item':185,'quantity':10}]}
    def market(self):return {'source':{'provider':'test fixture','url':'https://example.test/prices','currency':'ISK','priceKind':'average_price','fetchedAt':'2026-09-18T00:00:00Z','providerDate':None,'expiresAt':None,'contentSha256':'a'*64,'cacheState':'fresh','reason':None},'prices':{'587':100,'2881':5,'185':.1}}
    def test_public_protocol_and_unknown_quantities(self):
        b=bridge();fit=self.fit();market=self.market()
        for case in ('complete','unknown','missing','zero','mutated','stale'):
            f=copy.deepcopy(fit);s=copy.deepcopy(market)
            if case=='unknown':del f['slots'][0]['loadedCharges']
            if case=='missing':s['prices']={}
            if case=='zero':s['prices']['185']=0
            if case=='mutated':f['slots'][0]['mutation']={'baseTypeId':2881,'mutaplasmidTypeId':1,'attributes':{'54':100}}
            if case=='stale':s['source']['cacheState']='stale'
            result=value_fit(f,s);v=result['valuation']
            self.assertEqual(v['complete'],case in ('complete','zero','stale'))
            if case=='complete':self.assertEqual(v['total'],106.2)
            if case=='unknown':self.assertEqual(v['knownSubtotal'],106);self.assertIsNone(v['total'])
            if case=='missing':self.assertIsNone(v['knownSubtotal'])
            with tempfile.TemporaryDirectory() as directory:
                p=Path(directory)
                for key,value in result['request'].items():(p/(key+'.json')).write_text(json.dumps(value),encoding='utf-8')
                subprocess.run([str(b.root/'.tools/dotnet/dotnet.exe'),str(b.root/'src/NEngine.Cli/bin/Debug/net10.0/NEngine.Cli.dll'),
                    'sde-fit-valuation','--data',str(b.root/b.baseline['dataDirectory']),'--fit',str(p/'fit.json'),'--snapshot',str(p/'snapshot.json'),'--out',str(p/'out.json')],check=True,capture_output=True)
                self.assertEqual(json.loads((p/'out.json').read_text(encoding='utf-8-sig')),v,case)

if __name__=='__main__':unittest.main()
