"""Interpret pinned, decoded original CIL for stable mania's hold resolution.

The supplied REA metadata evidence must bind the pinned executable. This small
interpreter rejects unsupported instructions and missing state. Rendering/audio
callbacks and removal from the manager's active-hold display list are excluded;
every branch, numeric comparison and state write in the result method is original.
"""
import argparse
import hashlib
import json
from pathlib import Path
import struct
import math

parser = argparse.ArgumentParser()
parser.add_argument('--binary', type=Path, required=True)
parser.add_argument('--evidence', type=Path, required=True)
parser.add_argument('--output', type=Path, default=Path(__file__).resolve().parents[1] / 'RhythmEngine.Tests/Reference/stable-holds.json')
args = parser.parse_args()
binary = args.binary.read_bytes()
digest = hashlib.sha256(binary).hexdigest()
if digest != '3adc2d001aacced5f92ba8ae24121980d5e23f5653f37d14cf4765bca1abd9f9':
    raise ValueError('Only the pinned stable executable is admitted.')
records = json.loads(args.evidence.read_text(encoding='utf8'))['records']
metadata = next(r['normalized_result'] for r in records if r['operation'] == 'inspect_managed_members')
if metadata['artifact']['sha256'] != digest:
    raise ValueError('Metadata does not describe the admitted executable.')
method_token = '0x06003a0d'
method = next(m for m in metadata['methods'] if m['token'] == method_token)
fields = {f['field_token']: f['field_name'] for f in metadata['field_accesses']}
call_shapes = {'0x06003a0b': (0, True), '0x060043f8': (0, True), '0x060049dd': (1, True),
               '0x0a000539': (1, False), '0x060043ef': (0, True), '0x0600087b': (1, True), '0x0600087a': (0, True)}
grades = {-65536: 'miss', 16777216: 'meh', 33554432: 'ok', 67108864: 'good', 134217728: 'great', 268435456: 'perfect', -1048576: 'hold-break', 0: None}


def truth(value):
    return value is not None if isinstance(value, dict) else bool(value)


def evaluate(note, statics, token=method_token, arguments=None, helpers=None):
    selected = next(m for m in metadata['methods'] if m['token'] == token)
    instructions = selected['body']['anchors']
    positions = {i['il_offset']: index for index, i in enumerate(instructions)}
    selected_calls = {c['target_token']: c for c in metadata['call_edges'] if c['caller_token'] == token}
    arguments = arguments or [note]
    stack, locals_ = [], {}
    index = 0
    for _ in range(3000):
        instruction = instructions[index]
        opcode, operand = instruction['opcode'], instruction['operand']
        next_index = index + 1
        base_opcode = opcode.removesuffix('.s')
        if opcode.startswith('ldarg.'):
            stack.append(arguments[int(opcode.split('.')[1])])
        elif opcode.startswith('ldc.i4.'):
            value = opcode[7:]
            stack.append(-1 if value == 'm1' else int(operand) if value == 's' else int(value))
        elif opcode == 'ldc.i4':
            stack.append(int(operand))
        elif opcode in ('ldc.r4', 'ldc.r8'):
            stack.append(float(operand))
        elif opcode == 'ldnull':
            stack.append(None)
        elif opcode.startswith(('ldloc.', 'stloc.')):
            suffix = opcode.split('.')[1]
            slot = int(operand) if suffix == 's' else int(suffix)
            if opcode.startswith('ldloc'):
                stack.append(locals_[slot])
            else:
                locals_[slot] = stack.pop()
        elif opcode in ('ldfld', 'stfld'):
            name = fields[operand]
            if opcode == 'stfld':
                value = stack.pop()
                stack.pop()[name] = value
            else:
                stack.append(stack.pop()[name])
        elif opcode == 'ldsfld':
            stack.append(statics[fields[operand]])
        elif opcode == 'stsfld':
            statics[fields[operand]] = stack.pop()
        elif opcode in ('castclass', 'isinst'):
            if stack[-1] is not None and not isinstance(stack[-1], dict):
                raise TypeError('Unexpected managed reference type.')
        elif opcode == 'dup':
            stack.append(stack[-1])
        elif opcode == 'pop':
            stack.pop()
        elif opcode in ('add', 'sub', 'mul', 'div', 'and', 'or', 'rem'):
            right, left = stack.pop(), stack.pop()
            if opcode == 'add':
                value = left + right
            elif opcode == 'sub':
                value = left - right
            elif opcode == 'mul':
                value = left * right
            elif opcode == 'div':
                value = left / right
                if isinstance(left, int) and isinstance(right, int):
                    value = int(value)
            elif opcode == 'and':
                value = left & right
            elif opcode == 'or':
                value = left | right
            else:
                value = left % right
            stack.append(value)
        elif opcode == 'neg':
            stack.append(-stack.pop())
        elif opcode == 'conv.r4':
            stack.append(struct.unpack('<f', struct.pack('<f', stack.pop()))[0])
        elif opcode == 'conv.r8':
            stack.append(float(stack.pop()))
        elif opcode in ('conv.i4', 'conv.i8', 'conv.u2'):
            value = int(stack.pop())
            stack.append(value & 0xFFFF if opcode == 'conv.u2' else value)
        elif opcode in ('cgt.un', 'cgt', 'ceq'):
            right, left = stack.pop(), stack.pop()
            stack.append(int(left == right) if opcode == 'ceq' else int(left is not None) if right is None else int(left > right))
        elif base_opcode == 'br':
            next_index = positions[int(operand)]
        elif base_opcode in ('brtrue', 'brfalse'):
            value = truth(stack.pop())
            if value == (base_opcode == 'brtrue'):
                next_index = positions[int(operand)]
        elif base_opcode in ('bge', 'bgt', 'ble', 'blt', 'bgt.un', 'ble.un', 'beq', 'bne.un'):
            right, left = stack.pop(), stack.pop()
            taken = left == right if base_opcode == 'beq' else left != right if base_opcode == 'bne.un' else left >= right if base_opcode == 'bge' else left > right if base_opcode in ('bgt', 'bgt.un') else left <= right if base_opcode in ('ble', 'ble.un') else left < right
            if taken:
                next_index = positions[int(operand)]
        elif opcode == 'switch':
            choice = stack.pop()
            if 0 <= choice < int(operand):
                body = selected['body']
                header = body['file_offset']
                il_start = header + ((struct.unpack_from('<H', binary, header)[0] >> 12) * 4)
                relative = struct.unpack_from('<i', binary, il_start + instruction['il_offset'] + 5 + choice * 4)[0]
                target = instruction['il_offset'] + 5 + int(operand) * 4 + relative
                next_index = positions[target]
        elif opcode in ('call', 'callvirt'):
            shape = helpers.get(operand) if helpers else None
            arity, instance = shape[:2] if shape else call_shapes[operand]
            parameters = [stack.pop() for _ in range(arity)][::-1]
            if instance:
                target = stack.pop()
                if target is None:
                    raise ValueError('Null managed call receiver.')
            if shape:
                value = shape[2](target if instance else None, parameters)
                if shape[3]:
                    stack.append(value)
            elif selected_calls[operand]['target_declaring_type'] == 'System.Math':
                stack.append(abs(parameters[0]))
        elif opcode == 'ret':
            if selected['signature']['return_type'] == 'void':
                if stack:
                    raise RuntimeError('Void method left an invalid evaluation stack.')
                return None
            if len(stack) != 1:
                raise RuntimeError('Result method left an invalid evaluation stack.')
            return stack.pop()
        else:
            raise NotImplementedError((method_token, instruction))
        index = next_index
    raise RuntimeError('CIL instruction budget exhausted.')


def state(head_offset, tail_offset, broken, holding=False):
    start, end = 1000, 2000
    manager = dict(zip(('#=zS88EY698G788', '#=zGmP$DsELdAB0', '#=zIdkrfh9ftLzy', '#=zPA6wArNNdX1p', '#=zduPXoDY2scOF'), (16, 40, 73, 103, 127)))
    note = {'#=zteOlacZc1yu1': {'#=zCjFxUbLMOP_4': True}, '#=zCppVH7ytSH9m': start + 100,
            '#=z2ECXbFs=': False, '#=zFFWjQ_s=': 0, '#=zrhB8N5U=': start, '#=zYKaPfZU=': end,
            '#=zjI_2hrh9zta6': manager, '#=z45iB5Ik=': start + head_offset,
            '#=z3_F91jg=': holding, '#=zKyWyYKNxuqsE': broken, '#=zEg0GIs8=': end + tail_offset,
            '#=zaSDEnK3hAEfu': True, '#=zs4w345E=': 1000}
    statics = {'#=z1ownPGA=': end + tail_offset, '#=zmECDxTy8DK6q': {'#=zJlU2OFY=': None}}
    return note, statics


rows, held_rows = [], []
for broken in (False, True):
    for head_offset in (-128, -127, -50, -20, -19, 0, 19, 20, 44, 45, 73, 103):
        for tail_offset in (-128, -127, -126, -100, -50, -20, -1, 0, 1, 20, 50, 100, 126, 127, 128):
            note, statics = state(head_offset, tail_offset, broken)
            code = evaluate(note, statics)
            rows.append({'headOffset': head_offset, 'tailOffset': tail_offset, 'broken': broken, 'code': code, 'result': grades[code]})
for tail_offset in (0, 102, 103, 104, 126, 127):
    note, statics = state(0, tail_offset, False, True)
    code = evaluate(note, statics)
    held_rows.append({'tailOffset': tail_offset, 'code': code, 'result': grades[code]})


def score_samples(drain):
    score_method = '0x0600119e'
    score = {access['field_name']: 0 for access in metadata['field_accesses'] if access['method_token'] == score_method}
    score.update({'#=zAD1Ac5Wep9EJ': 3, '#=zJlU2OFY=': None, '#=zEkPJGuOrPkP8': 0, '#=zo18BWv4T1YTH': 0.0, '#=zE4k5CWU=': 0})
    combo = {'value': 0}
    health = {'value': 200.0, 'uncapped': 200.0}
    controller = {'#=zEvEcr0M=': 0}
    owner = {'#=z2XmMJKI=': score, '#=z7v6SWpThak1A': combo, '#=zqJdAKo3ZDGtR': health,
             '#=zVpnW9qcbfPFR': controller, '#=z2dtOzVY=': False, '#=zjI_2hrh9zta6': {'#=zvwRAXF89j$dC': {}},
             '#=zGbkz8rrAAleJ': 1.0, '#=zgumcH$2n967m': 100.0, '#=z$8HNDjTBXbMx': 500000.0 / 420,
             '#=zqg_29LyxifQ_': None}
    difficulty = {'#=zbC1WUym2l4AXFlXhmkdehMzGFMs4': float(drain)}
    statics = {'#=zmECDxTy8DK6q': score, '#=zfP9qebS1wrNS': 320, '#=zA0gH63FQwmyW': 384,
               '#=zPnm9a8ZHS6aD': 100, '#=zlJ9U5pecw3AJGGoX6w==': True, '#=zH1xQ8SGG$DfKgoNpjg==': False}

    def set_value(target, parameters):
        target['value'] = parameters[0]

    def set_score(target, parameters):
        target['displayScore'] = parameters[0]

    def increase(target, parameters):
        target['value'] = min(200.0, target['value'] + parameters[0])
        target['uncapped'] += parameters[0]

    def reduce(target, parameters):
        target['value'] = max(0.0, target['value'] - parameters[0])
        target['uncapped'] = max(0.0, target['uncapped'] - parameters[0])

    helpers = {
        '0x060022a6': (0, False, lambda t, p: difficulty, True),
        '0x06000e54': (0, False, lambda t, p: 3, True),
        '0x0a00038b': (1, False, lambda t, p: p[0], True),
        '0x0600136b': (3, False, lambda t, p: 1.0, True),
        '0x0a0002e8': (2, False, lambda t, p: min(p), True),
        '0x0a0002e7': (2, False, lambda t, p: max(p), True),
        '0x0a000100': (2, False, lambda t, p: max(p), True),
        '0x0a0003eb': (1, False, lambda t, p: math.sqrt(p[0]), True),
        '0x0600481a': (0, True, lambda t, p: t['value'], True),
        '0x0600481b': (1, True, set_value, False),
        '0x060040a3': (0, True, lambda t, p: t.get('displayScore', 0), True),
        '0x060040a4': (1, True, set_score, False),
        '0x06001d2d': (1, True, increase, False),
        '0x06001d2c': (1, True, reduce, False),
        '0x06000e8e': (0, True, lambda t, p: None, False),
    }
    sequence = [268435456] * 385 + [67108864, 134217728, 16777216, 33554432, -65536, 7, -1048576] + [268435456] * 28
    samples = []
    for index, code in enumerate(sequence):
        note = {'#=zPnOB7qLwOvZm$AcWIQ==': code not in (7, -1048576), '#=zyf5UpaZLUE2a': 0}
        evaluate(owner, statics, score_method, [owner, code, note], helpers)
        samples.append({'index': index, 'code': code, 'rawScore': score['#=zo18BWv4T1YTH'],
                        'displayScore': score.get('displayScore', 0), 'combo': combo['value'],
                        'maxCombo': score['#=zfKtv0MrhbKjB'], 'bonus': owner['#=zgumcH$2n967m'], 'hp': health['value']})
    return {'drain': drain, 'count': 420, 'samples': samples}


score_rows = [score_samples(drain) for drain in (0, 5, 10)]
data = {'authority': 'original decoded CIL interpreted without loading the target assembly', 'sha256': digest,
        'mvid': metadata['module']['mvid'], 'method': method_token, 'method_il_sha256': method['body']['il_sha256'], 'score_method': '0x0600119e', 'score_il_sha256': next(m['body']['il_sha256'] for m in metadata['methods'] if m['token'] == '0x0600119e'),
        'excluded': 'Visual/audio callbacks, active-hold display removal and managed score/combo/HP storage accessors. No-mod gameplay seeds a unit recovery multiplier; original CIL performs all numerical score/Bonus/combo/HP arithmetic. Application scheduler and raw input routing are outside this fixture.',
        'od': 8, 'windows': [16, 40, 73, 103, 127], 'holds': rows, 'held': held_rows, 'scores': score_rows}
args.output.write_text(json.dumps(data, indent=2) + '\n', encoding='utf8')
print(f'Generated {len(rows)} released and {len(held_rows)} held result samples, plus {sum(len(r["samples"]) for r in score_rows)} original-CIL score/HP states.')
