"""Run the pinned Android 4.3.7 ARM64 judgement functions in Unicorn.

No Android application, JNI entry point or operating-system call executes. The
statistics collector is stubbed; Score's cipher singleton receives controlled
valid inverse permutations. Judgement and score/HP arithmetic are original code.
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
parser.add_argument('--output', type=Path, default=Path(__file__).resolve().parents[1] / 'RhythmEngine.Tests/Reference/malody-android-437.json')
parser.add_argument('--routing-only', action='store_true')
args = parser.parse_args()
if args.unicorn_library:
    sys.path.insert(0, str(args.unicorn_library))
from unicorn import Uc, UC_ARCH_ARM64, UC_MODE_ARM, UC_HOOK_CODE
from unicorn.arm64_const import UC_ARM64_REG_X0, UC_ARM64_REG_X1, UC_ARM64_REG_X2, UC_ARM64_REG_SP, UC_ARM64_REG_LR, UC_ARM64_REG_PC, UC_ARM64_REG_CPACR_EL1
from unicorn import arm64_const

binary = args.binary.read_bytes()
digest = hashlib.sha256(binary).hexdigest()
if digest != '1140dc9374886d3326dc067bf4576159bc3c18d1dc3f34c2aaf5a41d55bc3bb0':
    raise ValueError('This harness only admits the pinned 4.3.7 arm64 library.')
phoff = struct.unpack_from('<Q', binary, 32)[0]
phsize, phcount = struct.unpack_from('<HH', binary, 54)
segments = [struct.unpack_from('<IIQQQQQQ', binary, phoff + i * phsize) for i in range(phcount)]
loads = [segment for segment in segments if segment[0] == 1]
machine = Uc(UC_ARCH_ARM64, UC_MODE_ARM)
machine.mem_map(0, (max(s[3] + s[6] for s in loads) + 4095) & ~4095)
for _, _, file_offset, address, _, file_size, _, _ in loads:
    machine.mem_write(address, binary[file_offset:file_offset + file_size])
shoff = struct.unpack_from('<Q', binary, 40)[0]
shsize, shcount = struct.unpack_from('<HH', binary, 58)
sections = [struct.unpack_from('<IIQQQQIIQQ', binary, shoff + i * shsize) for i in range(shcount)]
relocations = {}
for section in sections:
    if section[1] == 4:
        for offset in range(section[4], section[4] + section[5], section[9]):
            address, info, addend = struct.unpack_from('<QQq', binary, offset)
            if info & 0xFFFFFFFF == 1027:
                relocations[address] = addend
                machine.mem_write(address, struct.pack('<Q', addend))


def virtual_address(file_offset):
    return next(s[3] + file_offset - s[2] for s in loads if s[2] <= file_offset < s[2] + s[5])


note_name_address = virtual_address(binary.index(b'12HitObjectKey\0'))
note_type_info = next(address - 8 for address, addend in relocations.items() if addend == note_name_address)
note_vtable = next(address + 8 for address, addend in relocations.items()
                   if addend == note_type_info and machine.mem_read(address - 8, 8) == bytes(8)
                   and any(s[1] & 1 and s[3] <= relocations.get(address + 8, 0) < s[3] + s[6] for s in loads))
machine.mem_map(0x10000000, 0x100000)
machine.mem_map(0x20000000, 4096)
machine.reg_write(UC_ARM64_REG_CPACR_EL1, 0x300000)
judge_address = 0x10001000
note_address = 0x10002000
return_address = 0x20000000


def skip_statistics(uc, address, size, user_data):
    uc.reg_write(UC_ARM64_REG_PC, uc.reg_read(UC_ARM64_REG_LR))


machine.hook_add(UC_HOOK_CODE, skip_statistics, begin=0x30E610, end=0x30E610)


def cipher_singleton(uc, address, size, user_data):
    uc.reg_write(UC_ARM64_REG_X0, 0x10003000)
    uc.reg_write(UC_ARM64_REG_PC, uc.reg_read(UC_ARM64_REG_LR))


machine.hook_add(UC_HOOK_CODE, cipher_singleton, begin=0x418630, end=0x418630)


def call(procedure, *parameters):
    machine.reg_write(UC_ARM64_REG_SP, 0x100FFF00)
    machine.reg_write(UC_ARM64_REG_LR, return_address)
    for register, value in zip((UC_ARM64_REG_X0, UC_ARM64_REG_X1, UC_ARM64_REG_X2), parameters):
        machine.reg_write(register, value & 0xFFFFFFFFFFFFFFFF)
    machine.emu_start(procedure, return_address, timeout=20000, count=100000)
    if machine.reg_read(UC_ARM64_REG_PC) != return_address:
        raise RuntimeError('Native function did not return within its instruction/time budget.')
    return machine.reg_read(UC_ARM64_REG_X0) & 0xFFFFFFFF


def read_int(address):
    return struct.unpack('<i', machine.mem_read(address, 4))[0]


def initialise(rank, hold=False):
    machine.mem_write(judge_address, bytes(256))
    machine.mem_write(note_address, bytes(256))
    call(0x30E1D0, judge_address)
    call(0x30DBFC, judge_address, rank)
    machine.mem_write(note_address, struct.pack('<Q', note_vtable))
    for offset, value in [(48, 1000), (72, 2000 if hold else 1000), (80, 128 if hold else 0), (92, 2147483647)]:
        machine.mem_write(note_address + offset, struct.pack('<i', value))


def result(code):
    if code == 0x40000000:
        return None
    return {1: 'miss', 2: 'best', 4: 'cool', 8: 'good', 128: 'hold-tick', 0x40000: 'hold-drop'}[code if code in (128, 0x40000) else code & 15]


head_rows = []
release_rows = []
for rank in range(0 if args.routing_only else 5):
    initialise(rank)
    adjustment = read_int(judge_address + 32)
    bounds = [read_int(judge_address + offset) + adjustment for offset in (48, 52, 56, 60)]
    offsets = {0, 170}
    for bound in bounds:
        for delta in (-1, 0, 1):
            offsets.update((-(bound + delta), bound + delta))
    # Restrict late probes to the constructor's fixed expiry horizon; the caller loop is outside this fixture.
    for offset in sorted(value for value in offsets if value <= 170):
        initialise(rank)
        code = call(0x30E6E4, judge_address, note_address, 1000 + offset)
        head_rows.append({'rank': rank, 'offset': offset, 'code': code, 'result': result(code), 'consumed': bool(read_int(note_address + 88) & 1)})
    for time in (1869, 1870, 1871, 2000):
        initialise(rank, True)
        call(0x30E6E4, judge_address, note_address, 1000)
        code = call(0x30DD64, judge_address, note_address, time)
        release_rows.append({'rank': rank, 'time': time, 'code': code, 'result': result(code), 'finished': bool(read_int(note_address + 88) & 8)})

initialise(2, True)
call(0x30E6E4, judge_address, note_address, 1000)
ticks = []
for time in range(1100, 2001, 100):
    code = call(0x30DDB4, judge_address, note_address, time)
    ticks.append({'time': time, 'code': code, 'result': result(code)})


def score_rows(rotation):
    cipher_address, score_address = 0x10003000, 0x10004000
    encode = bytes((i + rotation) % 256 for i in range(256))
    decode = bytes((i - rotation) % 256 for i in range(256))
    machine.mem_write(cipher_address, bytes(8) + encode + decode)
    rows = []
    sequence = ['cool'] * 5 + ['best', 'good', 'miss'] * 2 + ['miss'] * 5 + ['best'] * 100
    for rank in range(5):
        machine.mem_write(score_address, bytes(256))
        call(0x318A18, score_address)
        call(0x314BF4, score_address, len(sequence))
        call(0x315668, score_address, rank, 0)
        for index, requested in enumerate(sequence):
            initialise(rank)
            adjustment = read_int(judge_address + 32)
            offset = {'best': 0, 'cool': read_int(judge_address + 48) + adjustment + 1,
                      'good': read_int(judge_address + 52) + adjustment + 1,
                      'miss': read_int(judge_address + 56) + adjustment + 1}[requested]
            code = call(0x30E6E4, judge_address, note_address, 1000 + offset)
            call(0x3166B8, score_address, code)
            value = call(0x317034, score_address)
            call(0x314C8C, score_address)
            accuracy = struct.unpack('<f', bytes(decode[b] for b in machine.mem_read(score_address + 76, 4)))[0] / 100
            hp, bonus = struct.unpack('<fi', machine.mem_read(score_address + 80, 8))
            combo, max_combo = struct.unpack('<II', bytes(decode[b] for b in machine.mem_read(score_address + 28, 8)))
            rows.append({'rank': rank, 'index': index, 'offset': offset, 'code': code, 'result': result(code),
                         'score': value, 'accuracy': accuracy, 'health': hp, 'bonus': bonus, 'combo': combo, 'max_combo': max_combo})
    return rows


if args.routing_only:
    previous = json.loads((Path(__file__).resolve().parents[1] / 'RhythmEngine.Tests/Reference/malody-android-437.json').read_text(encoding='utf8'))
    if previous['sha256'] != digest:
        raise ValueError('Retained numerical fixtures belong to another binary.')
    head_rows, release_rows, ticks, scores = (previous[key] for key in ('heads', 'releases', 'ticks', 'scores'))
else:
    scores = score_rows(0)
    if scores != score_rows(37):
        raise AssertionError('Score results depend on the byte-permutation key.')


def write(address, fmt, value):
    machine.mem_write(address, struct.pack('<' + fmt, value))


def register(name, value):
    machine.reg_write(getattr(arm64_const, 'UC_ARM64_REG_' + name), value)


game, game_vtable, options, lanes, lane_sprites = (0x10010000 + i * 4096 for i in range(5))
deque, wrappers, carriers = 0x10015000, 0x10016000, 0x10017000
event_sink = 0x20000100
route_events, route_notes = [], []
current_time = 0


def presentation(uc, address, size, user_data):
    if address == event_sink:
        target = uc.reg_read(UC_ARM64_REG_X1)
        code = uc.reg_read(UC_ARM64_REG_X2) & 0xFFFFFFFF
        route_events.append({'note': route_notes.index(target), 'time': current_time, 'code': code})
    uc.reg_write(UC_ARM64_REG_PC, uc.reg_read(UC_ARM64_REG_LR))


for address in (event_sink, 0x28DDB0, 0x28DDA4, 0x28DDBC):
    machine.hook_add(UC_HOOK_CODE, presentation, begin=address, end=address)


def block(start, end):
    register('SP', 0x100FF000)
    register('X29', 0x100FF000)
    try:
        machine.emu_start(start, end, timeout=20000, count=100000)
    except Exception as error:
        raise RuntimeError(f'Original block {start:#x} failed at {machine.reg_read(UC_ARM64_REG_PC):#x}.') from error
    if machine.reg_read(UC_ARM64_REG_PC) != end:
        raise RuntimeError('Original block exceeded its instruction/time budget.')


routes = []
for rank in range(5):
    for scenario in ('missed-hold', 'overlap-regrab', 'released-tail', 'held-tail', 'expiry-boundary'):
        initialise(rank, True)
        for address in (game, game_vtable, options, lanes, lane_sprites, deque, wrappers, carriers):
            machine.mem_write(address, bytes(4096))
        write(game, 'Q', game_vtable)
        write(game_vtable + 0x5C8, 'Q', event_sink)
        write(game + 0x3E8, 'Q', judge_address)
        write(game + 0x400, 'Q', options)
        write(game + 0x4A8, 'Q', lanes)
        write(game + 0x628, 'Q', lane_sprites)
        route_notes[:] = [note_address]
        chart_notes = [{'start': 1000, 'end': 2000}]
        if scenario == 'overlap-regrab':
            other = 0x10018000
            machine.mem_write(other, bytes(256))
            write(other, 'Q', note_vtable)
            write(other + 48, 'i', 1500)
            write(other + 72, 'i', 1500)
            write(other + 92, 'i', 2147483647)
            route_notes.append(other)
            chart_notes.append({'start': 1500, 'end': 1500})
        for i, target in enumerate(route_notes):
            wrapper, carrier = wrappers + i * 256, carriers + i * 256
            write(deque + i * 8, 'Q', wrapper)
            write(wrapper + 16, 'Q', carrier)
            write(carrier + 40, 'Q', target)
        for member, value in ((0x5C8, deque), (0x5D8, deque + 512), (0x5E0, deque + 2048), (0x5E8, deque + len(route_notes) * 8)):
            write(game + member, 'Q', value)
        route_events.clear()
        inputs = {'missed-hold': [(1400, True)],
                  'overlap-regrab': [(1000, True), (1250, False), (1500, True), (2050, False)],
                  'released-tail': [(1000, True), (1870, False)],
                  'held-tail': [(1000, True)],
                  'expiry-boundary': [(1170, True)]}[scenario]
        frames = sorted(set(range(800, 2301, 50)) | {t for t, _ in inputs} | {1170, 1171})
        for current_time in frames:
            write(game + 0x438, 'd', current_time)
            for time, pressed in inputs:
                if time != current_time:
                    continue
                write(lanes, 'b', int(pressed))
                for name, value in [('X24', game), ('X21', 0), ('X22', int(pressed)), ('X23', 0 if pressed else 2)]:
                    register(name, value)
                # Enter the original manual Key traversal after UI/control-count handling.
                block(0x39B9F0, 0x39BB00)
            register('X19', game)
            register('D3', struct.unpack('<Q', struct.pack('<d', current_time))[0])
            # Keep the shipped 170ms constant and original float comparisons.
            block(0x39F26C, 0x39F278)
            block(0x39F3B0, 0x39F548)
        routes.append({'rank': rank, 'scenario': scenario, 'notes': chart_notes,
                       'inputs': [{'time': t, 'pressed': p} for t, p in inputs], 'until': frames[-1], 'events': list(route_events)})
data = {
    'authority': 'original ARM64 instructions under Unicorn 2.1.4',
    'sha256': digest,
    'functions': {'constructor': '0x30e1d0', 'rank': '0x30dbfc', 'press': '0x30e6e4', 'release': '0x30dd64', 'tick': '0x30ddb4',
                  'score_constructor': '0x318a18', 'score_rank': '0x315668', 'score_update': '0x3166b8', 'score_getter': '0x317034', 'accuracy_getter': '0x314c8c',
                  'inputTraversal': '0x39b9f0..0x39bb00', 'expiryClock': '0x39f26c..0x39f278', 'expiryAndHeldTicks': '0x39f3b0..0x39f548'},
    'excluded': 'Hit-error statistics collector at 0x30e610; Android/JNI, application scheduler, control-count bookkeeping and presentation. Routing fixtures enter original manual Key traversal and expiry/tick basic blocks with valid loaded carrier/deque layouts. Event/presentation callbacks are sinks. Cipher singleton uses controlled inverse byte permutations; score/decimal/HP routines are not stubbed.',
    'heads': head_rows,
    'releases': release_rows,
    'ticks': ticks,
    'scores': scores,
    'routes': routes,
}
args.output.write_text(json.dumps(data, indent=2) + '\n', encoding='utf8')
print(f'Generated {len(head_rows)} native head cases, {len(release_rows)} releases, {len(ticks)} ticks, {len(scores)} score/HP states and {len(routes)} routing traces.')
