#!/usr/bin/env python3
"""Fit the exported WebGL tree to a delivery budget without changing native media.

Largest videos are encoded in pairs from the original read-only native bundle.
Only smaller derivatives that pass full decode and stream/duration checks are
installed, and every exported catalogue asset is revalidated against its receipt.
The output directory is deliberately fixed to Unity/Builds/WebGL.
"""
import argparse
import concurrent.futures
import copy
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
WEB = ROOT / 'Unity/Builds/WebGL'
OUTPUT = WEB / 'StreamingAssets/TikTok'
NATIVE = ROOT / 'Unity/Assets/StreamingAssets/TikTok'
MAC = ROOT / 'Unity/Builds/macOS-v0.8.0/SLOP CITY.app/Contents/Resources/Data/StreamingAssets/TikTok'
MAC_ZIP = ROOT.parent / 'SlopCity-macOS-v0.8.0-AppleSilicon.zip'
REPORT = ROOT / 'Unity/Builds/Reports/web-tiktok-optimization.json'
PROFILE = 'web-h264-crf26-max700k-480-24-aac64-square-v2'
MIB = 1024 * 1024


def utc():
    return datetime.now(timezone.utc).isoformat()


def sha(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def write_json(path, value):
    temporary = path.with_suffix(path.suffix + '.tmp')
    temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n')
    temporary.replace(path)


def run(args):
    result = subprocess.run(args, capture_output=True, text=True)
    if result.returncode:
        raise RuntimeError(result.stderr[-5000:] or 'Command failed: ' + args[0])
    return result


def probe(path):
    return json.loads(run(['ffprobe', '-v', 'error', '-show_streams', '-show_format', '-of', 'json', str(path)]).stdout)


def streams(info, kind):
    return [stream for stream in info['streams'] if stream['codec_type'] == kind]


def references(catalog):
    result = set()
    for post in catalog['videos']:
        values = [post.get('bundledThumbnailFile'), post.get('bundledVideoFile'), post.get('bundledAudioFile')]
        values += post.get('bundledImageFiles', [])
        for value in values:
            if not value:
                continue
            assert Path(value).name == value and '/' not in value and '\\' not in value
            result.add(value)
    return result


def verify_bundle(directory, receipt, catalog):
    assets = [asset for post in receipt['posts'] for asset in post['assets']]
    assert len(catalog['videos']) == len(receipt['posts']) == receipt['postCount']
    assert len(assets) == len({asset['file'] for asset in assets}) == receipt['assetCount']
    assert len({(p['creator'], p['id']) for p in catalog['videos']}) == receipt['postCount']
    assert {(p['creator'], p['id']) for p in catalog['videos']} == {(p['creator'], p['id']) for p in receipt['posts']}
    assert references(catalog) == {asset['file'] for asset in assets}
    for asset in assets:
        path = directory / 'media' / asset['file']
        assert path.is_file() and not path.is_symlink()
        assert path.stat().st_size == asset['bytes'] and sha(path) == asset['sha256'], asset['file']
    return assets


def encode(asset, temporary, settings):
    name = asset['file']
    source = NATIVE / 'media' / name
    target = temporary / name
    before = probe(source)
    run(['ffmpeg', '-hide_banner', '-loglevel', 'error', '-nostdin', '-y', '-threads', '2', '-i', str(source),
         '-map', '0:v:0', '-map', '0:a:0?', '-vf',
         f"scale=w='max(2,trunc(min(iw*sar,min({settings['maxEdge']},{settings['maxEdge']}*dar))/2)*2)':h='max(2,trunc(min(ih,min({settings['maxEdge']},{settings['maxEdge']}/dar))/2)*2)',setsar=1,fps=24",
         '-c:v', 'libx264', '-refs', '1', '-bf', '0', '-preset', 'fast', '-profile:v', 'main', '-pix_fmt', 'yuv420p',
         '-crf', str(settings['crf']), '-maxrate', str(settings['videoMaxrate']), '-bufsize', str(settings['videoMaxrate'] * 2), '-threads', '2',
         '-c:a', 'aac', '-b:a', str(settings['audioBitrate']), '-ac', '2', '-ar', '44100', '-movflags', '+faststart', str(target)])
    after = probe(target)
    before_video, after_video = streams(before, 'video'), streams(after, 'video')
    before_audio, after_audio = streams(before, 'audio'), streams(after, 'audio')
    assert len(before_video) == len(after_video) == 1
    assert after_video[0]['codec_name'] == 'h264' and after_video[0]['pix_fmt'] == 'yuv420p'
    assert max(after_video[0]['width'], after_video[0]['height']) <= settings['maxEdge']
    source_sar = before_video[0].get('sample_aspect_ratio', '1:1').split(':')
    source_sar = float(source_sar[0]) / float(source_sar[1]) if len(source_sar) == 2 and float(source_sar[1]) else 1
    assert abs(before_video[0]['width'] * source_sar / before_video[0]['height'] - after_video[0]['width'] / after_video[0]['height']) <= .02
    assert after_video[0]['avg_frame_rate'] in ('24/1', '24000/1000')
    assert len(before_audio) == len(after_audio) and all(s['codec_name'] == 'aac' for s in after_audio)
    duration_before = float(before['format']['duration'])
    duration_after = float(after['format']['duration'])
    assert abs(duration_after - duration_before) <= .15, name + ' duration changed'
    for left, right in zip(before_audio, after_audio):
        assert abs(float(left.get('duration', duration_before)) - float(right.get('duration', duration_after))) <= .15
    decoded = run(['ffmpeg', '-v', 'error', '-nostdin', '-threads', '2', '-i', str(target), '-f', 'null', '-'])
    assert not decoded.stderr.strip(), name + ' reported decode errors: ' + decoded.stderr
    size = target.stat().st_size
    assert 128 < size < 24 * MIB
    return {'file': name, 'beforeBytes': asset['bytes'], 'afterBytes': size,
            'savedBytes': asset['bytes'] - size, 'nativeInputSha256': sha(source), 'nativeInputBytes': source.stat().st_size,
            'beforeWebSha256': asset['sha256'], 'sha256': sha(target),
            'durationBefore': duration_before, 'durationAfter': duration_after,
            'width': after_video[0]['width'], 'height': after_video[0]['height'],
            'audioTracks': len(after_audio), 'fullDecodePassed': True,
            'streams': [{k: stream[k] for k in ('codec_type', 'codec_name', 'width', 'height', 'sample_rate', 'channels') if k in stream} for stream in after['streams']]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--target-mib', type=int, default=488)
    parser.add_argument('--compact', action='store_true', help='Resize all videos to 480px/CRF28/AAC48k; calculate a safe video rate from actual durations and the remaining tree budget.')
    args = parser.parse_args()
    assert 200 <= args.target_mib <= 490
    for executable in ('ffmpeg', 'ffprobe'):
        assert shutil.which(executable), executable + ' is required'
    assert WEB.resolve() == ROOT / 'Unity/Builds/WebGL' and OUTPUT.resolve() != NATIVE.resolve()
    assert not WEB.is_symlink() and not OUTPUT.is_symlink() and not (OUTPUT / 'media').is_symlink()
    catalog_bytes = (OUTPUT / 'catalog.json').read_bytes()
    catalog = json.loads(catalog_bytes)
    receipt = json.loads((OUTPUT / 'bundle-receipt.json').read_text())
    native_receipt = json.loads((NATIVE / 'bundle-receipt.json').read_text())
    assets = verify_bundle(OUTPUT, receipt, catalog)
    native_catalog = json.loads((NATIVE / 'catalog.json').read_text())
    native_assets = verify_bundle(NATIVE, native_receipt, native_catalog)
    assert catalog == native_catalog, 'Exported and native catalogues describe different snapshots'
    protected = {str(NATIVE / name): sha(NATIVE / name) for name in ('catalog.json', 'bundle-receipt.json')}
    if MAC.is_dir():
        mac_receipt = json.loads((MAC / 'bundle-receipt.json').read_text())
        mac_catalog = json.loads((MAC / 'catalog.json').read_text())
        mac_assets = verify_bundle(MAC, mac_receipt, mac_catalog)
        protected.update({str(MAC / name): sha(MAC / name) for name in ('catalog.json', 'bundle-receipt.json')})
        protected.update({str(MAC / 'media' / asset['file']): asset['sha256'] for asset in mac_assets})
    if MAC_ZIP.is_file():
        protected[str(MAC_ZIP)] = sha(MAC_ZIP)
    for asset in native_assets:
        protected[str(NATIVE / 'media' / asset['file'])] = asset['sha256']
    before_total = sum(path.stat().st_size for path in WEB.rglob('*') if path.is_file())
    # Leave metadata/tar overhead margin even before the outer Site packager runs.
    budget = args.target_mib * MIB - 128 * 1024
    planned_total = before_total
    settings = {'maxEdge': 480, 'crf': 26, 'videoMaxrate': 700000, 'audioBitrate': 64000}
    profile = PROFILE
    if args.compact:
        videos = [asset for asset in assets if asset['kind'] == 'video']
        with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:
            source_info = list(pool.map(lambda asset: probe(NATIVE / 'media' / asset['file']), videos))
        duration = sum(float(info['format']['duration']) for info in source_info)
        fixed_bytes = before_total - sum(asset['bytes'] for asset in videos)
        video_budget = args.target_mib * MIB - fixed_bytes - 2 * MIB
        maxrate = min(300000, int((video_budget * 8 / duration * .90 - 48000) // 1000) * 1000)
        assert maxrate >= 80000, 'Insufficient video bitrate budget; no exported media changed'
        settings = {'maxEdge': 480, 'crf': 28, 'videoMaxrate': maxrate, 'audioBitrate': 48000,
                    'sourceVideoDurationSeconds': duration, 'fixedNonvideoBytes': fixed_bytes, 'videoBudgetBytes': video_budget}
        profile = f'web-h264-crf28-max{maxrate // 1000}k-480-24-aac48-square-v2'
    candidates = sorted((asset for asset in assets if asset['kind'] == 'video' and
                         asset.get('webOptimizationProfile') != profile),
                        key=lambda asset: asset['bytes'], reverse=True)
    report = {'startedAtUtc': utc(), 'passed': False, 'profile': profile, 'settings': settings, 'targetMiB': args.target_mib,
              'webBytesBefore': before_total, 'posts': receipt['postCount'], 'assets': receipt['assetCount'], 'changed': [], 'errors': []}
    print(f'WEB BUDGET: {before_total:,} bytes -> <= {budget:,}; largest-first, two encoders', flush=True)
    print('ENCODING SETTINGS: ' + json.dumps(settings), flush=True)
    try:
        with tempfile.TemporaryDirectory(prefix='slop-city-web-video-') as temporary_name:
            temporary = Path(temporary_name)
            with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
                for index in range(0, len(candidates), 2):
                    pending = [pool.submit(encode, asset, temporary, settings) for asset in candidates[index:index + 2]]
                    for future in pending:
                        result = future.result()
                        report['changed'].append(result)
                        planned_total -= result['savedBytes']
                        print(f"VERIFIED {result['file']}: {result['beforeBytes']:,} -> {result['afterBytes']:,}; projected tree {planned_total:,}", flush=True)
            assert planned_total <= budget, 'Video optimization did not reach export budget; no output files replaced'
            updated = copy.deepcopy(receipt)
            changes = {item['file']: item for item in report['changed']}
            for post in updated['posts']:
                for asset in post['assets']:
                    change = changes.get(asset['file'])
                    if not change:
                        continue
                    asset.update({'bytes': change['afterBytes'], 'sha256': change['sha256'], 'streams': change['streams'],
                                  'webOptimizationProfile': profile, 'nativeBundledSha256': change['nativeInputSha256'],
                                  'nativeBundledBytes': change['nativeInputBytes']})
                    post['webOptimizationProfile'] = profile
            updated_assets = [asset for post in updated['posts'] for asset in post['assets']]
            updated['totalBytes'] = sum(asset['bytes'] for asset in updated_assets)
            updated['maximumAssetBytes'] = max(asset['bytes'] for asset in updated_assets)
            updated['webOptimization'] = {'profile': profile, 'optimizedAtUtc': utc(),
                'nativeBundleReceiptSha256': sha(NATIVE / 'bundle-receipt.json'),
                'catalogSha256': hashlib.sha256(catalog_bytes).hexdigest(),
                'changedVideoCount': len(changes), 'savedBytes': before_total - planned_total,
                'nativeAssetsUnchanged': True}
            for name in changes:
                # A platform's temp directory may be on another volume. Copy
                # the verified derivative beside its destination, then replace.
                installing = OUTPUT / 'media' / (name + '.installing')
                shutil.copy2(temporary / name, installing)
                installing.replace(OUTPUT / 'media' / name)
            write_json(OUTPUT / 'bundle-receipt.json', updated)
        assert (OUTPUT / 'catalog.json').read_bytes() == catalog_bytes
        verify_bundle(OUTPUT, updated, catalog)
        for name, expected in protected.items():
            assert sha(Path(name)) == expected, 'Protected native artifact changed: ' + name
        after_total = sum(path.stat().st_size for path in WEB.rglob('*') if path.is_file())
        assert after_total <= args.target_mib * MIB
        report.update({'passed': True, 'finishedAtUtc': utc(), 'webBytesAfter': after_total,
            'savedBytes': before_total - after_total, 'changedVideoCount': len(report['changed']),
            'changedVideoOutputBytes': sum(item['afterBytes'] for item in report['changed']),
            'allCatalogueReferencesResolve': True, 'allExportedAssetHashesVerified': True,
            'exportedAssetHashesVerified': len(updated_assets),
            'catalogUnchanged': True, 'protectedNativeAssetHashesVerified': len(protected),
            'macZipSha256Unchanged': protected.get(str(MAC_ZIP)),
            'scope': 'Only exported WebGL TikTok video files and exported bundle receipt changed. Unity source assets, native app, Mac ZIP and Site checkouts untouched.'})
        write_json(REPORT, report)
        print(json.dumps({key: value for key, value in report.items() if key != 'changed'}, indent=2), flush=True)
    except Exception as error:
        report['errors'].append(str(error)); report['finishedAtUtc'] = utc()
        write_json(REPORT, report)
        raise


if __name__ == '__main__':
    main()
