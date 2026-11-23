#!/usr/bin/env python3
"""Fetch meshoptimizer (MIT) from the official zeux/meshoptimizer repository.

Resolves the latest GitHub release to its commit, downloads src/ from that
exact commit, verifies every file against the Git blob SHA-1 reported by the
Git Trees API, and records provenance next to the sources.

Replaces the archived, Windows-bound AMD Tootle for triangle-order
optimisation in the cross-platform Maya cooker.
"""
import hashlib
import json
import sys
import urllib.request
from pathlib import Path

REPO = 'zeux/meshoptimizer'
API = 'https://api.github.com'
HEADERS = {'User-Agent': 'xray-maya-cook-provision', 'Accept': 'application/vnd.github+json'}


def get(url, raw=False):
    request = urllib.request.Request(url, headers=HEADERS)
    with urllib.request.urlopen(request, timeout=120) as response:
        data = response.read()
    return data if raw else json.loads(data)


def blob_sha(data):
    return hashlib.sha1(b'blob %d\0' % len(data) + data).hexdigest()


def main(target):
    target = Path(target)
    release = get(f'{API}/repos/{REPO}/releases/latest')
    tag = release['tag_name']
    ref = get(f'{API}/repos/{REPO}/git/ref/tags/{tag}')
    sha = ref['object']['sha']
    if ref['object']['type'] == 'tag':  # annotated tag -> dereference to commit
        sha = get(f'{API}/repos/{REPO}/git/tags/{sha}')['object']['sha']
    tree = get(f'{API}/repos/{REPO}/git/trees/{sha}?recursive=1')
    if tree.get('truncated'):
        raise SystemExit('Git tree is truncated; refusing partial provisioning')
    wanted = [e for e in tree['tree'] if e['type'] == 'blob' and
              ((e['path'].startswith('src/') and e['path'].endswith(('.cpp', '.h'))) or e['path'] == 'LICENSE.md')]
    if not any(e['path'] == 'src/meshoptimizer.h' for e in wanted):
        raise SystemExit('src/meshoptimizer.h missing from tree')
    target.mkdir(parents=True, exist_ok=True)
    files = []
    for entry in wanted:
        data = get(f'https://raw.githubusercontent.com/{REPO}/{sha}/{entry["path"]}', raw=True)
        if blob_sha(data) != entry['sha']:
            raise SystemExit(f'Git blob hash mismatch: {entry["path"]}')
        destination = target / Path(entry['path']).name
        destination.write_bytes(data)
        files.append({'path': entry['path'], 'local': destination.name, 'gitBlob': entry['sha'],
                      'sha256': hashlib.sha256(data).hexdigest(), 'bytes': len(data)})
    record = {'repository': f'https://github.com/{REPO}', 'release': tag, 'commit': sha,
              'license': 'MIT', 'files': sorted(files, key=lambda f: f['path'])}
    (target / 'provenance.json').write_text(json.dumps(record, indent=2) + '\n', encoding='utf-8')
    print(f'meshoptimizer {tag} @ {sha}: {len(files)} verified files -> {target}')


if __name__ == '__main__':
    main(sys.argv[1] if len(sys.argv) > 1 else 'sources/third_party/meshoptimizer')

