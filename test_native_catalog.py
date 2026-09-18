import json,subprocess,tempfile,unittest
from pathlib import Path
import server
from nengine_adapter import analyze,bridge

class NativeCatalog(unittest.TestCase):
    @classmethod
    def tearDownClass(cls):bridge().close()

    def test_pilot_catalog_public_coverage_and_booster_effects(self):
        from nengine_loadout_catalog import loadout_catalog,description_text
        from nengine_catalog import index_metadata
        from nengine_booster_plan import analyze_plan
        data=index_metadata();catalog=loadout_catalog();ids=set();cursor=None
        while True:
            page=bridge().call('catalog_search',{'query':{'categoryId':20,'limit':100,'cursor':cursor}})['result']
            ids.update(row['typeId'] for row in page['items']);cursor=page['nextCursor']
            if not cursor:break
        expected=set()
        for ident in ids:
            attributes={r['attributeID']:r['value'] for r in data['typeDogma'].get(ident,{}).get('dogmaAttributes',[])}
            if attributes.get(331,attributes.get(1087,0))>0:expected.add(ident)
        self.assertEqual(expected,{row['id'] for kind in ('implants','boosters') for row in catalog[kind]})
        for kind in ('implants','boosters'):
            for row in catalog[kind]:
                source=description_text(data['types'][row['id']].get('description',{}))
                if source:
                    for line in row['benefitTooltip'].splitlines():self.assertIn(line,source)
        for ident in (9950,10151,3898):
            row=next(t for t in catalog['boosters'] if t['id']==ident)
            result=analyze_plan({'implants':[],'boosters':[{'typeId':ident,'slot':row['slot'],'enabledSideEffects':[]}]})['analysis']
            self.assertEqual({r['id'] for r in row['sideEffects']},{r['effectId'] for r in result['boosters'][0]['sideEffects']})

    def test_loadout_details_match_public_metadata(self):
        from nengine_catalog import indexed_item,item_metadata
        b=bridge()
        for ident in [9941,9947,9950,10151]:
            view=item_metadata(indexed_item(ident))
            native=b.call('catalog_type_details',{'typeId':ident})['result']
            self.assertEqual(view['description'],native['type'].get('description',{}))
            self.assertEqual({a['id']:a['value'] for a in view['attributes']},
                {a['value']['attributeId']:a['value']['value'] for a in native['attributes']})
            self.assertEqual(view['source']['typeId'],ident)
            self.assertEqual(view['source']['indexSha256'],b.baseline['indexSha256'])
            with tempfile.TemporaryDirectory() as directory:
                output=Path(directory)/'metadata.json'
                subprocess.run([str(b.root/'.tools/dotnet/dotnet.exe'),str(b.root/'src/NEngine.Cli/bin/Debug/net10.0/NEngine.Cli.dll'),'sde-type-details','--data',str(b.root/b.baseline['dataDirectory']),'--type',str(ident),'--out',str(output)],check=True,capture_output=True)
                self.assertEqual(json.loads(output.read_text(encoding='utf-8-sig')),native)

    def test_all_published_ships_skills_drones_match_public_catalog(self):
        for category,kind in [(6,'ship'),(16,'skill'),(18,'drone'),(8,'ammo'),(32,'subsystem')]:
            ids=set();cursor=None
            while True:
                page=bridge().call('catalog_search',{'query':{'categoryId':category,'limit':100,'cursor':cursor}})['result']
                ids.update(row['typeId'] for row in page['items']);cursor=page['nextCursor']
                if not cursor:break
            self.assertEqual(ids,{row['id'] for row in server.CATALOG if row['kind']==kind})
        self.assertEqual(len(next(c for c in server.characters() if c['id']=='all5')['skills']),511)

    def test_new_types_metadata_cli_mcp(self):
        b=bridge()
        for ident in [91775,92454,92952,92033,92397,93983]:
            ui=server.TYPES[ident]
            metadata=b.call('catalog_type_details',{'typeId':ident})['result']
            self.assertEqual(ui['name'],metadata['type']['name'].get('zh',metadata['type']['name']['en']))
            for row in metadata['attributes']:
                self.assertEqual(ui['attrs'][str(row['value']['attributeId'])],row['value']['value'])
            with tempfile.TemporaryDirectory() as directory:
                output=Path(directory)/'metadata.json'
                subprocess.run([str(b.root/'.tools/dotnet/dotnet.exe'),str(b.root/'src/NEngine.Cli/bin/Debug/net10.0/NEngine.Cli.dll'),'sde-type-details','--data',str(b.root/b.baseline['dataDirectory']),'--type',str(ident),'--out',str(output)],check=True,capture_output=True)
                self.assertEqual(json.loads(output.read_text(encoding='utf-8-sig')),metadata)

    def test_new_skill_and_ship_validation_and_saved_role(self):
        all5=next(c for c in server.characters() if c['id']=='all5')['skills']
        fit={'name':'New catalog','shipId':91775,'slots':[],'skills':all5}
        server.validate_fit(fit)
        result=analyze(fit)
        self.assertEqual(result['nativeFit']['shipTypeId'],91775)
        self.assertEqual(len(result['nativeFit']['skills']),511)
        self.assertTrue(result['isValid'],result['issues'])
        before=server.STATE
        try:
            with tempfile.TemporaryDirectory() as directory:
                server.STATE=Path(directory)
                role=server.save_character({'name':'New skills','skills':all5},{'characters':[],'fits':[]})
                self.assertEqual(len(role['skills']),511)
        finally:server.STATE=before
        with self.assertRaises(ValueError):server.validate_fit({**fit,'skills':[{'skillTypeId':587,'level':5}]})

if __name__=='__main__':unittest.main()
