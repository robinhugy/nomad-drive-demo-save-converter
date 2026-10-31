"""Native selection workflows against synthetic worlds; no game installation required."""
import copy
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

from fixtures import c, world, make_slot

HARNESS=Path(sys.argv.pop(1)).resolve()
MAPPING=json.loads((Path(__file__).resolve().parents[1]/'src/engine/early-access.json').read_text())

class SelectionTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory(prefix='nomad_selection_')
        self.addCleanup(self.tmp.cleanup)
        self.root=Path(self.tmp.name)
        self.game=self.root/'game';catalog=self.game/'Nomad Drive_Data/StreamingAssets/aa/catalog.bin';catalog.parent.mkdir(parents=True);catalog.write_bytes(b'synthetic target catalog')
        self.mapping=copy.deepcopy(MAPPING);self.mapping['catalogSha256']=hashlib.sha256(catalog.read_bytes()).hexdigest()
        self.out=self.root/'output';self.backup=self.root/'backup';self.slots=[]
        for i in range(3):
            slot=make_slot(self.root/'source',remote=2 if i==1 else 0,name='World '+str(i),slot_id='slot_'+str(i))
            old=(slot/'save.dat').read_bytes();data,_=c.transform(old,self.mapping,True,3 if i==1 else 1)
            (slot/'save.dat').write_bytes(data);self.slots.append(slot)
        self.plan=dict(game=str(self.game),outputRoot=str(self.out),backupRoot=str(self.backup),selections=[])
    def request(self,index=0,action='migrate',players=None):
        slot=self.slots[index]
        return dict(source=str(slot),expectedSha256=hashlib.sha256((slot/'save.dat').read_bytes()).hexdigest(),action=action,players=players)
    def runplan(self,selections,apply=False,running=False):
        plan=copy.deepcopy(self.plan);plan['selections']=selections
        request=self.root/'request.json';result=self.root/'result.json'
        request.write_text(json.dumps(dict(mode='plan',mapping=self.mapping,plan=plan,apply=apply,game_running=running)),encoding='utf8')
        process=subprocess.run([str(HARNESS),str(request),str(result)],capture_output=True,timeout=60)
        data=json.loads(result.read_text(encoding='utf-8-sig'))
        if process.returncode:raise c.ConversionError(data['error'])
        return data
    def output(self,result,index=0):
        return Path(result['worlds'][index]['destination'])/'save.dat'
    def manifest(self,file):return c.parse_manifest(c.decode_save(file.read_bytes())[1]['game.manifest'])
    def assertNoWrites(self):self.assertFalse(self.out.exists());self.assertFalse(self.backup.exists())
    def test_preview_is_readonly(self):
        r=self.runplan([self.request()]);self.assertEqual(r['selected'],1);self.assertEqual(r['created'],0);self.assertNoWrites()
    def test_migration_keeps_current_save_byte_exact(self):
        before=[(s/'save.dat').read_bytes() for s in self.slots]
        r=self.runplan([self.request()],True)
        self.assertEqual(r['created'],1);self.assertEqual(self.output(r).read_bytes(),before[0]);self.assertEqual(len(list(self.out.glob('slot_*'))),1)
        self.assertEqual(before,[(s/'save.dat').read_bytes() for s in self.slots]);self.assertTrue(r['worlds'][0]['displayName'].endswith('(converted)'))
        self.assertEqual((Path(r['worlds'][0]['backup'])/'save.dat').read_bytes(),before[0])
    def test_enable_multiplayer_only_on_selected(self):
        r=self.runplan([self.request(action='multiplayer')],True)
        self.assertEqual(self.manifest(self.output(r))['economy']>>56,2)
        self.assertEqual(self.manifest(self.slots[2]/'save.dat')['economy']>>56,1)
    def test_enable_multiplayer_keeps_higher_limit(self):
        r=self.runplan([self.request(1,'multiplayer')],True);self.assertEqual(self.manifest(self.output(r))['economy']>>56,3)
    def test_lower_limit_retains_every_saved_player_and_sidecar(self):
        before=c.decode_save((self.slots[1]/'save.dat').read_bytes())[1]
        r=self.runplan([self.request(1,'players',1)],True);after=c.decode_save(self.output(r).read_bytes())[1]
        self.assertEqual(before['game.manifest'][:-1],after['game.manifest'][:-1]);self.assertEqual(after['game.manifest'][-1],1)
        self.assertEqual({k:v for k,v in before.items() if k!='game.manifest'},{k:v for k,v in after.items() if k!='game.manifest'})
        self.assertEqual(len(self.manifest(self.output(r))['remote']),2)
    def test_increase_to_eight(self):
        r=self.runplan([self.request(action='players',players=8)],True);self.assertEqual(self.manifest(self.output(r))['economy']>>56,8)
    def test_out_of_range_or_fractional_limit(self):
        for value in [0,9,1.5,'4']:
            with self.assertRaises(c.ConversionError):self.runplan([self.request(action='players',players=value)],True)
            self.assertNoWrites()
    def test_limit_required(self):
        with self.assertRaises(c.ConversionError):self.runplan([self.request(action='players')],True)
        self.assertNoWrites()
    def test_stale_snapshot_no_writes(self):
        req=self.request();(self.slots[0]/'save.dat').write_bytes(b'changed')
        with self.assertRaises(c.ConversionError):self.runplan([req],True)
        self.assertNoWrites()
    def test_missing_snapshot_no_writes(self):
        req=self.request();del req['expectedSha256']
        with self.assertRaises(c.ConversionError):self.runplan([req],True)
        self.assertNoWrites()
    def test_all_selected_preflight_before_writes(self):
        req=self.request(1);(self.slots[1]/'save.dat').write_bytes(b'corrupt');req['expectedSha256']=hashlib.sha256(b'corrupt').hexdigest()
        with self.assertRaises(c.ConversionError):self.runplan([self.request(),req],True)
        self.assertNoWrites()
    def test_duplicate_selection_rejected(self):
        with self.assertRaises(c.ConversionError):self.runplan([self.request(),self.request()],True)
        self.assertNoWrites()
    def test_empty_selection_rejected(self):
        with self.assertRaises(c.ConversionError):self.runplan([],True)
        self.assertNoWrites()
    def test_wrong_target_catalog_no_writes(self):
        self.mapping['catalogSha256']='0'*64
        with self.assertRaises(c.ConversionError):self.runplan([self.request()],True)
        self.assertNoWrites()
    def test_running_game_no_writes(self):
        with self.assertRaises(c.ConversionError):self.runplan([self.request()],True,True)
        self.assertNoWrites()
    def test_output_inside_selected_source_rejected(self):
        self.plan['outputRoot']=str(self.slots[1]/'nested')
        with self.assertRaises(c.ConversionError):self.runplan([self.request(),self.request(1)],True)
        self.assertFalse((self.slots[1]/'nested').exists());self.assertNoWrites()
    def test_backup_inside_source_rejected(self):
        self.plan['backupRoot']=str(self.slots[0]/'backup')
        with self.assertRaises(c.ConversionError):self.runplan([self.request()],True)
        self.assertFalse((self.slots[0]/'backup').exists());self.assertNoWrites()
    def test_legacy_demo_converts_to_early_access(self):
        data,_,_=world(2);(self.slots[0]/'save.dat').write_bytes(data)
        r=self.runplan([self.request()],True);m=self.manifest(self.output(r));self.assertEqual(m['format'],16);self.assertEqual(len(m['remote']),2);self.assertEqual(m['economy']>>56,3)
        self.assertEqual(r['worlds'][0]['targetEdition'],'Early Access');self.assertEqual(len(m['objects']),7)
    def test_mixed_formats_with_per_world_limits(self):
        data,_,_=world(2);(self.slots[0]/'save.dat').write_bytes(data)
        r=self.runplan([self.request(0,'players',1),self.request(1,'players',8)],True)
        self.assertEqual(r['created'],2);self.assertEqual(self.manifest(self.output(r))['economy']>>56,1);self.assertEqual(self.manifest(self.output(r,1))['economy']>>56,8);self.assertEqual(len(self.manifest(self.output(r))['remote']),2)
    def test_current_unknown_asset_refuses_without_omission(self):
        p=self.slots[0]/'save.dat';version,entries=c.decode_save(p.read_bytes());m=c.parse_manifest(entries['game.manifest']);m['objects'][0]['asset']='f'*32;entries['game.manifest']=c.serialize_manifest(m);p.write_bytes(c.encode_save(version,entries))
        with self.assertRaises(c.ConversionError):self.runplan([self.request()],True)
        self.assertNoWrites()
    def test_new_autosave_invalidates_preview_fingerprint(self):
        import os,time
        req=self.request();p=self.slots[0]/'autosave.dat';p.write_bytes((self.slots[1]/'save.dat').read_bytes());os.utime(p,(time.time()+60,time.time()+60))
        with self.assertRaises(c.ConversionError):self.runplan([req],True)
        self.assertNoWrites()

if __name__=='__main__':unittest.main(verbosity=2)
