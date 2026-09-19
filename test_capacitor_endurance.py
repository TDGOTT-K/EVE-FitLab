import copy
import unittest
from unittest.mock import patch
from nengine_capacitor import attach_capacitor


class EnduranceQuery(unittest.TestCase):
    def run_query(self, results):
        report = {'native': {}, 'nativeFit': {'shipTypeId': 1}}
        requests = []
        def call(operation, body):
            self.assertEqual(operation, 'capacitor_scenario')
            requests.append(copy.deepcopy(body))
            result = results[len(requests)-1]
            if isinstance(result, Exception):
                raise result
            return {'result': result}
        with patch('nengine_capacitor.bridge') as bridge:
            bridge.return_value.call.side_effect = call
            attach_capacitor({}, report, [])
        return report['capacitorScenario'], requests

    def test_longer_native_query_and_replay_input(self):
        short = {'average': {'complete': True, 'stableFromFullInAverageModel': False}, 'firstFailedPaymentSeconds': None}
        long = {**short, 'firstFailedPaymentSeconds': 615}
        cap, requests = self.run_query([short, long])
        self.assertEqual([r['query']['horizonSeconds'] for r in requests], [300, 3600])
        self.assertEqual(cap['request'], requests[-1])
        self.assertEqual(cap['result']['firstFailedPaymentSeconds'], 615)

    def test_stable_partial_and_zero_second_failure_do_not_extend(self):
        for complete, stable, failed in [(True, True, None), (False, None, None), (True, False, 0)]:
            result = {'average': {'complete': complete, 'stableFromFullInAverageModel': stable}, 'firstFailedPaymentSeconds': failed}
            cap, requests = self.run_query([result])
            self.assertEqual(len(requests), 1)

    def test_extension_failure_keeps_short_result_and_reason(self):
        result = {'average': {'complete': True, 'stableFromFullInAverageModel': False}, 'firstFailedPaymentSeconds': None}
        cap, requests = self.run_query([result, ValueError('native budget reached')])
        self.assertEqual(cap['request'], requests[0])
        self.assertIsNone(cap['result']['firstFailedPaymentSeconds'])
        self.assertEqual(cap['extensionUnavailableReason'], 'native budget reached')


if __name__ == '__main__':
    unittest.main()
