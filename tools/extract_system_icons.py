"""Export original Windows tray resources without loading executable code."""

import argparse
import csv
import hashlib
import io
import json
import os
from pathlib import Path
import re
import shutil
import struct
import sys
import tempfile
import zipfile
import zlib

sys.dont_write_bytecode = True


def digest(data):
    return hashlib.sha256(data).hexdigest()


def safe_name(value):
    text = str(value)
    clean = re.sub(r"[^A-Za-z0-9_.-]", "_", text)
    return clean if clean == text else clean + "-" + digest(text.encode())[:8]


def make_ico(frames):
    offset = 6 + 16 * len(frames)
    directory = bytearray(struct.pack("<HHH", 0, 1, len(frames)))
    payload = bytearray()
    for frame in frames:
        data = frame["data"]
        directory.extend(struct.pack(
            "<BBBBHHII", frame["width"] % 256, frame["height"] % 256,
            frame["colors"], 0, frame["planes"], frame["bpp"], len(data), offset))
        payload.extend(data)
        offset += len(data)
    return bytes(directory + payload)


def make_bmp(dib):
    header_size = struct.unpack_from("<I", dib)[0]
    if header_size == 12:
        bpp = struct.unpack_from("<H", dib, 10)[0]
        palette = (1 << bpp) * 3 if bpp <= 8 else 0
        offset = 14 + 12 + palette
    elif header_size >= 40:
        bpp = struct.unpack_from("<H", dib, 14)[0]
        compression = struct.unpack_from("<I", dib, 16)[0]
        colors = struct.unpack_from("<I", dib, 32)[0]
        palette = colors or ((1 << bpp) if bpp <= 8 else 0)
        masks = (12 if compression == 3 else 16 if compression == 6 else 0) if header_size == 40 else 0
        offset = 14 + header_size + masks + palette * 4
    else:
        raise ValueError(f"Unsupported DIB header size: {header_size}")
    if offset > len(dib) + 14:
        raise ValueError("DIB pixel offset exceeds resource size")
    return struct.pack("<2sIHHI", b"BM", len(dib) + 14, 0, 0, offset) + dib


def read_resources(pe):
    entries = {}
    root = getattr(pe, "DIRECTORY_ENTRY_RESOURCE", None)
    if root is None:
        return entries
    for resource_type in root.entries:
        if resource_type.id not in (2, 3, 14):
            continue
        for name in resource_type.directory.entries:
            resource_id = name.id if name.id is not None else str(name.name)
            for language in name.directory.entries:
                data = language.data.struct
                blob = pe.get_data(data.OffsetToData, data.Size)
                if len(blob) != data.Size:
                    raise ValueError(f"Truncated resource: {resource_type.id}/{resource_id}/{language.id}")
                entries[(resource_type.id, resource_id, language.id)] = blob
    return entries


def verify_cabs(cache):
    listing = cache / "win98-archive-list.txt"
    cabinet_root = cache / "win98se" / "cabs"
    if not listing.exists():
        return None
    records = []
    for block in listing.read_text(encoding="utf-8-sig").split("\n\n"):
        record = dict(line.split(" = ", 1) for line in block.splitlines() if " = " in line)
        path = record.get("Path", "")
        if path.upper().startswith("WIN98SE\\") and path.upper().endswith(".CAB"):
            file = cabinet_root / path
            data = file.read_bytes()
            crc = f"{zlib.crc32(data) & 0xFFFFFFFF:08X}"
            ok = len(data) == int(record["Size"]) and crc == record["CRC"].upper()
            records.append({"path": path, "size": len(data), "crc32": crc, "valid": ok})
    if not records or not all(record["valid"] for record in records):
        raise ValueError("Win98 CAB size/CRC validation failed")
    return {"outer_archive_warning": "7-Zip reported Unexpected end of archive", "validated_cabs": len(records), "cabs": records}


def export_module(source, version, stage):
    import pefile
    from PIL import Image

    folder = stage / version / safe_name(source.name.lower())
    folder.mkdir(parents=True)
    source_bytes = source.read_bytes()
    pe = pefile.PE(data=source_bytes, fast_load=True)
    try:
        pe.parse_data_directories(directories=[pefile.DIRECTORY_ENTRY["IMAGE_DIRECTORY_ENTRY_RESOURCE"]])
        resources = read_resources(pe)
        fixed = getattr(pe, "VS_FIXEDFILEINFO", [])
        file_version = None
        if fixed:
            value = fixed[0]
            file_version = ".".join(map(str, (
                value.FileVersionMS >> 16, value.FileVersionMS & 0xFFFF,
                value.FileVersionLS >> 16, value.FileVersionLS & 0xFFFF)))
        module = {"source": str(source), "module": source.name, "sha256": digest(source_bytes),
                  "version": file_version, "machine": hex(pe.FILE_HEADER.Machine), "icons": [], "bitmaps": [], "orphan_icons": []}
        previews = []
        referenced = set()
        for (kind, resource_id, language), group in resources.items():
            if kind != 14:
                continue
            reserved, icon_type, count = struct.unpack_from("<HHH", group)
            if reserved or icon_type != 1 or len(group) < 6 + count * 14:
                raise ValueError(f"Invalid group icon: {resource_id}")
            frames = []
            for index in range(count):
                width, height, colors, _, planes, bpp, size, icon_id = struct.unpack_from("<BBBBHHIH", group, 6 + index * 14)
                key = (3, icon_id, language)
                if key not in resources:
                    candidates = [candidate for candidate in resources if candidate[:2] == (3, icon_id)]
                    if len(candidates) != 1:
                        raise ValueError(f"Missing or ambiguous icon image: {icon_id}/{language}")
                    key = candidates[0]
                blob = resources[key]
                if len(blob) != size:
                    raise ValueError(f"Icon image size mismatch: {icon_id}")
                referenced.add(key)
                frames.append({"width": width or 256, "height": height or 256, "colors": colors,
                               "planes": planes, "bpp": bpp, "resource_id": icon_id, "language": key[2], "data": blob})
            stem = f"group-{safe_name(resource_id)}-lang-{language}"
            ico = folder / (stem + ".ico")
            ico.write_bytes(make_ico(frames))
            exported = []
            for index, frame in enumerate(frames):
                png = folder / f"{stem}-f{index:02}-{frame['width']}x{frame['height']}-{frame['bpp']}bpp.png"
                with Image.open(io.BytesIO(make_ico([frame]))) as image:
                    rgba = image.convert("RGBA")
                    if rgba.size != (frame["width"], frame["height"]):
                        raise ValueError(f"Decoded dimensions disagree with icon group: {png.name}")
                    rgba.save(png)
                metadata = {key: value for key, value in frame.items() if key != "data"}
                metadata.update({"image_sha256": digest(frame["data"]), "png": png.relative_to(stage).as_posix()})
                exported.append(metadata)
            best = min(exported, key=lambda frame: (abs(frame["width"] - 16) + abs(frame["height"] - 16), -frame["bpp"]))
            icon = {"id": resource_id, "language": language, "ico": ico.relative_to(stage).as_posix(), "frames": exported}
            module["icons"].append(icon)
            previews.append({"module": source.name, "id": resource_id, "language": language,
                             "kind": "icon", "png": best["png"]})
        for (kind, resource_id, language), blob in resources.items():
            stem = f"resource-{safe_name(resource_id)}-lang-{language}"
            if kind == 2:
                dib = folder / (stem + ".dib")
                bmp = folder / (stem + ".bmp")
                png = folder / (stem + ".png")
                dib.write_bytes(blob)
                bmp.write_bytes(make_bmp(blob))
                with Image.open(bmp) as image:
                    rgba = image.convert("RGBA")
                    rgba.save(png)
                    width, height = rgba.size
                module["bitmaps"].append({"id": resource_id, "language": language, "width": width,
                                          "height": height, "dib_sha256": digest(blob),
                                          "bmp": bmp.relative_to(stage).as_posix(), "png": png.relative_to(stage).as_posix()})
                previews.append({"module": source.name, "id": resource_id, "language": language,
                                 "kind": "bitmap", "png": png.relative_to(stage).as_posix()})
            elif kind == 3 and (kind, resource_id, language) not in referenced:
                raw = folder / (stem + "-orphan-icon.bin")
                raw.write_bytes(blob)
                module["orphan_icons"].append({"id": resource_id, "language": language, "sha256": digest(blob),
                                                "raw": raw.relative_to(stage).as_posix()})
        (folder / "resources.json").write_text(json.dumps(module, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        return module, previews
    finally:
        pe.close()


def contact_sheet(items, title, stage, destination):
    from PIL import Image, ImageDraw, ImageFont

    columns = 8
    cell_width, cell_height = 144, 110
    font_path = Path("C:/Windows/Fonts/segoeui.ttf")
    font = ImageFont.truetype(str(font_path), 11) if font_path.exists() else ImageFont.load_default()
    sheet = Image.new("RGB", (columns * cell_width, 36 + ((len(items) + columns - 1) // columns) * cell_height), "#fafafa")
    draw = ImageDraw.Draw(sheet)

    def label(text, x, y, color):
        if draw.textlength(text, font=font) > cell_width - 10:
            while text and draw.textlength(text + "...", font=font) > cell_width - 10:
                text = text[:-1]
            text += "..."
        draw.text((x, y), text, fill=color, font=font)

    draw.text((12, 10), title, fill="#111111", font=font)
    for index, item in enumerate(items):
        x = (index % columns) * cell_width
        y = 36 + (index // columns) * cell_height
        for cy in range(0, 72, 8):
            for cx in range(0, cell_width - 8, 8):
                color = "#c8cccf" if (cx // 8 + cy // 8) % 2 else "#f4f4f4"
                draw.rectangle((x + 4 + cx, y + cy, x + 11 + cx, y + cy + 7), fill=color)
        with Image.open(stage / item["png"]) as source:
            image = source.convert("RGBA")
            scale = min(128 / image.width, 64 / image.height, 3)
            image = image.resize((max(1, int(image.width * scale)), max(1, int(image.height * scale))), Image.Resampling.NEAREST)
            sheet.paste(image, (x + (cell_width - image.width) // 2, y + (72 - image.height) // 2), image)
        label(item["module"], x + 5, y + 75, "#161616")
        resource = f"{'G' if item['kind'] == 'icon' else 'B'} {item['id']} / L{item['language']}"
        label(resource, x + 5, y + 90, "#4c5357")
    destination.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(destination)


def overview_samples(images):
    chosen = []
    priorities = (
        ("SndVolSSO.dll", (120, 121, 122, 123)),
        ("systray.exe", (220, 221, 210, 300)),
        ("stobject.dll", (230, 231, "IDI_MUTE", 210)),
        ("pnidui.dll", (3027, 3028, 3029, 3030, 3020, 3021, 3022, 3051)),
        ("netshell.dll", (100, 101, 102, 103)),
        ("rnaui.dll", (100, 101, 102, 103)),
        ("batmeter.dll", None),
        ("hotplug.dll", None),
    )
    for module, ids in priorities:
        candidates = [image for image in images if image["module"].lower() == module.lower()]
        if ids is not None:
            candidates = sorted((image for image in candidates if image["id"] in ids), key=lambda image: ids.index(image["id"]))
        chosen.extend(candidates[:4 if ids is not None else 2])
    return chosen[:16]


def validate_output(stage, modules):
    from PIL import Image

    pngs = list(stage.rglob("*.png"))
    for path in pngs:
        with Image.open(path) as image:
            image.load()
            if not image.width or not image.height:
                raise ValueError(f"Empty image: {path}")
    frame_count = 0
    for module in modules:
        for group in module["icons"]:
            data = (stage / group["ico"]).read_bytes()
            count = struct.unpack_from("<H", data, 4)[0]
            if count != len(group["frames"]):
                raise ValueError("ICO frame count mismatch")
            for index, frame in enumerate(group["frames"]):
                size, offset = struct.unpack_from("<II", data, 6 + index * 16 + 8)
                if digest(data[offset:offset + size]) != frame["image_sha256"]:
                    raise ValueError("Original ICO image payload was not preserved")
                frame_count += 1
        for bitmap in module["bitmaps"]:
            bmp = stage / bitmap["bmp"]
            if digest(bmp.read_bytes()[14:]) != bitmap["dib_sha256"]:
                raise ValueError("Original bitmap DIB was not preserved")
    return {"validated_pngs": len(pngs), "validated_original_icon_frames": frame_count,
            "validated_bitmaps": sum(len(module["bitmaps"]) for module in modules)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cache", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--source", action="append", required=True, metavar="VERSION=DIRECTORY")
    parser.add_argument("--origin", action="append", default=[], metavar="VERSION=ORIGINAL_MEDIA")
    args = parser.parse_args()
    cache, output = args.cache.resolve(), args.output.resolve()
    if output.exists():
        raise ValueError("Output already exists; choose a new directory to preserve existing work")
    cache.mkdir(parents=True, exist_ok=True)
    temp = cache / "temp"
    temp.mkdir(exist_ok=True)
    for key in ("TEMP", "TMP", "TMPDIR"):
        os.environ[key] = str(temp)
    tempfile.tempdir = str(temp)
    sys.path.insert(0, str(cache / "python-deps"))
    import pefile
    import PIL

    stage = Path(tempfile.mkdtemp(prefix="icon-export-", dir=cache))
    manifest = {"cache": str(cache), "tools": {"pefile": pefile.__version__, "Pillow": PIL.__version__},
                "win98_cab_validation": verify_cabs(cache), "sources": [], "modules": []}
    origins = dict(origin.split("=", 1) for origin in args.origin)
    all_previews = {}
    for source in args.source:
        version, directory = source.split("=", 1)
        if safe_name(version) != version:
            raise ValueError(f"Invalid version label: {version}")
        folder = Path(directory).resolve()
        files = sorted(path for path in folder.rglob("*") if path.is_file() and path.suffix.lower() in (".dll", ".exe", ".cpl", ".mui"))
        if not files:
            raise ValueError(f"No source modules: {folder}")
        manifest["sources"].append({"version": version, "directory": str(folder), "original_media": origins.get(version), "module_count": len(files)})
        previews = []
        for path in files:
            module, images = export_module(path, version, stage)
            module["os"] = version
            manifest["modules"].append(module)
            previews.extend(images)
            print(f"{version}/{path.name}: {len(module['icons'])} icon groups, {len(module['bitmaps'])} bitmaps", flush=True)
        all_previews[version] = previews
        for start in range(0, len(previews), 96):
            contact_sheet(previews[start:start + 96], f"{version} - original resources - page {start // 96 + 1}", stage,
                          stage / "previews" / f"{version}-{start // 96 + 1:02}.png")
    overview_pages = []
    for version, images in all_previews.items():
        page = stage / "previews" / f"{version}-selected.png"
        contact_sheet(overview_samples(images), f"{version} - selected native tray resources (original IDs)", stage, page)
        overview_pages.append(page)
    from PIL import Image
    pages = [Image.open(path) for path in overview_pages]
    try:
        overview = Image.new("RGB", (max(page.width for page in pages), sum(page.height for page in pages)), "#fafafa")
        top = 0
        for page in pages:
            overview.paste(page, (0, top))
            top += page.height
        overview.save(stage / "overview.png")
    finally:
        for page in pages:
            page.close()
    manifest["validation"] = validate_output(stage, manifest["modules"])
    (stage / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    with (stage / "index.csv").open("w", encoding="utf-8-sig", newline="") as file:
        writer = csv.writer(file)
        writer.writerow(["os", "module", "kind", "resource_id", "language", "width", "height", "bpp", "ico", "png"])
        for module in manifest["modules"]:
            for group in module["icons"]:
                for frame in group["frames"]:
                    writer.writerow([module["os"], module["module"], "icon", group["id"], group["language"], frame["width"], frame["height"], frame["bpp"], group["ico"], frame["png"]])
            for bitmap in module["bitmaps"]:
                writer.writerow([module["os"], module["module"], "bitmap", bitmap["id"], bitmap["language"], bitmap["width"], bitmap["height"], "", "", bitmap["png"]])
    notes = [
        "Original Windows notification-area resource library", "",
        "ICO files preserve every original size, color depth, XOR/AND mask, and image payload.",
        "PNG files are lossless decoded previews; legacy XOR-invert pixels cannot be fully represented in RGBA.",
        "BMP/DIB files preserve original bitmap strips and atlases. No automatic slicing was applied.",
        "Use index.csv and module resources.json files to identify IDs; semantic live-state mapping is not yet implemented.",
        "Raw Windows executable modules, installers, dependencies, and extraction logs stay in the K: cache, not this package.",
        "No source Windows DLL or EXE was executed. No RetroBar application behavior was changed.",
        "Source resource copyrights remain with their original owners. This is a personal-use extraction.",
        "", json.dumps(manifest["validation"], indent=2), "",
    ]
    (stage / "README.txt").write_text("\n".join(notes), encoding="utf-8")
    (stage / "SHA256SUMS.txt").write_text("".join(
        f"{digest(path.read_bytes())}  {path.relative_to(stage).as_posix()}\n"
        for path in sorted(stage.rglob("*")) if path.is_file()), encoding="utf-8")
    shutil.copytree(stage, output)
    archive = output.with_suffix(".zip")
    if archive.exists():
        raise ValueError(f"Archive already exists: {archive}")
    with zipfile.ZipFile(archive, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=6) as package:
        for file in sorted(output.rglob("*")):
            if file.is_file():
                package.write(file, file.relative_to(output).as_posix())
    print(json.dumps({"output": str(output), "archive": str(archive), "validation": manifest["validation"]}), flush=True)


if __name__ == "__main__":
    main()
