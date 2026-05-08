"""
Python helper для эмуляции Warden модуля через unicorn.
Запускается как subprocess из C#, общается JSON over stdin/stdout — по строке на сообщение.

Протокол:
  > {"op": "ping"}                                                   → {"ok": true, "version": "2.1.4"}
  > {"op": "smoke"}                                                  → {"ok": true, "eax": "0xDEADBEEF"}
  > {"op": "open", "arch": "x86", "mode": 32}                        → {"ok": true, "uc_id": 0}
  > {"op": "map", "uc_id": 0, "addr": "0x400000", "size": "0x1000"}  → {"ok": true}
  > {"op": "write", "uc_id": 0, "addr": "0x400000", "data_b64": "..."} → {"ok": true}
  > {"op": "read", "uc_id": 0, "addr": "0x400000", "size": 16}       → {"ok": true, "data_b64": "..."}
  > {"op": "emu", "uc_id": 0, "begin": "...", "until": "...", "count": 0} → {"ok": true}
  > {"op": "reg_read", "uc_id": 0, "reg": "eax"}                     → {"ok": true, "value": "0x..."}
  > {"op": "reg_write", "uc_id": 0, "reg": "eax", "value": "0x..."}  → {"ok": true}
  > {"op": "close", "uc_id": 0}                                      → {"ok": true}

Все числа — hex-строки с префиксом 0x для удобной передачи (32+ bit).
"""

import sys
import json
import base64
import unicorn
from unicorn import Uc, UC_ARCH_X86, UC_MODE_32, UC_MODE_64
from unicorn.x86_const import (
    UC_X86_REG_EAX, UC_X86_REG_EBX, UC_X86_REG_ECX, UC_X86_REG_EDX,
    UC_X86_REG_ESI, UC_X86_REG_EDI, UC_X86_REG_EBP, UC_X86_REG_ESP,
    UC_X86_REG_EIP,
)

REGS = {
    "eax": UC_X86_REG_EAX, "ebx": UC_X86_REG_EBX, "ecx": UC_X86_REG_ECX,
    "edx": UC_X86_REG_EDX, "esi": UC_X86_REG_ESI, "edi": UC_X86_REG_EDI,
    "ebp": UC_X86_REG_EBP, "esp": UC_X86_REG_ESP, "eip": UC_X86_REG_EIP,
}

ARCHES = {"x86": UC_ARCH_X86}
MODES = {32: UC_MODE_32, 64: UC_MODE_64}

instances = {}
next_id = 0


def parse_int(v):
    if isinstance(v, int):
        return v
    s = str(v).strip()
    if s.startswith("0x") or s.startswith("0X"):
        return int(s, 16)
    return int(s)


def reply(**kwargs):
    sys.stdout.write(json.dumps(kwargs) + "\n")
    sys.stdout.flush()


def handle(msg):
    op = msg.get("op")
    if op == "ping":
        return {"ok": True, "version": ".".join(str(x) for x in unicorn.__version__) if hasattr(unicorn, "__version__") else "unknown"}

    if op == "smoke":
        # mov eax, 0xDEADBEEF; nop;
        addr = 0x1000
        code = b"\xb8\xef\xbe\xad\xde\x90"
        mu = Uc(UC_ARCH_X86, UC_MODE_32)
        mu.mem_map(addr, 0x1000)
        mu.mem_write(addr, code)
        mu.emu_start(addr, addr + len(code))
        eax = mu.reg_read(UC_X86_REG_EAX)
        return {"ok": True, "eax": f"0x{eax:08X}"}

    if op == "open":
        global next_id
        arch = ARCHES[msg.get("arch", "x86")]
        mode = MODES[int(msg.get("mode", 32))]
        mu = Uc(arch, mode)
        uc_id = next_id
        next_id += 1
        instances[uc_id] = mu
        return {"ok": True, "uc_id": uc_id}

    if op == "close":
        uc_id = int(msg["uc_id"])
        instances.pop(uc_id, None)
        return {"ok": True}

    uc_id = int(msg["uc_id"])
    mu = instances[uc_id]

    if op == "map":
        addr = parse_int(msg["addr"])
        size = parse_int(msg["size"])
        perms = parse_int(msg.get("perms", 7))
        mu.mem_map(addr, size, perms)
        return {"ok": True}

    if op == "write":
        addr = parse_int(msg["addr"])
        data = base64.b64decode(msg["data_b64"])
        mu.mem_write(addr, data)
        return {"ok": True}

    if op == "read":
        addr = parse_int(msg["addr"])
        size = int(msg["size"])
        data = mu.mem_read(addr, size)
        return {"ok": True, "data_b64": base64.b64encode(bytes(data)).decode("ascii")}

    if op == "emu":
        begin = parse_int(msg["begin"])
        until = parse_int(msg["until"])
        timeout = int(msg.get("timeout", 0))
        count = int(msg.get("count", 0))
        mu.emu_start(begin, until, timeout, count)
        return {"ok": True}

    if op == "reg_read":
        reg = REGS[msg["reg"].lower()]
        val = mu.reg_read(reg)
        return {"ok": True, "value": f"0x{val:08X}"}

    if op == "reg_write":
        reg = REGS[msg["reg"].lower()]
        val = parse_int(msg["value"])
        mu.reg_write(reg, val)
        return {"ok": True}

    return {"ok": False, "error": f"unknown op '{op}'"}


def main():
    sys.stderr.write(f"warden_emu_helper.py started, python {sys.version.split()[0]}\n")
    sys.stderr.flush()
    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        try:
            msg = json.loads(line)
            res = handle(msg)
        except Exception as ex:
            res = {"ok": False, "error": f"{type(ex).__name__}: {ex}"}
        reply(**res)


if __name__ == "__main__":
    main()
