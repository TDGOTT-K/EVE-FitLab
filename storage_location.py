"""Persistent data location and verified migration for the local Windows app."""
import json,os,shutil,subprocess,uuid,hashlib
from pathlib import Path
CONFIG=Path(os.environ['FITLAB_STORAGE_CONFIG']) if os.environ.get('FITLAB_STORAGE_CONFIG') else Path(os.environ.get('LOCALAPPDATA',Path.home()/'.config'))/'EVE-FitLab'/'storage.json'
def read_location(default):
 if CONFIG.exists():return Path(json.loads(CONFIG.read_text(encoding='utf-8'))['directory'])
 return default

def write_location(directory):
 CONFIG.parent.mkdir(parents=True,exist_ok=True);temp=CONFIG.with_suffix('.tmp');temp.write_text(json.dumps({'directory':str(directory)},ensure_ascii=False),encoding='utf-8');os.replace(temp,CONFIG)

def junction(source,destination):
 env=dict(os.environ,FITLAB_LINK_PATH=str(source),FITLAB_LINK_TARGET=str(destination))
 result=subprocess.run(['powershell.exe','-NoProfile','-NonInteractive','-Command','New-Item -ItemType Junction -Path $env:FITLAB_LINK_PATH -Target $env:FITLAB_LINK_TARGET -ErrorAction Stop | Out-Null'],env=env,capture_output=True,creationflags=getattr(subprocess,'CREATE_NO_WINDOW',0))
 if result.returncode:raise ValueError('无法建立数据目录兼容映射，原目录已保留。')

def migrate(current,requested):
 if not isinstance(requested,str) or not requested.strip():raise ValueError('请输入存储目录')
 target=Path(requested.strip()).expanduser()
 if not target.is_absolute():raise ValueError('请使用完整的绝对路径，例如 D:\\EVE-FitLab-Data')
 source=current.resolve();target=target.resolve()
 if target==source:return source,None
 if source in target.parents or target in source.parents:raise ValueError('新旧目录不能互相包含，请选择另一个独立目录')
 if str(target).startswith('\\\\'):raise ValueError('请选择本机磁盘目录')
 if target.exists() and (not target.is_dir() or any(target.iterdir())):raise ValueError('目标目录不为空，请选择空目录，避免覆盖现有文件')
 if os.name!='nt':raise ValueError('当前目录迁移仅支持 Windows')
 backup=source.with_name(source.name+'.migration-backup-'+uuid.uuid4().hex[:8])
 target.mkdir(parents=True,exist_ok=True)
 for item in source.rglob('*'):
  if item.is_symlink() or getattr(item.lstat(),'st_file_attributes',0)&0x400:raise ValueError('源目录含外部链接，不能自动迁移')
 shutil.copytree(source,target,dirs_exist_ok=True)
 for item in source.rglob('*'):
  if item.is_file():
   copied=target/item.relative_to(source)
   if hashlib.sha256(item.read_bytes()).digest()!=hashlib.sha256(copied.read_bytes()).digest():raise ValueError('文件在迁移中发生变化，原目录保持不变，请重试')
 source.rename(backup)
 try:
  junction(source,target);write_location(target)
 except Exception:
  if source.exists():os.rmdir(source) # Remove only the newly created junction, never its target.
  backup.rename(source)
  raise
 return target,backup

def open_directory(directory):
 if os.name!='nt':raise ValueError('当前环境无法打开 Windows 文件资源管理器')
 subprocess.Popen(['explorer.exe',str(directory.resolve())])
