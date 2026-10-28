"""Create an allowlisted GitHub source archive; exclude builds and user data."""
import hashlib
from pathlib import Path
import zipfile

repo=Path(__file__).resolve().parent.parent
version=(repo/'VERSION').read_text().strip()
dist=repo/'dist';dist.mkdir(exist_ok=True)
top={'README.md','README.hu.md','LICENSE','VERSION','requirements-build.txt','.gitignore','.gitattributes','.python-version'}
directories={'.github','scripts','src','tests'}
documentation={'docs/RELEASE_NOTES.md','docs/RELEASE_NOTES.hu.md'}
files=[]
for file in repo.rglob('*'):
    if not file.is_file():continue
    rel=file.relative_to(repo)
    if '__pycache__' in rel.parts or file.suffix.lower() in {'.pyc','.pyo','.exe','.dll','.dat','.bak','.log'}:continue
    if len(rel.parts)==1 and rel.name in top:files.append(file)
    elif rel.as_posix() in documentation:files.append(file)
    elif len(rel.parts)>1 and rel.parts[0] in directories:files.append(file)
files.sort()
source=dist/f'NomadDriveSaveConverter-v{version}-source.zip'
with zipfile.ZipFile(source,'w',zipfile.ZIP_DEFLATED,compresslevel=9) as archive:
    for file in files:
        name=f'NomadDriveSaveConverter-v{version}-source/'+file.relative_to(repo).as_posix()
        archive.write(file,name)
assets=[dist/f'NomadDriveSaveConverter-v{version}-win-x64.exe',
        dist/f'NomadDriveSaveConverter-v{version}-win-x64.zip',source]
checksums=[hashlib.sha256(p.read_bytes()).hexdigest()+'  '+p.name for p in assets if p.is_file()]
(dist/'SHA256SUMS.txt').write_text('\n'.join(checksums)+'\n',encoding='ascii')
print(f'Source archive: {source.name} ({len(files)} files)')
