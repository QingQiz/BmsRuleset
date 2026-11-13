"""Execute the pinned 4.3.7 PC judgement instructions, with only statistics stubbed.

The harness loads the PE image at its preferred base and calls the real judgement
constructor/rank setter. It supplies a note with the shipped HitObjectKey vtable
and the fields used by the head/release/tick routines. No application startup,
Windows API, audio or rendering entry point executes.
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
parser.add_argument('--output', type=Path, default=Path(__file__).resolve().parents[1] / 'RhythmEngine.Tests/Reference/malody-pc-437.json')
parser.add_argument('--routing-only', action='store_true')
args = parser.parse_args()
if args.unicorn_library:
    sys.path.insert(0, str(args.unicorn_library))
from unicorn import Uc, UC_ARCH_X86, UC_MODE_32, UC_HOOK_CODE
from unicorn.x86_const import UC_X86_REG_EAX, UC_X86_REG_ECX, UC_X86_REG_ESP, UC_X86_REG_EIP, UC_X86_REG_FPCW
from unicorn import x86_const

binary = args.binary.read_bytes()
digest = hashlib.sha256(binary).hexdigest()
if digest != 'd0cf9ecb493f3fdbd1e3ce48cbfad06ef816d19931ab0c51d9e9655f5eb63ce3':
    raise ValueError('This harness only admits the pinned 4.3.7 x86 executable.')
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


def virtual_address(file_offset):
    return next(image_base + s[2] + file_offset - s[4] for s in sections if s[4] <= file_offset < s[4] + s[3])


def pointers_to(address):
    pattern = struct.pack('<I', address)
    position = binary.find(pattern)
    while position != -1:
        yield position
        position = binary.find(pattern, position + 1)


descriptor = virtual_address(binary.index(b'.?AVHitObjectKey@@\0') - 8)
locators = [virtual_address(position - 12) for position in pointers_to(descriptor) if binary[position - 12:position] == bytes(12)]
note_vtable = next(virtual_address(position + 4) for locator in locators for position in pointers_to(locator)
                   if any(s[-1] & 0x20000000 and image_base + s[2] <= struct.unpack_from('<I', binary, position + 4)[0] < image_base + s[2] + s[1] for s in sections))
machine.mem_map(0x10000000, 0x100000)
machine.mem_map(0x20000000, 4096)
judge_address = 0x10001000
note_address = 0x10002000
return_address = 0x20000000


def skip_statistics(uc, address, size, user_data):
    stack = uc.reg_read(UC_X86_REG_ESP)
    target = struct.unpack('<I', uc.mem_read(stack, 4))[0]
    uc.reg_write(UC_X86_REG_ESP, stack + 8)
    uc.reg_write(UC_X86_REG_EIP, target)


machine.hook_add(UC_HOOK_CODE, skip_statistics, begin=0x535B00, end=0x535B00)


def call(procedure, *parameters, instance=judge_address):
    machine.reg_write(UC_X86_REG_ESP, 0x100FFF00)
    machine.reg_write(UC_X86_REG_ECX, instance)
    machine.mem_write(0x100FFF00, struct.pack('<' + 'I' * (1 + len(parameters)), return_address, *parameters))
    machine.emu_start(procedure, return_address, timeout=20000, count=100000)
    if machine.reg_read(UC_X86_REG_EIP) != return_address:
        raise RuntimeError('Native function did not return within its instruction/time budget.')
    return machine.reg_read(UC_X86_REG_EAX)


def read_int(address):
    return struct.unpack('<i', machine.mem_read(address, 4))[0]


def initialise(rank, hold=False):
    machine.mem_write(judge_address, bytes(256))
    machine.mem_write(note_address, bytes(256))
    call(0x535B80)
    call(0x5359C0, rank)
    machine.mem_write(note_address, struct.pack('<I', note_vtable))
    for offset, value in [(76, 1000), (100, 2000 if hold else 1000), (108, 128 if hold else 0), (120, 2147483647)]:
        machine.mem_write(note_address + offset, struct.pack('<i', value))


def result(code):
    if code == 0x40000000:
        return None
    return {1: 'miss', 2: 'best', 4: 'cool', 8: 'good', 128: 'hold-tick', 0x40000: 'hold-drop'}[code if code in (128, 0x40000) else code & 15]


heads = []
releases = []
for rank in range(0 if args.routing_only else 5):
    initialise(rank)
    adjustment = read_int(judge_address + 16)
    bounds = [read_int(judge_address + offset) + adjustment for offset in (32, 36, 40, 44)]
    offsets = {0, 160}
    for bound in bounds:
        for delta in (-1, 0, 1):
            offsets.update((-(bound + delta), bound + delta))
    for offset in sorted(value for value in offsets if value <= 160):
        initialise(rank)
        code = call(0x535C40, note_address, 1000 + offset)
        heads.append({'rank': rank, 'offset': offset, 'code': code, 'result': result(code), 'consumed': bool(read_int(note_address + 116) & 1)})
    for time in (1889, 1890, 1891, 2000):
        initialise(rank, True)
        call(0x535C40, note_address, 1000)
        code = call(0x535D50, note_address, time)
        releases.append({'rank': rank, 'time': time, 'code': code, 'result': result(code), 'finished': bool(read_int(note_address + 116) & 8)})

initialise(2, True)
call(0x535C40, note_address, 1000)
ticks = []
for time in range(1100, 2001, 100):
    code = call(0x535DA0, note_address, time)
    ticks.append({'time': time, 'code': code, 'result': result(code)})

# The Score constructor expects the process's byte-permutation singleton and an
# exception-chain slot. Both permutations below are valid inverse maps. All
# decimal score, combo, bonus and HP instructions execute without stubs.
machine.mem_map(0, 4096)
machine.mem_write(0, struct.pack('<I', 0xFFFFFFFF))
cipher_address = 0x10003000
score_address = 0x10004000
machine.mem_write(0xC311C0, struct.pack('<I', cipher_address))


def decoded_int(offset, decode):
    value = bytes(decode[b] for b in machine.mem_read(score_address + offset, 4))
    return struct.unpack('<I', value)[0]


def score_rows(rotation):
    encode = bytes((i + rotation) % 256 for i in range(256))
    decode = bytes((i - rotation) % 256 for i in range(256))
    machine.mem_write(cipher_address, bytes(4) + encode + decode)
    rows = []
    # Includes fractional cumulative credit, zero-bonus clamping and recovery.
    sequence = ['cool', 'best', 'cool', 'good', 'miss', 'best', 'cool', 'good', 'miss', 'miss', 'miss', 'miss'] + ['best'] * 104
    for rank in range(5):
        machine.mem_write(score_address, bytes(256))
        call(0x53BDF0, instance=score_address)
        call(0x53C0A0, len(sequence), instance=score_address)
        call(0x53C0E0, rank, 0, instance=score_address)
        for index, requested in enumerate(sequence):
            initialise(rank)
            adjustment = read_int(judge_address + 16)
            offset = {'best': 0, 'cool': read_int(judge_address + 32) + adjustment + 1,
                      'good': read_int(judge_address + 36) + adjustment + 1,
                      'miss': read_int(judge_address + 40) + adjustment + 1}[requested]
            code = call(0x535C40, note_address, 1000 + offset)
            call(0x53C1D0, code, instance=score_address)
            value = call(0x53C830, instance=score_address)
            call(0x53C980, instance=score_address)
            accuracy = struct.unpack('<f', bytes(decode[b] for b in machine.mem_read(score_address + 68, 4)))[0] / 100
            hp, bonus = struct.unpack('<fi', machine.mem_read(score_address + 72, 8))
            rows.append({'rank': rank, 'index': index, 'offset': offset, 'code': code, 'result': result(code),
                         'score': value, 'accuracy': accuracy, 'health': hp, 'bonus': bonus,
                         'combo': decoded_int(20, decode), 'max_combo': decoded_int(24, decode)})
    return rows


if args.routing_only:
    previous = json.loads((Path(__file__).resolve().parents[1] / 'RhythmEngine.Tests/Reference/malody-pc-437.json').read_text(encoding='utf8'))
    if previous['sha256'] != digest:
        raise ValueError('Retained numerical fixtures belong to another binary.')
    heads, releases, ticks, scores = (previous[key] for key in ('heads', 'releases', 'ticks', 'scores'))
else:
    scores = score_rows(0)
    if scores != score_rows(37):
        raise AssertionError('Score results depend on the byte-permutation key.')


def write(address, fmt, value):
    machine.mem_write(address, struct.pack('<' + fmt, value))


def register(name, value):
    machine.reg_write(getattr(x86_const, 'UC_X86_REG_' + name), value)


game, game_vtable, options, lanes, lane_sprites = (0x10010000 + i * 4096 for i in range(5))
container, proxy, pointer_map, note_block, wrappers, carriers = (0x10015000 + i * 4096 for i in range(6))
event_sink = 0x20000100
route_events, route_notes = [], []
block_ends = set()
current_time = 0
frame_address = 0x100FF000


def presentation(uc, address, size, user_data):
    stack = uc.reg_read(UC_X86_REG_ESP)
    target = read_int(stack)
    if address == event_sink:
        note, code = struct.unpack('<II', uc.mem_read(stack + 4, 8))
        route_events.append({'note': route_notes.index(note), 'time': current_time, 'code': code})
    uc.reg_write(UC_X86_REG_ESP, stack + (12 if address == event_sink else 8))
    uc.reg_write(UC_X86_REG_EIP, target)


def stop_block(uc, address, size, user_data):
    if address in block_ends:
        uc.emu_stop()


for address in (event_sink, 0x502AB0):
    machine.hook_add(UC_HOOK_CODE, presentation, begin=address, end=address)
for address in (0x5A6FA8, 0x5A7019, 0x5A70A1):
    machine.hook_add(UC_HOOK_CODE, stop_block, begin=address, end=address)


def block(start, ends):
    block_ends.clear()
    block_ends.update(ends)
    register('ESP', frame_address - 256)
    register('EBP', frame_address)
    register('EDI', game)
    try:
        machine.emu_start(start, next(iter(ends)), timeout=20000, count=100000)
    except Exception as error:
        raise RuntimeError(f'Original block {start:#x} failed at {machine.reg_read(UC_X86_REG_EIP):#x}.') from error
    if machine.reg_read(UC_X86_REG_EIP) not in ends:
        raise RuntimeError('Original block exceeded its instruction/time budget.')


routes = []
for rank in range(5):
    for scenario in ('missed-hold', 'overlap-regrab', 'released-tail', 'held-tail', 'expiry-boundary'):
        initialise(rank, True)
        for address in (game, game_vtable, options, lanes, lane_sprites, container, proxy, pointer_map, note_block, wrappers, carriers):
            machine.mem_write(address, bytes(4096))
        write(game, 'I', game_vtable)
        write(game_vtable + 0x2E0, 'I', event_sink)
        write(game + 0x2FC, 'I', judge_address)
        write(game + 0x308, 'I', options)
        write(game + 0x388, 'I', lanes)
        write(game + 0x41C, 'I', lane_sprites)
        write(proxy, 'I', container)
        write(container + 4, 'I', pointer_map)
        write(container + 8, 'I', 1)
        write(pointer_map, 'I', note_block)
        route_notes[:] = [note_address]
        chart_notes = [{'start': 1000, 'end': 2000}]
        if scenario == 'overlap-regrab':
            other = 0x10020000
            machine.mem_write(other, bytes(256))
            write(other, 'I', note_vtable)
            write(other + 76, 'i', 1500)
            write(other + 100, 'i', 1500)
            write(other + 120, 'i', 2147483647)
            route_notes.append(other)
            chart_notes.append({'start': 1500, 'end': 1500})
        for i, target in enumerate(route_notes):
            wrapper, carrier = wrappers + i * 256, carriers + i * 256
            write(note_block + i * 4, 'I', wrapper)
            write(wrapper + 8, 'I', carrier)
            write(carrier + 40, 'I', target)
        route_events.clear()
        inputs = {'missed-hold': [(1400, True)],
                  'overlap-regrab': [(1000, True), (1250, False), (1500, True), (2050, False)],
                  'released-tail': [(1000, True), (1890, False)],
                  'held-tail': [(1000, True)],
                  'expiry-boundary': [(1160, True)]}[scenario]
        frames = sorted(set(range(800, 2301, 50)) | {t for t, _ in inputs} | {1160, 1161})
        for current_time in frames:
            write(game + 0x330, 'd', current_time)
            machine.mem_write(frame_address - 128, bytes(160))
            for time, pressed in inputs:
                if time != current_time:
                    continue
                write(lanes, 'b', int(pressed))
                register('EBX', 0)
                for offset, value in ((-16, proxy), (-8, 0), (-20, len(route_notes)), (12, 0 if pressed else 2)):
                    write(frame_address + offset, 'I', value)
                block(0x5A6E75, {0x5A6FA8, 0x5A7019, 0x5A70A1})
            for offset, value in ((-88, proxy), (-80, 0), (-48, len(route_notes))):
                write(frame_address + offset, 'I', value)
            register('XMM5', struct.unpack('<Q', struct.pack('<d', current_time))[0])
            block(0x5A61D9, {0x5A61E1})
            write(frame_address - 28, 'Q', machine.reg_read(x86_const.UC_X86_REG_XMM5) & 0xFFFFFFFFFFFFFFFF)
            block(0x5A6372, {0x5A63E3})
            block(0x5A6412, {0x5A64EE})
        routes.append({'rank': rank, 'scenario': scenario, 'notes': chart_notes,
                       'inputs': [{'time': t, 'pressed': p} for t, p in inputs], 'until': frames[-1], 'events': list(route_events)})

args.output.write_text(json.dumps({
    'authority': 'original x86 instructions under Unicorn 2.1.4',
    'x87_control_word': '0x027f (MSVC double precision, round to nearest)',
    'sha256': digest,
    'functions': {'constructor': '0x535b80', 'rank': '0x5359c0', 'press': '0x535c40', 'release': '0x535d50', 'tick': '0x535da0',
                  'score_constructor': '0x53bdf0', 'score_rank': '0x53c0e0', 'score_update': '0x53c1d0', 'score_getter': '0x53c830', 'accuracy_getter': '0x53c980',
                  'inputTraversal': '0x5a6e75..original-return-blocks', 'expiryClock': '0x5a61d9..0x5a61e1',
                  'expiry': '0x5a6372..0x5a63e3', 'heldTicks': '0x5a6412..0x5a64ee'},
    'excluded': 'Hit-error statistics collector at 0x535b00; application scheduler, control-count bookkeeping, audio and rendering. Routing fixtures enter original manual Key traversal and expiry/tick basic blocks with valid loaded carrier/deque layouts. Event/presentation callbacks are sinks. Score cipher singleton uses controlled inverse byte permutations.',
    'heads': heads, 'releases': releases, 'ticks': ticks, 'scores': scores, 'routes': routes,
}, indent=2) + '\n', encoding='utf8')
print(f'Generated {len(heads)} native PC heads, {len(releases)} releases, {len(ticks)} ticks, {len(scores)} score/HP states and {len(routes)} routing traces.')
