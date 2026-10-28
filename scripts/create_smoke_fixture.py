"""Prepare isolated GUI test worlds. Never reads or writes the user's saves."""
from pathlib import Path
import sys
from unittest.mock import patch

repo=Path(__file__).resolve().parent.parent
sys.path.insert(0,str(repo/'tests'))
from fixtures import c,make_slot

root=repo/'.build/smoke-saves';root.mkdir(parents=True,exist_ok=True)
game=c.discover_game()
if game is None:raise SystemExit('An installed supported game is required for the optional GUI smoke test.')
for index in range(3):
    slot=root/f'slot_synthetic_{index}'
    if not slot.exists():make_slot(root,index,'Synthetic world '+str(index+1),slot.name)
with patch.object(c,'game_running',return_value=False):
    result=c.batch_convert(root,repo/'.build/smoke-backups',game=game)
if result['errors']:raise SystemExit(str(result['errors']))
print('Synthetic GUI fixtures ready:',root)
