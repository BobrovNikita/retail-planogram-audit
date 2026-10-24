namespace RetailVision.Core;

/// <summary>Bounded, deterministic one-to-one matching with separate uncertainty and inventory metrics.</summary>
public sealed class ComplianceAnalyzer : IComplianceAnalyzer
{
    public ComplianceResult Analyze(IReadOnlyList<ExpectedPosition> expected, IReadOnlyList<Detection> detections,
        double confidenceThreshold = 0.5) => Analyze(expected, detections, new ComplianceOptions { ConfidenceThreshold = confidenceThreshold });

    public ComplianceResult Analyze(PlanogramDocument planogram, IReadOnlyList<Detection> detections,
        ComplianceOptions? options = null) => Analyze(PlanogramPositions.Expand(planogram), detections, options ?? new());

    public ComplianceResult Analyze(IReadOnlyList<ExpectedPosition> expected, IReadOnlyList<Detection> detections, ComplianceOptions options)
    {
        Validate(expected, detections, options);
        var known = (options.KnownSkus ?? expected.Select(e => e.Sku)).ToHashSet(StringComparer.Ordinal);
        bool Recognized(Detection d) => d.Sku is not null && known.Contains(d.Sku) && d.Confidence >= options.ConfidenceThreshold;
        bool Certain(Detection d) => d.Shelf > 0 && Recognized(d);
        var a = expected.OrderBy(e => e.Shelf).ThenBy(e => e.Position).ToArray();
        var b = detections.Select(d => AssignShelf(d, options)).OrderBy(d => d.Shelf)
            .ThenBy(Center).ThenBy(d => d.Y).ThenBy(d => d.Id, StringComparer.Ordinal).ToArray();
        var ranks = b.Select((d, i) => (d.Id, Rank: i)).ToDictionary(x => x.Id, x => x.Rank, StringComparer.Ordinal);
        var matches = new List<PositionMatch>();
        var usedExpected = new HashSet<ExpectedPosition>();
        var usedDetected = new HashSet<string>(StringComparer.Ordinal);
        void Add(ExpectedPosition? e, Detection? d, ViolationKind? kind, bool certain, string reason)
        {
            if (e is not null && !usedExpected.Add(e)) throw new InvalidOperationException("Expected position matched twice.");
            if (d is not null && !usedDetected.Add(d.Id)) throw new InvalidOperationException("Detection matched twice.");
            matches.Add(new(e, d, kind, certain, reason));
        }

        var anchors = new List<(ExpectedPosition Expected, Detection Detection)>();
        var sequenceCount = 0;
        foreach (var shelf in a.Select(e => e.Shelf).Union(b.Select(d => d.Shelf)).Order())
        {
            var shelfA = a.Where(e => e.Shelf == shelf).ToArray();
            var shelfB = b.Where(d => d.Shelf == shelf).ToArray();
            var sequence = Align(shelfA, shelfB, (e, d) => Certain(d) && e.Sku == d.Sku);
            sequenceCount += sequence.Count;
            var pairs = options.Strategy == MatchingStrategy.Sequence ? sequence
                : shelfA.Zip(shelfB).Where(p => Certain(p.Second) && p.First.Sku == p.Second.Sku)
                    .Select(p => (Expected: p.First, Detection: p.Second)).ToList();
            foreach (var (e, d) in pairs)
            {
                anchors.Add((e, d));
                var displaced = e.CenterX.HasValue && Distance(e, d) > options.PositionTolerance + 1e-9;
                Add(e, d, displaced ? ViolationKind.WrongPosition : null, true,
                    displaced ? "SKU и порядок совпали, но горизонтальная позиция вне допуска." : "SKU, полка и порядок совпали.");
            }
        }
        // Prefer all local relocations before any cross-shelf relocation.
        foreach (var sameShelf in new[] { true, false })
            foreach (var e in a.Where(e => !usedExpected.Contains(e)))
            {
                var candidate = b.Where(d => !usedDetected.Contains(d.Id) && Certain(d) && d.Sku == e.Sku && (d.Shelf == e.Shelf) == sameShelf)
                    .OrderBy(d => Distance(e, d)).ThenBy(d => d.Shelf).ThenBy(Center).ThenBy(d => d.Id, StringComparer.Ordinal).FirstOrDefault();
                if (candidate is not null) Add(e, candidate, sameShelf ? ViolationKind.WrongPosition : ViolationKind.WrongShelf, true,
                    sameShelf ? "Товар найден на своей полке, но нарушает ожидаемый порядок." : "Товар найден на другой полке.");
            }

        // Fill only the same gaps between LCS anchors; do not turn a missing item into a cascade of substitutions.
        foreach (var group in a.Where(e => !usedExpected.Contains(e)).GroupBy(e => e.Shelf))
        {
            var shelfAnchors = anchors.Where(p => p.Expected.Shelf == group.Key).ToArray();
            int ExpectedGap(ExpectedPosition e) => shelfAnchors.Count(p => p.Expected.Position < e.Position);
            var detectedGaps = b.Where(d => d.Shelf == group.Key).ToDictionary(d => d.Id,
                d => shelfAnchors.Count(p => ranks[p.Detection.Id] < ranks[d.Id]), StringComparer.Ordinal);
            foreach (var gap in group.GroupBy(ExpectedGap))
            {
                var gapB = b.Where(d => d.Shelf == group.Key && !usedDetected.Contains(d.Id) && detectedGaps[d.Id] == gap.Key).ToArray();
                foreach (var (e, d) in Align(gap.ToArray(), gapB,
                             (e, d) => !e.CenterX.HasValue || Distance(e, d) <= options.PositionTolerance + 1e-9))
                    Add(e, d, Certain(d) ? ViolationKind.WrongProduct : ViolationKind.UnknownProduct, Certain(d),
                        Certain(d) ? "На ожидаемой позиции обнаружен другой известный SKU." : "Объект сопоставлен с ожидаемой позицией, но его SKU не подтверждён.");
            }
        }
        // A recognized SKU without an assigned shelf can explain one unambiguous expected item, but never a correct position.
        foreach (var d in b.Where(d => !usedDetected.Contains(d.Id) && d.Shelf == 0 && Recognized(d)))
        {
            var candidates = a.Where(e => !usedExpected.Contains(e) && e.Sku == d.Sku).ToArray();
            if (candidates.Length == 1) Add(candidates[0], d, ViolationKind.UnknownProduct, false, "SKU распознан, но полка не определена однозначно.");
        }
        var unresolved = b.Where(d => !usedDetected.Contains(d.Id) && !Certain(d)).ToArray();
        foreach (var e in a.Where(e => !usedExpected.Contains(e)))
        {
            var uncertain = unresolved.Any(d => d.Shelf == e.Shelf || d.Shelf == 0);
            Add(e, null, ViolationKind.MissingProduct, !uncertain,
                uncertain ? "Товар не подтверждён; неопределённые объекты требуют проверки." : "Ожидаемый товар не найден среди подтверждённых детекций.");
        }
        foreach (var d in b.Where(d => !usedDetected.Contains(d.Id)))
            Add(null, d, Certain(d) ? ViolationKind.ExtraProduct : ViolationKind.UnknownProduct, Certain(d),
                Certain(d) ? "Подтверждённый товар не имеет свободной ожидаемой позиции."
                    : d.Shelf == 0 ? "Полка не определена однозначно." : "Недостаточная уверенность или неизвестный SKU.");

        return Result(a, b, matches, sequenceCount, options, Recognized);
    }

    private static double Center(Detection d) => d.X + d.Width / 2;
    private static double Distance(ExpectedPosition e, Detection d) => e.CenterX.HasValue ? Math.Abs(e.CenterX.Value - Center(d)) : 0;

    // Maximize pairs, then minimize measured horizontal distance. Diagonal wins exact ties; skip expected wins other ties.
    private static List<(ExpectedPosition Expected, Detection Detection)> Align(ExpectedPosition[] a, Detection[] b,
        Func<ExpectedPosition, Detection, bool> canPair)
    {
        var count = new int[a.Length + 1, b.Length + 1];
        var cost = new double[a.Length + 1, b.Length + 1];
        var action = new byte[a.Length, b.Length];
        for (var i = a.Length - 1; i >= 0; i--)
            for (var j = b.Length - 1; j >= 0; j--)
            {
                count[i, j] = count[i + 1, j]; cost[i, j] = cost[i + 1, j]; action[i, j] = 2;
                void Consider(int n, double c, byte step, bool preferTie = false)
                {
                    if (n > count[i, j] || n == count[i, j] && (c < cost[i, j] - 1e-9 || preferTie && Math.Abs(c - cost[i, j]) <= 1e-9))
                    { count[i, j] = n; cost[i, j] = c; action[i, j] = step; }
                }
                Consider(count[i, j + 1], cost[i, j + 1], 3);
                if (canPair(a[i], b[j])) Consider(1 + count[i + 1, j + 1], Distance(a[i], b[j]) + cost[i + 1, j + 1], 1, true);
            }
        var result = new List<(ExpectedPosition, Detection)>();
        int x = 0, y = 0;
        while (x < a.Length && y < b.Length)
            if (action[x, y] == 1) result.Add((a[x++], b[y++]));
            else if (action[x, y] == 2) x++;
            else y++;
        return result;
    }

    private static Detection AssignShelf(Detection d, ComplianceOptions options)
    {
        if (options.Shelves.Count == 0) return d;
        var bottom = d.Y + d.Height;
        var candidates = options.Shelves.Where(s => Center(d) >= s.Left && Center(d) <= s.Right &&
            bottom >= s.Top - options.ShelfTolerance && bottom <= s.Bottom + options.ShelfTolerance).ToArray();
        return d with { Shelf = candidates.Length == 1 ? candidates[0].Shelf : 0 };
    }

    private static double Percent(int numerator, int denominator) => denominator == 0 ? 100 : Math.Round(100.0 * numerator / denominator, 1, MidpointRounding.AwayFromZero);

    private static ComplianceResult Result(ExpectedPosition[] expected, Detection[] detected, List<PositionMatch> matches,
        int sequenceCount, ComplianceOptions options, Func<Detection, bool> recognized)
    {
        var ordered = matches.OrderBy(m => m.Expected?.Shelf ?? m.Detection!.Shelf)
            .ThenBy(m => m.Expected?.Position ?? int.MaxValue).ThenBy(m => m.Detection?.Id, StringComparer.Ordinal).ToArray();
        var violations = ordered.Where(m => m.Kind.HasValue).Select(m => new Violation(m.Kind!.Value,
            m.Expected?.Shelf ?? m.Detection!.Shelf, m.Expected?.Position, m.Expected?.Sku ?? m.Detection?.Sku, m.Detection?.Id,
            m.Detection?.Sku, m.Detection?.Shelf, m.Detection?.Confidence, m.IsCertain, m.Reason)).ToArray();
        var correct = ordered.Where(m => m.Kind is null).Select(m => m.Detection!.Id).ToArray();
        var countsA = expected.GroupBy(e => (e.Shelf, e.Sku)).ToDictionary(g => g.Key, g => g.Count());
        var countsB = detected.Where(recognized).GroupBy(d => (d.Shelf, Sku: d.Sku!)).ToDictionary(g => g.Key, g => g.Count());
        var facings = countsA.Keys.Union(countsB.Keys).OrderBy(k => k.Shelf).ThenBy(k => k.Sku, StringComparer.Ordinal)
            .Select(k => new FacingComparison(k.Shelf, k.Sku, countsA.GetValueOrDefault(k), countsB.GetValueOrDefault(k))).ToArray();
        double FacingPercent(IEnumerable<FacingComparison> rows, int unknown) =>
            Percent(rows.Sum(f => Math.Min(f.Expected, f.Detected)), rows.Sum(f => Math.Max(f.Expected, f.Detected)) + unknown);
        var distinct = expected.Select(e => e.Sku).Distinct(StringComparer.Ordinal).ToArray();
        var present = distinct.Count(sku => detected.Any(d => recognized(d) && d.Sku == sku));
        var total = Math.Max(expected.Length, detected.Length);
        var metrics = new ComplianceMetrics(Percent(correct.Length, total), Percent(sequenceCount, total),
            Percent(present, distinct.Length), FacingPercent(facings, detected.Count(d => !recognized(d))),
            present, distinct.Length, violations.Count(v => v.IsCertain), violations.Count(v => !v.IsCertain));
        var shelves = expected.Select(e => e.Shelf).Union(detected.Select(d => d.Shelf)).Order().Select(s =>
        {
            var n = expected.Count(e => e.Shelf == s); var m = detected.Count(d => d.Shelf == s);
            var ok = ordered.Count(p => p.Kind is null && p.Expected?.Shelf == s);
            return new ShelfStatistics(s, n, m, ok, Percent(ok, Math.Max(n, m)),
                FacingPercent(facings.Where(f => f.Shelf == s), detected.Count(d => d.Shelf == s && !recognized(d))),
                violations.Count(v => v.Shelf == s && v.IsCertain), violations.Count(v => v.Shelf == s && !v.IsCertain));
        }).ToArray();
        return new(metrics.PositionsPercent, correct.Length, expected.Length, detected.Length, violations, correct,
            ordered, metrics, shelves, facings, options.Strategy, expected.Any(e => e.CenterX.HasValue));
    }

    private static void Validate(IReadOnlyList<ExpectedPosition> expected, IReadOnlyList<Detection> detections, ComplianceOptions options)
    {
        ArgumentNullException.ThrowIfNull(expected); ArgumentNullException.ThrowIfNull(detections); ArgumentNullException.ThrowIfNull(options);
        static bool Unit(double x) => double.IsFinite(x) && x >= 0 && x <= 1;
        if (!Unit(options.ConfidenceThreshold) || !Unit(options.PositionTolerance) || !Unit(options.ShelfTolerance) || !Enum.IsDefined(options.Strategy))
            throw new ArgumentOutOfRangeException(nameof(options), "Thresholds must be finite and within 0..1; strategy must be supported.");
        if (expected.Count > 500 || detections.Count > 500) throw new ArgumentException("At most 500 positions are supported.");
        if (expected.Any(e => e is null || e.Shelf < 1 || e.Position < 1 || string.IsNullOrWhiteSpace(e.Sku) || e.CenterX.HasValue && !Unit(e.CenterX.Value)) ||
            expected.Select(e => (e.Shelf, e.Position)).Distinct().Count() != expected.Count)
            throw new ArgumentException("Expected positions must be valid and unique.", nameof(expected));
        if (detections.Any(d => d is null || string.IsNullOrWhiteSpace(d.Id) || d.Shelf < 0 || !Unit(d.Confidence) ||
            !Unit(d.X) || !Unit(d.Y) || !Unit(d.Width) || !Unit(d.Height) || d.Width == 0 || d.Height == 0 || d.X + d.Width > 1 + 1e-9 || d.Y + d.Height > 1 + 1e-9) ||
            detections.Select(d => d.Id).Distinct(StringComparer.Ordinal).Count() != detections.Count)
            throw new ArgumentException("Detection IDs, confidence and normalized boxes must be valid.", nameof(detections));
        if (options.KnownSkus is not null && (options.KnownSkus.Any(string.IsNullOrWhiteSpace) || expected.Any(e => !options.KnownSkus.Contains(e.Sku, StringComparer.Ordinal))))
            throw new ArgumentException("The known catalog must include every expected SKU.", nameof(options));
        if (options.Shelves is null || options.Shelves.Count > 100 ||
            options.Shelves.Any(s => s is null || s.Shelf < 1 || !Unit(s.Left) || !Unit(s.Right) || !Unit(s.Top) || !Unit(s.Bottom) || s.Left >= s.Right || s.Top >= s.Bottom) ||
            options.Shelves.Select(s => s.Shelf).Distinct().Count() != options.Shelves.Count)
            throw new ArgumentException("Shelf geometry must contain unique, valid normalized bands.", nameof(options));
        if (options.Shelves.Count > 0 && expected.Any(e => options.Shelves.All(s => s.Shelf != e.Shelf)))
            throw new ArgumentException("Every expected shelf needs a geometry band.", nameof(options));
    }
}
