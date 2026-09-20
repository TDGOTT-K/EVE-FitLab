"""Install the maintained FitLab skill without modifying unrelated skills or prompts."""
import argparse
import json
from pathlib import Path
import shutil

root=Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser()
parser.add_argument('--codex-skills',type=Path)
parser.add_argument('--dsh-workspace',type=Path)
args=parser.parse_args()
if not args.codex_skills and not args.dsh_workspace:parser.error('Choose at least one installation target')
targets=[]
if args.codex_skills:targets.append(args.codex_skills/'fitlab-agent')
if args.dsh_workspace:targets.append(args.dsh_workspace/'.agents/skills/fitlab-agent')
for target in targets:
    shutil.copytree(root/'skills/fitlab-agent',target,dirs_exist_ok=True)
    (target/'references/connection.json').write_text(json.dumps({'cli':str(root/'fitlab.py'),'config':str(root/'agent-config.json')},indent=2)+'\n',encoding='utf-8')
    print(str(target.resolve()))
if args.dsh_workspace:
    destination=args.dsh_workspace/'FITLAB-AGENT-PROMPT.md'
    shutil.copyfile(root/'prompts/fitlab-agent-system.md',destination)
    instructions=args.dsh_workspace/'AGENTS.md'
    original=instructions.read_text(encoding='utf-8') if instructions.exists() else ''
    start='<!-- fitlab-agent:start -->';end='<!-- fitlab-agent:end -->'
    block=start+'''\nFor EVE data, fitting and research tasks, use the local fitlab-agent skill in .agents/skills/fitlab-agent/SKILL.md.
It routes simple facts, equipment discovery, fitted/curve analysis and combat independently. The task API is shared by MCP and CLI.
Host integration guidance: FITLAB-AGENT-PROMPT.md. These instructions apply to FitLab tasks, not unrelated work.
'''+end
    if start in original and end in original:original=original[:original.index(start)]+block+original[original.index(end)+len(end):]
    else:original=original.rstrip()+'\n\n'+block+'\n'
    instructions.write_text(original,encoding='utf-8')
