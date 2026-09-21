import unittest,tempfile,json
from pathlib import Path
from update_backup import backup_for_update
class UpdateBackup(unittest.TestCase):
 def test_custom_directory_and_config_are_retained(self):
  with tempfile.TemporaryDirectory() as temp:
   p=Path(temp);source=p/'custom-data';source.mkdir();(source/'library.json').write_text('{"fits":[]}',encoding='utf8')
   (source/'nested').mkdir();(source/'nested/character.json').write_text('{"id":1}',encoding='utf8')
   config=p/'app/storage.json';config.parent.mkdir();config.write_text(json.dumps({'directory':str(source)}),encoding='utf8')
   result=backup_for_update(source,config);backup=Path(result['backup']);self.assertEqual(result['files'],2)
   self.assertEqual((backup/'Data/library.json').read_bytes(),(source/'library.json').read_bytes())
   self.assertEqual((backup/'storage.json').read_bytes(),config.read_bytes());self.assertTrue(json.loads((backup/'manifest.json').read_text())['complete'])
 def test_nested_backup_rejected(self):
  with tempfile.TemporaryDirectory() as temp:
   with self.assertRaises(ValueError):backup_for_update(Path(temp),Path(temp)/'storage.json')
if __name__=='__main__':unittest.main()
