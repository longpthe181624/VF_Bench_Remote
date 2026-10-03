import unittest
from types import SimpleNamespace
from unittest.mock import Mock
from bench_agent import BenchAgent


class DatabaseAgentTests(unittest.TestCase):
    def test_reconnect_subscribes_and_refreshes_manifest(self):
        agent = BenchAgent.__new__(BenchAgent)
        agent.id = 'TEST-DB'
        agent.database = Mock()
        client = Mock()
        agent._khi_noi(client, None, None, 0)
        client.subscribe.assert_any_call('bench/+/TEST-DB/cmd', qos=1)
        client.subscribe.assert_any_call('bench/database/changed', qos=1)
        agent.database.start.assert_called_once()

    def test_database_notification_never_starts_or_deploys_test(self):
        agent = BenchAgent.__new__(BenchAgent)
        agent.database = Mock()
        agent._chay_test = Mock()
        agent._nhan_goi = Mock()
        agent._khi_co_lenh(None, None, SimpleNamespace(topic='bench/database/changed', payload=b'{"action":"status"}'))
        agent.database.notify_changed.assert_called_once()
        agent._chay_test.assert_not_called()
        agent._nhan_goi.assert_not_called()


if __name__ == '__main__':
    unittest.main()
