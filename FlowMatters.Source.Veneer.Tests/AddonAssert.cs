namespace FlowMatters.Source.Veneer.Tests
{
    /// <summary>
    /// Substring assertion that compiles against every NUnit this codebase has to
    /// build under. There is no single classic-style helper that works across all
    /// of them: Does.Contain is NUnit 3+, StringAssert moved to
    /// NUnit.Framework.Legacy in NUnit 4, and Source 4.5.0 (GBR) ships NUnit
    /// 2.6.4. Assert.That(bool, Is.True) is the common subset.
    ///
    /// Keep this portable -- master builds against NUnit 4.0.1 and legacy_ci
    /// against whatever the targeted Source version bundles, from 2.6.4 up.
    /// </summary>
    internal static class AddonAssert
    {
        public static void Contains(string actual, string expected)
        {
            Contains(actual, expected, null);
        }

        public static void Contains(string actual, string expected, string context)
        {
            Assert.That(actual, Is.Not.Null, context);
            Assert.That(actual.Contains(expected), Is.True,
                        "expected to contain \"" + expected + "\" but was \"" + actual + "\"" +
                        (context == null ? "" : " -- " + context));
        }
    }
}
