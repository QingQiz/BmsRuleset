"""Run e-Games O2Jam 3.38 judgement, hold and score instructions in Unicorn.

The input ring buffer, note lists and clock list use the shipped layouts. Timing
conversion, nearest-note search, grade comparisons, missed heads/tails, pills,
JAM, combo, HP and score execute original instructions. Only presentation/audio
notifications and the observer callback are replaced. The game is never started.
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
parser.add_argument('--output', type=Path, default=Path(__file__).resolve().parents[1] / 'RhythmEngine.Tests/Reference/o2jam-338.json')
args = parser.parse_args()
if args.unicorn_library:
    sys.path.insert(0, str(args.unicorn_library))
from unicorn import Uc, UC_ARCH_X86, UC_MODE_32, UC_HOOK_CODE
from unicorn.x86_const import UC_X86_REG_EAX, UC_X86_REG_ECX, UC_X86_REG_ESP, UC_X86_REG_EIP, UC_X86_REG_FPCW

binary = args.binary.read_bytes()
digest = hashlib.sha256(binary).hexdigest()
if digest != '3f40d7f4e828da8e97bab391597d73b16d9580d46ff51d24ee185e1b1405a38b':
    raise ValueError('This harness only admits the pinned e-Games 3.38 executable.')
pe = struct.unpack_from('<I', binary, 60)[0]
count = struct.unpack_from('<H', binary, pe + 6)[0]
optional_size = struct.unpack_from('<H', binary, pe + 20)[0]
optional = pe + 24
image_base = struct.unpack_from('<I', binary, optional + 28)[0]
image_size, header_size = struct.unpack_from('<II', binary, optional + 56)
sections = [struct.unpack_from('<8sIIIIIIHHI', binary, optional + optional_size + i * 40) for i in range(count)]
machine = Uc(UC_ARCH_X86, UC_MODE_32)
machine.reg_write(UC_X86_REG_FPCW, 0x027F)
machine.mem_map(image_base, (image_size + 4095) & ~4095)
machine.mem_write(image_base, binary[:header_size])
for _, _, rva, size, file_offset, *_ in sections:
    if size:
        machine.mem_write(image_base + rva, binary[file_offset:file_offset + size])
machine.mem_map(0x10000000, 0x200000)
machine.mem_map(0x20000000, 4096)
base, controller = 0x10001000, 0x10003000
sentinel, head, tail = 0x10006000, 0x10006100, 0x10006200
clock_sentinel, clock_nodes = 0x10007000, 0x10007100
callback, notify, return_address = 0x10008000, 0x10008100, 0x20000000
events = []


def write(address, fmt, value):
    machine.mem_write(address, struct.pack('<' + fmt, value))


def read(address, fmt):
    return struct.unpack('<' + fmt, machine.mem_read(address, struct.calcsize(fmt)))[0]


def return_from(uc, popped):
    stack = uc.reg_read(UC_X86_REG_ESP)
    target = read(stack, 'I')
    uc.reg_write(UC_X86_REG_ESP, stack + 4 + popped)
    uc.reg_write(UC_X86_REG_EIP, target)


def observe(uc, address, size, user_data):
    stack = uc.reg_read(UC_X86_REG_ESP)
    event, payload, code, lane, _ = struct.unpack('<5I', uc.mem_read(stack + 4, 20))
    if event == 1:
        events.append({'payload': payload, 'code': code, 'lane': lane})
    return_from(uc, 20)


def skip_audio(uc, address, size, user_data):
    return_from(uc, 8)


def skip_notification(uc, address, size, user_data):
    return_from(uc, 16)


machine.hook_add(UC_HOOK_CODE, observe, begin=callback, end=callback)
machine.hook_add(UC_HOOK_CODE, skip_audio, begin=0x47EE80, end=0x47EE80)
machine.hook_add(UC_HOOK_CODE, skip_notification, begin=notify, end=notify)


def call(procedure, *parameters, instance=base):
    stack = 0x101FFF00
    machine.reg_write(UC_X86_REG_ESP, stack)
    machine.reg_write(UC_X86_REG_ECX, instance)
    machine.mem_write(stack, struct.pack('<' + 'I' * (1 + len(parameters)), return_address, *parameters))
    machine.emu_start(procedure, return_address, timeout=20000, count=100000)
    if machine.reg_read(UC_X86_REG_EIP) != return_address:
        raise RuntimeError('Native function did not return within its instruction/time budget.')
    return machine.reg_read(UC_X86_REG_EAX)


# This ABI bridge forwards an integer millisecond clock to the original clock
# mapper and original CRT truncation helper; it performs no timing arithmetic.
ftol = 0x48158B + 5 + struct.unpack('<i', machine.mem_read(0x48158C, 4))[0]
bridge = 0x10008200
machine.mem_write(bridge, b'\xff\x74\x24\x04\xb8' + struct.pack('<I', 0x4821E0) + b'\xff\xd0\xb8' + struct.pack('<I', ftol) + b'\xff\xd0\xc2\x04\x00')


def initialise(bpm, hold=False, clock_points=None):
    machine.mem_write(base, bytes(0x1000))
    machine.mem_write(sentinel, bytes(0x1000))
    events.clear()
    points = clock_points or [(int(i * 240000 / bpm), i * 192) for i in range(4)]
    timeline = base + 1048
    write(timeline + 4, 'I', clock_sentinel)
    write(timeline + 8, 'I', len(points))
    write(timeline + 12, 'I', clock_nodes)
    for i, (time, tick) in enumerate(points):
        node = clock_nodes + i * 32
        write(node, 'I', clock_nodes + (i + 1) * 32 if i + 1 < len(points) else clock_sentinel)
        write(node + 8, 'I', time)
        write(node + 12, 'I', tick)
    write(base + 1068, 'I', sentinel)
    write(base + 1148, 'I', head)
    write(base + 1928, 'I', 1)
    write(base + 2276, 'I', callback)
    write(sentinel, 'I', head)
    write(head, 'I', tail if hold else sentinel)
    write(head + 4, 'I', sentinel)
    write(head + 8, 'I', 0x2000001 if hold else 1)
    write(head + 16, 'I', 192)
    if hold:
        write(tail, 'I', sentinel)
        write(tail + 4, 'I', head)
        write(tail + 8, 'I', 0x1000001)
        write(tail + 16, 'I', 288)
    return points


def input_at(time, release=False):
    write(base, 'I', int(release) << 16)
    write(base + 4, 'I', time)
    write(base + 1024, 'I', 0)
    write(base + 1028, 'I', 1)
    call(0x481550)


def advance(time):
    tick = call(bridge, time, instance=base + 1048)
    call(0x481850, tick)
    return tick


heads, holds, timeouts = [], [], []
for bpm in (60, 120, 180, 240):
    points = initialise(bpm)
    nominal = points[1][0]
    probes = {nominal}
    for bound in (6, 7, 18, 19, 25, 26):
        for sign in (-1, 1):
            at = int((192 + sign * bound) * 240000 / (bpm * 192))
            probes.update((at - 1, at, at + 1))
    for time in sorted(probes):
        initialise(bpm)
        input_at(time)
        heads.append({'bpm': bpm, 'time': time, 'tick': call(bridge, time, instance=base + 1048), 'events': list(events)})
    end = int(288 * 240000 / (bpm * 192))
    for release in sorted({nominal + 1, end - 1, end, end + 1,
                           *[int((288 + sign * bound) * 240000 / (bpm * 192)) for sign in (-1, 1) for bound in (6, 7, 18, 19, 25, 26)]}):
        initialise(bpm, True)
        input_at(nominal)
        input_at(release, True)
        holds.append({'bpm': bpm, 'headTime': nominal, 'release': release, 'events': list(events)})
    for head_time in (nominal, int((192 + 20) * 240000 / (bpm * 192)), nominal + 10000):
        initialise(bpm, True)
        if head_time < nominal + 10000:
            input_at(head_time)
        tick = advance(end + 1000)
        holds.append({'bpm': bpm, 'headTime': head_time if head_time < nominal + 10000 else None,
                      'advance': end + 1000, 'tick': tick, 'events': list(events)})
    for held_tail in (False, True):
        boundary = int(((288 if held_tail else 192) + 26) * 240000 / (bpm * 192))
        for time in (boundary - 1, boundary, boundary + 1):
            initialise(bpm, True)
            if held_tail:
                input_at(nominal)
            tick = advance(time)
            timeouts.append({'bpm': bpm, 'heldTail': held_tail, 'time': time, 'tick': tick, 'events': list(events)})

changed_clock = [(0, 0), (1000, 192), (3000, 384), (3666, 576)]
for time in (860, 864, 865, 900, 901, 902, 963, 964, 965, 968, 969, 970, 999, 1000, 1001, 1062, 1063, 1072, 1073, 1187, 1188, 1197, 1198, 1260, 1261, 1270, 1271):
    initialise(120, clock_points=changed_clock)
    input_at(time)
    heads.append({'bpm': 120, 'clockPoints': changed_clock, 'time': time,
                  'tick': call(bridge, time, instance=base + 1048), 'events': list(events)})
for release in (1800, 1900, 1938, 2000, 2062, 2073, 2200, 2271):
    initialise(120, True, changed_clock)
    input_at(1000)
    input_at(release, True)
    holds.append({'bpm': 120, 'clockPoints': changed_clock, 'headTime': 1000, 'release': release, 'events': list(events)})

scores = []
for difficulty in range(3):
    machine.mem_write(controller, bytes(0x2000))
    machine.mem_write(0x5F1D5C, bytes(64))
    write(0x5F1D6C, 'H', 1000)
    write(0x5DB5E4, 'I', 0)
    write(0x5EFC84, 'B', 0)
    write(controller + 28, 'I', difficulty)
    call(0x47F340, instance=controller)
    call(0x47ED80, instance=controller)
    presentation = 0x1000B000
    vtable = 0x1000C000
    write(controller + 4, 'I', presentation + 60)
    write(presentation, 'I', vtable)
    write(presentation + 60, 'I', vtable)
    for slot in (8, 48):
        write(vtable + slot, 'I', notify)
    sequence = [16] * 10 + [128] + [16] * 5 + [32] + [16] * 80 + [32] * 30 + [64] * 8 + [128] * 35
    states = []
    for code in sequence:
        call(0x47DF10, 1, 0, code, 0, controller)
        states.append({'input': code, 'score': read(0x5F1D74, 'I'), 'hp': read(0x5F1D6C, 'H'),
                       'cool': read(0x5F1D5E, 'H'), 'good': read(0x5F1D60, 'H'), 'bad': read(0x5F1D62, 'H'),
                       'miss': read(0x5F1D64, 'H'), 'combo': read(0x5F1D66, 'H'), 'displayCombo': read(controller + 3220, 'H'), 'maxCombo': read(0x5F1D6E, 'H'),
                       'jamProgress': read(0x5F1D68, 'H'), 'jam': read(0x5F1D6A, 'H'),
                       'pills': read(0x5F1D7C, 'I'), 'coolStreak': read(0x5F1D80, 'I')})
        if read(0x5F1D6C, 'H') == 0:
            break
    scores.append({'difficulty': difficulty, 'states': states})

data = {'version': 'e-Games 3.38 (2005-08-08)', 'sha256': digest,
        'x87_control_word': '0x027f (MSVC double precision, round to nearest)',
        'authority': 'original x86 instructions under Unicorn 2.1.4',
        'functions': {'input': '0x481550', 'timeout': '0x481850', 'clock': '0x4821e0',
                      'judgementHandler': '0x47df10', 'score': '0x47ef20', 'healthTable': '0x47f340', 'jamTable': '0x47ed80'},
        'excluded': 'Audio callback 0x47ee80 and presentation notifications; the timing observer records native callbacks. No client startup, OS or rendering executes.',
        'heads': heads, 'holds': holds, 'timeouts': timeouts, 'scores': scores}
args.output.write_text(json.dumps(data, indent=2) + '\n', encoding='utf8')
print(f'Generated {len(heads)} head cases, {len(holds)} hold traces and {sum(len(s["states"]) for s in scores)} score/HP states.')
