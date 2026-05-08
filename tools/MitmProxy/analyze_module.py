"""
Reverse engineering helper for WoWCircle Warden module.
Phase 2 — static analysis without IDA.
"""

import struct
import sys
from pathlib import Path

MODULE = Path(__file__).parent / "warden_module.bin"

def main():
    data = MODULE.read_bytes()
    print(f"Loaded {len(data)} bytes from {MODULE.name}\n")

    # Header analysis — looking for TC WardenModuleHeader structure
    print("=" * 60)
    print("HEADER (interpret as series of LE uint32)")
    print("=" * 60)

    # Common TC Warden module header structure (from public reversing notes):
    # Offset 0: total module size or virtual size
    # Offset 4: code section RVA
    # Offset 8: code section size
    # Offset C: data section RVA
    # Offset 10: data section size
    # Offset 14: relocs/imports section RVA
    # ...

    # But this is custom, let me just enumerate
    print(f"  [+0x00] {struct.unpack('<I', data[0:4])[0]:#010x}  ({struct.unpack('<I', data[0:4])[0]:>6})")
    print(f"  [+0x04] {struct.unpack('<I', data[4:8])[0]:#010x}  ({struct.unpack('<I', data[4:8])[0]:>6})")
    print(f"  [+0x08] {struct.unpack('<I', data[8:12])[0]:#010x}  ({struct.unpack('<I', data[8:12])[0]:>6})")
    print(f"  [+0x0C] {struct.unpack('<I', data[12:16])[0]:#010x}  ({struct.unpack('<I', data[12:16])[0]:>6})")
    print(f"  [+0x10] {struct.unpack('<I', data[16:20])[0]:#010x}  ({struct.unpack('<I', data[16:20])[0]:>6})")

    # Theory: there are several "section descriptors" each with (RVA, size).
    # 0x00-0x04: 0xB000, 0x5B35  -> section A at RVA 0xB000 with size 0x5B35 (23349)
    # 0x08-0x0C: 0xA000, 0x14F   -> section B at RVA 0xA000 with size 0x14F (335)  ← imports?
    # 0x10-0x14: 0x8788, 0x1     -> section C at RVA 0x8788 with size... uses different format
    #
    # Hmm, doesn't quite fit. Let me try alternative: sequence of (offset, count) pairs.

    sz = len(data)
    print(f"\nFile size: {sz} ({sz:#x}) bytes")
    print()

    # Look for embedded x86 code starts: function prologue 'push ebp; mov ebp, esp'
    # = bytes 0x55 0x8B 0xEC
    print("=" * 60)
    print("FUNCTION PROLOGUES (push ebp; mov ebp, esp = 55 8B EC)")
    print("=" * 60)
    prologues = []
    for i in range(len(data) - 3):
        if data[i] == 0x55 and data[i+1] == 0x8B and data[i+2] == 0xEC:
            prologues.append(i)
    print(f"Found {len(prologues)} candidate function starts")
    print(f"First 20 offsets: {[hex(o) for o in prologues[:20]]}")
    print()

    # Look for RC4 KSA pattern. Classic init loop:
    #   mov ecx, 0
    #   loop1:
    #     mov [ebx+ecx], cl  ; or similar
    #     inc ecx
    #     cmp ecx, 256
    #     jl loop1
    # Hard to fingerprint exactly, but the constant 0x100 (256) appears nearby.
    # Look for byte 0x00 0x01 0x00 0x00 (cmp ecx, 100h pattern).
    print("=" * 60)
    print("Candidates for 256-byte loop (cmp ecx, 0x100)")
    print("=" * 60)
    pat_cmp_256 = bytes([0x81, 0xF9, 0x00, 0x01, 0x00, 0x00])  # cmp ecx, 100h
    pat_cmp_256_short = bytes([0x83, 0xF9, 0x00])  # cmp ecx, 0 - too generic
    offs = []
    i = 0
    while i < len(data) - 6:
        if data[i:i+6] == pat_cmp_256:
            offs.append(i)
        i += 1
    print(f"Found cmp ecx, 0x100 at: {[hex(o) for o in offs[:20]]}")

    # Also look for the alternate form: cmp eax, 100h or cmp edx, 100h
    print()

    # Look for SHA1 magic constants
    # SHA1 uses constants: 0x5A827999, 0x6ED9EBA1, 0x8F1BBCDC, 0xCA62C1D6
    # Initial H values: 0x67452301, 0xEFCDAB89, 0x98BADCFE, 0x10325476, 0xC3D2E1F0
    print("=" * 60)
    print("SHA1 CONSTANT REFERENCES")
    print("=" * 60)
    sha_constants = {
        b'\x99\x79\x82\x5A': 'K0 = 0x5A827999',
        b'\xA1\xEB\xD9\x6E': 'K1 = 0x6ED9EBA1',
        b'\xDC\xBC\x1B\x8F': 'K2 = 0x8F1BBCDC',
        b'\xD6\xC1\x62\xCA': 'K3 = 0xCA62C1D6',
        b'\x01\x23\x45\x67': 'H0 = 0x67452301',
        b'\x89\xAB\xCD\xEF': 'H1 = 0xEFCDAB89',
        b'\xFE\xDC\xBA\x98': 'H2 = 0x98BADCFE',
        b'\x76\x54\x32\x10': 'H3 = 0x10325476',
        b'\xF0\xE1\xD2\xC3': 'H4 = 0xC3D2E1F0',
    }
    for needle, name in sha_constants.items():
        i = 0
        hits = []
        while True:
            idx = data.find(needle, i)
            if idx == -1: break
            hits.append(idx)
            i = idx + 1
        if hits:
            print(f"  {name}: at {[hex(o) for o in hits]}")
    print()

    # Look for PE-style import name table
    print("=" * 60)
    print("EXPECTED IMPORT NAMES (location)")
    print("=" * 60)
    target_strings = [
        b'kernel32.dll', b'kernel32', b'IsDebuggerPresent',
        b'AddVectoredExceptionHandler', b'CreateToolhelp32Snapshot',
        b'wine_get_unix_file_name', b'GetProcAddress', b'LoadLibrary',
        b'GetTickCount', b'VirtualAlloc', b'VirtualProtect',
    ]
    for s in target_strings:
        idx = data.find(s)
        if idx != -1:
            print(f"  {s.decode():<35s} at {idx:#x}")
    print()

    # Detect data section by clusters of strings
    # We saw imports near offset 0x8000+ region (in earlier output the strings appeared there)
    print("=" * 60)
    print("STRING CLUSTERS (regions with many printable runs)")
    print("=" * 60)
    in_run = False
    run_start = 0
    runs = []
    for i, b in enumerate(data):
        if 0x20 <= b < 0x7F or b == 0:
            if not in_run:
                in_run = True
                run_start = i
        else:
            if in_run and i - run_start > 6:
                runs.append((run_start, i - run_start))
            in_run = False
    # find dense regions
    if runs:
        # cluster runs that are within 32 bytes of each other
        clusters = []
        cur = [runs[0]]
        for r in runs[1:]:
            if r[0] - (cur[-1][0] + cur[-1][1]) < 32:
                cur.append(r)
            else:
                if len(cur) > 5:
                    clusters.append((cur[0][0], cur[-1][0] + cur[-1][1] - cur[0][0], len(cur)))
                cur = [r]
        if len(cur) > 5:
            clusters.append((cur[0][0], cur[-1][0] + cur[-1][1] - cur[0][0], len(cur)))
        clusters.sort(key=lambda c: -c[2])
        for off, sz, count in clusters[:5]:
            print(f"  [{off:#06x}..{off+sz:#06x}]  span {sz}b  {count} string runs")
    print()

if __name__ == '__main__':
    main()
