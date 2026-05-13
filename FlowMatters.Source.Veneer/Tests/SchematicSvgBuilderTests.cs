using System.Drawing;
using FlowMatters.Source.Veneer.Formatting;
using NUnit.Framework;

namespace FlowMatters.Source.Veneer.Tests
{
    [TestFixture]
    public class SchematicSvgBuilderTests
    {
        [Test]
        public void ViewBox_TwoPoints_PadsCorrectly()
        {
            // Source schematic coords pass through unchanged.
            // Source: (0,0) and (100,50). Bbox: x in [0,100], y in [0,50]; width=100, height=50.
            // Pad = 5% of max(100,50) = 5. Expected viewBox: -5, -5, 110, 60.
            var result = SchematicSvgBuilder.ComputeViewBoxForTesting(
                new[] { new PointF(0, 0), new PointF(100, 50) });

            Assert.That(result[0], Is.EqualTo(-5.0).Within(1e-9));   // minX
            Assert.That(result[1], Is.EqualTo(-5.0).Within(1e-9));   // minY
            Assert.That(result[2], Is.EqualTo(110.0).Within(1e-9));  // width
            Assert.That(result[3], Is.EqualTo(60.0).Within(1e-9));   // height
            // iconSize = sqrt(100^2 + 50^2) / 240
            Assert.That(result[4], Is.EqualTo(System.Math.Sqrt(12500) / 240.0).Within(1e-9));
        }

        [Test]
        public void ViewBox_SinglePoint_FallsBackTo100x100()
        {
            var result = SchematicSvgBuilder.ComputeViewBoxForTesting(new[] { new PointF(42, 7) });
            // Center on (42, 7), 100x100 around it.
            Assert.That(result[0], Is.EqualTo(-8.0).Within(1e-9));    // minX = 42 - 50
            Assert.That(result[1], Is.EqualTo(-43.0).Within(1e-9));   // minY = 7 - 50
            Assert.That(result[2], Is.EqualTo(100.0).Within(1e-9));
            Assert.That(result[3], Is.EqualTo(100.0).Within(1e-9));
        }

        [Test]
        public void ViewBox_AllCoincident_FallsBackTo100x100()
        {
            var result = SchematicSvgBuilder.ComputeViewBoxForTesting(
                new[] { new PointF(0, 0), new PointF(0, 0), new PointF(0, 0) });
            Assert.That(result[2], Is.EqualTo(100.0).Within(1e-9));
            Assert.That(result[3], Is.EqualTo(100.0).Within(1e-9));
        }
    }
}
