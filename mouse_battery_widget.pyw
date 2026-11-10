"""로지텍 G HUB 마우스 배터리 위젯.

G HUB 로컬 API(ws://localhost:9010)에 연결해 배터리 잔량·충전 여부·예상 사용 시간을
실시간으로 받는다. 배터리 변경 이벤트를 구독하고, 놓치는 경우를 대비해 3초마다 직접 조회한다.
API에 연결할 수 없으면 %LOCALAPPDATA%\\LGHUB\\settings.db 의 마지막 저장값을 보여준다.

- 드래그: 위치 이동 (위치는 자동 저장)
- 우클릭: 새로고침 / 항상 위 / 종료
"""
import asyncio
import json
import os
import pathlib
import queue
import sqlite3
import threading
import tkinter as tk
from datetime import datetime, timezone

import websockets

WS_URL = "ws://localhost:9010"
DB_PATH = pathlib.Path(os.environ["LOCALAPPDATA"]) / "LGHUB" / "settings.db"
CONF_PATH = pathlib.Path(__file__).with_name("widget_config.json")
POLL_SEC = 3          # 실시간 API 조회 간격
RECONNECT_SEC = 5     # G HUB 연결이 끊겼을 때 재시도 간격
DB_REFRESH_MS = 30_000

BG = "#1e1f24"
FG = "#e8e8ec"
SUB = "#8a8c96"
TRACK = "#33353d"
CHARGE = "#42a5f5"


def level_color(pct: int) -> str:
    if pct <= 15:
        return "#ef5350"
    if pct <= 35:
        return "#ffb300"
    return "#4caf50"


# ---------------------------------------------------------------------------
# 데이터 소스 1: G HUB 로컬 웹소켓 API (실시간, 충전 상태 포함)
# ---------------------------------------------------------------------------
class GHubClient(threading.Thread):
    """백그라운드에서 G HUB에 연결해 상태를 out_q 로 보낸다.

    out_q 메시지: ("devices", {id: {name, pct, charging, full, hours}})
                  ("offline", 에러문자열)
    """

    def __init__(self, out_q: queue.Queue):
        super().__init__(daemon=True)
        self.q = out_q
        self.names = {}       # deviceId -> 표시 이름
        self.state = {}       # deviceId -> 배터리 상태
        self.kick = None      # 새로고침 요청용 asyncio.Event

    def run(self):
        asyncio.run(self.main())

    def request_refresh(self):
        if self.kick and self.loop:
            self.loop.call_soon_threadsafe(self.kick.set)

    async def main(self):
        self.loop = asyncio.get_running_loop()
        self.kick = asyncio.Event()
        while True:
            try:
                await self.session()
            except Exception as e:  # 연결 실패/끊김
                self.q.put(("offline", f"{type(e).__name__}: {e}"))
            await asyncio.sleep(RECONNECT_SEC)

    async def session(self):
        async with websockets.connect(
            WS_URL, subprotocols=["json"], origin="file://", max_size=None, open_timeout=5
        ) as ws:
            async def send(verb, path, mid=""):
                await ws.send(json.dumps({"msgId": mid, "verb": verb, "path": path}))

            await send("SUBSCRIBE", "/battery/state/changed")
            await send("SUBSCRIBE", "/devices/state/changed")
            await send("GET", "/devices/list", "list")

            async def poller():
                while True:
                    try:
                        await asyncio.wait_for(self.kick.wait(), POLL_SEC)
                    except asyncio.TimeoutError:
                        pass
                    self.kick.clear()
                    for dev_id in list(self.names):
                        await send("GET", f"/battery/{dev_id}/state", "bat")

            poll_task = asyncio.create_task(poller())
            try:
                async for raw in ws:
                    self.handle(json.loads(raw))
                    if self._need_list:
                        self._need_list = False
                        await send("GET", "/devices/list", "list")
            finally:
                poll_task.cancel()

    _need_list = False

    def handle(self, msg):
        path = msg.get("path", "")
        payload = msg.get("payload") or {}
        if path == "/devices/list":
            names = {}
            for d in payload.get("deviceInfos", []):
                if d.get("hasWirelessInterface") and d.get("state") == "ACTIVE":
                    names[d["id"]] = d.get("displayName") or d.get("deviceBaseModel") or d["id"]
            self.names = names
            self.state = {k: v for k, v in self.state.items() if k in names}
            self.request_refresh()
        elif path == "/devices/state/changed":
            self._need_list = True
        elif payload.get("@type", "").endswith("wireless.Battery"):
            dev_id = payload.get("deviceId")
            if dev_id not in self.names:
                self._need_list = True
                return
            if payload.get("batteryStateOfChargeSupport") == "STATE_OF_CHARGE_UNSUPPORTED":
                return
            self.state[dev_id] = {
                "name": self.names[dev_id],
                "pct": int(payload.get("percentage", 0)),
                "charging": bool(payload.get("charging")),
                "full": bool(payload.get("fullyCharged")),
                "hours": payload.get("mileage"),
            }
            self.q.put(("devices", dict(self.state)))


# ---------------------------------------------------------------------------
# 데이터 소스 2: settings.db (G HUB API를 못 쓸 때의 대체용, 충전 여부 없음)
# ---------------------------------------------------------------------------
def read_db_batteries():
    con = sqlite3.connect(DB_PATH.as_uri() + "?mode=ro", uri=True, timeout=2)
    try:
        row = con.execute("select file from data order by _id desc limit 1").fetchone()
    finally:
        con.close()
    data = json.loads(row[0])
    out = []
    for k, v in data.items():
        parts = k.split("/")
        if len(parts) == 3 and parts[0] == "battery" and parts[2] == "percentage":
            t = None
            if isinstance(v, dict) and v.get("time"):
                try:
                    t = datetime.fromisoformat(v["time"].replace("Z", "+00:00"))
                except ValueError:
                    pass
            out.append((parts[1].upper(), int(v.get("percentage", 0)), t))
    return out


def load_conf():
    try:
        return json.loads(CONF_PATH.read_text(encoding="utf-8"))
    except Exception:
        return {}


def save_conf(conf):
    try:
        CONF_PATH.write_text(json.dumps(conf), encoding="utf-8")
    except Exception:
        pass


# ---------------------------------------------------------------------------
# UI
# ---------------------------------------------------------------------------
class Widget:
    def __init__(self):
        self.conf = load_conf()
        self.root = tk.Tk()
        self.root.title("Mouse Battery")
        self.root.overrideredirect(True)
        self.root.configure(bg=BG)
        self.root.attributes("-alpha", 0.93)
        self.topmost = tk.BooleanVar(value=self.conf.get("topmost", True))
        self.root.attributes("-topmost", self.topmost.get())
        self.root.geometry(f"+{self.conf.get('x', 40)}+{self.conf.get('y', 40)}")

        self.frame = tk.Frame(self.root, bg=BG, padx=14, pady=10)
        self.frame.pack(fill="both", expand=True)

        self.menu = tk.Menu(self.root, tearoff=0)
        self.menu.add_command(label="새로고침", command=self.manual_refresh)
        self.menu.add_checkbutton(label="항상 위", variable=self.topmost, command=self.toggle_top)
        self.menu.add_separator()
        self.menu.add_command(label="종료", command=self.root.destroy)

        self.root.bind("<ButtonPress-1>", self.start_drag)
        self.root.bind("<B1-Motion>", self.on_drag)
        self.root.bind("<ButtonRelease-1>", self.end_drag)
        self.root.bind("<Button-3>", lambda e: self.menu.tk_popup(e.x_root, e.y_root))

        self.q = queue.Queue()
        self.live = False          # 실시간 API로 받고 있는지
        self.last_render = None
        self.db_job = None
        self.client = GHubClient(self.q)
        self.client.start()

        self.render_db()           # 연결 전까지는 DB 값 표시
        self.root.after(200, self.pump)

    # ---- data flow -----------------------------------------------------
    def pump(self):
        try:
            while True:
                kind, data = self.q.get_nowait()
                if kind == "devices":
                    self.live = True
                    self.cancel_db()
                    self.render_live(data)
                elif kind == "offline" and self.live:
                    self.live = False
                    self.render_db()
        except queue.Empty:
            pass
        self.root.after(200, self.pump)

    def manual_refresh(self):
        if self.live:
            self.client.request_refresh()
        else:
            self.render_db()

    def cancel_db(self):
        if self.db_job:
            self.root.after_cancel(self.db_job)
            self.db_job = None

    # ---- drawing -------------------------------------------------------
    def clear(self):
        for w in self.frame.winfo_children():
            w.destroy()

    def label(self, parent, text, size=10, color=FG, bold=False):
        font = ("Segoe UI", size, "bold" if bold else "normal")
        return tk.Label(parent, text=text, bg=BG, fg=color, font=font)

    def draw_device(self, name, pct, status, status_color, bar_color):
        row = tk.Frame(self.frame, bg=BG)
        row.pack(fill="x", pady=2)

        top = tk.Frame(row, bg=BG)
        top.pack(fill="x")
        self.label(top, "🖱  " + name, 9, SUB).pack(side="left", padx=(0, 12))
        self.label(top, f"{pct}%", 16, bar_color, bold=True).pack(side="right")

        bar = tk.Canvas(row, width=210, height=8, bg=BG, highlightthickness=0)
        bar.pack(fill="x", pady=(4, 2))
        bar.create_rectangle(0, 0, 210, 8, fill=TRACK, width=0)
        bar.create_rectangle(0, 0, max(2, int(210 * pct / 100)), 8, fill=bar_color, width=0)

        self.label(row, status, 8, status_color).pack(anchor="w")

    def render_live(self, devices):
        key = json.dumps(devices, sort_keys=True)
        if key == self.last_render:
            return
        self.last_render = key
        self.clear()
        if not devices:
            self.label(self.frame, "연결된 무선 기기가 없어요", 9, SUB).pack()
            return
        for d in devices.values():
            pct = d["pct"]
            if d["full"]:
                status, sc, bc = "✔ 충전 완료", CHARGE, CHARGE
            elif d["charging"]:
                status, sc, bc = "⚡ 충전 중", CHARGE, CHARGE
            else:
                status, sc, bc = "배터리 사용 중", SUB, level_color(pct)
                if isinstance(d.get("hours"), (int, float)) and d["hours"] > 0:
                    status += f" · 약 {d['hours']:.0f}시간 남음"
            self.draw_device(d["name"], pct, status, sc, bc)

    def render_db(self):
        self.last_render = None
        self.clear()
        try:
            devices = read_db_batteries()
            if not devices:
                self.label(self.frame, "배터리 정보가 있는 기기가 없어요", 9, SUB).pack()
            for name, pct, t in devices:
                when = ""
                if t:
                    mins = int((datetime.now(timezone.utc) - t).total_seconds() // 60)
                    when = " · " + ("방금" if mins < 1 else f"{mins}분 전" if mins < 60
                                    else t.astimezone().strftime("%m/%d %H:%M"))
                self.draw_device(name, pct, f"G HUB 연결 대기 중 (저장값{when})", SUB, level_color(pct))
        except Exception as e:
            self.label(self.frame, "G HUB 데이터를 읽지 못했어요", 9, "#ef5350").pack(anchor="w")
            self.label(self.frame, str(e)[:60], 8, SUB).pack(anchor="w")
        self.cancel_db()
        self.db_job = self.root.after(DB_REFRESH_MS, self.render_db)

    # ---- interaction ---------------------------------------------------
    def start_drag(self, e):
        self._dx, self._dy = e.x_root - self.root.winfo_x(), e.y_root - self.root.winfo_y()

    def on_drag(self, e):
        self.root.geometry(f"+{e.x_root - self._dx}+{e.y_root - self._dy}")

    def end_drag(self, _e):
        self.conf.update(x=self.root.winfo_x(), y=self.root.winfo_y())
        save_conf(self.conf)

    def toggle_top(self):
        self.root.attributes("-topmost", self.topmost.get())
        self.conf["topmost"] = self.topmost.get()
        save_conf(self.conf)

    def run(self):
        self.root.mainloop()


if __name__ == "__main__":
    Widget().run()
