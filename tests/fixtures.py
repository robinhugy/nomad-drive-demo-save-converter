"""Synthetic worlds only. No personal saves or game files are required."""
from datetime import datetime, timezone
import gzip
import json
from pathlib import Path
import struct
import sys
import zlib

REPO=Path(__file__).resolve().parent.parent
sys.path.insert(0,str(REPO/'src/engine'))
import converter_core as c

CAR='11111111111111111111111111111111'
HOOD='22222222222222222222222222222222'
TIRE='33333333333333333333333333333333'
APPLE='44444444444444444444444444444444'
REMOVED='55555555555555555555555555555555'
CHEST='66666666666666666666666666666666'
PLANT='77777777777777777777777777777777'


def png():
    def chunk(kind,data):return struct.pack('>I',len(data))+kind+data+struct.pack('>I',zlib.crc32(kind+data))
    return b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',1,1,8,6,0,0,0))+chunk(b'IDAT',zlib.compress(b'\0\xff\0\0\xff'))+chunk(b'IEND',b'')


def player(index):
    return dict(puid='synthetic-player-'+str(index),name='Test player '+str(index),position=(10.,20.,30.),
                euler=(0.,0.,0.),survival=b'\1'+struct.pack('<5f',70,80,90,100,0),
                equipped='',seat='',downed=False,hotbar=[],slots=0,active=0,backpack='')


def world(remote=0):
    def obj(guid,asset,state=None,position=(10.,20.,30.),parent='',kind=0,sub=0):
        return dict(guid=guid,asset=asset,seed=42,position=position,rotation=(0.,0.,0.,1.),
                    parent=parent,kind=kind,sub=sub,state=state or {'placement':b'\1\0'})
    w=c.Writer();w.byte(2);w.i(1);w.byte(2);w.string(HOOD);w.i(1);w.byte(1);w.byte(1)
    vehicle=w.finish()
    w=c.Writer();w.byte(1);w.string(CAR);w.byte(0);w.floats((1.,1.,1.));w.floats((0.,0.,0.))
    placement=w.finish()
    restoration=b'\1'+struct.pack('<if',1,12.5)+bytes([1,0,0,0,0,0])
    objects=[
        obj(CAR,'0b648ea7d80e0fc4da4d6773d1e308e7',{'vehicle':vehicle,'liquid':b'\1'+struct.pack('<fi',15,2)}),
        obj(HOOD,'0f2235c712e8b384b9e460c834ca6da4',{'placement':b'\1\0','restoration':restoration,'condition':b'\1'+struct.pack('<f',75)},parent=CAR,kind=3,sub=2),
        obj(TIRE,'0b832cfc907d0e04a9e3780cca1d90ef',{'placement':placement,'condition':b'\1'+struct.pack('<f',50)},position=(11.,21.,31.),parent=CAR,kind=4),
        obj(APPLE,'2ebe0d040ec567047a5c5ceb0e1e0951',{'placement':b'\1\0','food':b'\1'+struct.pack('<fBB',90,0,1)}),
        obj(REMOVED,'41c984347a602e04d90a1d5125d58f29'),
        obj(CHEST,'3aa61d6643d20ce49b6cbdfd2c8f0f87',{'placement':b'\1\0','chestloot':b'\1\1'}),
        obj(PLANT,'0b551ed83dfadf94b872ad3be626f019',{'placement':b'\1\0','plant':bytes([2,3,2,2])+struct.pack('<fi',3,0)}),
    ]
    # UTC DateTime.ToBinary for a fixed, valid public timestamp.
    ticks=621355968000000000+1_793_016_000*10_000_000
    m=dict(format=5,saved_at=ticks|(1<<62),seed=123456,spawn=(10.,20.,30.),time=12.5,
           host=player(0),objects=objects,refs=[],remote=[player(i+1) for i in range(remote)],seeds=[42,99],economy=0)
    entries={'game.manifest':c.serialize_manifest(m),f'restoration.{HOOD}.surf0.tex0':png(),
             'synthetic/objectives':b'public synthetic objective data'}
    # Legacy saves used raw gzip around the EVIL container.
    enveloped=c.encode_save(1,entries)
    return enveloped[14:],m,entries


def make_slot(root,remote=0,name='Example world',slot_id='slot_example'):
    folder=Path(root)/slot_id;folder.mkdir(parents=True)
    data,manifest,entries=world(remote)
    (folder/'save.dat').write_bytes(data)
    (folder/'meta.json').write_text(json.dumps(dict(slotId=slot_id,displayName=name,saveVersion=1,
        createdAt='2026-01-01T00:00:00Z',lastSavedAt='2026-01-01T00:00:00Z',playtimeSeconds=120)),encoding='utf8')
    return folder
