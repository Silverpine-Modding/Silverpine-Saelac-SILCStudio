"""Read-only Unity prefab artwork extraction; output is confined to an editor cache.

Packaged with PyInstaller so end users do not need Python or Unity installed.
No Unity game code, MonoBehaviours, assemblies or save scripts are executed.
"""
import json
import math
import os
import re
import struct
from pathlib import Path
import sys
import types

# UnityPy imports its optional audio exporter while importing sprite exporters.
# This image-only helper never decodes audio and must not require FMOD binaries.
audio_stub = types.ModuleType('fmod_toolkit')
def unsupported_audio(*args, **kwargs):
    raise RuntimeError('Audio extraction is not supported by the SILC sprite reader.')
audio_stub.raw_to_wav = unsupported_audio
sys.modules['fmod_toolkit'] = audio_stub

import UnityPy
from PIL import Image, ImageChops


def deref(pointer):
    return pointer.deref() if pointer is not None and pointer.path_id else None


def key(obj):
    return obj.assets_file.name, obj.path_id


def combine(a, b):
    aa, ab, ac, ad, ax, ay = a
    ba, bb, bc, bd, bx, by = b
    return (aa*ba+ab*bc, aa*bb+ab*bd, ac*ba+ad*bc, ac*bb+ad*bd,
            aa*bx+ab*by+ax, ac*bx+ad*by+ay)


def point(m, x, y):
    return m[0]*x+m[1]*y+m[4], m[2]*x+m[3]*y+m[5]


def transform_matrix(transform, root=False):
    q, s, p = transform.m_LocalRotation, transform.m_LocalScale, transform.m_LocalPosition
    return ((1-2*(q.y*q.y+q.z*q.z))*s.x, 2*(q.x*q.y-q.z*q.w)*s.y,
            2*(q.x*q.y+q.z*q.w)*s.x, (1-2*(q.x*q.x+q.z*q.z))*s.y,
            0 if root else p.x, 0 if root else p.y)


def fixed_scene_water(env):
    # Silverpine's bathhouse pool has no ISerializableMonoBehavior. Unlike its
    # floor, it remains in level0 when SerializationManager.Load replaces saved
    # objects. Read those actual scene coordinates; never invent the layout or
    # reintroduce movable/deletable furniture from the original scene.
    result = []
    for obj in env.objects:
        if obj.type.name != 'GameObject' or not re.fullmatch(r'level\d+', obj.assets_file.name):
            continue
        go = obj.read()
        if re.split(r'[ (]', go.m_Name, maxsplit=1)[0] != 'prefab_tile_water_bathhouse' or not go.m_IsActive:
            continue
        transform = next((e.component.deref() for e in go.m_Component if e.component.deref().type.name == 'Transform'), None)
        matrix, seen, active = (1, 0, 0, 1, 0, 0), set(), True
        while transform is not None:
            if key(transform) in seen or len(seen) >= 128:
                raise ValueError('Invalid scene transform hierarchy.')
            seen.add(key(transform))
            t = transform.read()
            active = active and t.m_GameObject.deref().read().m_IsActive
            matrix = combine(transform_matrix(t), matrix)
            transform = deref(t.m_Father)
        if active:
            result.append(dict(prefab='prefab_tile_water_bathhouse', x=matrix[4], y=matrix[5]))
    return result


def y_sort_offset(components):
    for component in components:
        if component.type.name != 'MonoBehaviour':
            continue
        mono = component.read(check_read=False)
        script = deref(mono.m_Script)
        if not script or script.read().m_ClassName != 'YSorter' or not mono.m_Enabled:
            continue
        # YSorter has one serialized custom field: public int offset. Private
        # runtime references are not serialized. Never execute the game code.
        raw = component.get_raw_data()
        header_size = 32 + ((len(mono.m_Name.encode('utf-8')) + 3) // 4) * 4
        if len(raw) != header_size + 4:
            raise ValueError('Unsupported YSorter layout; refusing to guess its offset.')
        return struct.unpack_from(component.reader.endian + 'i', raw, header_size)[0]
    return None


def renderers(obj, matrix=(1, 0, 0, 1, 0, 0), root=True, seen=None, shadow=False):
    seen = set() if seen is None else seen
    if key(obj) in seen:
        return
    seen.add(key(obj))
    go = obj.read()
    if not go.m_IsActive:
        return
    components = [entry.component.deref() for entry in go.m_Component]
    y_offset = y_sort_offset(components)
    shadow = shadow or any(name in go.m_Name.lower() for name in ('shadow', 'fakeao'))
    transform = next((c.read() for c in components if c.type.name == 'Transform'), None)
    if transform:
        matrix = combine(matrix, transform_matrix(transform, root))
    for component in components:
        if component.type.name == 'SpriteRenderer':
            renderer = component.read()
            sprite = deref(renderer.m_Sprite)
            if sprite and renderer.m_Enabled:
                yield renderer, sprite, matrix, y_offset, shadow or renderer.m_SortingLayer == -1
    if transform:
        for child in transform.m_Children:
            child_transform = child.deref().read()
            yield from renderers(child_transform.m_GameObject.deref(), matrix, False, seen, shadow)


def extract(obj, sprite_cache, entries=None):
    layers = []
    for renderer, sprite_obj, m, y_offset, shadow in (renderers(obj) if entries is None else entries):
        sprite = sprite_obj.read()
        if key(sprite_obj) not in sprite_cache:
            sprite_cache[key(sprite_obj)] = sprite.image.convert('RGBA')
        image = sprite_cache[key(sprite_obj)].copy()
        ppu = float(sprite.m_PixelsToUnits)
        if not ppu or ppu < 1:
            continue
        pivot = getattr(sprite, 'm_Pivot', None)
        px, py = (pivot.x, pivot.y) if pivot else (.5, .5)
        width, height = image.width/ppu, image.height/ppu
        if renderer.m_FlipX:
            m = combine(m, (-1, 0, 0, 1, 0, 0))
        if renderer.m_FlipY:
            m = combine(m, (1, 0, 0, -1, 0, 0))
        corners = [point(m, x, y) for x, y in [(-px*width, (1-py)*height),
                   ((1-px)*width, (1-py)*height), (-px*width, -py*height), ((1-px)*width, -py*height)]]
        color = renderer.m_Color
        if (color.r, color.g, color.b, color.a) != (1, 1, 1, 1):
            tint = Image.new('RGBA', image.size, tuple(max(0, min(255, round(c*255))) for c in (color.r, color.g, color.b, color.a)))
            image = ImageChops.multiply(image, tint)
        # Unity draws sorting layers before the order within a layer. Furniture
        # and its shadow often both have order 0; hierarchy traversal then put
        # the child shadow ON TOP of the table/chair. m_SortingLayer is the
        # signed layer value, not the unrelated m_SortingLayerID identifier.
        order = renderer.m_SortingOrder if y_offset is None else round(-m[5] * 10) + y_offset
        layers.append(((renderer.m_SortingLayer, order), image, corners))
    if not layers:
        return None
    left = min(p[0] for _, _, corners in layers for p in corners)
    right = max(p[0] for _, _, corners in layers for p in corners)
    bottom = min(p[1] for _, _, corners in layers for p in corners)
    top = max(p[1] for _, _, corners in layers for p in corners)
    if right-left <= 0 or top-bottom <= 0 or max(right-left, top-bottom) > 64:
        return None
    ppu = 32
    width, height = max(1, math.ceil((right-left)*ppu)), max(1, math.ceil((top-bottom)*ppu))
    canvas = Image.new('RGBA', (width, height))
    for _, image, corners in sorted(layers, key=lambda row: row[0]):
        p0, p1, p2 = [((x-left)*ppu, (top-y)*ppu) for x, y in corners[:3]]
        a, c = (p1[0]-p0[0])/image.width, (p1[1]-p0[1])/image.width
        b, d = (p2[0]-p0[0])/image.height, (p2[1]-p0[1])/image.height
        det = a*d-b*c
        if abs(det) < 1e-8:
            continue
        inverse = (d/det, -b/det, (b*p0[1]-d*p0[0])/det,
                   -c/det, a/det, (c*p0[0]-a*p0[1])/det)
        layer = image.transform(canvas.size, Image.Transform.AFFINE, inverse, Image.Resampling.BILINEAR)
        canvas.alpha_composite(layer)
    return canvas, left, bottom, width/ppu, height/ppu


def main():
    game, request, output = map(Path, sys.argv[1:4])
    data = next((p for p in (game/'Silverpine_Data', game/'Contents/Resources/Data',
                 game/'Silverpine.app/Contents/Resources/Data') if (p/'globalgamemanagers').is_file()), None)
    if data is None:
        raise ValueError('Select the Silverpine game folder or Silverpine.app bundle.')
    wanted = set(json.loads(request.read_text(encoding='utf-8-sig')))
    if len(wanted) > 10000 or any(not isinstance(name, str) or len(name) > 1000 for name in wanted):
        raise ValueError('Invalid prefab request.')
    output.mkdir(parents=True, exist_ok=True)
    env = UnityPy.load(str(data))
    static_objects = fixed_scene_water(env)
    wanted.update(p['prefab'] for p in static_objects)
    prefabs = {}
    for obj in env.objects:
        if obj.type.name != 'GameObject':
            continue
        go = obj.read()
        if go.m_Name in wanted:
            old = prefabs.get(go.m_Name)
            if old is None or ('resources' in obj.assets_file.name and 'resources' not in old.assets_file.name):
                prefabs[go.m_Name] = obj
    sprites, records, errors = {}, {}, []
    for index, name in enumerate(sorted(prefabs)):
        try:
            parts = []
            # Keep renderers separate: flattening a rug/furniture/shadow prefab
            # destroys Unity's GLOBAL sorting layer and YSorter relationships.
            for part, entry in enumerate(renderers(prefabs[name])):
                result = extract(None, sprites, [entry])
                if not result:
                    continue
                renderer, _, matrix, y_offset, shadow = entry
                image, left, bottom, width, height = result
                file = f'{index:05d}-{part:03d}.png'
                image.save(output/file)
                parts.append(dict(file=file, left=left, bottom=bottom, width=width, height=height,
                                  layer=renderer.m_SortingLayer, order=renderer.m_SortingOrder,
                                  ySort=y_offset is not None, yOffset=y_offset or 0,
                                  localY=matrix[5], shadow=shadow))
            if parts:
                records[name] = parts
        except Exception as error:
            errors.append(name + ': ' + str(error))
    manifest = dict(format=2, prefabs=records, staticObjects=static_objects, requested=sorted(wanted), missing=sorted(wanted-records.keys()), errors=errors)
    # A complete manifest is the cache commit marker; interrupted extractions are ignored.
    temporary = output/'manifest.pending'
    temporary.write_text(json.dumps(manifest), encoding='utf-8')
    os.replace(temporary, output/'manifest.json')
    print(json.dumps(dict(extracted=len(records), missing=len(manifest['missing']), errors=len(errors))))


if __name__ == '__main__':
    main()
