import os
import sys
from pathlib import Path
import fitlab
from agent_mcp import encoded
if __name__ == '__main__':
    base=Path(sys.executable).resolve().parent
    engine=base.parent/'nengine'
    os.environ.setdefault('FITLAB_NENGINE_ROOT',str(engine))
    os.environ.setdefault('FITLAB_NENGINE_STATE',str(Path(os.environ.get('LOCALAPPDATA',str(Path.home()))) / 'EVE-FitLab' / 'AgentData-v7'))
    os.environ.setdefault('FITLAB_AGENT_MCP_DLL',str(engine/'agent-runtime/NEngine.Mcp.dll'))
    os.environ.setdefault('FITLAB_AGENT_BASELINE',str(engine/'UI-CHARGE-FEEDBACK-BASELINE.json'))
    sys.stdin.reconfigure(encoding='utf-8');sys.stdout.reconfigure(encoding='utf-8')
    try:raise SystemExit(fitlab.main())
    except (ValueError,OSError) as e:
        print(encoded({'ok':False,'state':'error','issues':[{'code':'CLI_INPUT','message':str(e)}]}),file=sys.stderr)
        raise SystemExit(2)
