import json
import os
from pathlib import Path
import subprocess
import tempfile
import time
import unittest

from agent_mcp import AgentMcp, TOOLS, error_payload
from agent_attributes import resolve


class SearchTests(unittest.TestCase):
    def test_name_resolution_prefers_internal_identity(self):
        rows = [{'attributeId': 48, 'name': 'cpuOutput', 'label': 'CPU', 'aliases': ['cpuOutput', 'CPU', '48']},
                {'attributeId': 50, 'name': 'cpu', 'label': 'CPU usage', 'aliases': ['cpu', 'CPU usage', '50']}]
        self.assertEqual(resolve(rows, ['cpu'])[0][0]['attributeId'], 50)
        self.assertEqual(resolve(rows, ['CPU'])[1][0]['state'], 'ambiguous')

    def test_search_public_chain(self):
        root = Path(os.environ['FITLAB_NENGINE_ROOT'])
        dll = Path(os.environ['FITLAB_AGENT_MCP_DLL'])
        baseline = Path(os.environ['FITLAB_AGENT_BASELINE'])
        with tempfile.TemporaryDirectory() as state:
            a = AgentMcp(root, state, dll)
            try:
                self.assertEqual(json.loads(Path('contracts/agent-mcp-v4/tools.json').read_text(encoding='utf-8'))['tools'], TOOLS)
                a.native('engine_status', {})
                query = {'text': 'Shield Booster', 'categoryId': 7, 'filters': [{'attribute': 'cpu', 'max': 100}, {'attribute': 'power', 'min': 1}],
                         'attributes': ['cpu', 'power'], 'sortBy': 'power', 'limit': 3}
                native = a.native
                calls = []
                def counted(name, args):
                    calls.append(name)
                    return native(name, args)
                a.native = counted
                start = time.perf_counter()
                first = a.call('fitlab_search', {'query': query})
                cold = time.perf_counter() - start
                self.assertEqual(calls, ['catalog_search'])
                start = time.perf_counter()
                warm = a.call('fitlab_search', {'query': query})
                warm_ms = 1000 * (time.perf_counter() - start)
                self.assertEqual(first, warm)
                seen = []
                result = first
                while True:
                    page = result.get('value') or a.load_result(result['resultId'])
                    for row in page['items']:
                        values = {v['attributeId']: v['value'] for v in page['values'][str(row['typeId'])]}
                        self.assertLessEqual(values[50], 100)
                        self.assertGreaterEqual(values[30], 1)
                        seen.append((values[30], row['typeId']))
                    if not result['nextCall']:
                        break
                    result = a.call(result['nextCall']['tool'], result['nextCall']['arguments'])
                self.assertEqual(len(seen), first['total'])
                self.assertEqual(seen, sorted(set(seen)))
                with self.assertRaises(Exception):
                    a.call('fitlab_search', {'query': {**first['nextCall']['arguments']['query'], 'descending': True}})
                discovery = a.call('fitlab_search', {'query': {'attributeSearch': 'CPU', 'limit': 1}})
                self.assertTrue(discovery['nextCall'])
                self.assertNotEqual(discovery['value']['attributes'], a.call(**{'name': discovery['nextCall']['tool'], 'args': discovery['nextCall']['arguments']})['value']['attributes'])
                self.assertTrue(a.call('fitlab_search', {'names': ['Shield']})['value'][0]['nextCall'])
                absent = a.call('fitlab_search', {'query': {'text': 'Small Shield Booster I', 'attributes': ['warpSpeedMultiplier']}})['value']
                self.assertTrue(all(v['value'] is None and v['state'] == 'missing' for values in absent['values'].values() for v in values))
                empty = a.call('fitlab_search', {'query': {'text': 'Small Shield Booster I', 'filters': [{'attribute': 'warpSpeedMultiplier', 'min': 0}]}})['value']
                self.assertEqual(empty['total'], 0)
                self.assertGreater(empty['excludedMissingAttributes'], 0)
                for invalid in [{'filters': [{'attribute': 'cpu', 'min': 2, 'max': 1}]}, {'attributes': ['invented-field']}, {'limit': 31}, {'attributeSearch': 'cpu', 'groupId': 40}]:
                    with self.assertRaises(Exception):
                        a.call('fitlab_search', {'query': invalid})
                batch = a.call('fitlab_item', {'typeIds': [399, 400, 2147483647], 'names': ['cpu', 'power']})
                rows = batch.get('value') or a.load_result(batch['resultId'])
                self.assertIn('result', rows[0]); self.assertIn('error', rows[2])
                # Explicit pagination must work even on small values.
                ref = a.store(list(range(5)))
                read = a.call('fitlab_result', {'resultId': ref, 'limit': 2})
                self.assertEqual(len(read['entries']), 2)
                self.assertEqual(a.call(read['nextCall']['tool'], read['nextCall']['arguments'])['entries'][0]['value'], 2)
                output = Path('output'); output.mkdir(exist_ok=True)
                request = output/'catalog-cli-query.json'; request.write_text(json.dumps(query), encoding='utf-8')
                cli_result = output/'catalog-cli-native.json'
                subprocess.run([str(root/'.tools/dotnet/dotnet.exe'), str(dll.parent/'NEngine.Cli.dll'), 'sde-catalog-search', '--data',
                                str(root/a.bridge.baseline['dataDirectory']), '--input', str(request.resolve()), '--out', str(cli_result.resolve())], check=True, capture_output=True)
                expected = a.native('catalog_search', {'query': query})
                self.assertEqual(json.loads(cli_result.read_text(encoding='utf-8')), expected)
                request.write_text(json.dumps({'query': query}), encoding='utf-8')
                facade_result = output/'catalog-cli-agent.json'
                subprocess.run(['python', 'agent_mcp.py', '--engine', str(root), '--state', state, '--mcp-dll', str(dll), '--baseline', str(baseline),
                                '--call', 'fitlab_search', '--arguments', str(request), '--out', str(facade_result)], check=True, capture_output=True)
                self.assertEqual(json.loads(facade_result.read_text(encoding='utf-8'))['result'], first)
                (output/'catalog-verification.json').write_text(json.dumps({'count': len(seen), 'coldMs': cold*1000, 'warmMs': warm_ms,
                    'nativeCliParity': True, 'facadeCliParity': True, 'nativeCallsPerQuery': 1}, indent=2), encoding='utf-8')
            finally:
                a.close()


if __name__ == '__main__':
    unittest.main()
