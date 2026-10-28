"""Synthetic ownership, provenance and write-safety regression tests."""
import copy
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

from fixtures import c, world, make_slot, CAR, HOOD, TIRE, APPLE, CHEST, PLANT
import loot_cleanup as loot


class LootCleanupTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.root=Path(self.temp.name); self.slots=self.root/'saves'; self.slots.mkdir()
        self.backups=self.root/'backups'; self.source=make_slot(self.slots)
        self.legacy=(self.source/'save.dat').read_bytes(); self.mapping=c.load_mapping()
        self.current,_=c.transform(self.legacy,self.mapping)
        self.game=self.root/'game'; catalog=self.game/'Nomad Drive Demo_Data/StreamingAssets/aa/catalog.bin'
        catalog.parent.mkdir(parents=True); catalog.write_bytes(b'synthetic catalog')
        self.mapping['catalogSha256']=c.sha(catalog.read_bytes())
        self.guard=patch.object(c,'game_running',return_value=False); self.guard.start(); self.addCleanup(self.guard.stop)

    def edit(self, callback):
        version,entries=c.decode_save(self.current); m=c.parse_manifest(entries['game.manifest'])
        callback(m,entries); entries['game.manifest']=c.serialize_manifest(m)
        return c.encode_save(version,entries)

    def clean(self,data=None):
        out,report=loot.clean(data or self.current,self.legacy,self.mapping)
        m=c.parse_manifest(c.decode_save(out)[1]['game.manifest'])
        return m,report,out

    def obj(self,guid,state=None,parent=''):
        return dict(guid=guid,asset=self.mapping['replacements']['0b832cfc907d0e04a9e3780cca1d90ef'],
                    seed=0,position=(10.,20.,30.),rotation=(0.,0.,0.,1.),parent=parent,kind=0,sub=0,
                    state=state or {})

    def slot(self,**kwargs):
        return c.convert_slot(self.source,self.slots,self.backups,self.mapping,self.game,**kwargs)

    def test_unowned_imports_removed_but_vehicle_parts_and_nested_cargo_kept(self):
        m,r,_=self.clean(); self.assertEqual({o['guid'] for o in m['objects']},{CAR,HOOD,TIRE})
        self.assertEqual({o['guid'] for o in r['removed']},{APPLE,CHEST,PLANT})

    def test_players_survival_world_settings_and_all_sidecars_remain_exact(self):
        def edit(m,e):
            m['host'].update(equipped=APPLE,hotbar=[APPLE],slots=2)
            m['time']=20.75; m['host']['position']=(777.,888.,999.)
            e['new-native-objective-state']=b'new progress'; e['picture.unused']=b'keep archive'
        data=self.edit(edit); m,_,out=self.clean(data)
        before=c.decode_save(data)[1]; after=c.decode_save(out)[1]
        old=c.parse_manifest(before['game.manifest'])
        for key in old:
            if key not in ('objects','seeds'):self.assertEqual(old[key],m[key],key)
        self.assertEqual({k:v for k,v in before.items() if k!='game.manifest'},
                         {k:v for k,v in after.items() if k!='game.manifest'})
        self.assertIn(APPLE,{o['guid'] for o in m['objects']})

    def test_remote_hotbar_and_worn_backpack_contents_are_kept(self):
        def edit(m,e):
            p=copy.deepcopy(m['host']); p.update(puid='remote',hotbar=[CHEST],backpack=PLANT)
            m['remote']=[p]
            w=c.Writer(); w.byte(2); w.byte(3); w.byte(1); w.string(APPLE)
            next(o for o in m['objects'] if o['guid']==PLANT)['state']['backpack']=w.finish()
        m,_,_=self.clean(self.edit(edit))
        self.assertTrue({CHEST,PLANT,APPLE}<={o['guid'] for o in m['objects']})

    def test_new_object_protects_its_legacy_container_and_siblings(self):
        data=self.edit(lambda m,e:m['objects'].append(self.obj('new-item',parent=CHEST)))
        m,_,_=self.clean(data); self.assertTrue({CHEST,'new-item'}<={o['guid'] for o in m['objects']})

    def test_moved_import_is_preserved_without_reverting_progress(self):
        def edit(m,e):next(o for o in m['objects'] if o['guid']==APPLE).update(position=(99.,20.,30.))
        m,_,_=self.clean(self.edit(edit))
        self.assertEqual(next(o for o in m['objects'] if o['guid']==APPLE)['position'],(99.,20.,30.))

    def test_unowned_linked_ground_components_are_removed_together(self):
        def edit(m,e):next(o for o in m['objects'] if o['guid']==APPLE).update(parent=CHEST,kind=4)
        m,_,_=self.clean(self.edit(edit)); self.assertFalse({CHEST,APPLE}&{o['guid'] for o in m['objects']})

    def test_current_only_objects_are_kept_even_without_native_marker(self):
        data=self.edit(lambda m,e:m['objects'].append(self.obj('trainer-created-item')))
        m,_,_=self.clean(data); self.assertIn('trainer-created-item',{o['guid'] for o in m['objects']})

    def test_seed_reset_keeps_new_collected_reservations_and_overlapping_live_marker(self):
        def edit(m,e):
            m['seeds']=[42,99,123,456]
            m['objects'].append(self.obj('native-live',{'_wildLootSeed':b'\1'+c.struct.pack('<i',42)}))
        m,r,_=self.clean(self.edit(edit)); self.assertEqual(m['seeds'],[42,123,456])
        self.assertEqual(r['resetLegacySeeds'],1)

    def test_native_marker_missing_from_ledger_is_reserved(self):
        data=self.edit(lambda m,e:m['objects'].append(self.obj('native-live',{'_wildLootSeed':b'\1'+c.struct.pack('<i',777)})))
        m,_,_=self.clean(data); self.assertEqual(m['seeds'],[777])

    def test_unknown_link_schema_or_bad_native_marker_fails_closed(self):
        for key,blob in [('placement',b'\2'),('vehicle',b'\2'),('backpack',b'\3'),('plant',b'\2'),('_wildLootSeed',b'\1')]:
            def edit(m,e):m['objects'][0]['state'][key]=blob
            with self.subTest(key=key),self.assertRaises(c.ConversionError):self.clean(self.edit(edit))

    def test_different_world_seed_is_rejected(self):
        with self.assertRaises(c.ConversionError):self.clean(self.edit(lambda m,e:m.update(seed=789)))

    def test_repeated_pure_cleanup_is_idempotent(self):
        _,_,first=self.clean(); _,r,second=self.clean(first)
        self.assertEqual(first,second); self.assertEqual(r['removedCount'],0)

    def test_cleanup_creates_backup_and_separate_slot_without_changing_source(self):
        before={p.name:p.read_bytes() for p in self.source.iterdir()}
        r=self.slot(cleanup_loot=True)
        self.assertEqual(r['lootCleanup']['removedCount'],3)
        self.assertEqual(r['displayName'],'Example world (converted)')
        self.assertEqual(before,{p.name:p.read_bytes() for p in self.source.iterdir()})
        self.assertEqual((Path(r['backup'])/'save.dat').read_bytes(),self.legacy)

    def test_existing_conversion_uses_latest_autosave_and_preserves_lineage(self):
        r=self.slot(); self.source=Path(r['destination'])
        latest=self.edit(lambda m,e:next(o for o in m['objects'] if o['guid']==APPLE).update(position=(99.,20.,30.)))
        (self.source/'autosave.dat').write_bytes(latest)
        report=self.slot(repair=True,cleanup_loot=True)
        after=c.parse_manifest(c.decode_save((Path(report['destination'])/'save.dat').read_bytes())[1]['game.manifest'])
        self.assertEqual(next(o for o in after['objects'] if o['guid']==APPLE)['position'],(99.,20.,30.))
        self.assertEqual(report['originalSourceSha256'],c.sha(self.legacy))
        self.assertTrue((Path(report['backup'])/'conversion-report.json').is_file())

    def test_original_can_be_found_in_backup_when_legacy_slot_is_gone(self):
        r=self.slot()
        for p in self.source.iterdir():p.unlink()
        self.source.rmdir(); self.source=Path(r['destination'])
        report=self.slot(repair=True,cleanup_loot=True)
        self.assertEqual(report['lootCleanup']['removedCount'],3)

    def test_missing_exact_original_fails_before_any_write(self):
        r=self.slot(); self.source=Path(r['destination'])
        before=list(self.slots.iterdir())
        with self.assertRaises(c.ConversionError):
            c.convert_slot(self.source,self.slots,self.backups,self.mapping,self.game,repair=True,
                           cleanup_loot=True,legacy_source=self.source)
        self.assertEqual(before,list(self.slots.iterdir()))

    def test_native_world_without_converter_lineage_cannot_be_cleaned(self):
        (self.source/'save.dat').write_bytes(self.current)
        with self.assertRaises(c.ConversionError):self.slot(repair=True,cleanup_loot=True)
        self.assertFalse(self.backups.exists())

    def test_batch_cleanup_uses_latest_conversion_and_repeated_run_skips_it(self):
        self.slot()
        first=c.batch_convert(self.slots,self.backups,self.mapping,self.game,cleanup_loot=True)
        self.assertEqual((first['cleaned'],len(first['errors'])),(1,0))
        before=len(list(self.slots.iterdir()))
        second=c.batch_convert(self.slots,self.backups,self.mapping,self.game,cleanup_loot=True)
        self.assertEqual((second['cleaned'],len(second['errors'])),(0,0))
        self.assertEqual(before,len(list(self.slots.iterdir())))

    def test_game_open_prevents_cleanup_writes(self):
        c.game_running.return_value=True
        with self.assertRaises(c.ConversionError):self.slot(cleanup_loot=True)
        self.assertFalse(self.backups.exists())

    def test_report_lists_actual_cleanup_and_limits_in_english(self):
        r=self.slot(cleanup_loot=True); text=c.report_text(r)
        self.assertIn('LEGACY WORLD-LOOT CLEANUP',text)
        self.assertIn('Removed imported objects: 3',text)
        self.assertIn('ground stashes',text)


if __name__=='__main__':unittest.main()
