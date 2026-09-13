import tempfile,unittest
from pathlib import Path
from unittest.mock import patch
import storage_location as storage
class StorageTests(unittest.TestCase):
 def test_verified_move_and_legacy_engine_path(self):
  with tempfile.TemporaryDirectory(prefix='fitlab-storage-test-') as temp:
   root=Path(temp);source=root/'old';target=root/'new';source.mkdir();(source/'engine').mkdir();(source/'library.json').write_text('{"fits":[]}',encoding='utf-8');(source/'engine'/'state.json').write_text('engine',encoding='utf-8')
   with patch.object(storage,'CONFIG',root/'config.json'):
    directory,backup=storage.migrate(source,str(target))
    self.assertEqual(directory,target.resolve());self.assertEqual(storage.read_location(source),target.resolve())
    self.assertEqual((backup/'library.json').read_text(),'{"fits":[]}')
    (source/'engine'/'later.txt').write_text('new engine write')
    self.assertEqual((target/'engine'/'later.txt').read_text(),'new engine write')
    self.assertEqual((source/'library.json').read_text(),(target/'library.json').read_text())
 def test_reject_nonempty_and_nested(self):
  with tempfile.TemporaryDirectory(prefix='fitlab-storage-test-') as temp:
   root=Path(temp);source=root/'old';source.mkdir();(source/'keep').write_text('keep');target=root/'occupied';target.mkdir();(target/'keep').write_text('unrelated')
   for destination in [str(target),str(source/'nested'),str(root),'relative/path']:
    with self.assertRaises(ValueError):storage.migrate(source,destination)
   self.assertEqual((source/'keep').read_text(),'keep');self.assertEqual((target/'keep').read_text(),'unrelated')
 def test_rollback_when_junction_fails(self):
  with tempfile.TemporaryDirectory(prefix='fitlab-storage-test-') as temp:
   root=Path(temp);source=root/'old';source.mkdir();(source/'keep').write_text('keep')
   with patch.object(storage,'junction',side_effect=ValueError('failed')),patch.object(storage,'CONFIG',root/'config.json'):
    with self.assertRaises(ValueError):storage.migrate(source,str(root/'new'))
    self.assertEqual((source/'keep').read_text(),'keep');self.assertFalse((root/'config.json').exists())
if __name__=='__main__':unittest.main()
