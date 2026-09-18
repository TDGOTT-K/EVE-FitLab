"""FitLab-owned, serialized MCP client for the pinned, independent NEngine copy.

No battle tools are exposed. Engine source and engine-owned state are never edited.
"""
import atexit
import json
import os
from pathlib import Path
import queue
import subprocess
import threading

DEFAULT_ROOT = Path(__file__).resolve().parent.parent / 'N号引擎-UI接入-0.190-r33'

class NEngineBridge:
    def __init__(self, root=None, state=None):
        self.root = Path(root or os.environ.get('FITLAB_NENGINE_ROOT', DEFAULT_ROOT)).resolve()
        if not (self.root / 'UI-BASELINE.json').is_file():
            raise ValueError('N 号引擎路径必须是带 UI-BASELINE.json 的独立副本')
        # Explicit current local derivative; original delivery manifest remains immutable.
        manifest=self.root/'UI-LOCAL-BASELINE.json'
        if not manifest.is_file():manifest=self.root/'UI-BASELINE.json'
        self.baseline = json.loads(manifest.read_text(encoding='utf-8-sig'))
        if not self.baseline.get('independentClone'):
            raise ValueError('拒绝连接非独立引擎副本')
        self.state = Path(state or os.environ.get('FITLAB_NENGINE_STATE',Path(__file__).resolve().parent / 'state/nengine-ui-local-r37')).resolve()
        self.lock = threading.RLock()
        self.process = None
        self.sequence = 0
        self.status = None
        atexit.register(self.close)

    def close(self):
        p, self.process = self.process, None
        if p and p.poll() is None:
            p.terminate()
            try: p.wait(timeout=5)
            except subprocess.TimeoutExpired: p.kill(); p.wait()
        if p:
            if p.stdin: p.stdin.close()
            if p.stdout: p.stdout.close()

    def _start(self):
        if self.process and self.process.poll() is None: return
        runtime = self.root / '.tools/dotnet'
        dll = self.root / 'src/NEngine.Mcp/bin/Debug/net10.0/NEngine.Mcp.dll'
        if not dll.is_file(): raise ValueError('N 号引擎副本缺少已构建的 MCP 宿主')
        self.state.mkdir(parents=True, exist_ok=True)
        self.process = subprocess.Popen([str(runtime/'dotnet.exe'), str(dll), '--data',
            str(self.root/self.baseline['dataDirectory']), '--state', str(self.state)],
            cwd=self.root, env={**os.environ, 'DOTNET_ROOT':str(runtime)},
            stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
            text=True, encoding='utf-8', bufsize=1,
            creationflags=getattr(subprocess,'CREATE_NO_WINDOW',0))
        self.responses = queue.Queue()
        def read(process, responses):
            try:
                for line in process.stdout:
                    try: responses.put(json.loads(line))
                    except json.JSONDecodeError: continue
            finally: responses.put({'_closed':True})
        threading.Thread(target=read,args=(self.process,self.responses),daemon=True).start()
        self._rpc('initialize',{'protocolVersion':'2025-03-26','capabilities':{},'clientInfo':{'name':'fitlab','version':'1'}})
        self.process.stdin.write(json.dumps({'jsonrpc':'2.0','method':'notifications/initialized'})+'\n')
        self.process.stdin.flush()

    def _rpc(self, method, params):
        self.sequence += 1
        ident = self.sequence
        self.process.stdin.write(json.dumps({'jsonrpc':'2.0','id':ident,'method':method,'params':params},ensure_ascii=False)+'\n')
        self.process.stdin.flush()
        while True:
            try: reply=self.responses.get(timeout=90)
            except queue.Empty:
                self.close(); raise ValueError('N 号引擎响应超时，装配草稿仍保留')
            if reply.get('_closed'): self.close(); raise ValueError('N 号引擎进程已退出')
            if reply.get('id')!=ident: continue
            if 'error' in reply: raise ValueError(str(reply['error'].get('message','NEngine RPC error')))
            return reply['result']

    def call(self, name, arguments=None):
        if name not in {'engine_status','catalog_search','catalog_item','catalog_type_details','catalog_variants','fit_analyze','fit_output_curves','fit_attributes','mutation_rule','mutation_roll','booster_plan_analyze','booster_plan_roll','booster_plan_verify','capacitor_scenario'}:
            raise ValueError('此适配层只开放静态装配和目录查询')
        with self.lock:
            self._start()
            result=self._rpc('tools/call',{'name':name,'arguments':arguments or {}})
            payload=result.get('structuredContent')
            if payload is None:
                content='\n'.join(x.get('text','') for x in result.get('content',[]) if x.get('type')=='text')
                try: payload=json.loads(content)
                except json.JSONDecodeError: payload={'message':content}
            if result.get('isError'): raise ValueError(json.dumps(payload,ensure_ascii=False))
            return payload

    def discover(self):
        with self.lock:
            if self.status is None:
                self.status=self.call('engine_status')['result']
                self.tools=self._rpc('tools/list',{})['tools']
                if self.baseline.get('toolCount') and len(self.tools)!=self.baseline['toolCount']:
                    self.status=None
                    raise ValueError('引擎工具清单与交付基线不一致')
                contract=self.status['publicContract']
                if (self.status['engineVersion']!=self.baseline['engineVersion'] or contract['revision']!=self.baseline['revision']
                    or (self.baseline.get('staticRule') and self.status['ruleVersion']!=self.baseline['staticRule'])
                    or self.status['source']['indexSha256']!=self.baseline['indexSha256']):
                    self.status=None
                    raise ValueError('引擎副本版本与锁定基线不一致，请先验收新契约')
            return self.status
