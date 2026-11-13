"""Execute LR2 beta3 100201's native timing, hold and gauge routines.

The rank/gauge bootstrap is the original basic-block sequence from its parser,
entered with the completed parse's note counts, TOTAL and options. The fixture
does not execute the parser, audio, replay output, skin timers or Windows APIs.
Judgement selection, note mutation, score, combo and gauge arithmetic are original.
"""
import argparse
import hashlib
import json
from pathlib import Path
import struct
import sys

parser = argparse.ArgumentParser()
parser.add_argument('--binary', type=Path, required=True)
parser.add_argument('--unicorn-library', type=Path)
parser.add_argument('--output', type=Path, default=Path(__file__).resolve().parents[1] / 'RhythmEngine.Tests/Reference/lr2-100201.json')
args = parser.parse_args()
if args.unicorn_library:
    sys.path.insert(0, str(args.unicorn_library))
from unicorn import Uc, UC_ARCH_X86, UC_MODE_32, UC_HOOK_CODE
from unicorn.x86_const import UC_X86_REG_EAX, UC_X86_REG_ECX, UC_X86_REG_ESP, UC_X86_REG_EIP, UC_X86_REG_EBP, UC_X86_REG_ESI, UC_X86_REG_FPCW, UC_X86_REG_FPTAG, UC_X86_REG_FPSW

binary = args.binary.read_bytes()
digest = hashlib.sha256(binary).hexdigest()
if digest != 'a46ef15343e41d3411771c83ac7b0c42d95e5f473db69c6b45a9584c07e94480':
    raise ValueError('This harness only admits the pinned LR2 beta3 100201 executable.')
pe = struct.unpack_from('<I', binary, 60)[0]
count = struct.unpack_from('<H', binary, pe + 6)[0]
optional_size = struct.unpack_from('<H', binary, pe + 20)[0]
optional = pe + 24
image_base = struct.unpack_from('<I', binary, optional + 28)[0]
image_size, header_size = struct.unpack_from('<II', binary, optional + 56)
sections = [struct.unpack_from('<8sIIIIIIHHI', binary, optional + optional_size + i * 40) for i in range(count)]
machine = Uc(UC_ARCH_X86, UC_MODE_32)
machine.mem_map(image_base, (image_size + 4095) & ~4095)
machine.mem_write(image_base, binary[:header_size])
for _, _, rva, size, file_offset, *_ in sections:
    if size:
        machine.mem_write(image_base + rva, binary[file_offset:file_offset + size])
machine.mem_map(0x10000000, 0x400000)
machine.mem_map(0x20000000, 4096)
base, notes, stack = 0x10000000, 0x10300000, 0x10280000
return_address = 0x20000000
emissions = []
current_time = 0


def write(address, fmt, value):
    machine.mem_write(address, struct.pack('<' + fmt, value))


def read(address, fmt):
    return struct.unpack('<' + fmt, machine.mem_read(address, struct.calcsize(fmt)))[0]


def skip_service(uc, address, size, user_data):
    current_stack = uc.reg_read(UC_X86_REG_ESP)
    target = read(current_stack, 'I')
    if address == 0x405FB0:
        emissions.append({'time': current_time, 'grade': read(current_stack + 4, 'i')})
        return
    uc.reg_write(UC_X86_REG_EAX, 1)
    uc.reg_write(UC_X86_REG_ESP, current_stack + (8 if address == 0x4A8740 else 4))
    uc.reg_write(UC_X86_REG_EIP, target)


for address in (0x405FB0, 0x4A8740, 0x4A8770, 0x4B8F20, 0x4B8140, 0x4B67D0, 0x4B6B80):
    machine.hook_add(UC_HOOK_CODE, skip_service, begin=address, end=address)
write(0x799EF0, 'B', 1)
write(0x799EE8, 'd', 0)


def call(procedure, *parameters):
    machine.reg_write(UC_X86_REG_ESP, stack)
    machine.mem_write(stack, struct.pack('<' + 'I' * (len(parameters) + 1), return_address, *parameters))
    try:
        machine.emu_start(procedure, return_address, timeout=20000, count=100000)
    except Exception:
        print("Native failure at", hex(machine.reg_read(UC_X86_REG_EIP)), "stack", hex(machine.reg_read(UC_X86_REG_ESP)), "entry", hex(procedure), "args", parameters)
        raise
    if machine.reg_read(UC_X86_REG_EIP) != return_address:
        raise RuntimeError('Original function did not return within its instruction budget.')
    return machine.reg_read(UC_X86_REG_EAX)


def initialise(rank, gauge=0, total=260, note_count=1, hold=False, health=None):
    machine.mem_write(base, bytes(0xA1000))
    machine.mem_write(notes, bytes(1024))
    machine.mem_write(stack, bytes(512))
    machine.reg_write(UC_X86_REG_FPCW, 0x027F)
    machine.reg_write(UC_X86_REG_FPSW, 0)
    machine.reg_write(UC_X86_REG_FPTAG, 0xFFFF)
    # The bootstrap runs once for each side, exactly as in the parser.
    machine.reg_write(UC_X86_REG_EBP, base + 146880)
    machine.reg_write(UC_X86_REG_ESI, base + 621112)
    machine.reg_write(UC_X86_REG_ESP, stack)
    write(stack + 0x1C, 'I', base)
    write(stack + 0x48, 'I', base + 8)
    write(stack + 0x6C, 'i', 0)
    write(stack + 0x84, 'i', rank)
    for offset in (0xD4, 0xD8):
        write(stack + offset, 'i', note_count)
    for offset in (0xE4, 0xE8):
        write(stack + offset, 'i', total)
    write(base + 8, 'i', gauge)
    write(base + 12, 'i', gauge)
    machine.emu_start(0x4B57C1, 0x4B5B32, timeout=20000, count=100000)
    if machine.reg_read(UC_X86_REG_EIP) != 0x4B5B32:
        raise RuntimeError('Rank/gauge bootstrap did not reach its original exit.')
    write(base + 621000, 'd', health if health is not None else 20 if gauge in (0, 3) else 100)
    write(base + 146916, 'I', notes)
    write(base + 146920, 'i', 1)
    write(base + 146924, 'i', 2)
    write(notes + 8, 'd', 2000)
    write(notes + 16, 'd', 1)
    write(notes + 40, 'd', 3000 if hold else 2000)
    write(notes + 64 + 8, 'd', 1_000_000_000)
    write(notes + 64 + 40, 'd', 1_000_000_000)
    emissions.clear()


def step(time, key_state):
    global current_time
    current_time = time
    call(0x419320, base, 0, key_state, time, 0)
    return {'time': time, 'keyState': key_state, 'events': list(emissions),
            'headState': read(notes + 24, 'i'), 'index': read(base + 146928, 'i')}


tables, heads, holds, gauge_rows = [], [], [], []
for rank in range(4):
    initialise(rank)
    bounds = [read(base + o, 'i') for o in (621116, 621112, 621108, 621104, 621100)]
    tables.append({'rank': rank, 'bounds': bounds})
    offsets = {0}
    for bound in bounds:
        for delta in (-1, 0, 1):
            offsets.update((bound + delta, -(bound + delta)))
    for offset in sorted(offsets):
        initialise(rank)
        observation = step(2000 + offset, 1)
        heads.append({'rank': rank, 'offset': offset, **observation})
    for offset in (0, bounds[0] + 1, bounds[1] + 1, -bounds[2] - 1, bounds[2] + 1):
        for release in (2500, 3000 - bounds[2] - 1, 3000 - bounds[2], 3000, 3001):
            initialise(rank, hold=True)
            before = step(2000 + offset, 1)
            after = step(release, 3)
            holds.append({'rank': rank, 'offset': offset, 'release': release, 'head': before, 'tail': after})
        initialise(rank, hold=True)
        before = step(2000 + offset, 1)
        after = step(3001, 2)
        holds.append({'rank': rank, 'offset': offset, 'release': None, 'head': before, 'tail': after})

arbitration = []
for delta, time in ((50, 2180), (100, 2200), (400, 2180), (500, 2200)):
    initialise(2, note_count=2)
    write(base + 146920, 'i', 2)
    write(base + 146924, 'i', 3)
    write(notes + 64 + 8, 'd', 2000 + delta)
    write(notes + 64 + 16, 'd', 2)
    write(notes + 64 + 40, 'd', 2000 + delta)
    write(notes + 128 + 8, 'd', 1_000_000_000)
    write(notes + 128 + 40, 'd', 1_000_000_000)
    observation = step(time, 1)
    arbitration.append({'secondDelta': delta, 'time': time, **observation})

for gauge in (0, 1, 2, 3):
    for total in (130, 260):
        for note_count in (2, 100, 1000):
            initialise(2, gauge, total, note_count)
            coefficients = [read(base + 621048 + i * 8, 'd') for i in range(6)]
            sequence = ([5, 4, 3, 2, 1, 0] * 5)[:note_count]
            states = []
            for index, grade in enumerate(sequence):
                call(0x405FB0, grade, base, 0, 0, base + 130632, 0)
                states.append({'grade': grade, 'hp': read(base + 621000, 'd'),
                               'exScore': read(base + 621128, 'i'), 'combo': read(base + 620944, 'i'),
                               'maxCombo': read(base + 620940, 'i')})
            gauge_rows.append({'gauge': gauge, 'total': total, 'noteCount': note_count, 'coefficients': coefficients, 'states': states})

data = {'version': 'LR2 beta3 100201', 'sha256': digest, 'authority': 'original x86 instructions under Unicorn 2.1.4',
        'functions': {'bootstrap_range': ['0x4b57c1', '0x4b5b32'], 'input': '0x419320', 'tap': '0x418850',
                      'hold': '0x418e40', 'result': '0x418820', 'scoreAndHealth': '0x405fb0'},
        'excluded': 'Completed parse values seed the original rank/gauge bootstrap basic blocks. Audio, skin timers, replay output and ghost processing are stubbed; client startup/parser/frame scheduler are outside this fixture.',
        'tables': tables, 'heads': heads, 'holds': holds, 'arbitration': arbitration, 'gauges': gauge_rows}
args.output.write_text(json.dumps(data, indent=2) + '\n', encoding='utf8')
print(f'Generated {len(tables)} tables, {len(heads)} heads, {len(holds)} holds and {len(gauge_rows)} gauge sequences.')
