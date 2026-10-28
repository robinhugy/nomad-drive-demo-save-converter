"""JSON command-line interface used by the Windows app and developer tools."""
import argparse
import json
from pathlib import Path
import sys
import converter_core as core


def main():
    parser=argparse.ArgumentParser(description='Nomad Drive Demo Save Converter: format 5 to 16 and multiplayer repair')
    modes=parser.add_mutually_exclusive_group(required=True)
    for mode in ('list','inspect','preview','convert','repair-multiplayer','batch'):
        modes.add_argument('--'+mode,action='store_true')
    parser.add_argument('--source',type=Path)
    parser.add_argument('--output-root',type=Path,default=core.DEFAULT_SAVES)
    parser.add_argument('--backup-root',type=Path,default=core.DEFAULT_BACKUPS)
    parser.add_argument('--game',type=Path,help='Game installation folder; Steam libraries are detected automatically')
    parser.add_argument('--players',type=int,choices=range(1,9),help='Player slots; automatic selection enables multiplayer and retains saved players')
    parser.add_argument('--omit-missing',action='store_true')
    parser.add_argument('--clean-legacy-loot',action='store_true',
                        help='Remove imported loose world loot in a copy; ground stashes can be lost')
    parser.add_argument('--legacy-source',type=Path,help='Exact format-5 original for a cleanup, if automatic lookup fails')
    parser.add_argument('--name')
    parser.add_argument('--report',type=Path)
    args=parser.parse_args();status=0
    try:
        if args.batch:
            result=core.batch_convert(args.output_root,args.backup_root,game=args.game,
                                     replace_missing=not args.omit_missing,player_slots=args.players,
                                     cleanup_loot=args.clean_legacy_loot)
            if result['errors']:status=1
        elif args.list:
            if not args.output_root.is_dir():raise core.ConversionError('The save folder does not exist.')
            rows=[]
            for slot in args.output_root.glob('slot_*'):
                if not slot.is_dir() or not any((slot/n).is_file() for n in ('save.dat','autosave.dat')):continue
                try:rows.append(core.inspect_source(slot))
                except Exception as ex:
                    try:name=core.read_meta(slot).get('displayName',slot.name)
                    except Exception:name=slot.name
                    rows.append(dict(slot=str(slot),name=name,error=str(ex),modified=0,format='?',objects='?'))
            detected=core.discover_game()
            result=dict(slots=sorted(rows,key=lambda row:row['modified'],reverse=True),
                        locations=dict(game=str(detected) if detected else '',saves=str(core.DEFAULT_SAVES),backups=str(core.DEFAULT_BACKUPS)))
        else:
            if args.source is None:raise core.ConversionError('Select a source save with --source.')
            if args.inspect:result=core.inspect_source(args.source)
            elif args.preview:
                mapping=core.load_mapping();core.verify_build(args.game,mapping)
                source=core.source_file(args.source);data=source.read_bytes()
                fmt=core.parse_manifest(core.decode_save(data)[1]['game.manifest'])['format']
                if fmt==16:output,result=core.repair_multiplayer(data,mapping,args.players)
                else:output,result=core.transform(data,mapping,not args.omit_missing,args.players)
                if args.clean_legacy_loot:
                    from loot_cleanup import apply
                    _,result=apply(source,output,result,mapping,args.backup_root,args.legacy_source)
                result['displayName']=core.converted_name(core.read_meta(source.parent).get('displayName',source.parent.name))
                result['reportText']=core.report_text(result)
            else:
                result=core.convert_slot(args.source,args.output_root,args.backup_root,
                            game=args.game,replace_missing=not args.omit_missing,display_name=args.name,
                            player_slots=args.players,repair=args.repair_multiplayer,
                            cleanup_loot=args.clean_legacy_loot,legacy_source=args.legacy_source)
                result['reportText']=core.report_text(result)
    except Exception as ex:result=dict(error=str(ex));status=1
    text=json.dumps(result,ensure_ascii=False,indent=2)
    if args.report:args.report.write_text(text,encoding='utf-8')
    if sys.stdout:print(text)
    return status


if __name__=='__main__':sys.exit(main())
