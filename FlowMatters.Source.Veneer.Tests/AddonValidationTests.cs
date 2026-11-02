using FlowMatters.Source.Veneer.Addons;

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
                name = "a",
                type = "script",
                path = "tools/x.bat",
                script = new[] { "echo hi" }
            };
            AddonAssert.Contains(VeneerAddon.Validate(addon), "both");
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
        public void ExeWithNoPath_IsInvalid()
        {
            var addon = new VeneerAddon { name = "a", type = "exe" };
            AddonAssert.Contains(VeneerAddon.Validate(addon), "neither",
                                 "an exe addon with no path would otherwise only fail when clicked");
        }

        [Test]
        public void ExeWithEmptyPath_IsInvalid()
        {
            var addon = new VeneerAddon { name = "a", type = "exe", path = "" };
            Assert.That(VeneerAddon.Validate(addon), Is.Not.Null);
        }

        [Test]
        public void ScriptTypeWithNoLines_ReportsTheScriptReasonNotTheNeitherReason()
        {
            var addon = new VeneerAddon { name = "a", type = "script" };
            AddonAssert.Contains(VeneerAddon.Validate(addon), "no 'script' lines",
                                 "the more specific reason must win over the catch-all");
        }

        [Test]
        public void ExeWithArgsAndEnv_IsValid()
        {
            var addon = new VeneerAddon
            {
                name = "a",
                type = "exe",
                path = "tools/calib.exe",
                args = new[] { "--port", "%VENEER_PORT%" },
                workingDirectory = "tools"
            };
            Assert.That(VeneerAddon.Validate(addon), Is.Null);
        }

        [Test]
        public void UrlAddon_IsValid()
        {
            var addon = new VeneerAddon { name = "a", type = "url", url = "https://wiki.example.org" };
            Assert.That(VeneerAddon.Validate(addon), Is.Null);
        }

        [Test]
        public void UrlWithPath_IsInvalid()
        {
            var addon = new VeneerAddon
            {
                name = "a",
                type = "url",
                url = "https://wiki.example.org",
                path = "tools/x.bat"
            };
            AddonAssert.Contains(VeneerAddon.Validate(addon), "mutually exclusive");
        }

        [Test]
        public void UrlWithScript_IsInvalid()
        {
            var addon = new VeneerAddon
            {
                name = "a",
                type = "url",
                url = "https://wiki.example.org",
                script = new[] { "echo hi" }
            };
            AddonAssert.Contains(VeneerAddon.Validate(addon), "mutually exclusive");
        }

        [Test]
        public void UrlWithEmptyScriptArray_IsInvalid()
        {
            // The exclusion rule tests script != null, matching the existing
            // path/script rule -- an empty array must not mean "absent" for one
            // rule and "present" for another in the same method.
            var addon = new VeneerAddon
            {
                name = "a",
                type = "url",
                url = "https://wiki.example.org",
                script = new string[0]
            };
            AddonAssert.Contains(VeneerAddon.Validate(addon), "mutually exclusive");
        }

        [Test]
        public void UrlOnANonUrlType_IsInvalid()
        {
            // Without this rule the entry passes validation, dispatches to
            // LaunchExe, and dies in Path.Combine(dir, null).
            var addon = new VeneerAddon { name = "a", type = "exe", url = "https://wiki.example.org" };
            AddonAssert.Contains(VeneerAddon.Validate(addon), "type is not 'url'");
        }

        [Test]
        public void UrlTypeWithNoUrl_IsInvalid()
        {
            var addon = new VeneerAddon { name = "a", type = "url" };
            Assert.That(VeneerAddon.Validate(addon), Is.Not.Null);
        }

        [Test]
        public void UrlTypeWithNoUrl_ReportsTheUrlReasonNotTheNeitherReason()
        {
            var addon = new VeneerAddon { name = "a", type = "url" };
            AddonAssert.Contains(VeneerAddon.Validate(addon), "no 'url'",
                                 "the more specific reason must win over the catch-all");
        }

        [Test]
        public void UrlWithDisallowedScheme_IsInvalid()
        {
            var addon = new VeneerAddon { name = "a", type = "url", url = "file://server/share/x.pdf" };
            AddonAssert.Contains(VeneerAddon.Validate(addon), "mailto:");
        }

        [Test]
        public void UrlTypeIsMatchedCaseInsensitivelyByValidate()
        {
            // Validate compares case-insensitively but VeneerMenu's switch does
            // not, so "URL" validates and then renders as an unknown type. That
            // asymmetry pre-dates this feature and is recorded, not fixed.
            var addon = new VeneerAddon { name = "a", type = "URL", url = "https://wiki.example.org" };
            Assert.That(VeneerAddon.Validate(addon), Is.Null);
        }

        [Test]
        public void NeitherPathScriptNorUrl_IsInvalid()
        {
            var addon = new VeneerAddon { name = "a", type = "exe" };
            AddonAssert.Contains(VeneerAddon.Validate(addon), "neither");
        }
    }
}
