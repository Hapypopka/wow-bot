"""
Парсер кастомного формата Warden модуля (порт WoWee parseExecutableFormat).

Формат decompressed bytes:
  +0x00: uint32 LE finalCodeSize - размер итогового образа в памяти
  +0x04: stream of (copy_count, copy_data, skip_count) или вариантов
         три порядка: CopyDataSkip, SkipCopyData, CopySkipData
  Терминатор: пара из нулей
  После терминатора: delta-encoded relocations (uint16 LE), терминатор 0x0000
"""

import struct
import sys
from pathlib import Path
from enum import Enum


class PairFormat(Enum):
    COPY_DATA_SKIP_U16 = "copy/data/skip (u16)"
    SKIP_COPY_DATA_U16 = "skip/copy/data (u16)"
    COPY_SKIP_DATA_U16 = "copy/skip/data (u16)"
    COPY_DATA_SKIP_U32 = "copy/data/skip (u32)"
    SKIP_COPY_DATA_U32 = "skip/copy/data (u32)"
    COPY_SKIP_DATA_U32 = "copy/skip/data (u32)"


DEBUG = False

def _try_parse(data: bytes, fmt: PairFormat, module_size: int):
    """Возвращает (image_bytes, reloc_pos, final_offset, pair_count) или None при failure."""
    image = bytearray(module_size)
    pos = 4  # skip 4-byte size header
    dest = 0
    pair_count = 0
    n = len(data)
    is_u32 = "u32" in fmt.value
    word_size = 4 if is_u32 else 2
    word_fmt = "<I" if is_u32 else "<H"
    if DEBUG: print(f"  trying {fmt.value}: data={n}b, module_size={module_size}")

    def read_word(at: int) -> int:
        return struct.unpack_from(word_fmt, data, at)[0]

    is_copy_data_skip = fmt in (PairFormat.COPY_DATA_SKIP_U16, PairFormat.COPY_DATA_SKIP_U32)
    is_skip_copy_data = fmt in (PairFormat.SKIP_COPY_DATA_U16, PairFormat.SKIP_COPY_DATA_U32)
    is_copy_skip_data = fmt in (PairFormat.COPY_SKIP_DATA_U16, PairFormat.COPY_SKIP_DATA_U32)

    while pos + word_size <= n:
        copy_count = 0
        skip_count = 0

        if is_copy_data_skip:
            copy_count = read_word(pos)
            pos += word_size
            if DEBUG and pair_count < 5: print(f"    [{pair_count}] pos={pos-word_size:#x} copy={copy_count} dest={dest:#x}")
            if copy_count == 0:
                return bytes(image), pos, dest, pair_count
            if pos + copy_count > n or dest + copy_count > module_size:
                if DEBUG: print(f"    OOB on copy: pos+copy={pos+copy_count} n={n} | dest+copy={dest+copy_count} ms={module_size}")
                return None
            image[dest:dest + copy_count] = data[pos:pos + copy_count]
            pos += copy_count
            dest += copy_count
            if pos + word_size > n:
                return None
            skip_count = read_word(pos)
            pos += word_size

        elif is_skip_copy_data:
            if pos + 2 * word_size > n:
                return None
            skip_count = read_word(pos); pos += word_size
            copy_count = read_word(pos); pos += word_size
            if DEBUG and pair_count < 5: print(f"    [{pair_count}] pos={pos-2*word_size:#x} skip={skip_count} copy={copy_count} dest={dest:#x}")
            if skip_count == 0 and copy_count == 0:
                return bytes(image), pos, dest, pair_count
            if dest + skip_count > module_size:
                if DEBUG: print(f"    OOB: dest({dest})+skip({skip_count}) > {module_size}")
                return None
            dest += skip_count
            if pos + copy_count > n or dest + copy_count > module_size:
                return None
            image[dest:dest + copy_count] = data[pos:pos + copy_count]
            pos += copy_count
            dest += copy_count
            skip_count = 0

        elif is_copy_skip_data:
            if pos + 2 * word_size > n:
                return None
            copy_count = read_word(pos); pos += word_size
            skip_count = read_word(pos); pos += word_size
            if DEBUG and pair_count < 5: print(f"    [{pair_count}] pos={pos-2*word_size:#x} copy={copy_count} skip={skip_count} dest={dest:#x}")
            if copy_count == 0 and skip_count == 0:
                return bytes(image), pos, dest, pair_count
            if pos + copy_count > n or dest + copy_count > module_size:
                return None
            image[dest:dest + copy_count] = data[pos:pos + copy_count]
            pos += copy_count
            dest += copy_count

        if dest + skip_count > module_size:
            return None
        dest += skip_count
        pair_count += 1

    return None


def parse_module(decompressed_data: bytes):
    """Распарсить кастомный формат → (image_bytes, used_format, reloc_data_pos, pair_count)."""
    if len(decompressed_data) < 4:
        raise ValueError("data too small for size header")
    final_size = struct.unpack_from("<I", decompressed_data, 0)[0]
    if final_size == 0 or final_size > 5 * 1024 * 1024:
        raise ValueError(f"unreasonable final code size: {final_size}")

    for fmt in PairFormat:
        result = _try_parse(decompressed_data, fmt, final_size)
        if result is not None:
            image, reloc_pos, final_offset, pair_count = result
            return {
                "image": image,
                "format": fmt.value,
                "reloc_data_pos": reloc_pos,
                "final_offset": final_offset,
                "pair_count": pair_count,
                "expected_size": final_size,
            }

    # Fallback: raw copy of payload (без copy/skip кодирования).
    # WoWee делает то же самое чтобы не падать когда формат не распознан.
    image = bytearray(final_size)
    payload = decompressed_data[4:]
    raw_size = min(len(payload), final_size)
    image[:raw_size] = payload[:raw_size]
    return {
        "image": bytes(image),
        "format": "raw fallback",
        "reloc_data_pos": 0,            # неизвестно где relocs
        "final_offset": raw_size,
        "pair_count": 0,
        "expected_size": final_size,
    }


def apply_relocations(image: bytes, reloc_data: bytes, module_base: int) -> bytes:
    """
    Применить delta-encoded relocations.
    Каждое смещение в reloc_data — uint16 LE delta. Накапливаем currentOffset += delta.
    На каждом смещении в image добавляем module_base к 32-битному значению.
    Терминатор: 0x0000.
    """
    out = bytearray(image)
    pos = 0
    cur = 0
    count = 0
    while pos + 2 <= len(reloc_data):
        delta = struct.unpack_from("<H", reloc_data, pos)[0]
        pos += 2
        if delta == 0:
            break
        cur += delta
        if cur + 4 > len(out):
            print(f"reloc out of bounds at offset {cur:#x}, skipping", file=sys.stderr)
            break
        val = struct.unpack_from("<I", out, cur)[0]
        new_val = (val + module_base) & 0xFFFFFFFF
        struct.pack_into("<I", out, cur, new_val)
        count += 1
    return bytes(out), count


def main():
    if len(sys.argv) < 2:
        print("usage: module_loader.py <warden_module.bin> [output_image.bin] [--module-base 0x400000]", file=sys.stderr)
        return 1

    global DEBUG
    if "--debug" in sys.argv:
        DEBUG = True
        sys.argv.remove("--debug")

    in_path = Path(sys.argv[1])
    data = in_path.read_bytes()
    print(f"Input: {in_path} ({len(data)} bytes)")
    print(f"First 16 bytes: {data[:16].hex()}")

    result = parse_module(data)
    if result is None:
        print("FAIL: ни один из 3 форматов не подошёл")
        return 1

    print(f"Format used:        {result['format']}")
    print(f"Pair count:         {result['pair_count']}")
    print(f"Final offset:       {result['final_offset']} / {result['expected_size']}")
    print(f"Reloc data starts:  {result['reloc_data_pos']:#x}")
    print(f"Reloc data size:    {len(data) - result['reloc_data_pos']}b")
    print(f"Image size:         {len(result['image'])}b")

    out_path = Path(sys.argv[2]) if len(sys.argv) > 2 else in_path.with_name(in_path.stem + "_image.bin")
    out_path.write_bytes(result['image'])
    print(f"Wrote raw image →   {out_path}")

    # Применим relocations с условной базой 0x400000
    module_base = 0x400000
    for i, a in enumerate(sys.argv):
        if a == "--module-base" and i + 1 < len(sys.argv):
            module_base = int(sys.argv[i + 1], 16) if sys.argv[i + 1].startswith("0x") else int(sys.argv[i + 1])

    reloc_data = data[result['reloc_data_pos']:]
    relocated, n_relocs = apply_relocations(result['image'], reloc_data, module_base)
    reloc_path = out_path.with_name(out_path.stem + f"_relocated_{module_base:08X}.bin")
    reloc_path.write_bytes(relocated)
    print(f"Applied {n_relocs} relocations (base=0x{module_base:08X})")
    print(f"Wrote relocated → {reloc_path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
