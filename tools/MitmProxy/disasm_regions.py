"""
Disassemble interesting regions of the Warden module.
"""
import capstone
from pathlib import Path

MODULE = Path(__file__).parent / "warden_module.bin"
data = MODULE.read_bytes()

md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)
md.detail = False
md.skipdata = True

def disasm(offset, count=30, label=""):
    print(f"\n=== {label} (offset {offset:#x}) ===")
    seen = 0
    for ins in md.disasm(data[offset:], offset):
        print(f"  {ins.address:#06x}:  {ins.mnemonic:<8s} {ins.op_str}")
        seen += 1
        if seen >= count:
            break

# Let's look at the very beginning (likely init / entry)
disasm(0x00, 30, "MODULE START")

# First function prologue at 0x4e
disasm(0x4e, 50, "First function (probable init)")

# Around first SHA1 H0 reference (0xDF) — likely SHA1 init function
disasm(0xCB, 50, "Around SHA1 H constants @ 0xDF")

# Function at 0xCC (near SHA1 const) — try seeing what's there
disasm(0xC0, 60, "Pre-SHA1 region")

# Big function start at 0x232
disasm(0x232, 40, "Function @ 0x232")

# Function at 0x83c, 0x98c (potentially big handlers)
disasm(0x98c, 50, "Function @ 0x98c")

# Function around the imports/strings region — likely initialization that resolves imports
# Strings at 0x6FE7+, code that uses them is somewhere referencing those addresses
# Search for code that pushes string addresses

print("\n=== References to string region (push imm32 in 0x6FE7..0x7213) ===")
# push imm32 = 0x68 + 4 bytes
import struct
for i in range(len(data) - 5):
    if data[i] == 0x68:
        v = struct.unpack('<I', data[i+1:i+5])[0]
        if 0x6FE7 <= v <= 0x7213:
            # Show surrounding code
            try:
                s = data[v:v+30].split(b'\x00', 1)[0].decode('latin1', errors='replace')
                print(f"  {i:#06x}: push {v:#x}  ({s!r})")
            except:
                pass
