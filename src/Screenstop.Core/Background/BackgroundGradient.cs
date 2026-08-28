namespace Screenstop.Core.Background;

/// A named 3-stop linear gradient background (mac `AnnotationBackgroundGradient`
/// parity). Stop colors are stored inline so presets are self-contained.
public sealed record BackgroundGradient(
    string GradientId,
    string Title,
    IReadOnlyList<BackgroundColor> Colors,
    GradientPoint StartPoint,
    GradientPoint EndPoint)
{
    public static readonly IReadOnlyList<BackgroundGradient> Presets = new[]
    {
        new BackgroundGradient(
            "serene", "Serene Skies",
            Stops(("serene-a", "Serene A", 0.45, 0.68, 0.94), ("serene-b", "Serene B", 0.66, 0.84, 0.97), ("serene-c", "Serene C", 0.92, 0.96, 0.99)),
            GradientPoint.Top, GradientPoint.Bottom),
        new BackgroundGradient(
            "frost", "Frosted Glass",
            Stops(("frost-a", "Frost A", 0.93, 0.97, 0.98), ("frost-b", "Frost B", 0.72, 0.88, 0.93), ("frost-c", "Frost C", 0.52, 0.71, 0.88)),
            GradientPoint.TopLeading, GradientPoint.BottomTrailing),
        new BackgroundGradient(
            "aurora", "Aurora",
            Stops(("aurora-a", "Aurora A", 0.98, 0.31, 0.58), ("aurora-b", "Aurora B", 0.40, 0.32, 0.95), ("aurora-c", "Aurora C", 0.29, 0.84, 0.80)),
            GradientPoint.TopLeading, GradientPoint.BottomTrailing),
        new BackgroundGradient(
            "cobalt", "Cobalt",
            Stops(("cobalt-a", "Cobalt A", 0.04, 0.05, 0.50), ("cobalt-b", "Cobalt B", 0.26, 0.19, 0.93), ("cobalt-c", "Cobalt C", 0.42, 0.67, 0.98)),
            GradientPoint.Top, GradientPoint.BottomTrailing),
        new BackgroundGradient(
            "peach", "Peach",
            Stops(("peach-a", "Peach A", 0.98, 0.38, 0.36), ("peach-b", "Peach B", 0.99, 0.71, 0.36), ("peach-c", "Peach C", 0.90, 0.33, 0.65)),
            GradientPoint.TopLeading, GradientPoint.BottomTrailing),
        new BackgroundGradient(
            "glass", "Glass",
            Stops(("glass-a", "Glass A", 0.87, 0.95, 0.94), ("glass-b", "Glass B", 0.46, 0.77, 0.86), ("glass-c", "Glass C", 0.25, 0.53, 0.93)),
            GradientPoint.TopLeading, GradientPoint.BottomTrailing),
        new BackgroundGradient(
            "plasma", "Plasma",
            Stops(("plasma-a", "Plasma A", 0.08, 0.02, 0.22), ("plasma-b", "Plasma B", 0.35, 0.12, 0.84), ("plasma-c", "Plasma C", 0.95, 0.26, 0.42)),
            GradientPoint.TopTrailing, GradientPoint.BottomLeading),
        new BackgroundGradient(
            "mango", "Mango",
            Stops(("mango-a", "Mango A", 0.99, 0.75, 0.20), ("mango-b", "Mango B", 0.96, 0.33, 0.21), ("mango-c", "Mango C", 0.67, 0.19, 0.89)),
            GradientPoint.TopLeading, GradientPoint.BottomTrailing),
        new BackgroundGradient(
            "mist", "Mist",
            Stops(("mist-a", "Mist A", 0.94, 0.94, 0.92), ("mist-b", "Mist B", 0.80, 0.88, 0.94), ("mist-c", "Mist C", 0.95, 0.76, 0.70)),
            GradientPoint.TopLeading, GradientPoint.BottomTrailing),
        new BackgroundGradient(
            "lagoon", "Lagoon",
            Stops(("lagoon-a", "Lagoon A", 0.08, 0.30, 0.54), ("lagoon-b", "Lagoon B", 0.25, 0.64, 0.72), ("lagoon-c", "Lagoon C", 0.70, 0.92, 0.78)),
            GradientPoint.BottomLeading, GradientPoint.TopTrailing),
        new BackgroundGradient(
            "ember", "Ember",
            Stops(("ember-a", "Ember A", 0.18, 0.03, 0.08), ("ember-b", "Ember B", 0.86, 0.17, 0.18), ("ember-c", "Ember C", 1.00, 0.67, 0.25)),
            GradientPoint.TopLeading, GradientPoint.BottomTrailing),
        new BackgroundGradient(
            "violet", "Violet",
            Stops(("violet-a", "Violet A", 0.24, 0.08, 0.51), ("violet-b", "Violet B", 0.59, 0.22, 0.94), ("violet-c", "Violet C", 0.96, 0.42, 0.74)),
            GradientPoint.Top, GradientPoint.BottomTrailing),
        new BackgroundGradient(
            "seaglass", "Sea Glass",
            Stops(("seaglass-a", "Sea Glass A", 0.43, 0.86, 0.75), ("seaglass-b", "Sea Glass B", 0.25, 0.62, 0.80), ("seaglass-c", "Sea Glass C", 0.22, 0.35, 0.75)),
            GradientPoint.TopLeading, GradientPoint.BottomTrailing),
        new BackgroundGradient(
            "citrus", "Citrus",
            Stops(("citrus-a", "Citrus A", 0.99, 0.91, 0.30), ("citrus-b", "Citrus B", 0.44, 0.78, 0.29), ("citrus-c", "Citrus C", 0.12, 0.58, 0.42)),
            GradientPoint.TopTrailing, GradientPoint.BottomLeading),
        new BackgroundGradient(
            "amethyst", "Amethyst",
            Stops(("amethyst-a", "Amethyst A", 0.10, 0.08, 0.28), ("amethyst-b", "Amethyst B", 0.35, 0.15, 0.65), ("amethyst-c", "Amethyst C", 0.76, 0.39, 0.95)),
            GradientPoint.BottomLeading, GradientPoint.TopTrailing),
        new BackgroundGradient(
            "sorbet", "Sorbet",
            Stops(("sorbet-a", "Sorbet A", 1.00, 0.49, 0.51), ("sorbet-b", "Sorbet B", 1.00, 0.74, 0.48), ("sorbet-c", "Sorbet C", 0.56, 0.78, 0.98)),
            GradientPoint.TopLeading, GradientPoint.BottomTrailing),
        new BackgroundGradient(
            "mineral", "Mineral",
            Stops(("mineral-a", "Mineral A", 0.93, 0.96, 0.95), ("mineral-b", "Mineral B", 0.64, 0.72, 0.82), ("mineral-c", "Mineral C", 0.33, 0.42, 0.55)),
            GradientPoint.TopLeading, GradientPoint.BottomTrailing),
        new BackgroundGradient(
            "dawn", "Dawn",
            Stops(("dawn-a", "Dawn A", 0.98, 0.62, 0.77), ("dawn-b", "Dawn B", 0.98, 0.82, 0.47), ("dawn-c", "Dawn C", 0.42, 0.71, 0.96)),
            GradientPoint.BottomLeading, GradientPoint.TopTrailing),
    };

    public static BackgroundGradient? ByGradientId(string? id) =>
        Presets.FirstOrDefault(g => g.GradientId == id);

    private static BackgroundColor[] Stops(
        (string Id, string Title, double R, double G, double B) a,
        (string Id, string Title, double R, double G, double B) b,
        (string Id, string Title, double R, double G, double B) c) =>
    [
        new BackgroundColor(a.Id, a.Title, a.R, a.G, a.B),
        new BackgroundColor(b.Id, b.Title, b.R, b.G, b.B),
        new BackgroundColor(c.Id, c.Title, c.R, c.G, c.B),
    ];
}
