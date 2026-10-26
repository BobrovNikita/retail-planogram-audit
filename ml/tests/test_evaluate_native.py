import unittest
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from evaluate_native import match, rates


class NativeEvaluationTests(unittest.TestCase):
    def test_duplicate_predictions_cannot_match_one_target_twice(self):
        p = {'sku':'A','confidence':.9,'x':.1,'y':.1,'width':.2,'height':.2}
        result = match([p, p], [('A', (.1,.1,.3,.3))])
        self.assertEqual(result['A'], {'tp':1,'fp':1,'fn':0})
        self.assertEqual(rates(result['A'])['precision'], .5)

    def test_wrong_class_low_confidence_and_missed_target(self):
        p = {'sku':'B','confidence':.9,'x':.1,'y':.1,'width':.2,'height':.2}
        result = match([p, dict(p,sku='A',confidence=.49)], [('A', (.1,.1,.3,.3))])
        self.assertEqual(result['A'], {'tp':0,'fp':0,'fn':1})
        self.assertEqual(result['B'], {'tp':0,'fp':1,'fn':0})


if __name__ == '__main__': unittest.main()
