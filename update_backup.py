"""Verified update backup; caller holds the application write lock."""
import hashlib,json,os,shutil,uuid
from pathlib import Path
def backup_for_update(source,config):
 source=Path(source).resolve();config=Path(config)
 parent=config.parent.resolve()/'UpdateBackups'
 if parent==source or source in parent.parents:raise ValueError('备份目录不能位于数据目录内，请先调整存储目录')
 parent.mkdir(parents=True,exist_ok=True)
 destination=parent/('backup-'+uuid.uuid4().hex);destination.mkdir()
 entries=[]
 for item in source.rglob('*'):
  if item.is_symlink() or getattr(item.lstat(),'st_file_attributes',0)&0x400:raise ValueError('数据目录含外部链接，未进行自动更新')
  if not item.is_file():continue
  target=destination/'Data'/item.relative_to(source);target.parent.mkdir(parents=True,exist_ok=True)
  shutil.copy2(item,target)
  def digest(path):
   h=hashlib.sha256()
   with path.open('rb') as f:
    for chunk in iter(lambda:f.read(1024*1024),b''):h.update(chunk)
   return h.hexdigest()
  checksum=digest(item)
  if checksum!=digest(target):raise ValueError('备份期间数据发生变化，请稍后重试')
  entries.append({'path':item.relative_to(source).as_posix(),'sha256':checksum})
 if config.exists():shutil.copy2(config,destination/'storage.json')
 (destination/'manifest.json').write_text(json.dumps({'source':str(source),'files':entries,'complete':True},ensure_ascii=False,indent=2),encoding='utf8')
 return {'backup':str(destination),'files':len(entries)}
