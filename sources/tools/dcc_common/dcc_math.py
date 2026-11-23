"""Small RH column-matrix and editable-curve routines, independent of DCC hosts."""

import math


def identity():
    return [[float(i == j) for j in range(4)] for i in range(4)]


def mul(a, b):
    return [[sum(a[i][k]*b[k][j] for k in range(4)) for j in range(4)] for i in range(4)]


def inverse(matrix):
    rows = [list(row)+identity()[i] for i, row in enumerate(matrix)]
    for col in range(4):
        pivot = max(range(col, 4), key=lambda r: abs(rows[r][col]))
        if abs(rows[pivot][col]) < 1e-12:
            raise ValueError("Singular matrix")
        rows[col], rows[pivot] = rows[pivot], rows[col]
        size = rows[col][col]
        rows[col] = [v/size for v in rows[col]]
        for r in range(4):
            if r != col:
                factor = rows[r][col]
                rows[r] = [x-factor*y for x, y in zip(rows[r], rows[col])]
    return [row[4:] for row in rows]


def trs(t=(0, 0, 0), r=(0, 0, 0), s=(1, 1, 1), order="XYZ"):
    result = identity()
    for axis in order:
        index = "XYZ".index(axis)
        c, sn = math.cos(r[index]), math.sin(r[index])
        a, b = ((1, 2), (2, 0), (0, 1))[index]
        matrix = identity()
        matrix[a][a], matrix[a][b], matrix[b][a], matrix[b][b] = c, -sn, sn, c
        result = mul(matrix, result)
    for i in range(3):
        for j in range(3):
            result[i][j] *= s[j]
        result[i][3] = t[i]
    return result


def decompose(matrix):
    t = [matrix[i][3] for i in range(3)]
    s = [math.sqrt(sum(matrix[i][j]**2 for i in range(3))) for j in range(3)]
    if min(s) <= 1e-10:
        raise ValueError("Zero matrix scale")
    m = [[matrix[i][j]/s[j] for j in range(3)] for i in range(3)]
    y = math.asin(max(-1, min(1, -m[2][0])))
    if abs(math.cos(y)) > 1e-7:
        x, z = math.atan2(m[2][1], m[2][2]), math.atan2(m[1][0], m[0][0])
    else:
        x, z = 0.0, math.atan2(-m[0][1], m[1][1])
    restored = trs(t, (x, y, z), s)
    if max(abs(matrix[i][j]-restored[i][j]) for i in range(4) for j in range(4)) > 2e-5:
        raise ValueError("Sheared or reflected matrix cannot be represented as positive TRS")
    return t, [x, y, z], s


def curve_value(curve, time):
    keys = curve["keys"]
    first, last = keys[0], keys[-1]
    if time < first["time"] or time > last["time"]:
        before = time < first["time"]
        mode = curve.get("pre_infinity" if before else "post_infinity", "CONSTANT")
        end = first if before else last
        duration = last["time"]-first["time"]
        if mode in {"CYCLE", "CYCLE_RELATIVE", "OSCILLATE"} and duration > 0:
            cycle = math.floor((time-first["time"])/duration)
            local = time-cycle*duration
            if mode == "OSCILLATE" and cycle % 2:
                local = last["time"]-(local-first["time"])
            value = curve_value(dict(curve, pre_infinity="CONSTANT", post_infinity="CONSTANT"), local)
            return value+cycle*(last["value"]-first["value"]) if mode == "CYCLE_RELATIVE" else value
        if mode == "LINEAR":
            handle = end.get("handle_left" if before else "handle_right", (end["time"], end["value"]))
            dt = handle[0]-end["time"]
            return end["value"]+(time-end["time"])*(handle[1]-end["value"])/dt if abs(dt) > 1e-12 else end["value"]
        return end["value"]
    if time == last["time"]:
        return last["value"]
    for a, b in zip(keys, keys[1:]):
        if a["time"] <= time < b["time"]:
            if a["interpolation"] == "CONSTANT":
                return a["value"]
            if a["interpolation"] == "LINEAR":
                return a["value"]+(b["value"]-a["value"])*(time-a["time"])/(b["time"]-a["time"])
            p, q = a["handle_right"], b["handle_left"]
            def bezier(x0, x1, x2, x3, u):
                return (1-u)**3*x0+3*(1-u)**2*u*x1+3*(1-u)*u*u*x2+u**3*x3
            lo, hi = 0.0, 1.0
            for _ in range(48):
                u = (lo+hi)/2
                if bezier(a["time"], p[0], q[0], b["time"], u) < time:
                    lo = u
                else:
                    hi = u
            return bezier(a["value"], p[1], q[1], b["value"], (lo+hi)/2)
    return first["value"]
