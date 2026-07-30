using System.Collections.Generic;
using FlowMatters.Source.Veneer.DomainActions;
using NUnit.Framework;

namespace FlowMatters.Source.Veneer.Tests
{
    [TestFixture]
    public class AddonEnvironmentTests
    {
        private static AddonContext Ctx()
        {
            return new AddonContext
            {
                ProjectDirectory = @"C:\models\catchment",
                ProjectFile = @"C:\models\catchment\m.rsproj",
                Port = 9876
            };
        }

        [Test]
        public void BuildEffective_InjectsVeneerVariables()
        {
            var env = AddonEnvironment.BuildEffective(Ctx(), null);
            Assert.That(env["VENEER_PORT"], Is.EqualTo("9876"));
            Assert.That(env["VENEER_PROJECT_DIR"], Is.EqualTo(@"C:\models\catchment"));
            Assert.That(env["VENEER_PROJECT_FILE"], Is.EqualTo(@"C:\models\catchment\m.rsproj"));
        }

        [Test]
        public void BuildEffective_AddonEnvOverridesInjected()
        {
            var addonEnv = new Dictionary<string, string> { { "VENEER_PORT", "1234" } };
            var env = AddonEnvironment.BuildEffective(Ctx(), addonEnv);
            Assert.That(env["VENEER_PORT"], Is.EqualTo("1234"));
        }

        [Test]
        public void BuildEffective_ExpandsEnvValues()
        {
            var addonEnv = new Dictionary<string, string> { { "OUT", @"%VENEER_PROJECT_DIR%\out" } };
            var env = AddonEnvironment.BuildEffective(Ctx(), addonEnv);
            Assert.That(env["OUT"], Is.EqualTo(@"C:\models\catchment\out"));
        }

        [Test]
        public void BuildEffective_EnvEntriesDoNotCrossReference()
        {
            var addonEnv = new Dictionary<string, string>
            {
                { "A", "first" },
                { "B", "%A%" }
            };
            var env = AddonEnvironment.BuildEffective(Ctx(), addonEnv);
            Assert.That(env["B"], Is.EqualTo("%A%"), "env entries must not resolve against each other");
        }

        [Test]
        public void Expand_LeavesUnknownVariablesIntact()
        {
            var env = new Dictionary<string, string> { { "KNOWN", "yes" } };
            Assert.That(AddonEnvironment.Expand("%KNOWN% %NOPE%", env), Is.EqualTo("yes %NOPE%"));
        }

        [Test]
        public void Expand_IsSinglePass()
        {
            var env = new Dictionary<string, string>
            {
                { "OUTER", "%INNER%" },
                { "INNER", "resolved" }
            };
            Assert.That(AddonEnvironment.Expand("%OUTER%", env), Is.EqualTo("%INNER%"));
        }

        [Test]
        public void Expand_IsCaseInsensitive()
        {
            var env = AddonEnvironment.BuildEffective(Ctx(), null);
            Assert.That(AddonEnvironment.Expand("%veneer_port%", env), Is.EqualTo("9876"));
        }

        [Test]
        public void Expand_HandlesNullAndEmpty()
        {
            var env = new Dictionary<string, string>();
            Assert.That(AddonEnvironment.Expand(null, env), Is.Null);
            Assert.That(AddonEnvironment.Expand("", env), Is.EqualTo(""));
        }
    }
}
