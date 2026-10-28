"""Opt-in legacy world-loot cleanup. Never infer ownership from item type.

Legacy saves do not record which loose items a player touched. Protect explicit
ownership graphs, all current-only objects and imported objects moved since the
legacy snapshot. Remove only the remaining imported components in a new copy.
"""
from collections import Counter, defaultdict
import json
from pathlib import Path

import converter_core as c

CLEANUP_VERSION = 1


def object_links(obj):
    """Read the inspected format-16 link contributors; fail on unknown schemas."""
    links = {obj['parent']} if obj['parent'] else set()
    for key in ('placement', 'vehicle', 'backpack', 'plant'):
        blob = obj['state'].get(key)
        if blob is None:
            continue
        r = c.Reader(blob)
        version = r.byte()
        if key == 'placement':
            if version != 1:
                raise c.ConversionError('Unknown placement state; loot cleanup stopped.')
            kind = r.byte()
            if kind in (1, 2):
                links.add(r.string())
                if kind == 1:
                    r.u(); r.floats(3); r.floats(4); r.floats(3); r.floats(4)
                else:
                    r.floats(3); r.floats(3)
            elif kind != 0:
                raise c.ConversionError('Unknown placement link; loot cleanup stopped.')
        elif key == 'vehicle':
            if version != 1:
                raise c.ConversionError('Unknown vehicle state; loot cleanup stopped.')
            for _ in range(r.count(256)):
                r.byte(); links.add(r.string())
            for _ in range(r.count(256)):
                r.byte(); r.byte()
            r.i()  # odometer
        elif key == 'backpack':
            if version not in (1, 2):
                raise c.ConversionError('Unknown backpack state; loot cleanup stopped.')
            r.byte()  # variant
            if version == 2:
                for _ in range(r.byte()):
                    links.add(r.string())
        else:
            if version != 1:
                raise c.ConversionError('Unknown plant state; loot cleanup stopped.')
            r.byte(); r.byte(); r.byte(); r.floats(1)
            for _ in range(r.count(1000)):
                links.add(r.string()); r.floats(3); r.floats(3)
        r.end()
    return links - {''}


def clean(data, legacy_data, mapping):
    version, entries = c.decode_save(data)
    m = c.parse_manifest(entries['game.manifest'])
    old = c.parse_manifest(c.decode_save(legacy_data)[1]['game.manifest'])
    if m['format'] != 16 or old['format'] != 5 or m['seed'] != old['seed']:
        raise c.ConversionError('Loot cleanup requires this converted world and its matching format-5 original.')
    objects = {o['guid']: o for o in m['objects']}
    originals = {o['guid']: o for o in old['objects']}
    graph = defaultdict(set)
    reasons = defaultdict(set)
    native_seeds = set()
    for guid, obj in objects.items():
        for other in object_links(obj):
            if other in objects:
                graph[guid].add(other); graph[other].add(guid)
        if 'vehicle' in obj['state']:
            reasons[guid].add('vehicle')
        if guid not in originals:
            reasons[guid].add('created-after-conversion')
        elif sum((a-b)**2 for a, b in zip(obj['position'], originals[guid]['position'])) > 1.0:
            reasons[guid].add('moved-since-legacy-save')
        marker = obj['state'].get('_wildLootSeed')
        if marker is not None:
            r = c.Reader(marker)
            if r.byte() != 1:
                raise c.ConversionError('Unknown wild-loot marker; loot cleanup stopped.')
            native_seeds.add(r.i()); r.end()
            # Current provenance takes precedence even for an imported identity.
            reasons[guid].add('current-world-loot')
    for p in ([m['host']] if m['host'] else []) + m['remote']:
        for guid in [p['equipped'], p['seat'], p['backpack'], *p['hotbar']]:
            if guid in objects:
                reasons[guid].add('player-equipment')
    protected = set(reasons)
    pending = list(protected)
    while pending:
        for other in graph[pending.pop()] - protected:
            protected.add(other); reasons[other].add('linked-to-kept-object'); pending.append(other)
    removed = [o for o in m['objects'] if o['guid'] not in protected]
    kept = [o for o in m['objects'] if o['guid'] in protected]
    before_seeds = m['seeds']
    legacy_seeds = set(old['seeds'])
    # Drop old-generation reservations, retaining every new reservation (even
    # collected native loot) and any overlapping seed with a current live item.
    m['seeds'] = [s for s in before_seeds if s not in legacy_seeds or s in native_seeds]
    m['seeds'] += sorted(native_seeds - set(m['seeds']))
    m['objects'] = kept
    entries['game.manifest'] = c.serialize_manifest(m)
    output = c.encode_save(version, entries)
    checked_version, checked = c.decode_save(output)
    if checked_version != version or checked != entries:
        raise c.ConversionError('Loot cleanup read-back verification failed.')
    # Kept object states/poses, players, paint and other sidecars are unchanged.
    if c.parse_manifest(checked['game.manifest']) != m:
        raise c.ConversionError('Loot cleanup manifest verification failed.')
    report = dict(version=CLEANUP_VERSION, legacySourceSha256=c.sha(legacy_data),
                  removedCount=len(removed), keptCount=len(kept),
                  resetLegacySeeds=len(set(before_seeds)-set(m['seeds'])),
                  keptSeedCount=len(m['seeds']),
                  protectedReasons=dict(Counter(reason for values in reasons.values() for reason in values)),
                  removed=[dict(guid=o['guid'], asset=o['asset'],
                                name=mapping['assets'].get(o['asset'], {}).get('name', o['asset']),
                                position=o['position']) for o in removed])
    return output, report


def original_fingerprint(source):
    file = c.source_file(source)
    try:
        report = json.loads((file.parent/'conversion-report.json').read_text(encoding='utf-8-sig'))
        value = report.get('originalSourceSha256') or report.get('sourceSha256')
    except (OSError, ValueError, AttributeError):
        value = None
    if not isinstance(value, str) or not c.re.fullmatch('[0-9a-fA-F]{64}', value):
        raise c.ConversionError('No converter lineage found. Select a converted slot with conversion-report.json.')
    return value.lower()


def find_original(source, backup_root=c.DEFAULT_BACKUPS, explicit=None):
    file = c.source_file(source)
    data = file.read_bytes()
    if c.parse_manifest(c.decode_save(data)[1]['game.manifest'])['format'] == 5:
        return data
    fingerprint = original_fingerprint(file)
    if explicit:
        candidates = [c.source_file(explicit)]
    else:
        # Look only in sibling slots and the application's backup tree.
        candidates = [p for folder in sorted(file.parent.parent.glob('slot_*'))
                      if folder.is_dir() for p in (folder/'save.dat', folder/'autosave.dat') if p.is_file()]
        archive = Path(backup_root)
        if archive.is_dir():
            candidates += sorted(archive.glob('*/save.dat')) + sorted(archive.glob('*/autosave.dat'))
    for candidate in candidates:
        candidate_data = candidate.read_bytes()
        if c.sha(candidate_data) == fingerprint:
            old = c.parse_manifest(c.decode_save(candidate_data)[1]['game.manifest'])
            if old['format'] != 5:
                break
            return candidate_data
    raise c.ConversionError('The exact format-5 original was not found in sibling slots or converter backups. '
                            'Restore the original backup, or use --legacy-source. No cleanup was performed.')


def apply(source, output, report, mapping, backup_root=c.DEFAULT_BACKUPS, legacy_source=None):
    legacy = find_original(source, backup_root, legacy_source)
    output, detail = clean(output, legacy, mapping)
    m = c.parse_manifest(c.decode_save(output)[1]['game.manifest'])
    report.update(operation='cleanup-legacy-loot', lootCleanup=detail,
                  outputObjects=len(m['objects']), vehicles=sum('vehicle' in o['state'] for o in m['objects']),
                  outputSha256=c.sha(output), outputBytes=len(output),
                  verification='CRC and complete read-back passed. Kept object records, players and all sidecar bytes are unchanged by cleanup.')
    report['warnings'] += [
        'Cleanup removes imported loose ground items and their linked contents, including intentional stashes. The legacy save has no reliable visited/touched flag.',
        'All vehicles, their linked parts/cargo, player equipment/backpack contents, current-only objects and imported objects moved over 1 metre are kept.',
        'Old loot reservations are reset so the updated map can generate loot at its current locations. Previously cleared locations may receive new loot.',
        'Floating objects that are protected or have already moved may remain. Cleanup does not move vehicles or restore old buildings.',
    ]
    return output, report
