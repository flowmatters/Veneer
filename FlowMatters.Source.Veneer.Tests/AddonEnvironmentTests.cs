using FlowMatters.Source.Veneer.DomainActions;

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
                ConfigDirectory = @"C:\Users\joel\.veneer",
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
            Assert.That(env["VENEER_CONFIG_DIR"], Is.EqualTo(@"C:\Users\joel\.veneer"));
        }

        [Test]
        public void BuildEffective_NullConfigDirectoryBecomesEmptyString()
        {
            var context = Ctx();
            context.ConfigDirectory = null;
            var env = AddonEnvironment.BuildEffective(context, null);
            Assert.That(env["VENEER_CONFIG_DIR"], Is.EqualTo(""));
        }

        [Test]
        public void Expand_ResolvesConfigDirInAPath()
        {
            var env = AddonEnvironment.BuildEffective(Ctx(), null);
            Assert.That(AddonEnvironment.Expand(@"%VENEER_CONFIG_DIR%\tools\calibrate.bat", env),
                        Is.EqualTo(@"C:\Users\joel\.veneer\tools\calibrate.bat"));
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

        private static AddonContext CtxWithEnv(params string[] keysAndValues)
        {
            var context = Ctx();
            context.ConfigEnv = new Dictionary<string, string>();
            for (var i = 0; i < keysAndValues.Length; i += 2)
                context.ConfigEnv[keysAndValues[i]] = keysAndValues[i + 1];
            return context;
        }

        [Test]
        public void BuildEffective_InjectsFileLevelEnv()
        {
            var env = AddonEnvironment.BuildEffective(
                CtxWithEnv("TOOLS_ROOT", @"D:\my\tools"), null);
            Assert.That(env["TOOLS_ROOT"], Is.EqualTo(@"D:\my\tools"));
        }

        [Test]
        public void BuildEffective_FileLevelEnvExpandsVeneerVariables()
        {
            var env = AddonEnvironment.BuildEffective(
                CtxWithEnv("TOOLS_ROOT", @"%VENEER_CONFIG_DIR%\tools"), null);
            Assert.That(env["TOOLS_ROOT"], Is.EqualTo(@"C:\Users\joel\.veneer\tools"));
        }

        // Both rows insert the referenced key before the referencing one, which
        // is the ordering that catches the mistake this guards against: expanding
        // against the live dictionary would resolve %firstKey% here instead of
        // leaving it literal. The reverse ordering cannot distinguish a correct
        // implementation from that one -- with the base not yet written, both
        // leave the value literal -- so it is not worth a third row.
        [TestCase("AAA", "ZZZ")]
        [TestCase("ZZZ", "AAA")]
        public void BuildEffective_FileLevelEnvDoesNotResolveAgainstAnEarlierKey(
            string firstKey, string secondKey)
        {
            var env = AddonEnvironment.BuildEffective(
                CtxWithEnv(firstKey, @"D:\base", secondKey, "%" + firstKey + @"%\sub"), null);
            Assert.That(env[secondKey], Is.EqualTo("%" + firstKey + @"%\sub"),
                        "left literal, so a cross-reference is visible rather than silently resolved");
        }

        [Test]
        public void BuildEffective_AddonEnvResolvesAgainstFileLevelEnv()
        {
            var addonEnv = new Dictionary<string, string> { { "RUN_DIR", @"%TOOLS_ROOT%\runs" } };
            var env = AddonEnvironment.BuildEffective(
                CtxWithEnv("TOOLS_ROOT", @"D:\my\tools"), addonEnv);
            Assert.That(env["RUN_DIR"], Is.EqualTo(@"D:\my\tools\runs"));
        }

        [Test]
        public void BuildEffective_AddonEnvWinsOverFileLevelEnv()
        {
            var addonEnv = new Dictionary<string, string> { { "TOOLS_ROOT", @"E:\override" } };
            var env = AddonEnvironment.BuildEffective(
                CtxWithEnv("TOOLS_ROOT", @"D:\my\tools"), addonEnv);
            Assert.That(env["TOOLS_ROOT"], Is.EqualTo(@"E:\override"));
        }

        // Deliberately permitted rather than reserved. Pinned so that removing the
        // ability becomes a visible decision rather than an accident.
        [Test]
        public void BuildEffective_FileLevelEnvCanOverrideAVeneerVariable()
        {
            var env = AddonEnvironment.BuildEffective(CtxWithEnv("VENEER_PORT", "1234"), null);
            Assert.That(env["VENEER_PORT"], Is.EqualTo("1234"));
        }

        [Test]
        public void BuildEffective_NullFileLevelEnvIsHarmless()
        {
            var context = Ctx();
            context.ConfigEnv = null;
            var env = AddonEnvironment.BuildEffective(context, null);
            Assert.That(env["VENEER_PORT"], Is.EqualTo("9876"));
        }
    }
}
