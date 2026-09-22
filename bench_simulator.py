#!/usr/bin/env python3
"""
Bench Console — bench gia lap.

Gia vo lam nhieu bench that: moi bench mot ket noi MQTT rieng, co Last Will
rieng, day telemetry dinh ky, nhan lenh va tra ack/result. Giao thuc giong het
agent that se dung, nen backend khong phan biet duoc that hay gia.

Chay:
    pip install paho-mqtt
    python bench_simulator.py                 # 5 bench, broker localhost
    python bench_simulator.py --chaos         # thinh thoang rot mang de test trang thai offline
    python bench_simulator.py --host 10.0.0.5 --port 1883
"""

import argparse
import json
import random
import signal
import sys
import threading
import time
from datetime import datetime, timezone

import paho.mqtt.client as mqtt


# --------------------------------------------------------------------------
# Danh muc bench gia lap. Co y dat ma khong lien tuc, giong danh muc that.
# --------------------------------------------------------------------------
BENCHES = [
    {
        "id": "HIL-A02", "model": "vf6", "location": "Xuong 2 - Rack B1", "fw": "2.14.1",
        "channels": {
            "T_chamber":  {"base": 28.5, "drift": 0.25, "min": 18.0, "max": 65.0, "unit": "C"},
            "RH_chamber": {"base": 45.0, "drift": 0.60, "min": 20.0, "max": 90.0, "unit": "%"},
            "V_supply":   {"base": 12.1, "drift": 0.05, "min": 11.0, "max": 14.0, "unit": "V"},
        },
    },
    {
        "id": "HIL-A05", "model": "vf6", "location": "Xuong 2 - Rack B4", "fw": "2.14.1",
        "channels": {
            "T_chamber":  {"base": 30.0, "drift": 0.30, "min": 18.0, "max": 65.0, "unit": "C"},
            "RH_chamber": {"base": 42.0, "drift": 0.60, "min": 20.0, "max": 90.0, "unit": "%"},
            "V_supply":   {"base": 12.0, "drift": 0.05, "min": 11.0, "max": 14.0, "unit": "V"},
        },
    },
    {
        "id": "HIL-A07", "model": "vf6", "location": "Xuong 2 - Rack B3", "fw": "2.14.1",
        "channels": {
            "T_chamber":  {"base": 27.8, "drift": 0.22, "min": 18.0, "max": 65.0, "unit": "C"},
            "V_supply":   {"base": 12.2, "drift": 0.05, "min": 11.0, "max": 14.0, "unit": "V"},
        },
    },
    {
        "id": "EOL-B04", "model": "vf9", "location": "Xuong 3 - Rack A2", "fw": "2.13.0",
        "channels": {
            "P_line": {"base": 1.45, "drift": 0.04, "min": 0.8, "max": 2.5, "unit": "bar"},
            "Accel_g": {"base": 0.03, "drift": 0.02, "min": 0.0, "max": 1.2, "unit": "g"},
            "V_supply": {"base": 12.0, "drift": 0.05, "min": 11.0, "max": 14.0, "unit": "V"},
        },
    },
    {
        "id": "EOL-B09", "model": "vf9", "location": "Xuong 3 - Rack C1", "fw": "2.13.0",
        "channels": {
            "P_line": {"base": 1.30, "drift": 0.05, "min": 0.8, "max": 2.5, "unit": "bar"},
            "Accel_g": {"base": 0.02, "drift": 0.02, "min": 0.0, "max": 1.2, "unit": "g"},
        },
    },
]

TEST_CASES = {
    "warning_brake":   {"name": "Canh bao den phanh",        "steps": 3, "step_s": 6},
    "warning_door":    {"name": "Canh bao cua chua dong",    "steps": 3, "step_s": 5},
    "thermal_cycle":   {"name": "Chu ky nhiet cao/thap",     "steps": 5, "step_s": 12},
    "chassis_vibe":    {"name": "Rung khung gam cao toc",    "steps": 2, "step_s": 20},
    "tyre_pressure":   {"name": "Canh bao ap suat lop",      "steps": 3, "step_s": 8},
}

FAIL_RATE = 0.15        # ty le test fail, de UI co ca PASS lan FAIL
SENSOR_DROP_RATE = 0.04  # ty le mot phien chay bi mat tin hieu cam bien giua chung


def now_iso() -> str:
    return datetime.now(timezone.utc).astimezone().isoformat(timespec="seconds")


def _make_client(client_id: str) -> mqtt.Client:
    """Ho tro ca paho-mqtt 1.x lan 2.x (2.x doi chu ky callback)."""
    try:
        return mqtt.Client(mqtt.CallbackAPIVersion.VERSION2,
                           client_id=client_id, clean_session=True)
    except AttributeError:          # paho-mqtt 1.x
        return mqtt.Client(client_id=client_id, clean_session=True)


class SimulatedBench:
    """Mot bench gia lap = mot ket noi MQTT doc lap, giong agent that."""

    def __init__(self, spec: dict, host: str, port: int, chaos: bool):
        self.spec = spec
        self.id = spec["id"]
        self.model = spec["model"]
        self.prefix = f"bench/{self.model}/{self.id}"
        self.host, self.port, self.chaos = host, port, chaos

        # Gia tri cam bien hien tai, di chuyen dan theo random walk
        self.values = {ch: cfg["base"] for ch, cfg in spec["channels"].items()}

        self.state = "idle"          # idle | running | error
        self.current_test = None
        self.sensors_alive = True
        self.stop_flag = threading.Event()

        self.client = _make_client(f"bench-{self.id}")
        self.client.on_connect = self._on_connect
        self.client.on_message = self._on_message

        # Last Will: broker tu phat cai nay khi agent chet hoac dut mang.
        # Day chinh la thu tao ra trang thai "Mat ket noi" tren giao dien.
        self.client.will_set(
            f"{self.prefix}/status",
            json.dumps({"state": "offline", "ts": None}),
            qos=1, retain=True,
        )

    # ---------------- MQTT callbacks ----------------

    def _on_connect(self, client, userdata, flags, rc, properties=None):
        # paho 1.x truyen rc la int, 2.x truyen ReasonCode
        failed = getattr(rc, "is_failure", None)
        if failed is None:
            failed = rc != 0
        if failed:
            print(f"[{self.id}] khong ket noi duoc: {rc}")
            return
        client.subscribe(f"{self.prefix}/cmd", qos=1)
        self._publish_status("idle")
        print(f"[{self.id}] da ket noi, nghe {self.prefix}/cmd")

    def _on_message(self, client, userdata, msg):
        try:
            cmd = json.loads(msg.payload.decode())
        except json.JSONDecodeError:
            print(f"[{self.id}] lenh khong phai JSON, bo qua")
            return

        cmd_id = cmd.get("cmd_id", "?")
        action = cmd.get("action")

        # Agent that cung kiem tra dieu kien truoc khi nhan lenh
        if action == "start_test" and self.state == "running":
            self._publish("ack", {"cmd_id": cmd_id, "status": "rejected",
                                  "reason": "bench dang ban"})
            return

        self._publish("ack", {"cmd_id": cmd_id, "status": "accepted"})

        if action == "start_test":
            threading.Thread(target=self._run_test, args=(cmd,), daemon=True).start()
        elif action == "stop":
            self.state = "idle"
            self.current_test = None
            self._publish_status("idle")
        elif action == "reset_bench":
            self.sensors_alive = True
            self.state = "idle"
            self.current_test = None
            self.values = {ch: cfg["base"] for ch, cfg in self.spec["channels"].items()}
            self._publish_status("idle")

    # ---------------- publish helpers ----------------

    def _publish(self, leaf: str, payload: dict, retain: bool = False):
        payload.setdefault("ts", now_iso())
        self.client.publish(f"{self.prefix}/{leaf}", json.dumps(payload),
                            qos=1, retain=retain)

    def _publish_status(self, state: str, **extra):
        self.state = state
        self._publish("status", {"state": state, **extra}, retain=True)

    # ---------------- vong doi ----------------

    def _run_test(self, cmd: dict):
        tc_key = cmd.get("test_case", "warning_brake")
        tc = TEST_CASES.get(tc_key, TEST_CASES["warning_brake"])
        cmd_id = cmd.get("cmd_id", "?")
        plan = cmd.get("plan")

        self.current_test = {"key": tc_key, "name": tc["name"], "step": 1,
                             "total": tc["steps"], "plan": plan}
        self._publish_status("running", test_case=tc_key, plan=plan)
        started = time.time()

        for step in range(1, tc["steps"] + 1):
            if self.stop_flag.is_set() or self.state != "running":
                return
            self.current_test["step"] = step

            # Thinh thoang gia lap mat tin hieu cam bien giua chung phien chay
            if random.random() < SENSOR_DROP_RATE:
                self.sensors_alive = False
                self.state = "error"
                self._publish_status(
                    "error", test_case=tc_key, step=f"{step}/{tc['steps']}",
                    error="sensor_timeout",
                    detail=f"CAN timeout o step {step}/{tc['steps']} - sensor_door khong phan hoi",
                )
                self._publish("result", {"cmd_id": cmd_id, "test_case": tc_key,
                                         "verdict": "fail", "reason": "sensor_timeout",
                                         "duration_s": round(time.time() - started, 1)})
                self.current_test = None
                print(f"[{self.id}] LOI sensor_timeout o step {step}")
                return

            time.sleep(tc["step_s"])

        verdict = "fail" if random.random() < FAIL_RATE else "pass"
        self._publish("result", {
            "cmd_id": cmd_id, "test_case": tc_key, "plan": plan,
            "verdict": verdict,
            "duration_s": round(time.time() - started, 1),
            "detail": {"lamps_expected": 3, "lamps_detected": 3 if verdict == "pass" else 2},
        })
        self.current_test = None
        self._publish_status("idle")
        print(f"[{self.id}] {tc['name']} -> {verdict.upper()}")

    def _tick_sensors(self):
        """Random walk quanh gia tri nen — giong so lieu that hon la hinh sin."""
        for ch, cfg in self.spec["channels"].items():
            v = self.values[ch] + random.gauss(0, cfg["drift"])
            # keo nhe ve gia tri nen de khong troi vo han
            v += (cfg["base"] - v) * 0.05
            if self.state == "running" and ch.startswith("T_"):
                v += 0.15   # dang chay test thi nhiet do nhich len
            self.values[ch] = max(cfg["min"], min(cfg["max"], v))

    def _publish_telemetry(self):
        if not self.sensors_alive:
            return
        payload = {}
        for ch, v in self.values.items():
            payload[ch] = round(v, 2 if abs(v) < 10 else 1)
        if self.current_test:
            payload["test_case"] = self.current_test["key"]
            payload["step"] = f"{self.current_test['step']}/{self.current_test['total']}"
        self._publish("telemetry", payload)

    def run(self):
        self.client.connect(self.host, self.port, keepalive=30)
        self.client.loop_start()

        next_chaos = time.time() + random.uniform(90, 240)
        try:
            while not self.stop_flag.is_set():
                self._tick_sensors()
                self._publish_telemetry()

                # Che do chaos: rot ket noi dot ngot de test trang thai "Mat ket noi".
                # Khong gui status offline — de broker tu phat Last Will, dung nhu that.
                if self.chaos and time.time() > next_chaos:
                    down = random.uniform(20, 45)
                    print(f"[{self.id}] chaos: rot mang {down:.0f}s")
                    self.client.loop_stop()
                    self.client.disconnect()
                    time.sleep(down)
                    self.client.reconnect()
                    self.client.loop_start()
                    next_chaos = time.time() + random.uniform(120, 300)

                self.stop_flag.wait(5)
        finally:
            self._publish_status("offline")
            self.client.loop_stop()
            self.client.disconnect()

    def stop(self):
        self.stop_flag.set()


def main():
    ap = argparse.ArgumentParser(description="Bench gia lap cho Bench Console")
    ap.add_argument("--host", default="localhost")
    ap.add_argument("--port", type=int, default=1883)
    ap.add_argument("--count", type=int, default=len(BENCHES),
                    help="so bench gia lap (toi da %d)" % len(BENCHES))
    ap.add_argument("--chaos", action="store_true",
                    help="thinh thoang rot ket noi de test trang thai offline")
    args = ap.parse_args()

    benches = [SimulatedBench(spec, args.host, args.port, args.chaos)
               for spec in BENCHES[: args.count]]

    threads = [threading.Thread(target=b.run, daemon=True) for b in benches]
    for t in threads:
        t.start()

    def shutdown(signum, frame):
        print("\nDang dung...")
        for b in benches:
            b.stop()
        for t in threads:
            t.join(timeout=3)
        sys.exit(0)

    signal.signal(signal.SIGINT, shutdown)
    signal.signal(signal.SIGTERM, shutdown)

    print(f"Dang gia lap {len(benches)} bench toi {args.host}:{args.port}")
    print("Ctrl+C de dung.\n")
    while True:
        time.sleep(1)


if __name__ == "__main__":
    main()
