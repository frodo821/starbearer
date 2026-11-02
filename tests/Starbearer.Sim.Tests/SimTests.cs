using Starbearer.Sim;
using Xunit;

/// <summary>Shared fixtures for the tests; each file holds the tests of one part of the sim.</summary>
public partial class SimTests
{
    static readonly Cargo Box = new("box", 0.5, 0.9, 100);
    static double[] Unseen(Galaxy g) => Enumerable.Repeat(double.MaxValue, g.Stars.Length).ToArray();

    static Galaxy Empty(params Star[] stars) => new()
    {
        Seed = 1, Stars = stars, Nebulae = Array.Empty<Nebula>(), Ports = Array.Empty<int>(),
    };

    static Galaxy TwoStars() => Empty(
        new Star(0, "from", Vec3d.Zero, 1, 5000, Vec3d.Zero),
        new Star(1, "to", new Vec3d(8, 0, 0), 1, 5000, Vec3d.Zero));

    /// <summary>The stations of WithPorts: one at star 0, one at star 1.</summary>
    const int From = Galaxy.FirstStationId, To = Galaxy.FirstStationId + 1;

    static Galaxy WithPorts() => new()
    {
        Seed = 1,
        Stars = new[] { new Star(0, "from", Vec3d.Zero, 1, 5000, Vec3d.Zero), new Star(1, "to", new Vec3d(8, 0, 0), 1, 5000, Vec3d.Zero) },
        Nebulae = Array.Empty<Nebula>(),
        Ports = new[] { From, To },
        StationStars = new() { [From] = 0, [To] = 1 },
        Kinds = new() { [From] = StationKind.City, [To] = StationKind.City },
    };
}
