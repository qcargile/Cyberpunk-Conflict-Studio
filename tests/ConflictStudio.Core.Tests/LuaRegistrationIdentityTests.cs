using ConflictStudio.Core;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class LuaRegistrationIdentityTests
{
    [TestMethod]
    [DataRow("night", "GameObjectEffectHelper", "StartEffectEvent", "if self.runtimeData.enabled then return end wrapped(self)")]
    [DataRow("freefly", "ZoomEventsTransition", "OnEnter", "if self.runtimeData.active then return end wrapped(self)")]
    [DataRow("Pre20NCPDScannerIcons", "MappinUIUtils", "MappinToTexturePart;gamedataMappinVariant", "if Settings.AssaultInProgress then return 'gang_watch' end return wrapped(self)")]
    public void InitializerDefinitionAndInvocationDescribeOneRegistration(string initializer, string className, string methodName, string body)
    {
        string text = "function " + initializer + ":new()\nregisterForEvent('onInit', function()\nOverride('" + className + "', '" + methodName + "', function(self, wrapped) " + body + " end)\nend)\nend\nreturn " + initializer + ":new()";
        LuaSource[] sources = [new(initializer, "init.lua", text)];

        LuaCallbackEvidence[] callbacks = LuaCallbackEvidenceAnalyzer.Analyze(sources).Where(value => value.Kind == LuaCallbackEvidenceKind.Override).ToArray();

        Assert.HasCount(1, callbacks);
        Assert.AreEqual(className + "." + methodName, callbacks[0].Target);
        Assert.AreEqual(6, callbacks[0].Line);
        Assert.HasCount(0, LuaInteractionAnalyzer.Analyze(sources));
    }

    [TestMethod]
    [DataRow("local function Register()", "Register()", 1)]
    [DataRow("local function Register()", "Register()", 2)]
    [DataRow("function Module.Register()", "Module.Register()", 1)]
    [DataRow("function Module.Register()", "Module.Register()", 2)]
    [DataRow("function Module.Register()", "Module.Register()", 3)]
    public void EachHelperInvocationRetainsItsRegistration(string declaration, string invocation, int count)
    {
        string text = declaration + "\nOverride('PlayerPuppet', 'Value', function(self, wrapped) counter = counter + 1 return wrapped() end)\nend\n" + string.Join("\n", Enumerable.Repeat(invocation, count));
        LuaSource[] sources = [new("Alpha", "init.lua", text)];

        LuaCallbackEvidence[] callbacks = LuaCallbackEvidenceAnalyzer.Analyze(sources);

        Assert.HasCount(count, callbacks);
        Assert.HasCount(count, callbacks.Select(value => value.Line).Distinct().ToArray());
        if (count > 1)
        {
            LuaOverlap overlap = LuaInteractionAnalyzer.Analyze(sources).Single();
            Assert.AreEqual(LuaOverlapKind.OverrideReview, overlap.Kind);
            Assert.HasCount(count, overlap.Hooks);
        }
    }

    [TestMethod]
    public void SeparateRegistrationsInsideAnInvokedHelperKeepTheirSideEffects()
    {
        string registration = "Override('PlayerPuppet', 'Value', function(self, wrapped) counter = counter + 1 return wrapped() end)";
        string text = "function Module.Register()\n" + registration + "\n" + registration + "\nend\nModule.Register()\nModule.Register()";
        LuaSource[] sources = [new("Alpha", "init.lua", text)];

        Assert.HasCount(4, LuaCallbackEvidenceAnalyzer.Analyze(sources));
        LuaOverlap overlap = LuaInteractionAnalyzer.Analyze(sources).Single();
        Assert.HasCount(4, overlap.Hooks);
        Assert.AreEqual(LuaOverlapKind.OverrideReview, overlap.Kind);
    }

    [TestMethod]
    public void CallingOneExportedRootDoesNotEraseAnotherRootWithTheSameTarget()
    {
        string registration = "Override('PlayerPuppet', 'Value', function(self, wrapped) counter = counter + 1 return wrapped() end)";
        string text = "function Module.First()\n" + registration + "\nend\nfunction Module.Second()\n" + registration + "\nend\nModule.First()";

        Assert.HasCount(2, LuaCallbackEvidenceAnalyzer.Analyze([new("Alpha", "init.lua", text)]));
    }

    [TestMethod]
    [DataRow("if self.active then return end wrapped(self)", "Conditional")]
    [DataRow("if self.active then return 'custom' end return wrapped(self)", "Conditional")]
    [DataRow("if self.active then wrapped(self) end return", "Conditional")]
    [DataRow("return wrapped(self)", "Continues")]
    [DataRow("wrapped(self); return", "Continues")]
    [DataRow("return 1", "Missing")]
    [DataRow("local text = 'wrapped(self)'; return 1", "Missing")]
    public void ConditionalForwardingIsDistinctFromAbsentForwarding(string body, string expected)
    {
        LuaCallbackEvidence callback = LuaCallbackEvidenceAnalyzer.Analyze([new("Alpha", "init.lua", "Override('PlayerPuppet', 'Value', function(self, wrapped) " + body + " end)")]).Single();

        Assert.AreEqual(expected, callback.Continuation.ToString());
    }
}
