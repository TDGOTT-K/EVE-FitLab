"""FitLab task CLI and MCP server. Both dispatch through AgentApi and one registry."""
import argparse
import json
import os
import sys
from pathlib import Path

from agent_api import AgentApi
from agent_contract import OPS,tools,operation_for_tool
from agent_mcp import AgentMcp,encoded


def configured(args):
    path=Path(args.config) if args.config else Path(__file__).with_name('agent-config.json')
    config=json.loads(path.read_text(encoding='utf-8')) if path.exists() else {}
    def get(key,env):return getattr(args,key,None) or os.environ.get(env) or config.get(key)
    root=get('engine','FITLAB_NENGINE_ROOT');state=get('state','FITLAB_NENGINE_STATE')
    if not root or not state:raise ValueError('Configure engine/state in agent-config.json or --engine/--state. See docs/engine-v2/agent-platform-v5.md.')
    return AgentApi(AgentMcp(root,state,get('mcp_dll','FITLAB_AGENT_MCP_DLL'),get('baseline','FITLAB_AGENT_BASELINE')))


def serve(api):
    for line in sys.stdin:
        message=None
        try:
            message=json.loads(line)
            if 'id' not in message:continue
            method=message.get('method');p=message.get('params',{})
            if method=='initialize':result={'protocolVersion':p.get('protocolVersion','2025-03-26'),'serverInfo':{'name':'fitlab','version':'7.0'},'capabilities':{'tools':{}},
                'instructions':'FitLab v7 uses distinct operation tools named fitlab_DOMAIN_OPERATION (no action argument). Simple facts: fitlab_catalog_describe by official name. Research: catalog search/compare and fitting outputs/curves; combat only when needed. help overview routes tasks. Follow next tool/arguments. Results state distinguishes pending/partial/failed/complete. HP times are not interchangeable; damage application is not hit probability. A report is bound to experiment conditions, never to a different current fit. jobs compact previews storage cleanup. Do not delete engine files through a shell. Use raw only for advanced native capabilities.'}
            elif method=='ping':result={}
            elif method=='tools/list':result={'tools':tools()}
            elif method=='tools/call':
                name=p.get('name','');a=p.get('arguments',{})
                if not isinstance(a,dict):a={}
                operation=operation_for_tool(name)
                payload=api.call(*(operation or ('unknown','unknown')),a)
                result={'isError':not payload['ok'],'content':[{'type':'text','text':encoded(payload)}]}
            else:
                print(encoded({'jsonrpc':'2.0','id':message['id'],'error':{'code':-32601,'message':'Unknown method'}}),flush=True);continue
            print(encoded({'jsonrpc':'2.0','id':message['id'],'result':result}),flush=True)
        except Exception as e:
            print(encoded({'jsonrpc':'2.0','id':message.get('id') if isinstance(message,dict) else None,'error':{'code':-32603,'message':str(e)}}),flush=True)


def main(argv=None):
    parser=argparse.ArgumentParser(description='FitLab: source facts, equipment discovery, fitting analysis, curves and reproducible combat experiments.')
    for name in ('config','engine','state','mcp-dll','baseline'):parser.add_argument('--'+name)
    sub=parser.add_subparsers(dest='domain',required=True);sub.add_parser('mcp',help='Run stdio MCP server (no logging on stdout)')
    for domain,operations in OPS.items():
        actions=sub.add_parser(domain).add_subparsers(dest='action',required=True)
        for name,(description,schema) in operations.items():
            command=actions.add_parser(name,help=description,description=description)
            command.add_argument('--input',help='JSON file, or - for stdin');command.add_argument('--json',help='JSON object');command.add_argument('--out',help='Write JSON result to file')
            for key,spec in schema['properties'].items():
                command.add_argument('--'+key,dest='field_'+key,help=('Required. ' if key in schema['required'] else '')+str(spec.get('enum',spec.get('type'))))
    args=parser.parse_args(argv)
    # Help schemas and examples must be usable without a running engine.
    if args.domain=='help' and args.action in ('overview','operation'):
        class NoEngine:pass
        api=AgentApi(NoEngine())
    else:api=configured(args)
    try:
        if args.domain=='mcp':serve(api);return 0
        if args.input and args.json:raise ValueError('Choose --input or --json')
        values=json.loads(sys.stdin.read() if args.input=='-' else Path(args.input).read_text(encoding='utf-8')) if args.input else json.loads(args.json) if args.json else {}
        if not isinstance(values,dict):raise ValueError('Input must be a JSON object')
        for key,spec in OPS[args.domain][args.action][1]['properties'].items():
            raw=getattr(args,'field_'+key,None)
            if raw is None:continue
            if key in values:raise ValueError('Duplicate field '+key)
            values[key]=raw if spec.get('type')=='string' else json.loads(raw)
        payload=api.call(args.domain,args.action,values)
        # A stdio native host owns workers. Keep it alive while a CLI-started job runs.
        # Short CLI invocations must never silently cancel their own new background job.
        if args.domain=='battle' and args.action in ('run','start','resume'):
            try:
                while payload['ok'] and payload['state']=='pending':
                    print(encoded({'state':'pending','jobId':values['jobId']}),file=sys.stderr,flush=True)
                    payload=api.call('battle','wait',{'jobId':values['jobId'],'waitSeconds':30})
            except KeyboardInterrupt:
                api.call('battle','cancel',{'jobId':values['jobId']})
                payload=api.call('battle','wait',{'jobId':values['jobId'],'waitSeconds':60})
        output=encoded(payload)
        if args.out:Path(args.out).write_text(output,encoding='utf-8')
        else:print(output)
        return 0 if payload['ok'] and payload['state'] not in ('failed','error') else 2
    finally:
        if hasattr(api.core,'close'):api.close()


if __name__=='__main__':
    sys.stdin.reconfigure(encoding='utf-8');sys.stdout.reconfigure(encoding='utf-8')
    try:raise SystemExit(main())
    except (ValueError,OSError) as e:
        print(encoded({'apiVersion':'fitlab-agent-v7','ok':False,'state':'error','issues':[{'code':'CLI_INPUT','message':str(e)}]}),file=sys.stderr);raise SystemExit(2)
