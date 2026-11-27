"""Loopback-only fixture for the Windows UI test. No upstream connections."""

import base64
import hashlib
import json
import socket
import struct
import sys
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path


def save_result(name, data):
    path = output / name
    temp = path.with_suffix(".tmp")
    temp.write_text(json.dumps(data), encoding="utf-8")
    temp.replace(path)


def event(message_id, text):
    return {
        "post_type": "message",
        "message_type": "group",
        "message_id": message_id,
        "group_id": 100,
        "user_id": 2,
        "time": 1700000000 + message_id,
        "sender": {"user_id": 2, "nickname": "Fixture member"},
        "raw_message": text,
        "message": [{"type": "text", "data": {"text": text}}],
    }


class Adapter(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def authorized(self):
        if self.headers.get("Authorization") == "Bearer native-smoke-only":
            return True
        self.send_error(401)
        return False

    def do_POST(self):
        if not self.authorized():
            return
        params = json.loads(self.rfile.read(int(self.headers["Content-Length"])))
        if self.path == "/get_login_info":
            data = {"user_id": 1, "nickname": "Native fixture user"}
        elif self.path == "/get_group_list":
            data = [{
                "group_id": 100, "group_name": "Native fixture channel",
                "extra": {"oopz_area_id": "area-a", "oopz_channel_id": "channel-a"},
            }]
        elif self.path == "/get_latest_events":
            data = [event(10, "Native fixture cached")]
        elif self.path == "/send_group_msg":
            save_result("sent.json", params)
            data = {"message_id": 12}
        else:
            self.send_error(404)
            return
        body = json.dumps({"status": "ok", "retcode": 0, "data": data}).encode()
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        if not self.authorized():
            return
        key = self.headers.get("Sec-WebSocket-Key")
        if self.path != "/event" or not key or self.headers.get("Upgrade", "").lower() != "websocket":
            self.send_error(404)
            return
        accept = hashlib.sha1((key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11").encode("ascii")).digest()
        self.send_response(101)
        self.send_header("Connection", "Upgrade")
        self.send_header("Upgrade", "websocket")
        self.send_header("Sec-WebSocket-Accept", base64.b64encode(accept).decode("ascii"))
        self.end_headers()
        self.close_connection = True
        try:
            deadline = time.monotonic() + 35
            while not (output / "push-event").exists():
                if time.monotonic() >= deadline:
                    return
                time.sleep(0.1)
            body = json.dumps(event(11, "Native fixture incoming")).encode()
            self.wfile.write(b"\x81\x7e" + struct.pack("!H", len(body)) + body)
            self.connection.settimeout(60)
            # The test sends no application data; acknowledge the client close handshake.
            if self.rfile.read(1) == b"\x88":
                self.wfile.write(b"\x88\x00")
        except (ConnectionError, socket.timeout):
            pass


if __name__ == "__main__":
    output = Path(sys.argv[1])
    with ThreadingHTTPServer(("127.0.0.1", 0), Adapter) as server:
        save_result("ready.json", {"port": server.server_port})
        server.serve_forever()
