"""Collect upstream notices for a release; no credentials or build paths are copied."""
import argparse
import json
from pathlib import Path
import shutil
import sys
import urllib.request
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent
UPSTREAM = {
    'Avalonia-LICENSE.md': 'https://raw.githubusercontent.com/AvaloniaUI/Avalonia/11.3.12/licence.md',
    'Inter-OFL.txt': 'https://raw.githubusercontent.com/rsms/inter/v4.1/LICENSE.txt',
    'MicroCom-LICENSE': 'https://raw.githubusercontent.com/kekekeks/MicroCom/master/LICENSE',
    'Tmds.DBus-COPYING': 'https://raw.githubusercontent.com/tmds/Tmds.DBus/8cb04f66c330b64244e996ff68f685f27f7381a0/COPYING',
}


def legal(path):
    name = path.name.lower()
    return any(word in name for word in ('license', 'licence', 'copying', 'notice', 'copyright'))


def collect(source, destination, metadata=False):
    count = 0
    for path in sorted(source.rglob('*')):
        if not path.is_file():
            continue
        if not (legal(path) or (metadata and path.suffix == '.nuspec')):
            continue
        if path.suffix.lower() in ('.dll', '.exe', '.pdb', '.pyc'):
            continue
        target = destination / path.relative_to(source)
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(path, target)
        count += 1
    return count


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--nuget', type=Path, default=Path.home() / '.nuget/packages')
    parser.add_argument('--python-site', type=Path, action='append', required=True)
    args = parser.parse_args()
    version = ET.parse(ROOT / 'SilverpineSilcEditor.csproj').findtext('.//Version')
    output = ROOT / 'publish' / f'release-{version}' / 'licenses'
    output.mkdir(parents=True, exist_ok=True)
    assets = json.loads((ROOT / 'obj/project.assets.json').read_text(encoding='utf-8-sig'))
    count = 0
    for name, entry in assets['libraries'].items():
        if entry['type'] != 'package':
            continue
        path = args.nuget / name.lower()
        count += collect(path, output / 'dotnet' / name, metadata=True)
    for rid in ('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64'):
        parent = args.nuget / f'microsoft.netcore.app.runtime.{rid}'
        # All six builds in this release use the same SDK-selected runtime.
        versions = [p for p in parent.iterdir() if p.is_dir() and p.name.startswith('10.')]
        runtime = max(versions, key=lambda p: tuple(map(int, p.name.split('.'))))
        count += collect(runtime, output / 'dotnet-runtime' / rid / runtime.name, metadata=True)
    for site in args.python_site:
        for package in sorted(site.glob('*.dist-info')):
            if package.name.startswith(('fmod_toolkit-', 'pyfmodex-')):
                continue  # Deliberately excluded from the image-only executable.
            count += collect(package, output / 'windows-python' / package.name)
    python_license = Path(sys.base_prefix) / 'LICENSE.txt'
    if not python_license.is_file():
        raise FileNotFoundError('Run with the Python used to build the Windows helper')
    shutil.copy2(python_license, output / 'windows-python' / 'CPython-LICENSE.txt')
    for name, url in UPSTREAM.items():
        data = urllib.request.urlopen(url, timeout=30).read()
        if len(data) < 100 or b'<html' in data[:200].lower():
            raise ValueError(f'Unexpected upstream license response: {url}')
        (output / name).write_bytes(data)
    (output / 'UPSTREAM-SOURCES.txt').write_text(
        ''.join(f'{name}: {url}\n' for name, url in UPSTREAM.items()), encoding='utf-8')
    print(f'Collected {count + len(UPSTREAM) + 2} notices/metadata files in {output}')


if __name__ == '__main__':
    main()
