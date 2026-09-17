"""Archive source and verify both deliverables without including local SDKs or settings."""
from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED
import hashlib
import subprocess
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
version = ET.parse(root / 'Directory.Build.props').findtext('.//Version')
artifacts = root / 'artifacts'
source = artifacts / f'CozyTranslator-v{version}-source.zip'
tracked = subprocess.check_output(['git', 'ls-files', '--cached', '-z'], cwd=root)
ignored = subprocess.run(['git', 'check-ignore', '--no-index', '-z', '--stdin'],
                         input=tracked, cwd=root, capture_output=True)
if ignored.returncode != 1:
    raise RuntimeError('Source packaging requires tracked files to be free of ignored paths: '
                       + (ignored.stdout or ignored.stderr).decode('utf-8'))
files = [root / name for name in tracked.decode('utf-8').split('\0') if name]
with ZipFile(source, 'w', ZIP_DEFLATED, compresslevel=9) as archive:
    for path in sorted(files):
        archive.write(path, 'CozyTranslator/' + path.relative_to(root).as_posix())

release = artifacts / f'CozyTranslator-v{version}-win-x64.zip'
checksums = []
for path in (release, source):
    with ZipFile(path) as archive:
        assert archive.testzip() is None, f'Corrupt archive: {path.name}'
        if path == release:
            prefix = f'CozyTranslator-v{version}-win-x64/'
            for name in ('CozyTranslator.exe', 'CozyTranslator.dll', 'hostfxr.dll',
                         'coreclr.dll', 'PresentationFramework.dll', 'Markdig.dll', 'WpfMath.dll',
                         'XamlMath.Shared.dll', 'LICENSE', 'THIRD-PARTY.md', 'README.md', 'README.zh-CN.md'):
                content = archive.read(prefix + name)
                assert content == (artifacts / prefix / name).read_bytes(), name
    digest = hashlib.sha256(path.read_bytes()).hexdigest()
    checksums.append(f'{digest}  {path.name}')
    print(f'Verified {path.name}: {path.stat().st_size / 1024**2:.2f} MiB')
(artifacts / f'SHA256SUMS-v{version}.txt').write_text('\n'.join(checksums) + '\n', encoding='utf-8')
