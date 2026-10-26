"""Regression: checkpoint selection must use the same inference head as export/runtime."""
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import train


class TrainingContractTests(unittest.TestCase):
    def test_default_training_selects_the_exported_one_to_one_head(self):
        # Ultralytics 8.4.187 validator chooses one-to-one ONLY for nms is False;
        # omitted/None/True instead evaluates a different head and selects wrong best.pt.
        selected_heads = []
        model = SimpleNamespace(train=lambda **options: selected_heads.append(options.get('nms') is False))
        with tempfile.TemporaryDirectory() as folder:
            argv = ['train.py', '--data', 'data.yaml', '--classes', 'classes.json', '--groups', 'groups.csv', '--project', folder]
            with patch.object(sys, 'argv', argv), patch.object(train, 'check') as check, \
                 patch.object(train, 'resolved_data', return_value='resolved.yaml'), \
                 patch.dict(sys.modules, {'ultralytics': SimpleNamespace(YOLO=Mock(return_value=model))}):
                train.main()
            check.assert_called_once_with('data.yaml', 'classes.json', 'groups.csv')
            self.assertEqual(selected_heads, [True], 'Training checkpoint selection and ONNX export must use the same head')


if __name__ == '__main__': unittest.main()
