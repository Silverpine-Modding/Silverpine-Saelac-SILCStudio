"""Sprite composition regressions; no game files required."""
import unittest
from types import SimpleNamespace as N
from unittest.mock import patch

import read_assets as reader
from PIL import Image


class SpriteOrdering(unittest.TestCase):
    def sprite(self, index, color, layer, order=0):
        sprite = N(m_PixelsToUnits=32, m_Pivot=N(x=.5, y=.5),
                   image=Image.new('RGBA', (32, 32), color))
        obj = N(assets_file=N(name='test'), path_id=index, read=lambda: sprite)
        renderer = N(m_FlipX=False, m_FlipY=False,
                     m_Color=N(r=1, g=1, b=1, a=1),
                     m_SortingLayer=layer, m_SortingOrder=order)
        return renderer, obj, (1, 0, 0, 1, 0, 0), None, False

    def composite(self, layers):
        with patch.object(reader, 'renderers', return_value=layers):
            image, *_ = reader.extract(None, {})
            return image.getpixel((16, 16))

    def test_table_shadow_does_not_cover_furniture(self):
        self.assertEqual(self.composite([
            self.sprite(1, 'red', 1), self.sprite(2, 'black', 0)]), (255, 0, 0, 255))

    def test_chair_shadow_negative_layer(self):
        self.assertEqual(self.composite([
            self.sprite(1, 'red', 1), self.sprite(2, 'black', -1, 100)]), (255, 0, 0, 255))

    def test_order_within_same_layer_is_respected(self):
        self.assertEqual(self.composite([
            self.sprite(1, 'red', 1, 3), self.sprite(2, 'black', 1, 2)]), (255, 0, 0, 255))

    def test_higher_layer_overrides_order(self):
        self.assertEqual(self.composite([
            self.sprite(1, 'black', 0, 500), self.sprite(2, 'red', 1, -500)]), (255, 0, 0, 255))


if __name__ == '__main__':
    unittest.main()
