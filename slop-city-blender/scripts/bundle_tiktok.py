#!/usr/bin/env python3
"""Bundle both public creator catalogues, failing on omitted posts or assets.

Uses the same unauthenticated public listing as the game. No cookies, login,
challenge solver, or credentials. Outputs portable H.264/AAC, JPEG and PCM WAV.
Only a completed, verified snapshot replaces StreamingAssets/TikTok/catalog.json.
"""
import argparse
import concurrent.futures
import hashlib
import json
import math
import os
from pathlib import Path
import re
import random
import shutil
import subprocess
import tempfile
import time
import threading
from datetime import datetime, timezone
from email.utils import parsedate_to_datetime
import urllib.parse
import urllib.error
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
CREATORS = {
    "1984drh": "MS4wLjABAAAANqovoiLnOvAKhLkQEulN5JPLfBWj1h3k961etm8QuKmmkl_ak1svDsVBrdu4J7HU",
    "machine_publishers": "MS4wLjABAAAAGvvrkD-_D5H7MxpzKhFGnof7MvexPitzLGaPzlgzmOlE3wRKRTf68V_ayYD4bcmG",
}
API = "https://www.tiktok.com/api/creator/item_list/"
MIB = 1024 * 1024
MAX_OUTPUT = 22 * MIB
ID = re.compile(r"[0-9]{5,24}\Z")
REQUEST_LOCK = threading.RLock()
REQUEST_ABORT = threading.Event()
LAST_REQUEST_START = 0.0


def request_gate():
    global LAST_REQUEST_START
    if REQUEST_ABORT.is_set():
        raise RuntimeError("Public sync stopped after a blocked or exhausted request")
    delay = max(0.0, LAST_REQUEST_START + 3.0 + random.uniform(0.0, 2.0) - time.monotonic())
    if delay:
        time.sleep(delay)
    LAST_REQUEST_START = time.monotonic()


def retry_delay(error, attempt):
    delay = 30 * (2 ** attempt) + random.uniform(0.0, 2.0)
    if isinstance(error, urllib.error.HTTPError):
        raw = error.headers.get("Retry-After")
        if raw:
            try:
                instructed = float(raw)
            except ValueError:
                instructed = (parsedate_to_datetime(raw) - datetime.now(timezone.utc)).total_seconds()
            delay = max(delay, instructed)
    print(f"RATE LIMIT: public request retry in {delay:.1f}s ({type(error).__name__})", flush=True)
    time.sleep(delay)


def utc():
    return datetime.now(timezone.utc).isoformat()


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n")
    temporary.replace(path)


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def parse_featured_post(value):
    parts = value.split("/")
    if len(parts) != 2 or parts[0] not in CREATORS or not ID.fullmatch(parts[1]):
        raise argparse.ArgumentTypeError("Featured post must be CREATOR/POST_ID from a supported public creator")
    return tuple(parts)


def apply_featured_billboard(catalog, previous=None, requested=None):
    """Preserve an existing pin, or explicitly replace it with a bundled video.

    The optional flat fields remain compatible with the version-2 Unity catalog.
    A missing/removed/invalid pin must fail before a new manifest is installed.
    """
    if requested is None:
        previous = previous or {}
        if "featuredCreator" not in previous and "featuredPostId" not in previous:
            return None
        requested = (previous.get("featuredCreator"), previous.get("featuredPostId"))
        if all(value in (None, "") for value in requested):
            return None
    if (not isinstance(requested, (tuple, list)) or len(requested) != 2
            or not isinstance(requested[0], str) or requested[0] not in CREATORS
            or not isinstance(requested[1], str) or not ID.fullmatch(requested[1])):
        raise ValueError("Featured billboard requires a supported creator and numeric post ID")
    creator, post_id = requested
    matches = [post for post in catalog.get("videos", [])
               if post.get("creator") == creator and post.get("id") == post_id]
    if (len(matches) != 1 or matches[0].get("kind") != "video"
            or matches[0].get("sourceUrl") != f"https://www.tiktok.com/@{creator}/video/{post_id}"
            or matches[0].get("bundledVideoFile") != post_id + ".mp4"):
        raise ValueError("Featured billboard must match exactly one valid bundled video; existing catalog preserved")
    catalog["featuredCreator"], catalog["featuredPostId"] = creator, post_id
    return requested


def safe_media(url):
    try:
        uri = urllib.parse.urlsplit(url)
        host = (uri.hostname or "").lower()
        if uri.scheme != "https" or uri.port not in (None, 443) or uri.username or uri.password or uri.fragment:
            return False
        if host.endswith(".tiktok.com"):
            return bool(re.match(r"v[0-9]", host) and uri.path.startswith("/video/"))
        return any(host.endswith(s) for s in (".tiktokcdn.com", ".tiktokcdn-us.com", ".tiktokcdn-eu.com", ".byteoversea.com", ".ibytedtos.com", ".muscdn.com", ".tiktokv.com"))
    except (ValueError, TypeError):
        return False


class MediaRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, msg, headers, newurl):
        if not safe_media(newurl):
            REQUEST_ABORT.set()
            raise ValueError("Media redirect leaves the allowed public CDN hosts")
        request_gate()
        return super().redirect_request(request, fp, code, msg, headers, newurl)


class ListingRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, msg, headers, newurl):
        target = urllib.parse.urlsplit(newurl)
        if (target.scheme != "https" or target.hostname != "www.tiktok.com"
                or target.port not in (None, 443) or target.username or target.password
                or target.fragment or target.path != "/api/creator/item_list/"):
            REQUEST_ABORT.set()
            raise ValueError("Public listing redirect leaves the creator API; no challenge or authentication bypass")
        request_gate()
        return super().redirect_request(request, fp, code, msg, headers, newurl)


def request(url, destination=None):
    # A single shared lock covers API and CDN transfers, including redirects.
    with REQUEST_LOCK:
        return limited_request(url, destination)


def limited_request(url, destination=None):
    media = destination is not None
    if media and not safe_media(url):
        raise ValueError("Unexpected media URL")
    opener = urllib.request.build_opener(MediaRedirect() if media else ListingRedirect())
    limit = 512 * MIB if media else 8 * MIB
    for attempt in range(3):
        try:
            request_gate()
            req = urllib.request.Request(url, headers={"User-Agent": "SlopCity/0.8", "Referer": "https://www.tiktok.com/", "Accept": "*/*" if media else "application/json"})
            with opener.open(req, timeout=60) as response:
                if media and response.headers.get_content_type() in ("text/html", "application/json"):
                    REQUEST_ABORT.set()
                    raise ValueError("Public CDN returned a non-media/challenge response; no bypass")
                if int(response.headers.get("Content-Length", 0)) > limit:
                    raise ValueError("Public response exceeds size limit")
                if not media:
                    data = response.read(limit + 1)
                    if len(data) > limit:
                        raise ValueError("Public response exceeds size limit")
                    return json.loads(data)
                total = 0
                with destination.open("wb") as stream:
                    while block := response.read(MIB):
                        total += len(block)
                        if total > limit:
                            raise ValueError("Media response exceeds size limit")
                        stream.write(block)
                if total < 129:
                    raise ValueError("Empty or invalid media response")
                return
        except Exception as error:
            if destination:
                destination.unlink(missing_ok=True)
            retryable = isinstance(error, (urllib.error.URLError, TimeoutError, ConnectionError))
            if isinstance(error, urllib.error.HTTPError):
                retryable = error.code == 429 or 500 <= error.code < 600
            if attempt == 2 or not retryable:
                if isinstance(error, (urllib.error.URLError, TimeoutError, ConnectionError)):
                    REQUEST_ABORT.set()
                raise
            retry_delay(error, attempt)


def address(value):
    if isinstance(value, str):
        return value if safe_media(value) else ""
    if isinstance(value, dict):
        for url in value.get("UrlList", value.get("urlList", [])):
            if safe_media(url):
                return url
    return ""


def parse_post(item, creator):
    author = item.get("author", {})
    actual = author if isinstance(author, str) else author.get("uniqueId", "")
    post_id = str(item.get("id", ""))
    if actual.lower() != creator or not ID.fullmatch(post_id):
        raise ValueError("Listing returned wrong creator or malformed ID")
    if any(item.get(key) for key in ("privateItem", "secret", "forFriend", "isProhibited", "isReviewing", "takeDown")):
        raise ValueError(f"{creator}/{post_id}: unavailable post in public listing")
    photo = item.get("imagePost", item.get("itemImagePost"))
    video = item.get("video") or {}
    images = []
    if photo is not None:
        for image in photo.get("images", []):
            selected = address(image.get("imageURL", {}))
            if not selected:
                raise ValueError(f"{post_id}: slideshow image has no valid public URL")
            images.append(selected)
        if not 0 < len(images) <= 100:
            raise ValueError(f"{post_id}: invalid slideshow frame count")
    media = ""
    if photo is None:
        for value in [video.get("playAddr"), video.get("downloadAddr"), video.get("PlayAddrStruct"), *video.get("urls", []), *[b.get("PlayAddr") for b in video.get("bitrateInfo", [])]]:
            media = address(value)
            if media:
                break
        if not media:
            raise ValueError(f"{post_id}: video has no valid public URL")
    title = str(item.get("desc", item.get("text", "")))
    title = "".join(" " if char in "\r\n\t" else char for char in title if ord(char) >= 32 or char in "\r\n\t")[:180].replace("<", "[").replace(">", "]")
    audio_raw = (item.get("music") or {}).get("playUrl", "") if photo is not None else ""
    if audio_raw and not safe_media(audio_raw):
        raise ValueError(f"{post_id}: slideshow music URL is unsupported")
    result = {"id": post_id, "title": title or f"@{creator} / public post", "creator": creator,
            "sourceUrl": f"https://www.tiktok.com/@{creator}/{'photo' if photo is not None else 'video'}/{post_id}",
            "mediaUrl": media, "localFile": "", "kind": "slideshow" if photo is not None else "video",
            "audioUrl": audio_raw, "imageUrls": images, "createTime": int(item.get("createTime", 0)),
            "duration": int(video.get("duration", 0)), "bundledVideoFile": "", "bundledImageFiles": [],
            "bundledAudioFile": "", "bundledThumbnailFile": ""}
    if post_id == "7695765385894710559" and creator == "machine_publishers":
        music = item.get("music") or {}
        result["sourceMusicMetadata"] = {key: music[key] for key in ("id", "title", "authorName", "original", "duration") if key in music}
    return result


def fetch_catalog(work):
    posts, accounts = {}, []
    started = utc()
    for creator, sec_uid in CREATORS.items():
        cursor, seen, discovered, reported = int(time.time() * 1000), set(), {}, -1
        pages = []
        for number in range(1000):
            if cursor in seen:
                raise ValueError(f"@{creator}: repeated pagination cursor")
            seen.add(cursor)
            url = API + "?" + urllib.parse.urlencode({"aid": 1988, "secUid": sec_uid, "count": 15, "cursor": cursor, "type": 1})
            payload = request(url)
            items = payload.get("itemList")
            more = payload.get("hasMore", payload.get("hasMorePrevious"))
            if payload.get("statusCode", payload.get("status_code")) != 0 or not isinstance(items, list) or more not in (True, False, 0, 1):
                raise ValueError(f"@{creator}: unavailable or malformed public listing")
            pages.append({"cursor": cursor, "items": len(items), "hasMore": bool(more), "sha256": hashlib.sha256(json.dumps(payload, sort_keys=True).encode()).hexdigest()})
            previous = len(discovered)
            for item in items:
                post = parse_post(item, creator)
                discovered[post["id"]] = post
                for stats in (item.get("authorStats", {}), item.get("authorStatsV2", {})):
                    if "videoCount" in stats:
                        reported = max(reported, int(stats["videoCount"]))
            if not more:
                if reported >= 0 and len(discovered) < reported:
                    raise ValueError(f"@{creator}: only {len(discovered)} of {reported} posts found")
                break
            following = int(payload.get("cursor", int(items[-1]["createTime"]) * 1000 if items else 0))
            if following <= 0 or following >= cursor or len(discovered) == previous:
                raise ValueError(f"@{creator}: pagination did not advance")
            cursor = following
            time.sleep(.15)
        else:
            raise ValueError("Pagination safety limit exceeded")
        now = utc()
        accounts.append({"creator": creator, "profileUrl": f"https://www.tiktok.com/@{creator}", "status": "All public posts bundled.", "lastSyncUtc": now, "secUid": sec_uid, "totalReported": reported, "discoveredCount": len(discovered), "complete": True})
        write_json(work / f"{creator}-pagination.json", {"startedAtUtc": started, "finishedAtUtc": now, "account": accounts[-1], "pages": pages, "ids": sorted(discovered)})
        posts.update({f"{creator}/{key}": value for key, value in discovered.items()})
        print(f"CATALOG @{creator}: {len(discovered)} / {reported}, complete", flush=True)
    return {"version": 2, "syncedAtUtc": utc(), "bundledAtUtc": utc(), "accounts": accounts,
            "videos": sorted(posts.values(), key=lambda post: (-post["createTime"], post["id"]))}


def run(*args):
    result = subprocess.run(args, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    if result.returncode:
        raise RuntimeError(f"{Path(args[0]).name} failed: {result.stderr[-1800:]}")
    return result.stdout


def probe(path):
    return json.loads(run("ffprobe", "-v", "error", "-show_streams", "-show_format", "-of", "json", str(path)))


def ffmpeg(source, target, *options):
    temporary = target.with_name(target.stem + ".part" + target.suffix)
    try:
        run("ffmpeg", "-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-threads", "2", "-i", str(source), *options, str(temporary))
        if temporary.stat().st_size <= 128 or temporary.stat().st_size > MAX_OUTPUT:
            raise ValueError(f"Output size invalid: {target.name} ({temporary.stat().st_size})")
        temporary.replace(target)
    finally:
        temporary.unlink(missing_ok=True)


def package_post(post, output, work, cache):
    post_id = post["id"]
    receipt_path = work / "receipts" / f"{post_id}.json"
    if receipt_path.exists():
        saved = json.loads(receipt_path.read_text())
        if saved.get("profile") == "h264-640-24-v1" and saved.get("imageCount") == len(post["imageUrls"]) and saved.get("hasAudio") == bool(post["audioUrl"]):
            if all((output / asset["file"]).is_file() and digest(output / asset["file"]) == asset["sha256"] for asset in saved["assets"]):
                post.update(saved["bundledFields"])
                return saved
    assets = []
    with tempfile.TemporaryDirectory(prefix=post_id + "-", dir=work) as temporary:
        temp = Path(temporary)
        def source(kind, url, filename):
            cached = cache / filename if cache else None
            path = cached if cached and cached.is_file() and not cached.is_symlink() else temp / (kind + ".source")
            if path != cached:
                request(url, path)
            return path
        def record(target, original, kind):
            info = probe(target)
            assets.append({"file": target.name, "kind": kind, "bytes": target.stat().st_size,
                           "sha256": digest(target), "sourceSha256": digest(original), "streams": [{k: stream[k] for k in ("codec_type", "codec_name", "width", "height", "sample_rate", "channels") if k in stream} for stream in info["streams"]]})
        if post["kind"] == "video":
            original = source("video", post["mediaUrl"], post_id + ".mp4")
            info = probe(original)
            duration = float(info["format"].get("duration", post["duration"]))
            if not math.isfinite(duration) or duration <= 0:
                raise ValueError(f"{post_id}: invalid video duration")
            target = output / (post_id + ".mp4")
            # Rate budget leaves headroom for AAC/container data and host size limits.
            bitrate = min(1100000, int(MAX_OUTPUT * 8 * .86 / duration) - 64000)
            if bitrate < 80000:
                raise ValueError(f"{post_id}: clip too long for the portable host budget")
            ffmpeg(original, target, "-map", "0:v:0", "-map", "0:a:0?", "-vf", "scale=w='min(640,iw)':h='min(640,ih)':force_original_aspect_ratio=decrease:force_divisible_by=2,fps=24", "-c:v", "libx264", "-preset", "fast", "-profile:v", "main", "-pix_fmt", "yuv420p", "-b:v", str(bitrate), "-maxrate", str(bitrate), "-bufsize", str(bitrate * 2), "-threads", "2", "-c:a", "aac", "-b:a", "64k", "-ac", "2", "-ar", "44100", "-movflags", "+faststart")
            post["bundledVideoFile"] = target.name
            record(target, original, "video")
            thumbnail_source = target
        else:
            for index, url in enumerate(post["imageUrls"]):
                original = source("image" + str(index), url, f"{post_id}_{index}.jpg")
                target = output / f"{post_id}_{index}.jpg"
                ffmpeg(original, target, "-frames:v", "1", "-vf", "scale=w='min(1280,iw)':h='min(1280,ih)':force_original_aspect_ratio=decrease", "-q:v", "3")
                post["bundledImageFiles"].append(target.name)
                record(target, original, "image")
            thumbnail_source = output / post["bundledImageFiles"][0]
            if post["audioUrl"]:
                original = source("audio", post["audioUrl"], post_id + ".wav")
                target = output / (post_id + ".wav")
                ffmpeg(original, target, "-vn", "-c:a", "pcm_s16le", "-ac", "1", "-ar", "22050")
                post["bundledAudioFile"] = target.name
                record(target, original, "audio")
        thumbnail = output / (post_id + "_thumb.jpg")
        ffmpeg(thumbnail_source, thumbnail, "-frames:v", "1", "-vf", "scale=w='min(320,iw)':h='min(320,ih)':force_original_aspect_ratio=decrease", "-q:v", "4")
        post["bundledThumbnailFile"] = thumbnail.name
        record(thumbnail, thumbnail_source, "thumbnail")
    saved = {"id": post_id, "creator": post["creator"], "sourceUrl": post["sourceUrl"], "profile": "h264-640-24-v1",
             "imageCount": len(post["imageUrls"]), "hasAudio": bool(post["audioUrl"]), "assets": assets,
             "bundledFields": {key: value for key, value in post.items() if key.startswith("bundled")}}
    write_json(receipt_path, saved)
    return saved


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=ROOT / "Unity/Assets/StreamingAssets/TikTok")
    parser.add_argument("--work", type=Path, default=ROOT / "Unity/Builds/TikTokBundleWork")
    parser.add_argument("--cache", type=Path, default=Path.home() / "Library/Application Support/Machine Publishers/SLOP CITY/TikTok/media")
    parser.add_argument("--jobs", type=int, default=2)
    parser.add_argument("--featured-post", type=parse_featured_post,
                        help="Explicitly pin CREATOR/POST_ID; otherwise preserve the existing catalog pin")
    args = parser.parse_args()
    if not 1 <= args.jobs <= 4:
        parser.error("--jobs must be1–4")
    for tool in ("ffmpeg", "ffprobe"):
        if not shutil.which(tool):
            raise SystemExit(f"{tool} is required")
    args.work.mkdir(parents=True, exist_ok=True)
    media = args.output / "media"
    media.mkdir(parents=True, exist_ok=True)
    report_path = args.work / "bundle-report.json"
    report = {"startedAtUtc": utc(), "passed": False, "errors": [], "posts": []}
    try:
        previous_path = args.output / "catalog.json"
        previous_catalog = json.loads(previous_path.read_text()) if previous_path.is_file() else None
        catalog = fetch_catalog(args.work)
        write_json(args.work / "catalog-source.json", catalog)
        with concurrent.futures.ThreadPoolExecutor(max_workers=args.jobs) as pool:
            futures = {pool.submit(package_post, post, media, args.work, args.cache): post for post in catalog["videos"]}
            for future in concurrent.futures.as_completed(futures):
                post = futures[future]
                try:
                    result = future.result()
                    report["posts"].append(result)
                    print(f"BUNDLED {len(report['posts'])}/{len(catalog['videos'])} @{post['creator']} {post['id']} {post['kind']}", flush=True)
                except Exception as error:
                    report["errors"].append({"id": post["id"], "creator": post["creator"], "error": str(error)})
                    print(f"FAILED {post['id']}: {error}", flush=True)
                write_json(report_path, report)
        if report["errors"]:
            raise ValueError(f"{len(report['errors'])} post(s) could not be completely bundled; existing manifest preserved")
        verification = fetch_catalog(args.work / "verification")
        if {(p["creator"], p["id"]) for p in verification["videos"]} != {(p["creator"], p["id"]) for p in catalog["videos"]}:
            raise ValueError("Public catalogue changed during packaging; rerun to include newly posted items")
        featured = apply_featured_billboard(catalog, previous_catalog, args.featured_post)
        if featured:
            report.update({"featuredCreator": featured[0], "featuredPostId": featured[1]})
        expected = {asset["file"] for post in report["posts"] for asset in post["assets"]}
        # Removals are reconciled only after two complete matching listings.
        # Preserve Unity importer metadata; the build copies only referenced media.
        removed = []
        for path in media.iterdir():
            if path.is_file() and path.suffix.lower() in (".mp4", ".jpg", ".wav") and path.name not in expected:
                removed.append(path.name)
                path.unlink()
        report.update({"passed": True, "finishedAtUtc": utc(), "catalogAtUtc": catalog["syncedAtUtc"],
                       "accounts": catalog["accounts"], "postCount": len(catalog["videos"]),
                       "videoCount": sum(p["kind"] == "video" for p in catalog["videos"]),
                       "slideshowCount": sum(p["kind"] == "slideshow" for p in catalog["videos"]),
                       "assetCount": len(expected), "totalBytes": sum((media / name).stat().st_size for name in expected),
                       "maximumAssetBytes": max((media / name).stat().st_size for name in expected),
                       "removedUnreferencedMedia": removed,
                       "paginationPasses": 2,
                       "requestPolicy": {"concurrentTransfers": 1, "minimumStartIntervalSeconds": 3,
                                         "jitterSeconds": [0, 2], "authentication": "none"}})
        write_json(args.output / "catalog.json", catalog)
        write_json(args.output / "bundle-receipt.json", report)
        write_json(report_path, report)
        print(json.dumps({key: value for key, value in report.items() if key != "posts"}, indent=2))
    except Exception as error:
        report["errors"].append({"error": str(error)})
        report["finishedAtUtc"] = utc()
        write_json(report_path, report)
        raise


if __name__ == "__main__":
    main()
