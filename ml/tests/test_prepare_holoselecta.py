import hashlib
import io
import json
from pathlib import Path
import sys
import tempfile
import unittest
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from prepare_holoselecta import date_splits, prepare, safe_members, yolo_box


class HoloSelectaPreparationTests(unittest.TestCase):
    def test_voc_edges_and_invalid_coordinates(self):
        self.assertEqual(yolo_box([1, 1, 100, 50], 100, 50), (.5, .5, 1, 1))
        for box in ([1, 1, 101, 50], [1, 1, float('nan'), 50], [40, 1, 20, 10]):
            with self.assertRaises(ValueError):
                yolo_box(box, 100, 50)

    def test_date_cannot_cross_splits(self):
        with self.assertRaisesRegex(ValueError, 'overlapping'):
            date_splits({'dates': {'train': ['20200101'], 'val': ['20200101'], 'test': ['20200102']}})

    def test_unsafe_and_duplicate_archive_entries(self):
        for names in (['FinalDataset/../bad.jpg'], ['FinalDataset/a.jpg', 'FinalDataset/a.jpg']):
            with self.subTest(names=names), io.BytesIO() as memory:
                with zipfile.ZipFile(memory, 'w') as archive:
                    for name in names: archive.writestr(name, b'x')
                memory.seek(0)
                with zipfile.ZipFile(memory) as archive, self.assertRaises(ValueError):
                    safe_members(archive)

    def test_real_images_conversion_provenance_and_rejections(self):
        from PIL import Image
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory); source = root/'source.zip'
            profile = {'name': 'test', 'sourceUrl': 'test', 'license': 'test', 'attribution': 'test',
                       'maxImageSide': 50, 'dates': {'train': ['20200101'], 'val': ['20200102'], 'test': ['20200103']},
                       'products': [{'sku': 'TEST', 'label': 'test-label'}]}
            with zipfile.ZipFile(source, 'w') as archive:
                for i, date in enumerate(('20200101', '20200102', '20200103', '20200104')):
                    name = f'IMG_{date}_100000.jpg'
                    with io.BytesIO() as memory:
                        Image.new('RGB', (100, 50), (i*50, 0, 0)).save(memory, 'JPEG')
                        archive.writestr('FinalDataset/'+name, memory.getvalue())
                    xml = f'<annotation><filename>{name}</filename><size><width>100</width><height>50</height></size><object><name>test-label</name><bndbox><xmin>1</xmin><ymin>1</ymin><xmax>100</xmax><ymax>50</ymax></bndbox></object></annotation>'
                    archive.writestr('FinalDataset/'+name.replace('.jpg','.xml'), xml)
                archive.writestr('FinalDataset/untrusted.py', 'raise RuntimeError("must never execute")')
            profile['archiveSha256'] = hashlib.sha256(source.read_bytes()).hexdigest()
            profile_path = root/'profile.json'; profile_path.write_text(json.dumps(profile))
            output = root/'prepared'
            report = prepare(source, profile_path, output)
            self.assertEqual(report['counts'], {'train': 1, 'val': 1, 'test': 1})
            self.assertEqual(len(report['excluded']), 1)
            self.assertEqual(report['images'][0]['preparedSize'], [50, 25])
            self.assertIn('20200101', report['catalogImages']['TEST']['sourceImage'])
            self.assertFalse((output/'untrusted.py').exists())
            self.assertEqual((output/'labels/train/IMG_20200101_100000.txt').read_text().strip(),
                             '0 0.500000000 0.500000000 1.000000000 1.000000000')
            with self.assertRaisesRegex(ValueError, 'new directory'):
                prepare(source, profile_path, output)
            source.write_bytes(b'changed')
            with self.assertRaisesRegex(ValueError, 'SHA256'):
                prepare(source, profile_path, root/'other')


if __name__ == '__main__':
    unittest.main()
