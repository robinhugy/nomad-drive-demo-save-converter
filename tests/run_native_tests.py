"""Run the established regression cases against the production C# implementation.

The Python implementation remains an independent binary-format oracle. Native
transforms and cleanup must preserve identical decoded entries and manifests.
All write tests use TemporaryDirectory; no personal saves are used.
"""
import base64
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

from fixtures import c, make_slot
import loot_cleanup as loot

HARNESS = Path(sys.argv[1]).resolve()
APP = Path(sys.argv[2]).resolve()
reference_transform = c.transform
reference_repair = c.repair_multiplayer
reference_clean = loot.clean
equivalent = 0


def native(mode, **kwargs):
    request = dict(mode=mode, game_running=bool(c.game_running()))
    for key, value in kwargs.items():
        if isinstance(value, bytes): value = base64.b64encode(value).decode('ascii')
        if isinstance(value, Path): value = str(value)
        request[key] = value
    with tempfile.TemporaryDirectory(prefix='nomad_native_case_') as folder:
        root = Path(folder); source = root/'request.json'; out = root/'result.json'
        source.write_text(json.dumps(request, ensure_ascii=False), encoding='utf8')
        proc = subprocess.run([str(HARNESS), str(source), str(out)], capture_output=True, timeout=60)
        if not out.is_file(): raise AssertionError('Native harness did not return a report: '+repr(proc.stderr))
        result = json.loads(out.read_text(encoding='utf-8-sig'))
        if 'error' in result: raise c.ConversionError(result['error'])
        if proc.returncode: raise AssertionError('Unexpected native exit code '+str(proc.returncode))
        return result


def compare(expected, actual):
    global equivalent
    e, a = c.decode_save(expected)[1], c.decode_save(actual)[1]
    assert set(e) == set(a), (set(e)-set(a), set(a)-set(e))
    assert c.parse_manifest(e['game.manifest']) == c.parse_manifest(a['game.manifest']), 'Manifest differs from reference'
    assert {k:v for k,v in e.items() if k!='game.manifest'} == {k:v for k,v in a.items() if k!='game.manifest'}, 'Sidecar differs from reference'
    equivalent += 1


def transform(data, mapping, replace_missing=True, player_slots=None):
    result = native('transform', data=data, mapping=mapping, replace_missing=replace_missing, player_slots=player_slots)
    output = base64.b64decode(result['data'])
    expected, report = reference_transform(data, mapping, replace_missing, player_slots)
    compare(expected, output)
    for key in ('sourceObjects','outputObjects','vehicles','playerSlots','replacements','omitted','counters','paintHashes'):
        assert report[key] == result['report'][key], ('Report differs', key)
    return output, result['report']


def repair(data, mapping, player_slots=None):
    result = native('repair', data=data, mapping=mapping, player_slots=player_slots)
    output = base64.b64decode(result['data']); compare(reference_repair(data, mapping, player_slots)[0], output)
    return output, result['report']


def clean(data, legacy_data, mapping):
    result = native('clean', data=data, legacy=legacy_data, mapping=mapping)
    output = base64.b64decode(result['data']); expected, report = reference_clean(data, legacy_data, mapping)
    compare(expected, output)
    assert json.loads(json.dumps(report)) == result['report'], 'Cleanup report differs from reference'
    return output, result['report']


def convert_slot(source, output_root, backup_root=c.DEFAULT_BACKUPS, mapping=None, game=None,
                 replace_missing=True, display_name=None, player_slots=None, repair=False,
                 cleanup_loot=False, legacy_source=None):
    return native('convert_slot', source=source, output_root=output_root, backup_root=backup_root,
                  mapping=mapping or c.load_mapping(), game=game, replace_missing=replace_missing,
                  display_name=display_name, player_slots=player_slots, repair=repair,
                  cleanup_loot=cleanup_loot, legacy_source=legacy_source)


def batch_convert(output_root=c.DEFAULT_SAVES, backup_root=c.DEFAULT_BACKUPS, mapping=None,
                  game=None, replace_missing=True, player_slots=None, cleanup_loot=False):
    return native('batch', output_root=output_root, backup_root=backup_root,
                  mapping=mapping or c.load_mapping(), game=game, replace_missing=replace_missing,
                  player_slots=player_slots, cleanup_loot=cleanup_loot)


c.transform, c.repair_multiplayer = transform, repair
c.convert_slot, c.batch_convert, loot.clean = convert_slot, batch_convert, clean
suite = unittest.defaultTestLoader.discover(str(Path(__file__).parent), pattern='test_*.py')
result = unittest.TextTestRunner(verbosity=2, failfast=True).run(suite)
if not result.wasSuccessful(): sys.exit(1)

# Exercise the actual shipping entry point, without requiring game files.
with tempfile.TemporaryDirectory(prefix='nomad_native_cli_') as folder:
    root=Path(folder); slot=make_slot(root); out=root/'cli.json'
    proc=subprocess.run([str(APP),'--list','--output-root',str(root),'--report',str(out)],timeout=30)
    report=json.loads(out.read_text(encoding='utf-8-sig'))
    assert proc.returncode==0 and report['slots'][0]['name']=='Example world'
    before={p:p.read_bytes() for p in slot.iterdir()}
    proc=subprocess.run([str(APP),'--preview','--source',str(slot),'--game',str(root/'missing-game'),'--report',str(out)],timeout=30)
    assert proc.returncode==1 and 'error' in json.loads(out.read_text(encoding='utf-8-sig'))
    assert before=={p:p.read_bytes() for p in slot.iterdir()}
print(f'PASS: {result.testsRun} native regression cases, {equivalent} independent decoded-save comparisons, shipping CLI and failed-build write guard.')
(Path(__file__).resolve().parent.parent/'.build/native-test-results.json').write_text(json.dumps(
    dict(regressionCases=result.testsRun,decodedComparisons=equivalent,shippingCli=True,failedBuildWriteGuard=True)),encoding='utf8')
