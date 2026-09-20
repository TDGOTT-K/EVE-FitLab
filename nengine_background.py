"""A bounded, explicitly read-only lane so periodic queries cannot queue ahead of edits."""
import threading
from nengine_bridge import NEngineBridge

_lock=threading.Lock()
_client=None
def background_bridge():
 global _client
 from nengine_adapter import bridge
 with _lock:
  if _client is None:
   primary=bridge()
   client=NEngineBridge(root=primary.root,state=primary.state/'readonly-background',read_only=True)
   try:client.discover()
   except Exception:client.close();raise
   _client=client
  return _client
