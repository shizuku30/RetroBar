from pathlib import Path
import struct
import sys
import unittest

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from extract_system_icons import make_bmp, make_ico, safe_name


class ResourceExportTests(unittest.TestCase):
    def test_ico_keeps_every_payload_and_color_depth(self):
        frames = [
            {"width": 16, "height": 16, "colors": 16, "planes": 1, "bpp": 4, "data": b"original-mask-data"},
            {"width": 256, "height": 256, "colors": 0, "planes": 1, "bpp": 32, "data": b"original-png-data"},
        ]
        data = make_ico(frames)
        self.assertEqual(struct.unpack_from("<HHH", data), (0, 1, 2))
        for index, frame in enumerate(frames):
            width, height, _, _, planes, bpp, size, offset = struct.unpack_from("<BBBBHHII", data, 6 + index * 16)
            self.assertEqual((width or 256, height or 256, planes, bpp), (frame["width"], frame["height"], 1, frame["bpp"]))
            self.assertEqual(data[offset:offset + size], frame["data"])

    def test_rgb_bitmap_keeps_original_dib(self):
        dib = struct.pack("<IiiHHIIiiII", 40, 1, 1, 1, 24, 0, 4, 0, 0, 0, 0) + b"\x00\x00\xff\x00"
        bmp = make_bmp(dib)
        self.assertEqual(bmp[:2], b"BM")
        self.assertEqual(struct.unpack_from("<I", bmp, 10)[0], 54)
        self.assertEqual(bmp[14:], dib)

    def test_bitfield_masks_are_not_counted_as_pixels(self):
        dib = struct.pack("<IiiHHIIiiII", 40, 1, 1, 1, 16, 3, 4, 0, 0, 0, 0)
        dib += struct.pack("<III", 0xF800, 0x07E0, 0x001F) + b"\x00\xf8\x00\x00"
        bmp = make_bmp(dib)
        self.assertEqual(struct.unpack_from("<I", bmp, 10)[0], 66)
        self.assertEqual(bmp[14:], dib)

    def test_core_bitmap_uses_three_byte_palette_entries(self):
        dib = struct.pack("<IHHHH", 12, 1, 1, 1, 1) + b"\x00\x00\x00\xff\xff\xff" + b"\x80\x00\x00\x00"
        bmp = make_bmp(dib)
        self.assertEqual(struct.unpack_from("<I", bmp, 10)[0], 32)
        self.assertEqual(bmp[14:], dib)

    def test_named_resources_cannot_escape_or_collide(self):
        self.assertNotIn("/", safe_name("a/b"))
        self.assertNotEqual(safe_name("a/b"), safe_name("a_b"))
        self.assertEqual(safe_name(123), "123")


if __name__ == "__main__":
    unittest.main()
