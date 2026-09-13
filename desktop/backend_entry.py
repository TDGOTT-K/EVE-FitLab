import os
from pathlib import Path
import server
from http.server import ThreadingHTTPServer
if __name__=='__main__':
 server.SOURCE=Path(os.environ['FITLAB_DEFAULT_STATE'])/'external-characters-disabled.json'
 host=ThreadingHTTPServer(('127.0.0.1',int(os.environ['FITLAB_API_PORT'])),server.Handler)
 host.serve_forever()
