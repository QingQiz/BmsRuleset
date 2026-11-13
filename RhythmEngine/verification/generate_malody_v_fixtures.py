"""Execute the pinned Malody V 6.7.22 ARM64 timing and Key hold methods.

The managed object/array layout and virtual slots are supplied as IL2CPP runtime
services. Metadata setup, the GC write barrier, array allocation and the
hit-error statistics collector are stubbed. Encrypted score containers use plain
64-bit storage accessors. Window generation, comparisons, hold state, tick
draining, score arithmetic, float rounding, combo and HP execute original code.
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
parser.add_argument('--output', type=Path, default=Path(__file__).resolve().parents[1] / 'RhythmEngine.Tests/Reference/malody-v-6722.json')
parser.add_argument('--routing-only', action='store_true', help='Retain existing numerical fixtures while regenerating routing traces.')
args = parser.parse_args()
if args.unicorn_library:
    sys.path.insert(0, str(args.unicorn_library))
from unicorn import Uc, UC_ARCH_ARM64, UC_MODE_ARM, UC_HOOK_CODE
from unicorn.arm64_const import UC_ARM64_REG_X0, UC_ARM64_REG_X1, UC_ARM64_REG_X2, UC_ARM64_REG_X3, UC_ARM64_REG_X4, UC_ARM64_REG_X5, UC_ARM64_REG_SP, UC_ARM64_REG_LR, UC_ARM64_REG_PC, UC_ARM64_REG_CPACR_EL1, UC_ARM64_REG_D0

binary = args.binary.read_bytes()
digest = hashlib.sha256(binary).hexdigest()
if digest != '2f1a4f10a95ce62ba4fc37f4f848c95826c89214d239f98077b237cdd34ce16c':
    raise ValueError('This harness only admits the pinned 6.7.22 arm64 library.')
phoff = struct.unpack_from('<Q', binary, 32)[0]
phsize, phcount = struct.unpack_from('<HH', binary, 54)
segments = [struct.unpack_from('<IIQQQQQQ', binary, phoff + i * phsize) for i in range(phcount)]
loads = [s for s in segments if s[0] == 1]
machine = Uc(UC_ARCH_ARM64, UC_MODE_ARM)
machine.mem_map(0, (max(s[3] + s[6] for s in loads) + 4095) & ~4095)
for _, _, file_offset, address, _, file_size, _, _ in loads:
    machine.mem_write(address, binary[file_offset:file_offset + file_size])
shoff = struct.unpack_from('<Q', binary, 40)[0]
shsize, shcount = struct.unpack_from('<HH', binary, 58)
sections = [struct.unpack_from('<IIQQQQIIQQ', binary, shoff + i * shsize) for i in range(shcount)]
for s in sections:
    if s[1] == 4:
        for offset in range(s[4], s[4] + s[5], s[9]):
            address, info, addend = struct.unpack_from('<QQq', binary, offset)
            if info & 0xFFFFFFFF == 1027:
                machine.mem_write(address, struct.pack('<Q', addend))

machine.mem_map(0x10000000, 0x200000)
machine.mem_map(0x20000000, 4096)
machine.reg_write(UC_ARM64_REG_CPACR_EL1, 0x300000)
judge, play_info, settings = 0x10001000, 0x10002000, 0x10003000
carrier, note, state = 0x10004000, 0x10005000, 0x10006000
start_beat, end_beat = 0x10007000, 0x10008000
judge_type, carrier_type, math_type = 0x10009000, 0x1000A000, 0x1000B000
array, return_address = 0x1000C000, 0x20000000


def write(address, fmt, value):
    machine.mem_write(address, struct.pack('<' + fmt, value))


def read(address, fmt):
    return struct.unpack('<' + fmt, machine.mem_read(address, struct.calcsize(fmt)))[0]


def runtime_service(uc, address, size, user_data):
    if address == 0x1883968:
        machine.mem_write(array, bytes(256))
        write(array + 24, 'i', machine.reg_read(UC_ARM64_REG_X1))
        machine.reg_write(UC_ARM64_REG_X0, array)
    uc.reg_write(UC_ARM64_REG_PC, uc.reg_read(UC_ARM64_REG_LR))


def score_storage(uc, address, size, user_data):
    container = uc.reg_read(UC_ARM64_REG_X0)
    if address == 0x2E81ADC:
        uc.reg_write(UC_ARM64_REG_X0, read(container + 16, 'Q'))
    elif address == 0x2E81BC8:
        write(container + 16, 'Q', uc.reg_read(UC_ARM64_REG_X1))
    elif address == 0x2E81EC4:
        uc.reg_write(UC_ARM64_REG_D0, read(container + 16, 'Q'))
    elif address == 0x2E81F30:
        write(container + 16, 'Q', uc.reg_read(UC_ARM64_REG_D0))
    uc.reg_write(UC_ARM64_REG_PC, uc.reg_read(UC_ARM64_REG_LR))


for address in (0x1883884, 0x1883830, 0x1883968, 0x1B457C0):
    machine.hook_add(UC_HOOK_CODE, runtime_service, begin=address, end=address)
for address in (0x2E81ADC, 0x2E81BC8, 0x2E81EC4, 0x2E81F30):
    machine.hook_add(UC_HOOK_CODE, score_storage, begin=address, end=address)
write(0x3D918B0, 'Q', math_type)
write(math_type + 224, 'i', 1)
write(judge_type + 424, 'Q', 0x1B45A40)
write(carrier_type + 392, 'Q', 0x1B039CC)


def call(procedure, *parameters):
    machine.reg_write(UC_ARM64_REG_SP, 0x101FFF00)
    machine.reg_write(UC_ARM64_REG_LR, return_address)
    for register, value in zip((UC_ARM64_REG_X0, UC_ARM64_REG_X1, UC_ARM64_REG_X2, UC_ARM64_REG_X3), parameters):
        machine.reg_write(register, value & 0xFFFFFFFFFFFFFFFF)
    try:
        machine.emu_start(procedure, return_address, timeout=20000, count=100000)
    except Exception as error:
        raise RuntimeError(f'Native call {procedure:#x} {parameters} failed at {machine.reg_read(UC_ARM64_REG_PC):#x}.') from error
    if machine.reg_read(UC_ARM64_REG_PC) != return_address:
        raise RuntimeError('Native function did not return within its instruction/time budget.')
    return machine.reg_read(UC_ARM64_REG_X0) & 0xFFFFFFFF


def initialise(rank, pro, end_judgement=False, suppress_early_miss=False, scale=1, convert=0, hold=False):
    for address in (judge, play_info, settings, carrier, note, state, start_beat, end_beat):
        machine.mem_write(address, bytes(256))
    call(0x1B08750, settings)
    write(judge, 'Q', judge_type)
    write(play_info + 88, 'i', rank)
    write(play_info + 92, '?', pro)
    write(play_info + 96, 'Q', settings)
    write(settings + 16, '?', end_judgement)
    write(settings + 18, '?', suppress_early_miss)
    write(settings + 20, 'f', scale)
    write(settings + 28, 'i', convert)
    call(0x1B459CC, judge, play_info)
    write(carrier, 'Q', carrier_type)
    write(carrier + 16, 'd', 1000)
    write(carrier + 48, 'd', 2000 if hold else 1000)
    write(carrier + 104, 'Q', note)
    write(carrier + 128, 'Q', state)
    write(note + 16, 'Q', start_beat)
    write(note + 24, 'Q', end_beat if hold else 0)
    write(note + 32, 'i', int(hold))
    write(start_beat + 32, 'i', 1000)
    write(end_beat + 32, 'i', 2000)
    write(state + 28, 'i', 0x7FFFFFFF)


def snapshot(code):
    flags = read(state + 16, 'i')
    return {'code': code, 'headResolved': bool(flags & 1), 'held': bool(flags & 4),
            'finished': bool(flags & 8), 'nextTick': read(state + 28, 'i')}


windows, comparisons = [], []
for rank in range(0 if args.routing_only else 5):
    for pro in (False, True):
        for convert in (0, 1, 25, 50, 51, 100):
            initialise(rank, pro, convert=convert)
            bounds = list(struct.unpack('<4i', machine.mem_read(array + 32, 16)))
            windows.append({'rank': rank, 'pro': pro, 'convert': convert, 'windows': bounds})
        for scale in (0.5, 1, 1.5):
            for suppress in (False, True):
                initialise(rank, pro, suppress_early_miss=suppress, scale=scale)
                bounds = list(struct.unpack('<4i', machine.mem_read(array + 32, 16)))
                offsets = {0}
                for bound in (20, *bounds, bounds[2] + 34):
                    for delta in (-1, 0, 1):
                        offsets.update((int(bound * scale) + delta, -(int(bound * scale) + delta)))
                for offset in sorted(offsets):
                    code = call(0x1B45C8C, judge, -offset)
                    comparisons.append({'rank': rank, 'pro': pro, 'scale': scale,
                                        'suppressEarlyMiss': suppress, 'offset': offset, 'code': code})

holds = []
for rank in range(0 if args.routing_only else 5):
    for pro in (False, True):
        for end_judgement in (False, True):
            for release in (1500, 1835, 1870, 1890, 1914, 1955, 1980, 2000, 2020, 2045, 2085, 2130, 2171):
                initialise(rank, pro, end_judgement=end_judgement, hold=True)
                head = snapshot(call(0x1B45E48, judge, carrier, 1, 1000))
                outputs = []
                # The caller repeats the original method while its Repeat flag is set.
                for _ in range(100):
                    code = call(0x1B45E48, judge, carrier, 2, release)
                    outputs.append(snapshot(code))
                    if code & 0x20000000 == 0:
                        break
                else:
                    raise RuntimeError('Release did not drain its repeat outputs.')
                holds.append({'rank': rank, 'pro': pro, 'endJudgement': end_judgement,
                              'release': release, 'head': head, 'outputs': outputs})

scores = []
score = 0x10010000
for rank in range(0 if args.routing_only else 5):
    machine.mem_write(score, bytes(4096))
    for i, member in enumerate((16, 24, 32, 40, 80)):
        write(score + member, 'Q', score + 256 * (i + 1))
    write(score + 56, 'Q', score + 2048)
    write(score + 2048 + 24, 'i', 8)
    write(score + 48, '?', True)
    write(score + 72, 'f', 1)
    write(score + 76, 'f', 100)
    write(score + 106, 'h', rank)
    multiplier = read(0xA81BB0 + rank * 8, 'd')
    write(read(score + 80, 'Q') + 16, 'd', multiplier)
    # Include integer/fractional score edges, all grades, tail grades, a drop,
    # bonus zero and recovery, tick combo, and the critical sub-counter.
    sequence = [4] * 5 + [2, 8, 1, 128, 0x40000, 0x4000002, 0x842, 0x824, 0x821] + [1] * 6 + [2] * 40
    states = []
    for code in sequence:
        call(0x1B484F8, score, code)
        displayed = call(0x1B4825C, score)
        call(0x1B48CA4, score)
        # Accuracy returns single precision in S0, not D0.
        accuracy = struct.unpack('<f', struct.pack('<I', machine.reg_read(UC_ARM64_REG_D0) & 0xFFFFFFFF))[0]
        states.append({'code': code, 'score': displayed, 'rawScore': read(read(score + 16, 'Q') + 16, 'd'),
                       'accuracy': accuracy, 'hp': read(score + 72, 'f'), 'bonus': read(score + 76, 'f'),
                       'combo': read(read(score + 24, 'Q') + 16, 'Q'),
                       'maxCombo': read(read(score + 32, 'Q') + 16, 'Q'),
                       'grades': list(struct.unpack('<5i', machine.mem_read(score + 2048 + 32, 20)))})
    scores.append({'rank': rank, 'multiplier': multiplier, 'states': states})

if args.routing_only:
    previous = json.loads((Path(__file__).resolve().parents[1] / 'RhythmEngine.Tests/Reference/malody-v-6722.json').read_text(encoding='utf8'))
    if previous['sha256'] != digest:
        raise ValueError('The retained numerical fixtures must belong to the admitted library.')
    windows, comparisons, holds, scores = (previous[key] for key in ('windows', 'comparisons', 'holds', 'scores'))

# Supply collection storage and the notification sink, retaining the original
# Key input/expiry loops, base dispatcher, repeat loop and judge virtual calls.
logic, logic_type = 0x10020000, 0x10021000
lane_state, input_type, input_static = 0x10022000, 0x10023000, 0x10024000
route_notes, route_events = [], []


def collection_service(uc, address, size, user_data):
    pointer = uc.reg_read(UC_ARM64_REG_X0)
    if address == 0x1B36968:
        uc.reg_write(UC_ARM64_REG_X0, 0x10025000)
    elif address == 0x2C77AB4:
        uc.reg_write(UC_ARM64_REG_X0, 0)
        uc.reg_write(UC_ARM64_REG_X1, 0)
    elif address == 0x268AE58:
        index = read(pointer, 'Q')
        write(pointer, 'Q', index + 1)
        uc.reg_write(UC_ARM64_REG_X0, int(index < len(route_notes)))
    elif address == 0x268ADEC:
        uc.reg_write(UC_ARM64_REG_X0, route_notes[read(pointer, 'Q') - 1])
    elif address == 0x1B2EC70:
        target = uc.reg_read(UC_ARM64_REG_X1)
        route_events.append({'note': route_notes.index(target), 'input': uc.reg_read(UC_ARM64_REG_X2),
                             'time': uc.reg_read(UC_ARM64_REG_X4), 'code': uc.reg_read(UC_ARM64_REG_X5) & 0xFFFFFFFF})
    uc.reg_write(UC_ARM64_REG_PC, uc.reg_read(UC_ARM64_REG_LR))


for address in (0x1B36968, 0x2C77AB4, 0x268AE58, 0x268ADEC, 0x1B2EC70, 0x1B2F424):
    machine.hook_add(UC_HOOK_CODE, collection_service, begin=address, end=address)
write(0x3D96968, 'Q', input_type)
write(input_type + 184, 'Q', input_static)
write(input_static, '?', True)
write(logic_type + 504, 'Q', 0x1B38D2C)
write(judge_type + 408, 'Q', 0x1B45E48)

routes = []
for rank in range(5):
    for pro in (False, True):
        for end_judgement in (False, True):
            for scenario in ('missed-hold', 'overlap-regrab', 'released-tail', 'held-tail', 'late-head'):
                initialise(rank, pro, end_judgement=end_judgement, hold=True)
                machine.mem_write(logic, bytes(256))
                machine.mem_write(lane_state, bytes(256))
                write(logic, 'Q', logic_type)
                write(logic + 40, 'i', 100)
                write(logic + 48, 'Q', judge)
                write(logic + 104, 'Q', lane_state)
                write(logic + 112, 'i', read(array + 40, 'i'))
                write(logic + 116, 'i', -2147483648)
                write(logic + 120, '?', end_judgement)
                write(lane_state + 24, 'i', 1)
                route_notes[:] = [carrier]
                route_events.clear()
                inputs = {'missed-hold': [(1400, True)],
                          'overlap-regrab': [(1000, True), (1250, False), (1500, True), (2050, False)],
                          'released-tail': [(1000, True), (1950, False)],
                          'held-tail': [(1000, True)],
                          'late-head': [(1000 + read(array + 40, 'i'), True)]}[scenario]
                chart_notes = [{'start': 1000, 'end': 2000}]
                if scenario == 'overlap-regrab':
                    other, other_note, other_state, other_beat = 0x10026000, 0x10027000, 0x10028000, 0x10029000
                    for address in (other, other_note, other_state, other_beat):
                        machine.mem_write(address, bytes(256))
                    write(other, 'Q', carrier_type)
                    write(other + 16, 'd', 1500)
                    write(other + 48, 'd', 1500)
                    write(other + 104, 'Q', other_note)
                    write(other + 128, 'Q', other_state)
                    write(other_note + 16, 'Q', other_beat)
                    write(other_beat + 32, 'i', 1500)
                    write(other_state + 28, 'i', 2147483647)
                    route_notes.append(other)
                    chart_notes.append({'start': 1500, 'end': 1500})
                frames = sorted(set(range(800, 2401, 100)) | {t for t, _ in inputs}
                                | {1000 + read(array + 40, 'i'), 1001 + read(array + 40, 'i'),
                                   2000 + read(array + 44, 'i'), 2001 + read(array + 44, 'i')})
                for time in frames:
                    for input_time, pressed in inputs:
                        if input_time == time:
                            call(0x1B389B8, logic, 0, int(pressed), time)
                    call(0x1B385AC, logic, time)
                routes.append({'rank': rank, 'pro': pro, 'endJudgement': end_judgement, 'scenario': scenario,
                               'notes': chart_notes, 'inputs': [{'time': t, 'pressed': p} for t, p in inputs],
                               'until': frames[-1], 'events': list(route_events)})

data = {
    'authority': 'original ARM64 instructions under Unicorn 2.1.4', 'version': '6.7.22 (381)', 'sha256': digest,
    'functions': {'settingsConstructor': '0x1b08750', 'init': '0x1b459cc', 'windows': '0x1b45a40',
                  'compare': '0x1b45c8c', 'hold': '0x1b45e48', 'tailConversion': '0x1b45dd4'},
    'excluded': 'IL2CPP metadata resolution, GC barrier, array/collection storage and hit-error statistics. Encrypted score containers use plain 64-bit getter/setter storage; score arithmetic, float rounding, combo, grade counts and HP execute original instructions. Key routing/expiry uses original loops and judge dispatch with an event notification sink; Unity and the application scheduler are excluded.',
    'windows': windows, 'comparisons': comparisons, 'holds': holds, 'scores': scores, 'routes': routes,
}
args.output.write_text(json.dumps(data, indent=2) + '\n', encoding='utf8')
print(f'Generated {len(windows)} window tables, {len(comparisons)} comparisons, {len(holds)} release traces, {sum(len(s["states"]) for s in scores)} score states and {len(routes)} routing traces.')
