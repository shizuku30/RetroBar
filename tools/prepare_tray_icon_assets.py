"""Curate the previously extracted Windows resources for RetroBar."""

import argparse
import hashlib
import json
from pathlib import Path
import shutil
import struct
import sys
import tempfile

sys.dont_write_bytecode = True


def bitmap_rgba(path):
    from PIL import Image

    data = path.read_bytes()
    size, width, height, _, bpp, compression = struct.unpack_from("<IiiHHI", data)
    if size == 40 and bpp == 32 and compression == 0:
        stride = ((width * bpp + 31) // 32) * 4
        image = Image.frombytes("RGBA", (width, abs(height)), data[size:], "raw", "BGRA", stride, -1 if height > 0 else 1)
        return image
    from extract_system_icons import make_bmp
    import io
    image = Image.open(io.BytesIO(make_bmp(data))).convert("RGBA")
    # The legacy image lists use a magenta color key, not an alpha channel.
    image.putdata([(r, g, b, 0 if (r, g, b) == (255, 0, 255) else a) for r, g, b, a in image.get_flattened_data()])
    return image


def inspect_atlases(source, cache):
    from extract_system_icons import contact_sheet

    manifest = json.loads((source / "manifest.json").read_text(encoding="utf-8"))
    for module in manifest["modules"]:
        if module["module"].lower() != "batmeter.dll":
            continue
        version = module["os"]
        bitmap = next(item for item in module["bitmaps"] if item["height"] == 16)
        image = bitmap_rgba(source / bitmap["bmp"].replace(".bmp", ".dib"))
        items = []
        for index in range(image.width // 16):
            filename = f"{version}-battery-{index:02}.png"
            image.crop((index * 16, 0, index * 16 + 16, 16)).save(cache / filename)
            items.append({"module": version, "id": index, "language": bitmap["language"], "kind": "bitmap", "png": filename})
        contact_sheet(items, version + " - battery strip cell indices", cache, cache / (version + "-battery-strip.png"))


def prepare(source, cache, output):
    from extract_system_icons import export_module

    manifest = json.loads((source / "manifest.json").read_text(encoding="utf-8"))
    modules = {(module["os"], module["module"].lower()): (source, module) for module in manifest["modules"]}
    stage = Path(tempfile.mkdtemp(prefix="tray-assets-", dir=cache))
    rna_source = Path(tempfile.mkdtemp(prefix="rna-resource-", dir=cache))
    rna, _ = export_module(cache / "win98se/files/rnaapp.exe", "win98se", rna_source)
    modules[("win98se", "rnaapp.exe")] = (rna_source, rna)
    catalog, provenance = {}, []

    def add_icon(pack, name, version, module_name, resource_id):
        root, module = modules[(version, module_name.lower())]
        icon = next(item for item in module["icons"] if item["id"] == resource_id)
        frames = {}
        for frame in icon["frames"]:
            size = frame["width"]
            if frame["height"] == size and 16 <= size <= 64 and (size not in frames or frames[size]["bpp"] < frame["bpp"]):
                frames[size] = frame
        variants = []
        for size, frame in sorted(frames.items()):
            relative = f"{pack}/{name}-{size}.png"
            destination = stage / relative
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(root / frame["png"], destination)
            variants.append({"Size": size, "Path": relative})
        catalog.setdefault(pack, {})[name] = variants
        provenance.append({"pack": pack, "state": name, "os": version, "module": module_name,
                           "module_sha256": module["sha256"], "group_id": resource_id, "language": icon["language"]})

    def alias(pack, name, original):
        catalog[pack][name] = catalog[pack][original]

    def add_battery(pack, name, version, cell, overlay=None):
        root, module = modules[(version, "batmeter.dll")]
        variants = []
        classic = pack in ("Win98", "Win2000", "WinXP")
        for bitmap in module["bitmaps"]:
            size = bitmap["height"]
            if classic and size not in (16, 32) or not classic and bitmap["id"] not in (303, 304, 305, 306):
                continue
            image = bitmap_rgba(root / bitmap["bmp"].replace(".bmp", ".dib"))
            tile = image.crop((cell * size, 0, (cell + 1) * size, size))
            if overlay is not None:
                tile.alpha_composite(image.crop((overlay * size, 0, (overlay + 1) * size, size)))
            relative = f"{pack}/{name}-{size}.png"
            destination = stage / relative
            destination.parent.mkdir(parents=True, exist_ok=True)
            tile.save(destination)
            variants.append({"Size": size, "Path": relative})
        catalog.setdefault(pack, {})[name] = sorted(variants, key=lambda item: item["Size"])
        provenance.append({"pack": pack, "state": name, "os": version, "module": "batmeter.dll",
                           "module_sha256": module["sha256"], "cell": cell, "overlay_cell": overlay})

    packs = {"Win98": "win98se", "Win2000": "windows-2000-sp4", "WinXP": "windows-xp-sp3",
             "Vista": "windows-vista-sp2", "Win7": "windows-7-sp1"}
    for pack, version in packs.items():
        classic = pack in ("Win98", "Win2000", "WinXP")
        if classic:
            module = "systray.exe" if pack == "Win98" else "stobject.dll"
            add_icon(pack, "VolumeOn", version, module, 220 if pack == "Win98" else 230)
            add_icon(pack, "VolumeMuted", version, module, 221 if pack == "Win98" else 231)
            for name in ("VolumeZero", "VolumeLow", "VolumeMedium", "VolumeHigh"):
                alias(pack, name, "VolumeOn")
            alias(pack, "VolumeUnavailable", "VolumeMuted")
            for name, cell in {"PowerFull": 0, "PowerMedium": 1, "PowerLow": 2, "PowerCritical": 5,
                               "PowerUnknown": 4, "PowerUnavailable": 3, "PowerAcOnly": 6,
                               "PowerAc": 8, "PowerCharging": 9}.items():
                add_battery(pack, name, version, cell)
        else:
            for name, resource_id in {"VolumeMuted": 120, "VolumeZero": 121, "VolumeLow": 122,
                                      "VolumeMedium": 123, "VolumeHigh": 124, "VolumeUnavailable": 125}.items():
                add_icon(pack, name, version, "SndVolSSO.dll", resource_id)
            for index in range(9):
                add_battery(pack, f"PowerBattery{index}", version, index)
                add_battery(pack, f"PowerLow{index}", version, 9 + index, 18)
                add_battery(pack, f"PowerCritical{index}", version, 9 + index, 19)
                add_battery(pack, f"PowerCharging{index}", version, 20, 21 + index)
                add_battery(pack, f"PowerAc{index}", version, 33 + index)
            for name, cell in {"PowerUnknown": 30, "PowerUnavailable": 31, "PowerAcOnly": 32}.items():
                add_battery(pack, name, version, cell)

        if pack == "Win98":
            for name, resource_id in {"NetworkOnline": 113, "NetworkLimited": 110, "NetworkDisconnected": 114}.items():
                add_icon(pack, name, version, "rnaapp.exe", resource_id)
        elif pack in ("Win2000", "WinXP"):
            for name, resource_id in {"NetworkOnline": 190, "NetworkLimited": 196 if pack == "WinXP" else 190,
                                      "NetworkDisconnected": 195}.items():
                add_icon(pack, name, version, "netshell.dll", resource_id)
        else:
            for name, resource_id in {"NetworkOnline": 3048, "NetworkLimited": 3051 if pack == "Win7" else 3054,
                                      "NetworkDisconnected": 3020}.items():
                add_icon(pack, name, version, "pnidui.dll", resource_id)
        for bars in range(1, 5):
            wifi = f"NetworkWiFi{bars}"
            limited = f"NetworkWiFiLimited{bars}"
            if pack == "WinXP":
                add_icon(pack, wifi, version, "netshell.dll", (2611, 2613, 2614, 2615)[bars - 1])
                alias(pack, limited, "NetworkLimited")
            elif not classic:
                add_icon(pack, wifi, version, "pnidui.dll", (3022, 3023, 3025, 3026)[bars - 1])
                if pack == "Win7":
                    add_icon(pack, limited, version, "pnidui.dll", (3028, 3029, 3031, 3032)[bars - 1])
                else:
                    alias(pack, limited, "NetworkLimited")
            else:
                alias(pack, wifi, "NetworkOnline")
                alias(pack, limited, "NetworkLimited")
        alias(pack, "NetworkOther", "NetworkOnline")
        add_icon(pack, "Hardware", version, "systray.exe" if pack == "Win98" else "stobject.dll", 300 if pack == "Win98" else 210)

    (stage / "catalog.json").write_text(json.dumps(catalog, indent=2) + "\n", encoding="utf-8")
    (stage / "sources.json").write_text(json.dumps({"assets": provenance, "sha256": {
        path.relative_to(stage).as_posix(): hashlib.sha256(path.read_bytes()).hexdigest()
        for path in sorted(stage.rglob("*.png"))}}, indent=2) + "\n", encoding="utf-8")
    output.mkdir(parents=True, exist_ok=True)
    shutil.copytree(stage, output, dirs_exist_ok=True)
    print(f"Prepared {sum(1 for _ in stage.rglob('*.png'))} original-image variants in {len(catalog)} theme packs.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--cache", type=Path, required=True)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    sys.path.insert(0, str(args.cache / "python-deps"))
    review = args.cache / "tray-review"
    review.mkdir(exist_ok=True)
    if args.output:
        prepare(args.source, args.cache, args.output)
    else:
        inspect_atlases(args.source, review)


if __name__ == "__main__":
    main()
