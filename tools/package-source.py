"""Archive source and verify both deliverables without including local SDKs or settings."""
from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED
import hashlib
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
version = ET.parse(root / 'Directory.Build.props').findtext('.//Version')
artifacts = root / 'artifacts'
source = artifacts / f'CozyTranslator-v{version}-source.zip'
folders = ('src', 'tests', 'tools', '设计', 'docs')
files = [root / name for name in ('.gitignore', 'AGENTS.md', 'build.ps1', 'Directory.Build.props', 'global.json', 'README.md', 'THIRD-PARTY.md', 'VALIDATION.md', 'LICENSE')]
for folder in folders:
    files.extend(p for p in (root / folder).rglob('*') if p.is_file()
                 and not set(p.relative_to(root).parts) & {'bin', 'obj', '__pycache__'})
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
                         'XamlMath.Shared.dll', 'LICENSE', 'THIRD-PARTY.md', 'README.md', 'VALIDATION.md'):
                content = archive.read(prefix + name)
                assert content == (artifacts / prefix / name).read_bytes(), name
    digest = hashlib.sha256(path.read_bytes()).hexdigest()
    checksums.append(f'{digest}  {path.name}')
    print(f'Verified {path.name}: {path.stat().st_size / 1024**2:.2f} MiB')
(artifacts / f'SHA256SUMS-v{version}.txt').write_text('\n'.join(checksums) + '\n', encoding='utf-8')
