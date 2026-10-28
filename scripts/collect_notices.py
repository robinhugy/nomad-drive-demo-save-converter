"""Collect the exact installed build/runtime licenses for the Windows release."""
import importlib.metadata
from pathlib import Path
import sys

repo=Path(__file__).resolve().parent.parent
out=Path(sys.argv[1])
python_license=Path(sys.base_prefix)/'LICENSE.txt'
if not python_license.is_file():raise SystemExit('The installed Python LICENSE.txt was not found.')
distribution=importlib.metadata.distribution('pyinstaller')
copying=next((distribution.locate_file(p) for p in distribution.files if str(p).endswith('licenses/COPYING.txt')),None)
if copying is None:raise SystemExit('The installed PyInstaller COPYING.txt was not found.')
sections=[
    'Nomad Drive Save Converter: licenses and third-party notices\n',
    'Application source license\n\n'+(repo/'LICENSE').read_text(encoding='utf8'),
    'Python runtime '+sys.version.split()[0]+'\n\n'+python_license.read_text(encoding='utf8'),
    'PyInstaller bootloader '+distribution.version+'\n\n'+Path(copying).read_text(encoding='utf8'),
    'Game names and compatibility identifiers belong to their respective owners.\n'
    'This distribution contains no game assemblies, media, or personal save files.\n',
]
out.parent.mkdir(parents=True,exist_ok=True)
out.write_text('\n\n'.join(sections),encoding='utf8')
