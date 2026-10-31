"""Exercise the actual Windows Forms workflow against isolated synthetic saves."""
from pathlib import Path
import hashlib,json,os,subprocess,sys,tempfile
from fixtures import c,make_slot

repo=Path(__file__).resolve().parents[1]
compiler=Path(os.environ['WINDIR'])/'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
area=Path(sys.argv[1]).resolve() if len(sys.argv)>1 else Path(tempfile.mkdtemp(prefix='nomad_ui_workflow_'))
area.mkdir(parents=True,exist_ok=True)
mapping=json.loads((repo/'src/engine/early-access.json').read_text())
catalog=area/'game/Nomad Drive_Data/StreamingAssets/aa/catalog.bin';catalog.parent.mkdir(parents=True,exist_ok=True);catalog.write_bytes(b'synthetic UI target catalog')
mapping['catalogSha256']=hashlib.sha256(catalog.read_bytes()).hexdigest()
mapfile=area/'early-access-test.json';mapfile.write_text(json.dumps(mapping),encoding='utf8')
for i in range(3):
    slot=make_slot(area/'source',name='Synthetic world '+str(i),slot_id='slot_'+str(i))
    data,_=c.transform((slot/'save.dat').read_bytes(),mapping,True,1);(slot/'save.dat').write_bytes(data)
exe=area/'workflow-ui-tests.exe'
args=[str(compiler),'/nologo','/target:exe','/main:WorkflowUiTests','/define:TEST_HARNESS','/codepage:65001','/r:System.Core.dll','/r:System.Web.Extensions.dll','/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/out:'+str(exe),'/resource:'+str(mapfile)+',NomadDriveSaveConverter.EarlyAccessMapping','/resource:'+str(repo/'src/engine/mapping.json')+',NomadDriveSaveConverter.Mapping',str(repo/'.build/BuildInfo.cs'),str(repo/'src/gui/ConverterWindow.cs'),str(repo/'tests/WorkflowUiTests.cs')]+[str(p) for p in (repo/'src/engine').glob('*.cs')]
subprocess.run(args,check=True,timeout=60)
subprocess.run([str(exe),str(area)],check=True,timeout=60)
print('UI artifacts: '+str(area))
