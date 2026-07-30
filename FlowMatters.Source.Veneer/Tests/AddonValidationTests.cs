using FlowMatters.Source.Veneer.Addons;
using NUnit.Framework;

namespace FlowMatters.Source.Veneer.Tests
{
    [TestFixture]
    public class AddonValidationTests
    {
        [Test]
        public void ExeWithPathOnly_IsValid()
        {
            var addon = new VeneerAddon { name = "a", type = "exe", path = "tools/x.bat" };
            Assert.That(VeneerAddon.Validate(addon), Is.Null);
        }

        [Test]
        public void ScriptWithLines_IsValid()
        {
            var addon = new VeneerAddon { name = "a", type = "script", script = new[] { "echo hi" } };
            Assert.That(VeneerAddon.Validate(addon), Is.Null);
        }

        [Test]
        public void BothPathAndScript_IsInvalid()
        {
            var addon = new VeneerAddon
            {
                name = "a", type = "script",
                path = "tools/x.bat", script = new[] { "echo hi" }
            };
            Assert.That(VeneerAddon.Validate(addon), Does.Contain("both"));
        }

        [Test]
        public void ScriptTypeWithNoLines_IsInvalid()
        {
            var addon = new VeneerAddon { name = "a", type = "script" };
            Assert.That(VeneerAddon.Validate(addon), Is.Not.Null);
        }

        [Test]
        public void ScriptTypeWithEmptyLines_IsInvalid()
        {
            var addon = new VeneerAddon { name = "a", type = "script", script = new string[0] };
            Assert.That(VeneerAddon.Validate(addon), Is.Not.Null);
        }

        [Test]
        public void ScriptTypeIsMatchedCaseInsensitively()
        {
            var addon = new VeneerAddon { name = "a", type = "SCRIPT" };
            Assert.That(VeneerAddon.Validate(addon), Is.Not.Null);
        }

        [Test]
        public void ExeWithArgsAndEnv_IsValid()
        {
            var addon = new VeneerAddon
            {
                name = "a", type = "exe", path = "tools/calib.exe",
                args = new[] { "--port", "%VENEER_PORT%" },
                workingDirectory = "tools"
            };
            Assert.That(VeneerAddon.Validate(addon), Is.Null);
        }
    }
}
