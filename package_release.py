"""Package only release files; no settings, game data, personal fixtures, or caches.

Run after Build-Release.ps1. Uses only Python's standard library. A pre-collected
licenses directory is required; all output stays beneath the project publish/.
"""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import struct
import tarfile
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parent
RIDS = ('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')
DOCS = ('README.md', 'INSTALL.md', 'RELEASE_NOTES.md', 'VALIDATION.md',
        'THIRD-PARTY-NOTICES.md', 'artwork-requirements.txt')


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def verify_machine(path, rid):
    with path.open('rb') as stream:
        header = stream.read(64)
        arm = rid.endswith('arm64')
        if rid.startswith('win-'):
            assert header[:2] == b'MZ', 'Missing PE header'
            stream.seek(struct.unpack_from('<I', header, 60)[0])
            pe = stream.read(6)
            assert pe[:4] == b'PE\0\0'
            assert struct.unpack_from('<H', pe, 4)[0] == (0xaa64 if arm else 0x8664)
        elif rid.startswith('linux-'):
            assert header[:6] == b'\x7fELF\x02\x01', 'Expected 64-bit little-endian ELF'
            assert struct.unpack_from('<H', header, 18)[0] == (183 if arm else 62)
        else:
            assert header[:4] == b'\xcf\xfa\xed\xfe', 'Expected 64-bit Mach-O'
            assert struct.unpack_from('<I', header, 4)[0] == (0x100000c if arm else 0x1000007)


def source_files():
    # Explicit allowlist; never recurse through publish/, build/, artifacts/, or obj/.
    for name in (*DOCS, '.gitignore', 'SilverpineSilcEditor.csproj', 'Program.cs',
                 'Build-Release.ps1', 'package_release.py', 'collect_licenses.py'):
        yield ROOT / name
    for folder in ('Core', 'Desktop', 'UI', 'Properties', 'LegacyWinForms', 'Tests', 'PortableTests'):
        for path in sorted((ROOT / folder).iterdir()):
            if path.is_file() and path.suffix in ('.cs', '.csproj'):
                yield path
    for name in ('read_assets.py', 'test_read_assets.py', 'build_reader.py', 'requirements.txt'):
        yield ROOT / 'AssetReader' / name
    for name in ('README.md', 'silhouette.png', 'rule-of-thirds.png'):
        yield ROOT / 'Assets' / 'PortraitGuides' / name


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--licenses', type=Path, required=True)
    args = parser.parse_args()
    version = ET.parse(ROOT / 'SilverpineSilcEditor.csproj').findtext('.//Version')
    release = ROOT / 'publish' / f'release-{version}'
    archives = release / 'archives'
    archives.mkdir(parents=True, exist_ok=True)
    licenses = sorted(p for p in args.licenses.rglob('*') if p.is_file())
    if not licenses:
        raise RuntimeError('No third-party license files supplied')
    results = []
    for rid in RIDS:
        exe = 'SilverpineSilcEditor.exe' if rid.startswith('win-') else 'SilverpineSilcEditor'
        binary = release / rid / exe
        verify_machine(binary, rid)
        files = [(binary, exe)] + [(ROOT / name, name) for name in DOCS]
        files += [(p, 'licenses/' + p.relative_to(args.licenses).as_posix()) for p in licenses]
        package = f'SILC-Studio-{version}-{rid}'
        output = archives / (package + ('.zip' if rid.startswith('win-') else '.tar.gz'))
        if output.exists():
            raise FileExistsError(f'Refusing to replace existing package: {output}')
        if rid.startswith('win-'):
            with zipfile.ZipFile(output, 'x', zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
                for path, name in files:
                    archive.write(path, package + '/' + name)
            with zipfile.ZipFile(output) as archive:
                assert archive.testzip() is None
                assert len(archive.infolist()) == len(files)
                assert hashlib.sha256(archive.read(package + '/' + exe)).hexdigest() == digest(binary)
        else:
            with tarfile.open(output, 'w:gz', compresslevel=6) as archive:
                for path, name in files:
                    info = archive.gettarinfo(str(path), arcname=package + '/' + name)
                    info.uid = info.gid = 0
                    info.uname = info.gname = ''
                    info.mode = 0o755 if name == exe else 0o644
                    with path.open('rb') as stream:
                        archive.addfile(info, stream)
            with tarfile.open(output) as archive:
                assert len(archive.getmembers()) == len(files)
                member = archive.getmember(package + '/' + exe)
                assert member.mode == 0o755
                with archive.extractfile(member) as stream:
                    assert hashlib.file_digest(stream, 'sha256').hexdigest() == digest(binary)
        results.append(dict(rid=rid, file=output.name, size=output.stat().st_size,
                            sha256=digest(output), executableSha256=digest(binary)))
        print(f'Verified {output.name}: {output.stat().st_size:,} bytes', flush=True)

    source = archives / f'SILC-Studio-{version}-source.zip'
    # Also stage an audited source-only tree for the first repository commit.
    source_tree = release / 'source'
    source_tree.mkdir(exist_ok=True)
    with zipfile.ZipFile(source, 'x', zipfile.ZIP_DEFLATED) as archive:
        for path in source_files():
            relative = path.relative_to(ROOT)
            archive.write(path, f'SILC-Studio-{version}-source/' + relative.as_posix())
            destination = source_tree / relative
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(path, destination)
    with zipfile.ZipFile(source) as archive:
        assert archive.testzip() is None
    results.append(dict(rid='source', file=source.name, size=source.stat().st_size,
                        sha256=digest(source)))
    (archives / 'SHA256SUMS.txt').write_text(
        ''.join(f"{item['sha256']}  {item['file']}\n" for item in results), encoding='utf-8')
    (release / 'package-manifest.json').write_text(json.dumps(results, indent=2) + '\n', encoding='utf-8')
    print(f'Verified source archive and checksums: {archives}', flush=True)


if __name__ == '__main__':
    main()
