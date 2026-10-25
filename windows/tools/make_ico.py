#!/usr/bin/env python3
"""Packs square PNGs into a Windows .ico (an ICONDIR followed by PNG-compressed entries).

The Windows app icon is the Mac one: render the iconset with Scripts/make_icon.swift, add a 48 px
size, then pack 16, 32, 48 and 256:

    swiftc -O -target "$(uname -m)-apple-macos13.0" -o build/obj/make_icon Scripts/make_icon.swift
    build/obj/make_icon build/obj/AppIcon.iconset
    sips -z 48 48 build/obj/AppIcon.iconset/icon_32x32@2x.png --out build/obj/icon_48.png
    python3 windows/tools/make_ico.py windows/src/TodoApp/Assets/AppIcon.ico \\
        build/obj/AppIcon.iconset/icon_16x16.png build/obj/AppIcon.iconset/icon_32x32.png \\
        build/obj/icon_48.png build/obj/AppIcon.iconset/icon_256x256.png
"""
import struct
import sys

PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"


def png_size(data, path):
    """Width and height from the IHDR chunk, which always comes first."""
    if data[:8] != PNG_SIGNATURE or data[12:16] != b"IHDR":
        sys.exit(f"{path}: not a PNG file")
    return struct.unpack(">II", data[16:24])


def main(argv):
    if len(argv) < 3:
        sys.exit("usage: make_ico.py <output.ico> <image.png>...")
    output, paths = argv[1], argv[2:]

    images = []
    for path in paths:
        with open(path, "rb") as file:
            data = file.read()
        width, height = png_size(data, path)
        if width != height or not 1 <= width <= 256:
            sys.exit(f"{path}: {width}x{height}; icons must be square, 1 to 256 px")
        images.append((width, data))
    images.sort(key=lambda image: image[0])

    header = struct.pack("<HHH", 0, 1, len(images))  # reserved, type 1 = icon, count
    offset = len(header) + 16 * len(images)
    entries, blobs = b"", b""
    for size, data in images:
        side = 0 if size == 256 else size  # a size byte of 0 means 256
        # width, height, palette colours, reserved, colour planes, bits per pixel, byte count, offset
        entries += struct.pack("<BBBBHHII", side, side, 0, 0, 1, 32, len(data), offset)
        blobs += data
        offset += len(data)

    with open(output, "wb") as file:
        file.write(header + entries + blobs)
    print(f"Wrote {output} ({', '.join(str(size) for size, _ in images)} px)")


if __name__ == "__main__":
    main(sys.argv)
