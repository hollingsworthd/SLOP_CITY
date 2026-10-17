#!/usr/bin/env python3
"""Read exact Unity Web exports without modifying them or mounting/unloading assets.

Usage: audit_web_wasm_memory.py EXPORT [EXPORT ...] --output RECEIPT.json
Wasm memory limits use binary section/LEB parsing. Growth policy is read from
the compiled Emscripten helper. UnityWebData is streamed with only its header
retained; the audit itself does not inflate the full data file into RAM.
"""
import argparse
from datetime import datetime, timezone
import gzip
import hashlib
import json
from pathlib import Path
import re
import struct


def sha(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def open_raw(path):
    with path.open('rb') as stream:
        compressed = stream.read(2) == b'\x1f\x8b'
    return gzip.open(path, 'rb') if compressed else path.open('rb')


class Reader:
    def __init__(self, data):
        self.data, self.pos = data, 0

    def take(self, length):
        end = self.pos + length
        if length < 0 or end > len(self.data):
            raise ValueError('Truncated binary section')
        value = self.data[self.pos:end]
        self.pos = end
        return value

    def uleb(self):
        value = 0
        for shift in range(0, 70, 7):
            byte = self.take(1)[0]
            value |= (byte & 127) << shift
            if not byte & 128:
                return value
        raise ValueError('Invalid unsigned LEB128')

    def name(self):
        return self.take(self.uleb()).decode('utf-8')

    def reftype(self):
        kind = self.take(1)[0]
        if kind in (0x63, 0x64):  # nullable/non-nullable typed references
            self.uleb()  # signed heap type's bytes have the same continuation framing

    def limits(self, memory=False):
        flags = self.uleb()
        if flags & ~7:
            raise ValueError('Unsupported limits flags: ' + str(flags))
        initial = self.uleb()
        maximum = self.uleb() if flags & 1 else None
        value = {'flags': flags, 'initialPages': initial, 'maximumPages': maximum}
        if memory:
            value.update(initialBytes=initial * 65536,
                         maximumBytes=maximum * 65536 if maximum is not None else None,
                         shared=bool(flags & 2), memory64=bool(flags & 4))
        return value


def wasm_memories(data):
    reader = Reader(data)
    if reader.take(8) != b'\0asm\1\0\0\0':
        raise ValueError('Expected a version 1 Wasm module')
    imported, defined, exports = [], [], []
    seen = set()
    while reader.pos < len(data):
        section_id = reader.take(1)[0]
        section = Reader(reader.take(reader.uleb()))
        if section_id and section_id in seen:
            raise ValueError('Duplicate Wasm section ' + str(section_id))
        seen.add(section_id)
        if section_id == 2:
            for _ in range(section.uleb()):
                module, name, kind = section.name(), section.name(), section.take(1)[0]
                if kind == 0:
                    section.uleb()
                elif kind == 1:
                    section.reftype(); section.limits()
                elif kind == 2:
                    imported.append({'origin': 'import', 'module': module, 'name': name, **section.limits(True)})
                elif kind == 3:
                    section.reftype(); section.take(1)
                elif kind == 4:
                    section.take(1); section.uleb()
                else:
                    raise ValueError('Unknown Wasm import kind ' + str(kind))
        elif section_id == 5:
            defined = [{'origin': 'defined', **section.limits(True)} for _ in range(section.uleb())]
        elif section_id == 7:
            for _ in range(section.uleb()):
                name, kind, index = section.name(), section.take(1)[0], section.uleb()
                if kind == 2:
                    exports.append({'name': name, 'memoryIndex': index})
        if section_id in (2, 5, 7) and section.pos != len(section.data):
            raise ValueError('Unparsed bytes in Wasm section ' + str(section_id))
    memories = imported + defined
    for index, memory in enumerate(memories):
        memory['memoryIndex'] = index
        memory['exports'] = [entry['name'] for entry in exports if entry['memoryIndex'] == index]
    if any(entry['memoryIndex'] >= len(memories) for entry in exports):
        raise ValueError('Memory export references missing memory')
    return memories


def function_body(source, name):
    matches = list(re.finditer(r'function\s+' + re.escape(name) + r'\s*\([^)]*\)\s*\{', source))
    if len(matches) != 1:
        raise ValueError('Expected one compiled helper: ' + name)
    start = matches[0].start()
    pos = matches[0].end() - 1
    depth = 0
    quote = None
    while pos < len(source):
        char = source[pos]
        if quote:
            if char == '\\':
                pos += 1
            elif char == quote:
                quote = None
        elif char in ('"', "'", '`'):
            quote = char
        elif source[pos:pos + 2] == '//':
            newline = source.find('\n', pos + 2)
            pos = len(source) if newline < 0 else newline
        elif source[pos:pos + 2] == '/*':
            end = source.find('*/', pos + 2)
            if end < 0:
                raise ValueError('Unterminated helper comment')
            pos = end + 1
        elif char == '{':
            depth += 1
        elif char == '}':
            depth -= 1
            if depth == 0:
                return source[start:pos + 1]
        pos += 1
    raise ValueError('Unclosed helper: ' + name)


def growth_policy(source):
    body = function_body(source, '_emscripten_resize_heap')
    number = r'(\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?'
    compact = re.sub(r'\s+', '', body)
    linear = re.search(r'overGrownHeapSize=oldSize\+(' + number + r')/cutDown', compact)
    geometric = re.search(r'overGrownHeapSize=oldSize\*\(1\+(' + number + r')/cutDown\)', compact)
    if bool(linear) == bool(geometric):
        raise ValueError('Unrecognized or ambiguous compiled growth policy')
    cap = re.search(r'Math\.min\(overGrownHeapSize,requestedSize\+(' + number + r')\)', compact)
    max_body = function_body(source, 'getHeapMax')
    maximum = re.search(r'return\s+(\d+)', max_body)
    if not maximum:
        raise ValueError('Unrecognized compiled maximum heap')
    return {'mode': 'Linear' if linear else 'Geometric',
            'linearStepBytes': int(float(linear.group(1))) if linear else None,
            'geometricStep': float(geometric.group(1)) if geometric else None,
            'geometricCapBytes': int(float(cap.group(1))) if cap else None,
            'maximumBytes': int(maximum.group(1)),
            'resizeHelperSha256': hashlib.sha256(body.encode()).hexdigest(),
            'helperUsesWasmMemoryGrow': 'wasmMemory.grow' in function_body(source, 'emscripten_realloc_buffer')}


def unity_data(path):
    marker = b'UnityWebData1.0\0'
    digest = hashlib.sha256()
    with open_raw(path) as stream:
        prefix = stream.read(len(marker) + 4)
        if prefix[:len(marker)] != marker or len(prefix) != len(marker) + 4:
            raise ValueError('Unrecognized UnityWebData header')
        header_size = struct.unpack_from('<I', prefix, len(marker))[0]
        if not len(prefix) <= header_size <= 16 * 1024 * 1024:
            raise ValueError('Unsafe UnityWebData header size')
        header = prefix + stream.read(header_size - len(prefix))
        if len(header) != header_size:
            raise ValueError('Truncated UnityWebData header')
        reader = Reader(header); reader.pos = len(prefix)
        mounts = []
        while reader.pos < header_size:
            offset, length, name_length = struct.unpack('<III', reader.take(12))
            name = reader.take(name_length).decode('utf-8')
            mounts.append({'path': name, 'offset': offset, 'bytes': length})
        digest.update(header)
        raw_length = len(header)
        payload_digests = [hashlib.sha256() for _ in mounts]
        while True:
            block = stream.read(1024 * 1024)
            if not block:
                break
            for item, payload_digest in zip(mounts, payload_digests):
                start = max(raw_length, item['offset'])
                end = min(raw_length + len(block), item['offset'] + item['bytes'])
                if end > start:
                    payload_digest.update(memoryview(block)[start - raw_length:end - raw_length])
            raw_length += len(block); digest.update(block)
        for item, payload_digest in zip(mounts, payload_digests):
            item['sha256'] = payload_digest.hexdigest()
    ordered = sorted(mounts, key=lambda item: item['offset'])
    previous = header_size
    for item in ordered:
        if item['offset'] < previous or item['offset'] + item['bytes'] > raw_length:
            raise ValueError('Overlapping/out-of-range VFS entry: ' + item['path'])
        previous = item['offset'] + item['bytes']
    return {'compressedBytes': path.stat().st_size, 'inputSha256': sha(path),
            'rawBytes': raw_length, 'rawSha256': digest.hexdigest(),
            'headerBytes': header_size, 'mountedFileCount': len(mounts),
            'mountedPayloadBytes': sum(item['bytes'] for item in mounts), 'mounts': mounts,
            'residency': 'Pinned loader mounts subarray views with canOwn=true; MEMFS retains one shared decompressed data buffer. RawBytes is that backing-buffer size, not the sum of independently allocated copies.'}


def audit(directory):
    def one(pattern):
        matches = list((directory / 'Build').glob(pattern))
        if len(matches) != 1:
            raise ValueError('Expected one ' + pattern + ' in ' + str(directory))
        return matches[0]
    wasm, framework, data, loader = (one(pattern) for pattern in (
        '*.wasm.unityweb', '*.framework.js.unityweb', '*.data.unityweb', '*.loader.js'))
    with open_raw(wasm) as stream:
        raw_wasm = stream.read()
    with open_raw(framework) as stream:
        raw_framework = stream.read().decode('utf-8')
    memories, growth = wasm_memories(raw_wasm), growth_policy(raw_framework)
    if not memories or any(memory['maximumBytes'] != growth['maximumBytes'] for memory in memories):
        raise ValueError('Wasm/compiled helper heap limits do not match')
    loader_source = loader.read_text()
    if not re.search(r'FS_createDataFile\([^;]*?t\.subarray\(l,l\+c\),!0,!0,!0\)', loader_source):
        raise ValueError('Pinned loader VFS zero-copy ownership contract changed')
    return {'exportPath': str(directory.resolve()),
            'wasm': {'path': str(wasm.resolve()), 'inputSha256': sha(wasm),
                     'compressedBytes': wasm.stat().st_size, 'rawBytes': len(raw_wasm),
                     'memories': memories},
            'framework': {'path': str(framework.resolve()), 'inputSha256': sha(framework),
                          'growthPolicy': growth},
            'loaderSha256': sha(loader), 'data': unity_data(data)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('exports', type=Path, nargs='+')
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    report = {'schemaVersion': 1, 'checkedAtUtc': datetime.now(timezone.utc).isoformat(),
              'passed': True, 'exports': [audit(directory) for directory in args.exports],
              'limitations': ['Static compiled allocation policy and VFS residency arithmetic; no browser process RSS or native Unity startup allocation peak measurement.',
                              'Wasm cannot shrink: a settled buffer length captures its allocated high-water, but not the exact peak bytes simultaneously used by Unity.',
                              'No mounted asset was deleted, unloaded, copied into the scene, or modified by this audit.']}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps({'passed': True, 'receipt': str(args.output), 'exports': [
        {'path': item['exportPath'], 'initialBytes': item['wasm']['memories'][0]['initialBytes'],
         'growthPolicy': item['framework']['growthPolicy'], 'vfsRawBytes': item['data']['rawBytes']}
        for item in report['exports']]}, indent=2))


if __name__ == '__main__':
    main()
