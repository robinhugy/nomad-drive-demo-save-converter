import copy
import json
import os
from pathlib import Path
import struct
import subprocess
import sys
import tempfile
import types
import unittest
from unittest.mock import patch

from fixtures import c,world,make_slot,CAR,HOOD,TIRE,APPLE,REMOVED


class ConverterTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory(prefix='nomad-converter-test-')
        self.addCleanup(self.tmp.cleanup);self.root=Path(self.tmp.name)
        self.slots=self.root/'saves';self.slots.mkdir();self.backups=self.root/'backups'
        self.game=self.root/'game';catalog=self.game/'Nomad Drive Demo_Data/StreamingAssets/aa/catalog.bin'
        catalog.parent.mkdir(parents=True);catalog.write_bytes(b'synthetic supported catalog')
        self.mapping=c.load_mapping();self.mapping['catalogSha256']=c.sha(catalog.read_bytes())
        self.source=make_slot(self.slots);self.data=(self.source/'save.dat').read_bytes()
        self.running=patch.object(c,'game_running',return_value=False);self.running.start();self.addCleanup(self.running.stop)

    def transform(self,data=None,**kw):return c.transform(data or self.data,self.mapping,**kw)

    def convert(self,source=None,**kw):
        return c.convert_slot(source or self.source,self.slots,self.backups,self.mapping,self.game,**kw)

    def batch(self,**kw):return c.batch_convert(self.slots,self.backups,self.mapping,self.game,**kw)

    def edit_legacy(self,fn):
        version,entries=c.decode_save(self.data);m=c.parse_manifest(entries['game.manifest']);fn(m,entries)
        entries['game.manifest']=c.serialize_manifest(m);return c.encode_save(version,entries)

    def test_legacy_gzip_and_new_envelope(self):
        self.assertTrue(self.data.startswith(b'\x1f\x8b'));new,report=self.transform()
        self.assertTrue(new.startswith(b'EVL2'));self.assertEqual(report['sourceFormat'],5);self.assertEqual(report['targetFormat'],16)

    def test_supported_objects_replacement_and_omission(self):
        data,r=self.transform();m=c.parse_manifest(c.decode_save(data)[1]['game.manifest'])
        self.assertEqual((r['sourceObjects'],r['outputObjects'],r['vehicles']),(7,6,1))
        self.assertEqual(len(r['replacements']),1);self.assertEqual(r['omitted'][0]['guid'],REMOVED)
        tire=next(o for o in m['objects'] if o['guid']==TIRE)
        self.assertEqual(tire['asset'],'5bacfe73ed441194095b2eb79cf47062')

    def test_object_ids_positions_rotations_and_seeds_retained(self):
        out,_=self.transform();m=c.parse_manifest(c.decode_save(out)[1]['game.manifest']);old=world()[1]
        source={o['guid']:o for o in old['objects']}
        for obj in m['objects']:
            for key in ('guid','position','rotation','seed'):self.assertEqual(obj[key],source[obj['guid']][key])
        for key in ('host','spawn','seed','time','seeds','saved_at'):self.assertEqual(m[key],old[key])

    def test_png_bytes_are_retained_under_stable_surface_id(self):
        out,r=self.transform();entries=c.decode_save(out)[1]
        self.assertEqual(len(r['paintHashes']),1)
        key=next(iter(r['paintHashes']));c.valid_png(entries[key])
        self.assertEqual(entries[key],world()[2][f'restoration.{HOOD}.surf0.tex0'])

    def test_opaque_old_global_state_is_archived(self):
        out,_=self.transform();entries=c.decode_save(out)[1]
        self.assertNotIn('synthetic/objectives',entries)
        self.assertEqual(entries['legacy.converter.synthetic/objectives'],world()[2]['synthetic/objectives'])

    def test_vehicle_links_remain_attached(self):
        out,_=self.transform();objects=c.parse_manifest(c.decode_save(out)[1]['game.manifest'])['objects']
        vehicle=next(o for o in objects if o['guid']==CAR);blob=vehicle['state']['vehicle']
        self.assertEqual(blob[0],1);self.assertIn(HOOD.encode(),blob);self.assertEqual(blob[-4:],b'\0'*4)
        hood=next(o for o in objects if o['guid']==HOOD);self.assertEqual((hood['parent'],hood['kind'],hood['sub']),(CAR,3,2))

    def test_placement_reconstructs_the_original_world_pose(self):
        out,_=self.transform();objs={o['guid']:o for o in c.parse_manifest(c.decode_save(out)[1]['game.manifest'])['objects']}
        r=c.Reader(objs[TIRE]['state']['placement']);self.assertEqual((r.byte(),r.byte()),(1,1))
        parent=r.string();pid=r.u();local=r.floats(3);rotation=r.floats(4);rootpos=r.floats(3);rootrot=r.floats(4);r.end()
        support=objs[parent];plane=next(p for p in self.mapping['assets'][support['asset']]['planes'] if p['planeId']==pid)
        p=c.qrot(plane['rotation'],local);p=tuple(a+b for a,b in zip(p,plane['position']))
        p=c.qrot(support['rotation'],p);p=tuple(a+b for a,b in zip(p,support['position']))
        self.assertLess(max(abs(a-b) for a,b in zip(p,objs[TIRE]['position'])),0.0001)

    def test_no_replacement_option_is_honored(self):
        _,report=self.transform(replace_missing=False)
        self.assertEqual(report['replacements'],[]);self.assertEqual(len(report['omitted']),2)

    def test_auto_player_limit_never_makes_a_legacy_world_solo(self):
        _,r=self.transform();self.assertEqual(r['playerSlots'],2);self.assertTrue(r['multiplayerEnabled'])

    def test_multiplayer_limit_includes_host_and_saved_remote_players(self):
        for remote in (1,2,3,7):
            out,r=self.transform(world(remote)[0]);m=c.parse_manifest(c.decode_save(out)[1]['game.manifest'])
            self.assertEqual(r['playerSlots'],remote+1);self.assertEqual(m['economy']>>56,remote+1)
            self.assertEqual(len(m['remote']),remote)

    def test_player_override_is_written(self):
        out,r=self.transform(player_slots=4);self.assertEqual(r['playerSlots'],4)
        self.assertEqual(c.parse_manifest(c.decode_save(out)[1]['game.manifest'])['economy']>>56,4)

    def test_player_override_cannot_exclude_saved_players(self):
        with self.assertRaises(c.ConversionError):self.transform(world(2)[0],player_slots=2)

    def test_invalid_player_limits_fail(self):
        for value in (0,9,-1,1.5,True,'4'):
            with self.assertRaises(c.ConversionError):self.transform(player_slots=value)

    def test_repair_changes_only_the_player_limit_byte(self):
        current,_=self.transform(world(2)[0]);version,entries=c.decode_save(current)
        manifest=c.parse_manifest(entries['game.manifest']);manifest['economy']=c.economy_with_players(manifest['economy'],1)
        entries['game.manifest']=c.serialize_manifest(manifest);broken=c.encode_save(version,entries)
        repaired,r=c.repair_multiplayer(broken,self.mapping);after=c.decode_save(repaired)[1]
        self.assertEqual(r['playerSlots'],3);self.assertEqual(r['previousPlayerSlots'],1)
        self.assertEqual(entries['game.manifest'][:-1],after['game.manifest'][:-1])
        self.assertEqual({k:v for k,v in entries.items() if k!='game.manifest'},{k:v for k,v in after.items() if k!='game.manifest'})

    def test_repair_preserves_resource_density_bytes(self):
        current,_=self.transform();old=c.parse_manifest(c.decode_save(current)[1]['game.manifest'])['economy']
        repaired,_=c.repair_multiplayer(current,self.mapping,4)
        new=c.parse_manifest(c.decode_save(repaired)[1]['game.manifest'])['economy']
        self.assertEqual(old & ((1<<56)-1),new & ((1<<56)-1))

    def test_repair_will_not_force_solo(self):
        current,_=self.transform()
        with self.assertRaises(c.ConversionError):c.repair_multiplayer(current,self.mapping,1)

    def test_repair_requires_current_format(self):
        with self.assertRaises(c.ConversionError):c.repair_multiplayer(self.data,self.mapping)

    def test_converted_suffix_is_english_and_not_duplicated(self):
        for name in ('Example','Example (converted)','Example (CONVERTED)','Example (\u00e1talak\u00edtott)'):
            self.assertEqual(c.converted_name(name),'Example (converted)')

    def test_backup_and_separate_slot_are_created(self):
        before={p.name:p.read_bytes() for p in self.source.iterdir()};r=self.convert()
        dest=Path(r['destination']);meta=c.read_meta(dest)
        self.assertNotEqual(dest,self.source);self.assertEqual(meta['displayName'],'Example world (converted)')
        self.assertEqual(meta['contentFormatVersion'],16);self.assertEqual(meta['generation'],1)
        self.assertEqual(meta['contentHash'],c.sha((dest/'save.dat').read_bytes()))
        self.assertEqual((Path(r['backup'])/'save.dat').read_bytes(),self.data)
        self.assertEqual(before,{p.name:p.read_bytes() for p in self.source.iterdir()})

    def test_consecutive_manual_runs_never_overwrite(self):
        a=self.convert();b=self.convert();self.assertNotEqual(a['destination'],b['destination'])
        self.assertEqual(a['backup'],b['backup'])

    def test_source_folder_cannot_be_used_as_destination(self):
        with self.assertRaises(c.ConversionError):c.convert_slot(self.source,self.source,self.backups,self.mapping,self.game)

    def test_backup_folder_cannot_be_inside_source(self):
        with self.assertRaises(c.ConversionError):c.convert_slot(self.source,self.slots,self.source/'backup',self.mapping,self.game)

    def test_game_running_prevents_any_output(self):
        c.game_running.return_value=True
        with self.assertRaises(c.ConversionError):self.convert()
        self.assertFalse(self.backups.exists());self.assertEqual(len(list(self.slots.iterdir())),1)

    def test_wrong_build_prevents_any_output(self):
        bad=copy.deepcopy(self.mapping);bad['catalogSha256']='0'*64
        with self.assertRaises(c.ConversionError):c.convert_slot(self.source,self.slots,self.backups,bad,self.game)
        self.assertFalse(self.backups.exists());self.assertEqual(len(list(self.slots.iterdir())),1)

    def test_corrupt_source_prevents_any_output(self):
        (self.source/'save.dat').write_bytes(b'not a save')
        with self.assertRaises(c.ConversionError):self.convert()
        self.assertFalse(self.backups.exists());self.assertEqual(len(list(self.slots.iterdir())),1)

    def test_crc_and_truncation_are_rejected(self):
        out,_=self.transform();corrupt=bytearray(out);corrupt[-1]^=1
        for data in (bytes(corrupt),out[:13],out[:-1],self.data[:-8]):
            with self.assertRaises(c.ConversionError):c.decode_save(data)

    def test_unknown_format_and_contributor_are_rejected(self):
        with self.assertRaises(c.ConversionError):self.transform(self.edit_legacy(lambda m,e:m.update(format=15)))
        with self.assertRaises(c.ConversionError):self.transform(self.edit_legacy(lambda m,e:m['objects'][0]['state'].update(unknown=b'\1')))

    def test_duplicate_identifiers_are_rejected(self):
        with self.assertRaises(c.ConversionError):self.transform(self.edit_legacy(lambda m,e:m['objects'].append(copy.deepcopy(m['objects'][0]))))

    def test_missing_or_corrupt_paint_is_rejected(self):
        with self.assertRaises(c.ConversionError):self.transform(self.edit_legacy(lambda m,e:e.pop(f'restoration.{HOOD}.surf0.tex0')))
        with self.assertRaises(c.ConversionError):self.transform(self.edit_legacy(lambda m,e:e.update({f'restoration.{HOOD}.surf0.tex0':b'bad png'})))

    def test_newer_autosave_is_selected(self):
        auto=self.source/'autosave.dat';auto.write_bytes(self.data)
        os.utime(self.source/'save.dat',(10,10));os.utime(auto,(20,20))
        r=self.convert();self.assertEqual(Path(r['source']).name,'autosave.dat')

    def test_batch_converts_every_legacy_slot_and_is_idempotent(self):
        make_slot(self.slots,1,'Second world','slot_second');make_slot(self.slots,2,'Third world','slot_third')
        first=self.batch();self.assertEqual((first['converted'],first['repaired'],len(first['errors'])),(3,0,0))
        limits=sorted(r['playerSlots'] for r in first['worlds']);self.assertEqual(limits,[2,2,3])
        count=len(list(self.slots.iterdir()));again=self.batch()
        self.assertEqual((again['converted'],again['repaired']),(0,0));self.assertEqual(len(list(self.slots.iterdir())),count)

    def test_batch_repairs_latest_progress_instead_of_reconverting_old_world(self):
        self.source=make_slot(self.slots,2,'Co-op','slot_coop');report=self.convert()
        current=Path(report['destination']);version,entries=c.decode_save((current/'save.dat').read_bytes())
        m=c.parse_manifest(entries['game.manifest']);m['objects'][0]['position']=(999.,888.,777.)
        m['economy']=c.economy_with_players(m['economy'],1);entries['game.manifest']=c.serialize_manifest(m)
        (current/'save.dat').write_bytes(c.encode_save(version,entries))
        result=self.batch();self.assertEqual(result['repaired'],1)
        repaired=next(r for r in result['worlds'] if r['status']=='repaired')
        after=c.parse_manifest(c.decode_save((Path(repaired['destination'])/'save.dat').read_bytes())[1]['game.manifest'])
        self.assertEqual(after['objects'][0]['position'],(999.,888.,777.));self.assertEqual(after['economy']>>56,3)
        again=self.batch();self.assertEqual((again['converted'],again['repaired']),(0,0))

    def test_batch_ignores_native_current_format_worlds(self):
        out,_=self.transform();(self.source/'save.dat').write_bytes(out)
        r=self.batch();self.assertEqual((r['converted'],r['repaired']),(0,0));self.assertEqual(r['worlds'][0]['status'],'current-format')

    def test_batch_continues_past_a_damaged_slot(self):
        bad=make_slot(self.slots,0,'Damaged','slot_bad');(bad/'save.dat').write_bytes(b'bad')
        r=self.batch();self.assertEqual(r['converted'],1);self.assertEqual(len(r['errors']),1)
        self.assertEqual((bad/'save.dat').read_bytes(),b'bad')

    def test_batch_skips_metadata_only_folders(self):
        empty=self.slots/'slot_empty';empty.mkdir();(empty/'meta.json').write_text('{}')
        r=self.batch();self.assertEqual(r['converted'],1);self.assertFalse(r['errors'])

    def test_batch_repair_works_when_original_legacy_slot_is_gone(self):
        r=self.convert();current=Path(r['destination'])
        version,entries=c.decode_save((current/'save.dat').read_bytes());m=c.parse_manifest(entries['game.manifest'])
        m['economy']=c.economy_with_players(m['economy'],1);entries['game.manifest']=c.serialize_manifest(m)
        (current/'save.dat').write_bytes(c.encode_save(version,entries))
        for file in self.source.iterdir():file.unlink()
        self.source.rmdir();result=self.batch();self.assertEqual(result['repaired'],1)

    def test_batch_does_not_create_duplicates_for_identical_source_fingerprints(self):
        self.convert();duplicate=make_slot(self.slots,0,'Duplicate','slot_duplicate')
        result=self.batch();self.assertEqual((result['converted'],result['repaired']),(0,0))

    def test_read_only_repeat_is_allowed_while_game_runs(self):
        self.batch();c.game_running.return_value=True
        result=self.batch();self.assertEqual((result['converted'],result['repaired']),(0,0))

    def test_steam_library_discovery_uses_configured_library_paths(self):
        steam=self.root/'ProgramFiles/Steam';(steam/'steamapps').mkdir(parents=True)
        library=self.root/'ExtraLibrary';game=library/'steamapps/common/Nomad Drive Demo/Nomad Drive Demo_Data/StreamingAssets/aa'
        game.mkdir(parents=True);(game/'catalog.bin').write_bytes(b'catalog')
        escaped=str(library).replace('\\','\\\\')
        (steam/'steamapps/libraryfolders.vdf').write_text('"libraryfolders" { "0" { "path" "'+escaped+'" } }',encoding='utf8')
        fake=types.SimpleNamespace(HKEY_CURRENT_USER=1,HKEY_LOCAL_MACHINE=2,OpenKey=lambda *a:(_ for _ in ()).throw(OSError()))
        with patch.dict(os.environ,{'ProgramFiles':str(self.root/'ProgramFiles')},clear=True),patch.dict(sys.modules,{'winreg':fake}):
            self.assertEqual(c.discover_game(),game.parents[2])

    def test_cli_lists_synthetic_worlds(self):
        script=Path(__file__).resolve().parents[1]/'src/engine/converter_engine.py'
        out=self.root/'report.json'
        result=subprocess.run([sys.executable,str(script),'--list','--output-root',str(self.slots),'--report',str(out)],capture_output=True)
        self.assertEqual(result.returncode,0);report=json.loads(out.read_text(encoding='utf8'));self.assertEqual(report['slots'][0]['name'],'Example world')

    def test_english_report_and_version_match_release_metadata(self):
        _,report=self.transform();text=c.report_text(report)
        self.assertIn('REPLACEMENTS',text);self.assertIn('Player slots: 2',text)
        self.assertEqual(c.VERSION,(Path(__file__).resolve().parents[1]/'VERSION').read_text().strip())


if __name__=='__main__':unittest.main(verbosity=2)
