"""Nomad Drive Demo: inspected build 26274244, world format 5 -> 16.

No game binaries are changed. Original save bytes are archived before a new,
independently named slot is committed. Unknown source schemas fail closed.
"""
from __future__ import annotations

import copy
import gzip
import hashlib
import io
import json
import math
import os
from pathlib import Path
import re
import shutil
import struct
import sys
import tempfile
import uuid
import zlib
from collections import Counter
from datetime import datetime, timezone

VERSION = '1.3.0'
MAX_DATA = 512 * 1024 * 1024
MAX_COUNT = 100_000
DEFAULT_SAVES = Path.home() / 'AppData/LocalLow/Indie Devil/Nomad Drive Demo/EvilSave'
DATA_ROOT = Path(os.environ.get('LOCALAPPDATA', Path.home()/'AppData/Local'))/'NomadDriveSaveConverter'
DEFAULT_BACKUPS = DATA_ROOT/'Backups'


def discover_game():
    """Find Steam and its configured libraries; never assume a user's drive."""
    roots=[]
    if os.name=='nt':
        import winreg
        for hive,key,value in (
            (winreg.HKEY_CURRENT_USER,r'Software\Valve\Steam','SteamPath'),
            (winreg.HKEY_LOCAL_MACHINE,r'SOFTWARE\WOW6432Node\Valve\Steam','InstallPath'),
            (winreg.HKEY_LOCAL_MACHINE,r'SOFTWARE\Valve\Steam','InstallPath')):
            try:
                with winreg.OpenKey(hive,key) as handle:roots.append(Path(winreg.QueryValueEx(handle,value)[0]))
            except OSError:pass
    for variable in ('ProgramFiles(x86)','ProgramFiles'):
        if os.environ.get(variable):roots.append(Path(os.environ[variable])/'Steam')
    libraries=list(roots)
    for root in roots:
        config=root/'steamapps/libraryfolders.vdf'
        if not config.is_file():continue
        try:
            text=config.read_text(encoding='utf-8-sig')
            libraries += [Path(p.replace('\\\\','\\')) for p in re.findall(r'"path"\s*"((?:\\.|[^"\\])*)"',text)]
        except (OSError,UnicodeError):continue
    seen=set()
    for root in libraries:
        candidate=root/'steamapps/common/Nomad Drive Demo'
        if str(candidate).lower() in seen:continue
        seen.add(str(candidate).lower())
        if (candidate/'Nomad Drive Demo_Data/StreamingAssets/aa/catalog.bin').is_file():return candidate
    return None


def converted_name(name):
    base=re.sub(r'\s*\((?:converted|\u00e1talak\u00edtott)\)\s*$','',name,flags=re.I).strip()
    return (base or 'Legacy save')+' (converted)'


def player_slot_count(manifest, requested=None):
    host=(manifest.get('host') or {}).get('puid','')
    remote={p['puid'] for p in manifest['remote'] if p['puid'] and p['puid']!=host}
    required=1+len(remote)
    if required>8:raise ConversionError('This save contains more players than the supported limit of 8.')
    current=(manifest.get('economy',0)>>56) if manifest['format']==16 else 0
    if requested is None:return max(2,required,min(8,current))
    if not isinstance(requested,int) or isinstance(requested,bool) or not 1<=requested<=8:
        raise ConversionError('Player slots must be an integer from 1 to 8.')
    if requested<required:raise ConversionError(f'Choose at least {required} player slots to keep all saved players.')
    return requested


def economy_with_players(packed, players):
    # Keep all five resource densities, the preset and the settings version.
    return (packed & ((1<<56)-1)) | (players<<56)


class ConversionError(ValueError):
    pass


class Reader:
    def __init__(self, data: bytes):
        self.data, self.pos = data, 0

    def raw(self, n):
        if not isinstance(n, int) or n < 0 or n > len(self.data) - self.pos:
            raise ConversionError(f'Truncated or invalid data at byte {self.pos}.')
        result = self.data[self.pos:self.pos+n]
        self.pos += n
        return result

    def value(self, fmt):
        return struct.unpack('<' + fmt, self.raw(struct.calcsize('<' + fmt)))[0]

    def i(self): return self.value('i')
    def u(self): return self.value('I')
    def byte(self): return self.value('B')

    def boolean(self):
        n = self.byte()
        if n > 1: raise ConversionError('Invalid Boolean value.')
        return bool(n)

    def count(self, maximum=MAX_COUNT):
        n = self.i()
        if not 0 <= n <= maximum: raise ConversionError(f'Invalid element count: {n}.')
        return n

    def string(self):
        n = 0
        for shift in range(0, 35, 7):
            b = self.byte()
            if shift == 28 and b > 7: raise ConversionError('Invalid string length.')
            n |= (b & 127) << shift
            if b < 128:
                if n > 1_048_576: raise ConversionError('String is too long.')
                try: return self.raw(n).decode('utf-8')
                except UnicodeError as ex: raise ConversionError('Invalid UTF-8 string.') from ex
        raise ConversionError('Invalid string length.')

    def blob(self): return self.raw(self.i())

    def floats(self, n):
        values = struct.unpack('<' + 'f' * n, self.raw(4*n))
        if not all(math.isfinite(x) for x in values):
            raise ConversionError('Position or state contains a non-finite value.')
        return values

    def end(self):
        if self.pos != len(self.data):
            raise ConversionError(f'Unexpected trailing data: {len(self.data)-self.pos} bytes.')


class Writer:
    def __init__(self): self.parts = []
    def raw(self, b): self.parts.append(b)
    def value(self, fmt, value): self.raw(struct.pack('<'+fmt, value))
    def i(self, n): self.value('i', n)
    def u(self, n): self.value('I', n)
    def byte(self, n): self.value('B', n)
    def boolean(self, b): self.byte(int(bool(b)))
    def string(self, text):
        b = text.encode('utf-8'); n = len(b)
        while n >= 128:
            self.byte((n & 127) | 128); n >>= 7
        self.byte(n); self.raw(b)
    def blob(self, b): self.i(len(b)); self.raw(b)
    def floats(self, values): self.raw(struct.pack('<'+'f'*len(values), *values))
    def finish(self): return b''.join(self.parts)


def load_mapping():
    root = Path(getattr(sys, '_MEIPASS', Path(__file__).parent))
    return json.loads((root/'mapping.json').read_text(encoding='utf-8'))


def sha(data): return hashlib.sha256(data).hexdigest()


def decode_save(data):
    if len(data) > MAX_DATA: raise ConversionError('Save file is too large.')
    if data.startswith(b'EVL2'):
        if len(data) < 14: raise ConversionError('Truncated EVL2 header.')
        version, flags, length, crc = struct.unpack_from('<BBII', data, 4)
        if version != 1 or flags not in (0, 1):
            raise ConversionError('Unsupported encryption or save envelope.')
        payload = data[14:]
        if len(payload) != length or zlib.crc32(payload) != crc:
            raise ConversionError('Damaged save: invalid length or CRC.')
        data = payload
        compressed = bool(flags & 1)
    else:
        compressed = data.startswith(b'\x1f\x8b')
    if compressed:
        try:
            with gzip.GzipFile(fileobj=io.BytesIO(data)) as f:
                data = f.read(MAX_DATA+1)
        except (OSError, EOFError, zlib.error) as ex:
            raise ConversionError('The compressed save is damaged.') from ex
    if len(data) > MAX_DATA: raise ConversionError('The decompressed save is too large.')
    r = Reader(data)
    if r.raw(4) != b'EVIL': raise ConversionError('This is not a Nomad Drive save file.')
    version = r.i()
    if version != 1: raise ConversionError(f'Unsupported EVIL data version: {version}.')
    entries = {}
    for _ in range(r.count()):
        try: key = r.raw(r.value('H')).decode('utf-8')
        except UnicodeError as ex: raise ConversionError('Invalid data key.') from ex
        if key in entries: raise ConversionError('Duplicate data key in save.')
        entries[key] = r.blob()
    r.end()
    if 'game.manifest' not in entries: raise ConversionError('World manifest is missing.')
    return version, entries


def encode_save(version, entries):
    w = Writer(); w.raw(b'EVIL'); w.i(version); w.i(len(entries))
    for key, value in entries.items():
        b = key.encode('utf-8')
        if len(b) > 65535: raise ConversionError('Data key is too long.')
        w.value('H', len(b)); w.raw(b); w.blob(value)
    payload = gzip.compress(w.finish(), compresslevel=6, mtime=0)
    return b'EVL2' + struct.pack('<BBII', 1, 1, len(payload), zlib.crc32(payload)) + payload


def read_player(r, fmt):
    p = dict(puid=r.string(), name=r.string(), position=r.floats(3), euler=r.floats(3),
             survival=r.blob(), equipped=r.string(), seat=r.string(), downed=r.boolean())
    if fmt == 16:
        p.update(hotbar=[r.string() for _ in range(r.count(255))], slots=r.byte(),
                 active=r.byte(), backpack=r.string())
    else: p.update(hotbar=[], slots=0, active=0, backpack='')
    return p


def write_player(w, p, fmt):
    w.string(p['puid']); w.string(p['name']); w.floats(p['position']); w.floats(p['euler'])
    w.blob(p['survival']); w.string(p['equipped']); w.string(p['seat']); w.boolean(p['downed'])
    if fmt == 16:
        w.i(len(p['hotbar']))
        for guid in p['hotbar']: w.string(guid)
        w.byte(p['slots']); w.byte(p['active']); w.string(p['backpack'])


def parse_manifest(data):
    r = Reader(data); fmt = r.i()
    if fmt not in (5, 16): raise ConversionError(f'Unsupported world format: {fmt}. Only format 5 to 16 is supported.')
    m = dict(format=fmt, saved_at=r.value('Q'), seed=r.i(), spawn=r.floats(3), time=r.floats(1)[0])
    m['host'] = read_player(r, fmt) if r.boolean() else None
    m['objects'] = []
    for _ in range(r.count()):
        o = dict(guid=r.string(), asset=r.string(), seed=r.i(), position=r.floats(3),
                 rotation=r.floats(4), parent=r.string(), kind=r.byte(), sub=r.byte(), state={})
        if not o['guid'] or o['kind'] > 7: raise ConversionError('Invalid object identifier or parent link.')
        for _ in range(r.count(256)):
            key = r.string()
            if key in o['state']: raise ConversionError('Duplicate object state.')
            o['state'][key] = r.blob()
        m['objects'].append(o)
    m['refs'] = [r.string() for _ in range(r.count())]
    m['remote'] = [read_player(r, fmt) for _ in range(r.count(256))]
    m['seeds'] = [r.i() for _ in range(r.count(5_000_000))]
    m['economy'] = r.value('Q') if fmt == 16 else 0
    r.end()
    ids = [o['guid'] for o in m['objects']]
    if len(ids) != len(set(ids)): raise ConversionError('Duplicate object identifier.')
    return m


def serialize_manifest(m):
    w = Writer(); w.i(m['format']); w.value('Q', m['saved_at']); w.i(m['seed'])
    w.floats(m['spawn']); w.floats((m['time'],)); w.boolean(m['host'] is not None)
    if m['host']: write_player(w, m['host'], m['format'])
    w.i(len(m['objects']))
    for o in m['objects']:
        w.string(o['guid']); w.string(o['asset']); w.i(o['seed']); w.floats(o['position']); w.floats(o['rotation'])
        w.string(o['parent']); w.byte(o['kind']); w.byte(o['sub']); w.i(len(o['state']))
        for key, value in o['state'].items(): w.string(key); w.blob(value)
    w.i(len(m['refs']))
    for key in m['refs']: w.string(key)
    w.i(len(m['remote']))
    for p in m['remote']: write_player(w, p, m['format'])
    w.i(len(m['seeds']))
    for seed in m['seeds']: w.i(seed)
    if m['format'] == 16: w.value('Q', m['economy'])
    return w.finish()


def qmul(a, b):
    x,y,z,w=a; X,Y,Z,W=b
    return (w*X+x*W+y*Z-z*Y, w*Y-x*Z+y*W+z*X,
            w*Z+x*Y-y*X+z*W, w*W-x*X-y*Y-z*Z)


def qinv(q):
    norm = sum(x*x for x in q)
    if norm < 0.00001: raise ConversionError('Invalid rotation.')
    return (-q[0]/norm, -q[1]/norm, -q[2]/norm, q[3]/norm)


def qrot(q, v): return qmul(qmul(q, (*v, 0)), qinv(q))[:3]
def sub(a, b): return tuple(x-y for x,y in zip(a,b))


def valid_png(data):
    if not data.startswith(b'\x89PNG\r\n\x1a\n'): raise ConversionError('Invalid paint image.')
    pos = 8; first = True
    while pos + 12 <= len(data):
        n = struct.unpack_from('>I', data, pos)[0]
        if n > len(data)-pos-12: raise ConversionError('Truncated paint image.')
        kind = data[pos+4:pos+8]; chunk = data[pos+4:pos+8+n]
        if zlib.crc32(chunk) != struct.unpack_from('>I', data, pos+8+n)[0]:
            raise ConversionError('Invalid PNG checksum.')
        if first and (kind != b'IHDR' or n != 13): raise ConversionError('PNG header is missing.')
        first = False; pos += n+12
        if kind == b'IEND':
            if n or pos != len(data): raise ConversionError('Invalid PNG end chunk.')
            return
    raise ConversionError('Truncated PNG file.')


def vehicle_state(blob, existing):
    r = Reader(blob); version = r.byte()
    if version not in (1, 2): raise ConversionError('Unknown legacy vehicle state.')
    slots = [(r.byte(), r.string()) for _ in range(r.count(256))]
    interactables = [(r.byte(),r.byte()) for _ in range(r.count(256))] if version >= 2 else []
    r.end(); w = Writer(); w.byte(1)
    slots = [(slot,guid) for slot,guid in slots if guid in existing]
    w.i(len(slots))
    for slot,guid in slots: w.byte(slot); w.string(guid)
    w.i(len(interactables))
    for key,value in interactables: w.byte(key); w.byte(value)
    w.i(0)  # The old save has no per-vehicle odometer.
    return w.finish()


def plant_state(blob, existing, mapping):
    r = Reader(blob); version = r.byte()
    if version not in (1, 2): raise ConversionError('Unknown legacy plant state.')
    plant, state, phase = r.byte(),r.byte(),r.byte(); elapsed = r.floats(1)[0]
    links = [(r.string(), r.floats(3), r.floats(3)) for _ in range(r.count(1000))] if version >= 2 else []
    r.end()
    durations = mapping['plantPhases'].get(str(plant), [])
    if plant and not durations: raise ConversionError('Unknown plant type.')
    elapsed += sum(durations[:min(phase,max(0,len(durations)-1))])
    w = Writer(); w.byte(1); w.byte(plant); w.byte(state); w.byte(phase); w.floats((max(0,elapsed),))
    links = [link for link in links if link[0] in existing]; w.i(len(links))
    for guid,pos,euler in links: w.string(guid); w.floats(pos); w.floats(euler)
    return w.finish()


def placement_state(blob, o, objects, mapping, report):
    r = Reader(blob)
    if r.byte() != 1: raise ConversionError('Unknown legacy placement state.')
    parent = r.string()
    if not parent:
        r.end(); return b'\x01\x00'
    old_index = r.byte(); old_pos = r.floats(3); old_euler = r.floats(3); r.end()
    support = objects.get(parent)
    if support is None:
        report['counters']['lostSupports'] += 1
        return b'\x01\x00'
    inv = qinv(support['rotation'])
    root_pos = qrot(inv, sub(o['position'],support['position']))
    root_rot = qmul(inv, o['rotation'])
    planes = mapping['assets'][support['asset']]['planes']
    w = Writer(); w.byte(1)
    if planes:
        # The old byte was an array index, not a stable plane ID. Choose a
        # current plane on this same container, preserving the exact world pose.
        plane = min(planes, key=lambda p: sum(x*x for x in sub(root_pos,p['position'])))
        local_pos = qrot(qinv(plane['rotation']),sub(root_pos,plane['position']))
        local_rot = qmul(qinv(plane['rotation']),root_rot)
        w.byte(1); w.string(parent); w.u(plane['planeId'])
        w.floats(local_pos); w.floats(local_rot); w.floats(root_pos); w.floats(root_rot)
        report['counters']['placements'] += 1
    elif 'vehicle' in support['state']:
        w.byte(2); w.string(parent); w.floats(old_pos); w.floats(old_euler)
        report['counters']['vehicleRestFallbacks'] += 1
    else:
        w.byte(0); report['counters']['lostSupports'] += 1
    return w.finish()


def restoration_state(blob, o, entries, mapping, report):
    r = Reader(blob)
    if r.byte() != 1: raise ConversionError('Unknown legacy restoration state.')
    count = r.count(1024); surfaces = mapping['assets'][o['asset']]['surfaces']; rows=[]
    if count > 1:
        report['paintHierarchyNeedsVisualCheck'] = True
    for index in range(count):
        age = r.floats(1)[0]; flags = [r.boolean() for _ in range(6)]
        if index >= len(surfaces):
            report['unmappedPaintSurfaces'].append(dict(guid=o['guid'], asset=mapping['assets'][o['asset']]['name'], oldIndex=index))
            continue
        surface_id = surfaces[index]['surfaceId']; out_flags=[]
        for tex, has in enumerate(flags):
            old = f'restoration.{o["guid"]}.surf{index}.tex{tex}'
            new = f'restoration.{o["guid"]}.s{surface_id}.tex{tex}'
            if has:
                if old not in entries: raise ConversionError(f'Missing paint image: {old}')
                png = entries[old]; valid_png(png)
                if new in entries and entries[new] != png: raise ConversionError('Conflicting paint surface identifier.')
                entries[new] = png
                report['paintHashes'][new] = sha(png)
            out_flags.append(has)
        rows.append((surface_id,out_flags)); report['counters']['paintSurfaces'] += 1
    r.end(); w = Writer(); w.byte(1); w.i(len(rows))
    for surface_id,flags in rows:
        w.u(surface_id)
        for flag in flags: w.boolean(flag)
    return w.finish()


PASSTHROUGH = {'polaroid','liquid','picture','sunvisor','hood','handbrake','engineheat','door',
               'treasure','charge','condition','chest','plate','food'}


def transform(data, mapping, replace_missing=True, player_slots=None):
    version, entries = decode_save(data); m = parse_manifest(entries['game.manifest'])
    if m['format'] != 5: raise ConversionError('This save is already in the current format. Conversion is not needed.')
    old = copy.deepcopy(m)
    report = dict(converterVersion=VERSION, targetBuild=mapping['build'], sourceSha256=sha(data),
                  sourceFormat=5, targetFormat=16, sourceObjects=len(m['objects']), replacements=[],
                  omitted=[], counters=Counter(), unmappedPaintSurfaces=[], paintHashes={}, warnings=[],
                  paintHierarchyNeedsVisualCheck=False)
    kept = []
    for o in m['objects']:
        if o['asset'] not in mapping['assets']:
            name = mapping['oldNames'].get(o['asset'],o['asset'])
            target = mapping['replacements'].get(o['asset']) if replace_missing else None
            if target:
                report['replacements'].append(dict(guid=o['guid'], oldAsset=o['asset'], old=name,
                                                   newAsset=target, new=mapping['assets'][target]['name']))
                o['asset'] = target
            else:
                report['omitted'].append(dict(guid=o['guid'], asset=o['asset'], name=name)); continue
        kept.append(o)
    m['objects'] = kept; objects={o['guid']:o for o in kept}; existing=set(objects)
    if not kept: raise ConversionError('This save contains no transferable objects.')
    # Saveable scripts changed too. Archive their opaque values rather than feed
    # an uninspected quest blob into a different script's reader.
    for key in list(entries):
        if key != 'game.manifest' and not key.startswith(('restoration.','picture.','polaroid.')):
            entries['legacy.converter.'+key] = entries.pop(key)
            report['counters']['archivedGlobalStates'] += 1
    for o in kept:
        if o['parent'] and o['parent'] not in existing:
            o.update(parent='',kind=0,sub=0); report['counters']['orphanLinksCleared'] += 1
        for key, blob in list(o['state'].items()):
            if key == 'vehicle': o['state'][key] = vehicle_state(blob, existing)
            elif key == 'placement': o['state'][key] = placement_state(blob,o,objects,mapping,report)
            elif key == 'restoration': o['state'][key] = restoration_state(blob,o,entries,mapping,report)
            elif key == 'plant': o['state'][key] = plant_state(blob,existing,mapping)
            elif key == 'chestloot':
                r=Reader(blob)
                if r.byte()!=1: raise ConversionError('Unknown legacy chest loot state.')
                opened=r.boolean();r.end();w=Writer();w.byte(1);w.boolean(opened)
                w.i(o['seed']);w.i(max(0,min(2_147_483_647,round(o['position'][2]))));o['state'][key]=w.finish()
            elif key == 'food':
                expected = 12 if mapping['assets'][o['asset']]['organicFood'] else 7
                if len(blob) not in (7,12) or blob[0]!=1: raise ConversionError('Unknown legacy food state.')
                o['state'][key] = blob[:expected] if len(blob)>=expected else blob+b'\x00'*5
            elif key == 'plate':
                r=Reader(blob)
                if r.byte()!=1: raise ConversionError('Unknown legacy license plate state.')
                r.byte();r.boolean();r.byte();r.string();r.end()
                # Current city IDs 0..9 equal the old indices. v1 is explicitly
                # accepted by the current Plate.RestoreSelfState implementation.
                if blob[1]>9: raise ConversionError('Unknown license plate city.')
            elif key.startswith('nomadtrainer.'):
                if key == 'nomadtrainer.paint-amount':
                    r=Reader(blob)
                    if r.byte()!=1: raise ConversionError('Unknown trainer paint capacity state.')
                    r.floats(1);r.end()
                elif key != 'nomadtrainer.delete-tool':
                    raise ConversionError(f'Unknown trainer state: {key}')
            elif key not in PASSTHROUGH:
                raise ConversionError(f'Unknown object state: {key}. The original save is unchanged.')
        report['counters']['states'] += len(o['state'])
    for p in ([m['host']] if m['host'] else []) + m['remote']:
        for key in ('equipped','seat'):
            if p[key] and p[key] not in existing: p[key]=''
        survival=p['survival']
        if survival and (len(survival)!=21 or survival[0]!=1):
            raise ConversionError('Unknown legacy player state.')
    players=player_slot_count(m,player_slots)
    m['format']=16; m['economy']=economy_with_players(mapping['economyPacked'],players)
    report.update(operation='convert',playerSlots=players,multiplayerEnabled=players>1)
    entries['game.manifest']=serialize_manifest(m)
    output=encode_save(version,entries)
    out_ver,out_entries=decode_save(output); checked=parse_manifest(out_entries['game.manifest'])
    if out_ver!=version or checked!=m: raise ConversionError('Read-back verification failed.')
    originals={o['guid']:o for o in old['objects']}
    for o in checked['objects']:
        before=originals[o['guid']]
        if any(o[k]!=before[k] for k in ('guid','position','rotation','seed')):
            raise ConversionError('An object pose or identifier changed during conversion.')
    for key,digest in report['paintHashes'].items():
        if sha(out_entries[key])!=digest: raise ConversionError('Paint image bytes changed during conversion.')
    for key,value in decode_save(data)[1].items():
        if key=='game.manifest':continue
        destination=key if key in out_entries else 'legacy.converter.'+key
        if out_entries.get(destination)!=value: raise ConversionError('Original sidecar data was lost.')
    report.update(outputObjects=len(kept),vehicles=sum('vehicle' in o['state'] for o in kept),
                  remotePlayers=len(m['remote']), outputSha256=sha(output),outputBytes=len(output),
                  verification='Container CRC, complete format-16 reparse, object identities/world poses, original sidecars and converted PNG SHA-256 verified. In-game loading not verified.')
    report['warnings'] += [
        'Inspect the converted world in game; terrain and buildings may differ after the game update.',
        'Legacy quest and shared odometer data are archived; the new systems start from their defaults.',
        'Placed items are linked to a current plane on the same container while retaining their saved world pose.',
        'The old paint age value no longer exists. Multi-surface paint mapping uses the current prefab hierarchy and needs a visual check.',
    ]
    if report['unmappedPaintSurfaces']:
        report['warnings'].append(f'{len(report["unmappedPaintSurfaces"])} old paint surface(s) have no match in the current model; their images remain in the original backup and archived sidecars.')
    if report['counters']['lostSupports']:
        report['warnings'].append(f'{report["counters"]["lostSupports"]} item support(s) could not be restored; the items retain their saved world pose.')
    return output, report


def repair_multiplayer(data, mapping, player_slots=None):
    """Repair a format-16 world in a copy without repeating legacy migration."""
    version,entries=decode_save(data);manifest=parse_manifest(entries['game.manifest'])
    if manifest['format']!=16:raise ConversionError('Multiplayer repair requires a format-16 save.')
    players=player_slot_count(manifest,player_slots)
    if players<2:raise ConversionError('Multiplayer repair requires at least 2 player slots.')
    before=entries['game.manifest'];old_players=manifest['economy']>>56
    manifest['economy']=economy_with_players(manifest['economy'],players)
    entries['game.manifest']=serialize_manifest(manifest)
    if entries['game.manifest'][:-1]!=before[:-1]:
        raise ConversionError('Multiplayer repair changed data outside the player limit.')
    output=encode_save(version,entries)
    checked_version,checked=decode_save(output)
    if checked_version!=version or checked!=entries:raise ConversionError('Repair read-back verification failed.')
    report=dict(converterVersion=VERSION,operation='repair-multiplayer',targetBuild=mapping['build'],
                sourceSha256=sha(data),sourceFormat=16,targetFormat=16,
                sourceObjects=len(manifest['objects']),outputObjects=len(manifest['objects']),
                vehicles=sum('vehicle' in o['state'] for o in manifest['objects']),
                remotePlayers=len(manifest['remote']),replacements=[],omitted=[],counters=Counter(),
                unmappedPaintSurfaces=[],paintHashes={},paintHierarchyNeedsVisualCheck=False,
                previousPlayerSlots=old_players,playerSlots=players,multiplayerEnabled=True,
                outputSha256=sha(output),outputBytes=len(output),
                verification='The player-limit byte, container CRC and exact read-back were verified. All other manifest and sidecar bytes are unchanged.',
                warnings=['Only the saved player limit is changed. Player progress, inventory, vehicles, paint and resource densities are preserved.'])
    return output,report


def source_file(source):
    source=Path(source).resolve()
    if source.is_file(): return source
    if not source.is_dir(): raise ConversionError('Save not found.')
    files=[p for p in (source/'save.dat',source/'autosave.dat') if p.is_file()]
    if not files: raise ConversionError('The folder contains no save.dat or autosave.dat.')
    return max(files,key=lambda p:p.stat().st_mtime_ns)


def read_meta(slot):
    file=Path(slot)/'meta.json'
    if file.is_file():
        try: return json.loads(file.read_text(encoding='utf-8-sig'))
        except (ValueError,OSError) as ex: raise ConversionError('Invalid meta.json.') from ex
    return {}


def inspect_source(source):
    file=source_file(source);data=file.read_bytes();version,entries=decode_save(data)
    m=parse_manifest(entries['game.manifest']);meta=read_meta(file.parent)
    return dict(path=str(file),slot=str(file.parent),name=meta.get('displayName',file.parent.name),
                format=m['format'],objects=len(m['objects']),vehicles=sum('vehicle' in o['state'] for o in m['objects']),
                modified=file.stat().st_mtime,sha256=sha(data))


def game_running():
    if os.name!='nt': return False
    import ctypes
    from ctypes import wintypes as wt
    class ProcessEntry(ctypes.Structure):
        _fields_=[('size',wt.DWORD),('usage',wt.DWORD),('pid',wt.DWORD),
                  ('heap',ctypes.c_size_t),('module',wt.DWORD),('threads',wt.DWORD),
                  ('parent',wt.DWORD),('priority',wt.LONG),('flags',wt.DWORD),('name',wt.WCHAR*260)]
    api=ctypes.WinDLL('kernel32',use_last_error=True)
    api.CreateToolhelp32Snapshot.argtypes=[wt.DWORD,wt.DWORD];api.CreateToolhelp32Snapshot.restype=wt.HANDLE
    for method in (api.Process32FirstW,api.Process32NextW):
        method.argtypes=[wt.HANDLE,ctypes.POINTER(ProcessEntry)];method.restype=wt.BOOL
    api.CloseHandle.argtypes=[wt.HANDLE];api.CloseHandle.restype=wt.BOOL
    handle=api.CreateToolhelp32Snapshot(2,0)
    if handle==ctypes.c_void_p(-1).value:
        raise ConversionError('Could not check whether the game is running.')
    try:
        row=ProcessEntry();row.size=ctypes.sizeof(row)
        ok=api.Process32FirstW(handle,ctypes.byref(row))
        if not ok:raise ConversionError('Could not read the running process list.')
        while ok:
            if row.name.lower().replace(' ','') in {'nomaddrivedemo.exe','nomaddrive.exe'}:return True
            ok=api.Process32NextW(handle,ctypes.byref(row))
        if ctypes.get_last_error()!=18:raise ConversionError('The running game check did not complete.')
        return False
    finally:api.CloseHandle(handle)


def verify_build(game, mapping):
    game=Path(game) if game else discover_game()
    if game is None:raise ConversionError('Game folder not found. Choose the Nomad Drive Demo installation folder.')
    catalog=game/'Nomad Drive Demo_Data/StreamingAssets/aa/catalog.bin'
    if not catalog.is_file(): raise ConversionError('Invalid game folder: catalog.bin was not found.')
    if sha(catalog.read_bytes())!=mapping['catalogSha256']:
        raise ConversionError('This game build is not supported. The converter needs an updated compatibility mapping.')


def report_text(report):
    lines=[f'Nomad Drive Save Converter {VERSION}', '',
           f'World: {report.get("displayName", "")}',
           f'Format: {report["sourceFormat"]} -> {report["targetFormat"]}; Steam build {report["targetBuild"]}',
           f'Objects: {report["sourceObjects"]} -> {report["outputObjects"]}',
           f'Vehicles: {report["vehicles"]}',
           f'Player slots: {report.get("playerSlots", "unknown")}',
           f'Replaced: {len(report["replacements"])}; omitted: {len(report["omitted"])}',
           f'Paint images transferred: {len(report["paintHashes"])}',
           f'New slot: {report.get("destination", "")}',
           f'Backup: {report.get("backup", "")}', '', 'REPLACEMENTS']
    counts=Counter((r['old'],r['new']) for r in report['replacements'])
    lines += [f'{n} x {a} -> {b}' for (a,b),n in sorted(counts.items())] or ['None.']
    lines += ['', 'OMITTED OBJECTS']
    counts=Counter(r['name'] for r in report['omitted'])
    lines += [f'{n} x {name}' for name,n in sorted(counts.items())] or ['None.']
    cleanup=report.get('lootCleanup')
    if cleanup:
        lines += ['', 'LEGACY WORLD-LOOT CLEANUP',
                  f'Removed imported objects: {cleanup["removedCount"]}; kept: {cleanup["keptCount"]}',
                  f'Old loot reservations reset: {cleanup["resetLegacySeeds"]}',
                  'Loose ground stashes can be removed. Vehicles, linked cargo and player equipment are protected.']
        counts=Counter(row['name'] for row in cleanup['removed'])
        lines += [f'{n} x {name}' for name,n in sorted(counts.items())] or ['No removable imported loot found.']
    lines += ['', 'LIMITATIONS AND CHANGES', *report['warnings'], '',
              'VERIFICATION', report['verification'],
              'File validation does not replace an in-game check of this world.', '',
              f'Source SHA-256: {report["sourceSha256"]}',f'Converted SHA-256: {report["outputSha256"]}']
    return '\n'.join(lines)+'\n'


def convert_slot(source, output_root, backup_root=DEFAULT_BACKUPS, mapping=None, game=None,
                 replace_missing=True, display_name=None, player_slots=None, repair=False,
                 cleanup_loot=False, legacy_source=None):
    mapping=mapping or load_mapping(); verify_build(game,mapping)
    if game_running(): raise ConversionError('Close the game before converting saves.')
    src=source_file(source); data=src.read_bytes(); meta=read_meta(src.parent)
    output,report=(repair_multiplayer(data,mapping,player_slots) if repair
                   else transform(data,mapping,replace_missing,player_slots))
    if cleanup_loot:
        from loot_cleanup import apply
        output,report=apply(src,output,report,mapping,backup_root,legacy_source)
    root=Path(output_root).resolve(); archive=Path(backup_root).resolve()
    if not root.is_dir(): raise ConversionError('The destination save folder does not exist.')
    if root==src.parent or src.parent in root.parents:
        raise ConversionError('The new slot cannot be created inside the original slot.')
    if archive==src.parent or src.parent in archive.parents:
        raise ConversionError('The backup cannot be created inside the original slot.')
    slot_id='slot_'+uuid.uuid4().hex; destination=root/slot_id
    if destination.exists() or destination.resolve()==src.parent:
        raise ConversionError('The destination already exists. Overwriting is not allowed.')
    now=datetime.now(timezone.utc).isoformat(timespec='microseconds').replace('+00:00','Z')
    name=(display_name or converted_name(meta.get('displayName', 'Legacy save'))).strip()
    if not name or len(name)>120: raise ConversionError('The save name must contain 1 to 120 characters.')
    # Archive only immutable snapshots, outside the game's live slot folders.
    archive.mkdir(parents=True,exist_ok=True)
    backup=archive/(src.parent.name+'_'+sha(data)[:16])
    backup.mkdir(exist_ok=True)
    archive_file=backup/src.name
    if archive_file.exists():
        if archive_file.read_bytes()!=data: raise ConversionError('A different backup already exists at this path.')
    else:
        with archive_file.open('xb') as f: f.write(data)
    for aux in ('meta.json','thumbnail.jpg','conversion-report.json','CONVERSION_REPORT.txt'):
        source_aux=src.parent/aux;target_aux=backup/aux
        if source_aux.is_file() and not target_aux.exists():
            with target_aux.open('xb') as f: f.write(source_aux.read_bytes())
    report.update(displayName=name,source=str(src),destination=str(destination),backup=str(backup),convertedAt=now)
    lineage=report['sourceSha256']
    previous_report=src.parent/'conversion-report.json'
    if repair and previous_report.is_file():
        try:
            previous=json.loads(previous_report.read_text(encoding='utf-8-sig'))
            candidate=previous.get('originalSourceSha256') or previous.get('sourceSha256')
            if isinstance(candidate,str) and re.fullmatch('[0-9a-fA-F]{64}',candidate):lineage=candidate.lower()
            if 'lootCleanup' in previous and 'lootCleanup' not in report:
                report['lootCleanup']=previous['lootCleanup']
        except (ValueError,OSError):pass
    report['originalSourceSha256']=lineage
    new_meta={**meta, 'slotId':slot_id,'displayName':name,'saveVersion':1,
              'createdAt':meta.get('createdAt',now),'lastSavedAt':now,'playtimeSeconds':meta.get('playtimeSeconds',0),
              'generation':1,'contentFile':'save.dat','contentHash':sha(output),'contentSize':len(output),
              'contentFormatVersion':16,'buildVersion':f'converter-{VERSION} / Steam-'+mapping['build'],
              'deviceId':'','cloudAccount':''}
    # Temporary folder has no slot_ prefix and therefore cannot appear as an
    # incomplete slot in the game. Only our own confined temporary tree is removed.
    staging=Path(tempfile.mkdtemp(prefix='.converter_',dir=root))
    try:
        (staging/'save.dat').write_bytes(output)
        (staging/'meta.json').write_text(json.dumps(new_meta,ensure_ascii=False,indent=4),encoding='utf-8')
        if (src.parent/'thumbnail.jpg').is_file(): shutil.copyfile(src.parent/'thumbnail.jpg',staging/'thumbnail.jpg')
        (staging/'conversion-report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
        (staging/'CONVERSION_REPORT.txt').write_text(report_text(report),encoding='utf-8-sig')
        saved=(staging/'save.dat').read_bytes()
        if sha(saved)!=new_meta['contentHash']: raise ConversionError('The written file did not pass verification.')
        if sha(src.read_bytes())!=report['sourceSha256']: raise ConversionError('The source save changed during conversion. No new slot was installed.')
        os.rename(staging,destination)
    finally:
        if staging.is_dir() and staging.parent==root and staging.name.startswith('.converter_'):
            shutil.rmtree(staging)
    return report


def batch_convert(output_root=DEFAULT_SAVES, backup_root=DEFAULT_BACKUPS, mapping=None,
                  game=None, replace_missing=True, player_slots=None, cleanup_loot=False):
    """Find all legacy worlds and older converter output, then process each once.

    Matching a source fingerprint to its most recently saved converted descendant
    avoids overwriting progress or creating the same converted world on every run.
    Native format-16 worlds without a converter report are left alone.
    """
    mapping=mapping or load_mapping();verify_build(game,mapping)
    root=Path(output_root)
    if not root.is_dir():raise ConversionError('The save folder does not exist. Use Browse to choose it.')
    infos=[];errors=[]
    for folder in sorted(root.glob('slot_*')):
        if not folder.is_dir() or not any((folder/n).is_file() for n in ('save.dat','autosave.dat')):continue
        try:
            info=inspect_source(folder)
            info['manifest']=parse_manifest(decode_save(Path(info['path']).read_bytes())[1]['game.manifest'])
            info['converterReport']=None
            report_file=folder/'conversion-report.json'
            if report_file.is_file():
                try:info['converterReport']=json.loads(report_file.read_text(encoding='utf-8-sig'))
                except (ValueError,OSError):pass
            infos.append(info)
        except Exception as ex:errors.append(dict(name=folder.name,error=str(ex)))
    descendants={}
    for info in infos:
        report=info['converterReport']
        if info['format']!=16 or not isinstance(report,dict):continue
        key=report.get('originalSourceSha256') or report.get('sourceSha256')
        if isinstance(key,str):descendants.setdefault(key.lower(),[]).append(info)
    results=[];tasks=[];handled=set();scheduled=set()
    def add_existing(info):
        if info['slot'] in scheduled:return
        scheduled.add(info['slot'])
        desired=player_slot_count(info['manifest'],player_slots)
        actual=info['manifest']['economy']>>56
        cleaned=isinstance((info.get('converterReport') or {}).get('lootCleanup'),dict)
        if actual!=desired or info['name']!=converted_name(info['name']) or (cleanup_loot and not cleaned):
            tasks.append((info,True))
        else:results.append(dict(name=info['name'],status='already-converted',destination=info['slot'],playerSlots=actual))
    for info in infos:
        if info['format']!=5:continue
        matches=descendants.get(info['sha256'],[])
        if matches:
            handled.update(row['slot'] for row in matches)
            latest=max(matches,key=lambda row:row['modified'])
            try:add_existing(latest)
            except ConversionError as ex:errors.append(dict(name=latest['name'],error=str(ex)))
        else:tasks.append((info,False))
    for info in infos:
        if info['format']!=16 or info['slot'] in handled:continue
        if isinstance(info['converterReport'],dict):
            # If the original legacy file is gone, still group repair descendants.
            report=info['converterReport'];key=report.get('originalSourceSha256') or report.get('sourceSha256')
            group=descendants.get(str(key).lower(),[info]);handled.update(row['slot'] for row in group)
            latest=max(group,key=lambda row:row['modified'])
            try:add_existing(latest)
            except ConversionError as ex:errors.append(dict(name=latest['name'],error=str(ex)))
        else:results.append(dict(name=info['name'],status='current-format',destination=info['slot']))
    if tasks and game_running():raise ConversionError('Close the game, then click Back up and convert all.')
    for info,repair in tasks:
        try:
            needs_cleanup=cleanup_loot and not isinstance((info.get('converterReport') or {}).get('lootCleanup'),dict)
            report=convert_slot(info['path'],root,backup_root,mapping,game,
                                replace_missing,converted_name(info['name']),player_slots,repair,
                                cleanup_loot=needs_cleanup)
            results.append(dict(name=report['displayName'],status='cleaned' if needs_cleanup else 'repaired' if repair else 'converted',
                                destination=report['destination'],backup=report['backup'],
                                objects=report['outputObjects'],playerSlots=report['playerSlots'],
                                lootRemoved=report.get('lootCleanup',{}).get('removedCount',0),
                                replaced=len(report['replacements']),omitted=len(report['omitted'])))
        except Exception as ex:errors.append(dict(name=info['name'],error=str(ex)))
    converted=sum(r['status']=='converted' for r in results)
    repaired=sum(r['status']=='repaired' for r in results)
    cleaned=sum(r['status']=='cleaned' for r in results)
    skipped=len(results)-converted-repaired-cleaned
    text=[f'Converted: {converted} | Multiplayer repaired: {repaired} | Loot cleaned: {cleaned} | Unchanged: {skipped} | Errors: {len(errors)}','']
    for row in results:
        text += [row['name']+' - '+row['status'], '  '+row['destination']]
        if row.get('backup'):text += ['  Backup: '+row['backup']]
        if row.get('status')=='cleaned':text += ['  Imported objects removed: '+str(row['lootRemoved'])]
    for row in errors:text += [row['name']+' - ERROR: '+row['error']]
    text += ['', 'Load the newest (converted) entries in the game.',
             'Original slots are unchanged. Individual conversion reports are saved beside each new world.']
    result=dict(converted=converted,repaired=repaired,cleaned=cleaned,unchanged=skipped,errors=errors,worlds=results,
                reportText='\n'.join(text),locations=dict(game=str(Path(game) if game else discover_game() or ''),
                                                       saves=str(root),backups=str(backup_root)))
    if converted or repaired or cleaned:
        archive=Path(backup_root);archive.mkdir(parents=True,exist_ok=True)
        audit=archive/('batch-'+datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ')+'-'+uuid.uuid4().hex[:8]+'.json')
        audit.write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf8')
    return result
