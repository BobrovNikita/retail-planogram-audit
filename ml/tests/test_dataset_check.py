import csv, json, tempfile, unittest, sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from dataset_check import check, resolved_data

class DatasetCheckTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory(); self.root=Path(self.temp.name)
        (self.root/'classes.json').write_text('["A"]',encoding='utf-8')
        (self.root/'data.yaml').write_text('path: data\ntrain: images/train\nval: images/val\ntest: images/test\nnames: [A]\n',encoding='utf-8')
        self.rows=[]
        for split in ('train','val','test'):
            images=self.root/'data'/'images'/split;images.mkdir(parents=True)
            labels=self.root/'data'/'labels'/split;labels.mkdir(parents=True)
            (images/'one.png').write_bytes(split.encode());(labels/'one.txt').write_text('0 0.5 0.5 0.2 0.4',encoding='utf-8')
            self.rows.append([f'images/{split}/one.png',split+'-capture',split])
    def tearDown(self): self.temp.cleanup()
    def run_check(self):
        with (self.root/'groups.csv').open('w',newline='',encoding='utf-8') as f:
            writer=csv.writer(f);writer.writerow(['image','capture_id','split']);writer.writerows(self.rows)
        return check(self.root/'data.yaml',self.root/'classes.json',self.root/'groups.csv')
    def test_valid_splits(self): self.assertEqual(self.run_check(),{'train':1,'val':1,'test':1})
    def test_training_yaml_uses_the_same_absolute_root(self):
        import yaml
        path=resolved_data(self.root/'data.yaml',self.root/'output'/'resolved.yaml')
        self.assertEqual(Path(yaml.safe_load(Path(path).read_text())['path']),self.root/'data')
    def test_capture_leak(self):
        self.rows[1][1]=self.rows[0][1]
        with self.assertRaisesRegex(ValueError,'crosses splits'): self.run_check()
    def test_duplicate_image_leak(self):
        (self.root/'data/images/val/one.png').write_bytes(b'train')
        with self.assertRaisesRegex(ValueError,'Identical image'): self.run_check()
    def test_class_mismatch(self):
        (self.root/'classes.json').write_text('["B"]',encoding='utf-8')
        with self.assertRaisesRegex(ValueError,'exactly match'): self.run_check()
    def test_nan_and_bad_class(self):
        for label in ('0 nan .5 .2 .2','1 .5 .5 .2 .2','0 .1 .1 .8 .8'):
            (self.root/'data/labels/train/one.txt').write_text(label,encoding='utf-8')
            with self.assertRaisesRegex(ValueError,'Invalid YOLO'): self.run_check()
    def test_missing_annotation(self):
        (self.root/'data/labels/train/one.txt').unlink()
        with self.assertRaisesRegex(ValueError,'Missing annotation'): self.run_check()
    def test_empty_negative_annotation(self):
        (self.root/'data/labels/train/one.txt').write_text('',encoding='utf-8');self.run_check()

if __name__=='__main__': unittest.main()
