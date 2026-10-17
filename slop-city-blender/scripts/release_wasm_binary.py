"""Avoid copying raw Wasm bytes and release them after successful instantiation.

The compiled Module is the same object the loader calls b. Both it and the
framework closure retain the downloaded bytes unless explicitly cleared. Never
clear during compilation or its streaming/ArrayBuffer fallback. Patch staging
before content hashes/SRI, retaining Unity's gzip header and detection marker.
"""

import gzip
import struct
import zlib


SUCCESS_HOOK = 'function receiveInstance(instance,module){var exports=instance.exports;'
RELEASE = 'wasmBinary=undefined;delete Module["wasmBinary"];'
BINARY_COPY_HOOK = 'if(file==wasmBinaryFile&&wasmBinary){return new Uint8Array(wasmBinary)}'
BINARY_VIEW = 'if(file==wasmBinaryFile&&wasmBinary){return wasmBinary instanceof Uint8Array?wasmBinary:new Uint8Array(wasmBinary)}'


def release_framework_binary(data):
    compressed = data.startswith(b'\x1f\x8b')
    original = gzip.decompress(data) if compressed else data
    source = original.decode('utf-8')
    if (source.count(SUCCESS_HOOK) != 1 or source.count('var wasmBinary;') != 1
            or source.count(BINARY_COPY_HOOK) != 1):
        raise ValueError('Pinned Unity Wasm instantiation contract changed; review the framework before packaging.')
    if RELEASE in source:
        raise ValueError('The source framework already contains the Wasm release patch.')
    # The loader already supplies an immutable Uint8Array. Constructing another
    # Uint8Array from it copies the entire downloaded Wasm during compilation.
    # Preserve normalization for ArrayBuffer input while borrowing that view.
    source = source.replace(BINARY_COPY_HOOK, BINARY_VIEW, 1)
    patched = source.replace(SUCCESS_HOOK, SUCCESS_HOOK + RELEASE, 1).encode('utf-8')
    if not compressed:
        return patched
    flags = data[3]
    if flags & 0xe0 or data[2] != 8:
        raise ValueError('Unsupported Unity gzip header.')
    offset = 10
    if flags & 4:
        offset += 2 + struct.unpack_from('<H', data, offset)[0]
    for flag in (8, 16):
        if flags & flag:
            offset = data.index(b'\0', offset) + 1
    # Header CRC would need recalculation if the header changed. We preserve it.
    if flags & 2:
        offset += 2
    if b'UnityWeb Compressed Content (gzip)\0' not in data[:offset]:
        raise ValueError('Expected Unity gzip detection marker in compressed framework.')
    compressor = zlib.compressobj(level=9, wbits=-15)
    result = data[:offset] + compressor.compress(patched) + compressor.flush()
    result += struct.pack('<II', zlib.crc32(patched), len(patched) & 0xffffffff)
    if gzip.decompress(result) != patched:
        raise ValueError('Patched framework gzip verification failed.')
    return result
