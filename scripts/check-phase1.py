"""Repeatable static-product regression gate, not a claim of release readiness.

Run from any directory. Never edits the user's running library or launches battle jobs.
Each command's output and exit status are retained, including failures.
"""
import datetime,json,os,subprocess,sys,tempfile,time
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
PYTHON_TESTS=[
 'test_analysis_status','test_source_binding','test_read_cache','test_native_admission','test_module_activation',
 'test_nengine_integration','test_nengine_output','test_nengine_scenario','test_nengine_mutations',
 'test_native_inventory','test_native_edps','test_native_curves','test_native_capacitor',
 'test_native_attributes','test_native_booster_plan','test_native_catalog',
 'test_native_persistence','test_native_preview','test_native_sessions','test_native_edit_commands',
 'test_eft_import','test_valuation','test_abyssal_instances','test_loadout_plans','test_loadout_layout']
JS_TESTS=['test_analysis_status.mjs','test_native_share_code.mjs','test_fit_image_code.mjs',
 'test_native_edit_history.mjs','test_fit_save_controller.mjs','test_fitting_edit_snapshot.mjs',
 'test_native_attribute_detail.mjs','test_plan_attribute_inspection.mjs','test_crystal_stock.mjs','test_valuation_view.mjs']

def main():
 stamp=datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%SZ')
 output=ROOT/'output'/('phase1-gate-'+stamp);output.mkdir(parents=True)
 commands=[[sys.executable,'-m','unittest',*PYTHON_TESTS,'-v'],*(['node',test] for test in JS_TESTS),['node','scripts/check-ui.cjs']]
 results=[]
 with tempfile.TemporaryDirectory(prefix='fitlab-release-gate-') as directory:
  env={**os.environ,'FITLAB_STORAGE_CONFIG':str(Path(directory)/'storage.json'),'FITLAB_DEFAULT_STATE':str(Path(directory)/'ui'),'FITLAB_NENGINE_STATE':str(Path(directory)/'native')}
  for index,command in enumerate(commands):
   start=time.perf_counter()
   try:
    result=subprocess.run(command,cwd=ROOT,env=env,capture_output=True,timeout=300)
    code=result.returncode;log=result.stdout+result.stderr
   except subprocess.TimeoutExpired as error:code=124;log=(error.stdout or b'')+(error.stderr or b'')+b'\nTIMEOUT'
   (output/f'{index:02d}.log').write_bytes(log)
   results.append({'command':command,'exitCode':code,'seconds':round(time.perf_counter()-start,3),'log':f'{index:02d}.log'})
   print(('PASS' if code==0 else 'FAIL')+' '+('Python static suites' if index==0 else command[-1]),flush=True)
 head=subprocess.run(['git','rev-parse','HEAD'],cwd=ROOT,capture_output=True,text=True,check=True).stdout.strip()
 summary={'startedAtUtc':stamp,'baseCommit':head,'testsPassed':all(r['exitCode']==0 for r in results),'releaseReady':False,
  'releaseBlockers':'docs/engine-v2/phase1-release.md','results':results}
 (output/'result.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2),encoding='utf8')
 print(output/'result.json')
 return 0 if summary['testsPassed'] else 1

if __name__=='__main__':sys.exit(main())
