using ConflictStudio.App;
using ConflictStudio.Core;
using System.IO;

namespace ConflictStudio.App.Tests;

[TestClass]
public sealed class ArchiveOrderWorkspaceViewModelTests
{
    private static readonly string[] ReversedOrder = ["zeta.archive", "Alpha.archive"];
    private static readonly string[] ExistingManagedOrder = ["Beta.archive", "Alpha.archive"];
    private static readonly string[] DirectOrder = ["Gamma.archive", "Alpha.archive", "Beta.archive"];
    private static readonly string[] InitialOrder = ["Alpha.archive", "zeta.archive"];
    private static readonly string[] ProfileInitialOrder = ["Alpha.archive", "Beta.archive"];

    [TestMethod]
    public void MoveChangesTheProposedArchiveOrder()
    {
        ArchiveOrderWorkspaceViewModel viewModel = new();
        string[] order = ["Alpha.archive", "zeta.archive"];
        viewModel.SetProposedOrder(order);

        viewModel.Move(1, -1);

        CollectionAssert.AreEqual(ReversedOrder, viewModel.ProposedOrder.ToArray());
    }

    [TestMethod]
    public void MoveToSupportsDirectArchiveReordering()
    {
        ArchiveOrderWorkspaceViewModel viewModel = new();
        viewModel.SetProposedOrder(["Alpha.archive", "Beta.archive", "Gamma.archive"]);

        viewModel.MoveTo(2, 0);

        CollectionAssert.AreEqual(DirectOrder, viewModel.ProposedOrder.ToArray());
    }

    [TestMethod]
    public void MoveManyPreservesSelectionOrderAsOneBlock()
    {
        ArchiveOrderWorkspaceViewModel viewModel = new();
        viewModel.SetProposedOrder(["A.archive", "B.archive", "C.archive", "D.archive", "E.archive"]);

        viewModel.MoveMany(["B.archive", "D.archive"], "E.archive");

        string[] expected = ["A.archive", "C.archive", "E.archive", "B.archive", "D.archive"];
        CollectionAssert.AreEqual(expected, viewModel.ProposedOrder.ToArray());
    }

    [TestMethod]
    public void MoveManyCanMoveAnAdjacentBlockDownward()
    {
        ArchiveOrderWorkspaceViewModel viewModel = new();
        viewModel.SetProposedOrder(["A.archive", "B.archive", "C.archive", "D.archive"]);

        viewModel.MoveMany(["B.archive", "C.archive"], "D.archive");

        string[] expected = ["A.archive", "D.archive", "B.archive", "C.archive"];
        CollectionAssert.AreEqual(expected, viewModel.ProposedOrder.ToArray());
    }

    [TestMethod]
    public void MoveManyCanMoveABlockUpward()
    {
        ArchiveOrderWorkspaceViewModel viewModel = new();
        viewModel.SetProposedOrder(["A.archive", "B.archive", "C.archive", "D.archive"]);

        viewModel.MoveMany(["C.archive", "D.archive"], "A.archive");

        string[] expected = ["C.archive", "D.archive", "A.archive", "B.archive"];
        CollectionAssert.AreEqual(expected, viewModel.ProposedOrder.ToArray());
    }

    [TestMethod]
    public void PreviewClosesWhenTheProposedOrderReturnsToBaseline()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-preview-baseline-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string alphaPath = Path.Combine(root, "Alpha.archive");
            string betaPath = Path.Combine(root, "Beta.archive");
            File.WriteAllText(alphaPath, "alpha");
            File.WriteAllText(betaPath, "beta");
            Mo2ArchiveProfile profile = new("Standard", Path.Combine(root, "profile-modlist.txt"), [new Mo2Archive("Alpha", "Alpha.archive", alphaPath, 5, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(alphaPath)))), new Mo2Archive("Beta", "Beta.archive", betaPath, 4, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(betaPath))))], ["Alpha.archive", "Beta.archive"]);
            ArchiveOrderWorkspaceViewModel viewModel = new();
            viewModel.LoadProfile(profile, new Mo2ArchiveWriteTarget(Path.Combine(root, "modlist.txt"), "Test"), root);
            viewModel.SetResourceProviders(ProfileInstallationIdentity.Create(root), "Standard", []);
            viewModel.MoveMany(["Beta.archive"], "Alpha.archive");
            viewModel.PreviewOrder();
            Assert.IsTrue(viewModel.CanApply);

            viewModel.MoveMany(["Beta.archive"], "Alpha.archive");
            viewModel.PreviewOrder();

            Assert.IsFalse(viewModel.CanReset);
            Assert.IsFalse(viewModel.CanApply);
            Assert.HasCount(0, viewModel.WinnerDeltas);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ProfileApplyVerifiesManagedTargetWithoutRescanningOverwriteAsArchives()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-profile-apply-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            string alphaPath = Path.Combine(root, "Alpha.archive");
            string betaPath = Path.Combine(root, "Beta.archive");
            File.WriteAllText(alphaPath, "alpha");
            File.WriteAllText(betaPath, "beta");
            string alphaHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(alphaPath)));
            string betaHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(betaPath)));
            Mo2ArchiveProfile profile = new("Standard", Path.Combine(root, "profiles", "Standard", "modlist.txt"), [new Mo2Archive("Alpha", "Alpha.archive", alphaPath, 5, alphaHash), new Mo2Archive("Beta", "Beta.archive", betaPath, 4, betaHash)], ["Alpha.archive", "Beta.archive"]);
            Mo2ArchiveWriteTarget target = Mo2ArchiveWriteTargetResolver.Resolve(root);
            ArchiveOrderWorkspaceViewModel viewModel = new(() => new ArchiveOrderWriter(() => DateTimeOffset.UtcNow, () => []));
            string modsRoot = Path.Combine(root, "mods");
            Directory.CreateDirectory(Path.Combine(modsRoot, "Alpha", "archive", "pc", "mod"));
            Directory.CreateDirectory(Path.Combine(modsRoot, "Beta", "archive", "pc", "mod"));
            string alphaProfilePath = Path.Combine(modsRoot, "Alpha", "archive", "pc", "mod", "Alpha.archive");
            string betaProfilePath = Path.Combine(modsRoot, "Beta", "archive", "pc", "mod", "Beta.archive");
            File.Copy(alphaPath, alphaProfilePath);
            File.Copy(betaPath, betaProfilePath);
            Directory.CreateDirectory(Path.GetDirectoryName(profile.ProfileModlistPath)!);
            File.WriteAllText(profile.ProfileModlistPath, "+Alpha\n+Beta\n");
            profile = Mo2ArchiveProfileScanner.Scan(modsRoot, profile.ProfileModlistPath);
            viewModel.LoadProfile(profile, target, root);
            viewModel.SetResourceProviders(ProfileInstallationIdentity.Create(root), "Standard", []);
            viewModel.Move(1, -1);
            viewModel.PreviewOrder();

            viewModel.ApplyOrder();

            Assert.AreEqual("Beta.archive\r\nAlpha.archive\r\n", File.ReadAllText(target.ModlistPath));
            Assert.IsFalse(viewModel.CanApply);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ManualApplyWritesAndVerifiesAnIncompleteRepairDraft()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-manual-repair-" + Guid.NewGuid().ToString("N"));
        try
        {
            string archiveRoot = Path.Combine(root, "archive", "pc", "mod");
            Directory.CreateDirectory(archiveRoot);
            File.WriteAllText(Path.Combine(archiveRoot, "Alpha.archive"), "alpha");
            File.WriteAllText(Path.Combine(archiveRoot, "Beta.archive"), "beta");
            string orderPath = Path.Combine(archiveRoot, "modlist.txt");
            File.WriteAllText(orderPath, "Beta.archive\r\nBeta.archive\r\nstale.archive\r\n");
            Mo2ArchiveProfile profile = ManualArchiveProfileScanner.Scan(root);
            Mo2ArchiveWriteTarget target = new(orderPath, "Game directory", ModManagerKind.Manual);
            ArchiveOrderWorkspaceViewModel viewModel = new(() => new ArchiveOrderWriter(() => DateTimeOffset.UtcNow, () => []));
            viewModel.LoadProfile(profile, target, root, () => ManualArchiveProfileScanner.Scan(root), ProfileInstallationIdentity.Create("Manual", root));
            viewModel.SetResourceProviders(ProfileInstallationIdentity.Create("Manual", root), profile.ProfileName, []);

            viewModel.PreviewOrder();
            Assert.IsTrue(viewModel.CanApply);
            viewModel.ApplyOrder();

            Assert.AreEqual("Beta.archive\r\nAlpha.archive\r\n", File.ReadAllText(orderPath));
            Assert.IsFalse(viewModel.CanApply);
            StringAssert.Contains(viewModel.PreviewStatus, "written and verified");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void Mo2ApplyWritesAndVerifiesAnIncompleteRepairDraftInOverwrite()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-mo2-repair-" + Guid.NewGuid().ToString("N"));
        try
        {
            string modsRoot = Path.Combine(root, "mods");
            string highRoot = Path.Combine(modsRoot, "High", "archive", "pc", "mod");
            string lowRoot = Path.Combine(modsRoot, "Low", "archive", "pc", "mod");
            Directory.CreateDirectory(highRoot);
            Directory.CreateDirectory(lowRoot);
            File.WriteAllText(Path.Combine(highRoot, "Alpha.archive"), "alpha");
            File.WriteAllText(Path.Combine(lowRoot, "Beta.archive"), "beta");
            string originalOrderPath = Path.Combine(highRoot, "modlist.txt");
            File.WriteAllText(originalOrderPath, "Beta.archive\r\nBeta.archive\r\nstale.archive\r\n");
            string profilePath = Path.Combine(root, "profiles", "Standard", "modlist.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(profilePath)!);
            File.WriteAllText(profilePath, "+High\r\n+Low\r\n");
            Mo2ArchiveProfile profile = Mo2ArchiveProfileScanner.ScanInstance(root, profilePath);
            Mo2ArchiveWriteTarget target = Mo2ArchiveWriteTargetResolver.Resolve(root, profile.OrderEvidence);
            ArchiveOrderWorkspaceViewModel viewModel = new(() => new ArchiveOrderWriter(() => DateTimeOffset.UtcNow, () => []));
            viewModel.LoadProfile(profile, target, root, () => Mo2ArchiveProfileScanner.ScanInstance(root, profilePath, null, true), ProfileInstallationIdentity.Create(root));
            viewModel.SetResourceProviders(ProfileInstallationIdentity.Create(root), profile.ProfileName, []);

            viewModel.PreviewOrder();
            Assert.IsTrue(viewModel.CanApply);
            viewModel.ApplyOrder();

            Assert.AreEqual("Beta.archive\r\nAlpha.archive\r\n", File.ReadAllText(target.ModlistPath));
            Assert.AreEqual("Beta.archive\r\nBeta.archive\r\nstale.archive\r\n", File.ReadAllText(originalOrderPath));
            Assert.IsFalse(viewModel.CanApply);
            StringAssert.Contains(viewModel.PreviewStatus, "written and verified");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void VortexApplyWritesAndVerifiesAnIncompleteRepairDraftThroughTheBridge()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-vortex-repair-apply-" + Guid.NewGuid().ToString("N"));
        try
        {
            string game = Path.Combine(root, "game");
            string staging = Path.Combine(root, "staging");
            string providerRoot = Path.Combine(staging, "Provider");
            string providerArchiveRoot = Path.Combine(providerRoot, "archive", "pc", "mod");
            string gameArchiveRoot = Path.Combine(game, "archive", "pc", "mod");
            Directory.CreateDirectory(providerArchiveRoot);
            Directory.CreateDirectory(gameArchiveRoot);
            File.WriteAllText(Path.Combine(providerArchiveRoot, "Alpha.archive"), "alpha");
            File.WriteAllText(Path.Combine(providerArchiveRoot, "Beta.archive"), "beta");
            string orderPath = Path.Combine(gameArchiveRoot, "modlist.txt");
            File.WriteAllText(orderPath, "Beta.archive\r\nBeta.archive\r\nstale.archive\r\n");
            string contextPath = Path.Combine(root, "context.json");
            DateTimeOffset now = DateTimeOffset.UtcNow;
            byte[] originalOrder = File.ReadAllBytes(orderPath);
            string orderHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(orderPath)));
            VortexManagerContext context = new(1, new string('a', 64), now, "profile", "Standard", game, staging, true, [new("provider", "Provider", providerRoot, 0)], [], [], orderHash);
            File.WriteAllText(contextPath, System.Text.Json.JsonSerializer.Serialize(context));
            Mo2ArchiveProfile profile = VortexArchiveProfileScanner.Scan(context);
            Mo2ArchiveWriteTarget target = VortexArchiveWriteTargetResolver.Resolve(contextPath, context);
            int exchanges = 0;
            ArchiveOrderWorkspaceViewModel viewModel = new(_ => new VortexArchiveOrderWriter(context, request =>
            {
                exchanges++;
                string backupPath = orderPath + "." + request.RequestId + ".bak";
                File.Copy(orderPath, backupPath);
                if (request.RestorePrevious) File.Copy(request.RestoreBackupPath!, orderPath, true);
                else File.WriteAllBytes(orderPath, ArchiveOrderText.Merge(File.ReadAllBytes(orderPath), request.ProposedOrder));
                byte[] written = File.ReadAllBytes(orderPath);
                string writtenHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(written));
                string[] writtenOrder = ArchiveOrderText.ArchiveEntries(File.ReadAllLines(orderPath));
                string contextId = new string(exchanges == 1 ? 'b' : 'c', 64);
                VortexManagerContext refreshed = context with { ContextId = contextId, CapturedAtUtc = now, ArchiveOrder = writtenOrder, ArchiveOrderSha256 = writtenHash };
                File.WriteAllText(contextPath, System.Text.Json.JsonSerializer.Serialize(refreshed));
                return new VortexOrderResponse(1, request.RequestId, true, "Applied", backupPath, writtenHash, now, contextId);
            }, () => now, () => []), (_, _) => profile);
            string installationId = ProfileInstallationIdentity.Create("Vortex", game + "|profile");
            viewModel.LoadProfile(profile, target, staging, () => VortexArchiveProfileScanner.Scan(VortexManagerContextStore.Read(contextPath)), installationId);
            viewModel.SetResourceProviders(installationId, profile.ProfileName, []);

            viewModel.PreviewOrder();
            Assert.IsTrue(viewModel.CanApply);
            viewModel.ApplyOrder();

            Assert.AreEqual("Beta.archive\r\nAlpha.archive\r\n", File.ReadAllText(orderPath));
            Assert.IsFalse(viewModel.CanApply);
            StringAssert.Contains(viewModel.PreviewStatus, "written and verified");
            viewModel.UndoLastApply();
            CollectionAssert.AreEqual(originalOrder, File.ReadAllBytes(orderPath));
            Assert.AreEqual(2, exchanges);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ManagedApplyRefreshesArchiveFingerprintsOnlyOnce()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-profile-rollback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string alphaPath = Path.Combine(root, "Alpha.archive");
            string betaPath = Path.Combine(root, "Beta.archive");
            File.WriteAllText(alphaPath, "alpha");
            File.WriteAllText(betaPath, "beta");
            string alphaHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(alphaPath)));
            string betaHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(betaPath)));
            Mo2ArchiveProfile profile = new("Standard", Path.Combine(root, "profile-modlist.txt"), [new Mo2Archive("Alpha", "Alpha.archive", alphaPath, 5, alphaHash), new Mo2Archive("Beta", "Beta.archive", betaPath, 4, betaHash)], ["Alpha.archive", "Beta.archive"]);
            Mo2ArchiveWriteTarget target = Mo2ArchiveWriteTargetResolver.Resolve(root);
            int scanCount = 0;
            ArchiveOrderWorkspaceViewModel viewModel = new(() => new ArchiveOrderWriter(() => DateTimeOffset.UtcNow, () => []), (_, _) => { scanCount++; return profile; });
            viewModel.LoadProfile(profile, target, root);
            viewModel.SetResourceProviders(ProfileInstallationIdentity.Create(root), "Standard", [new ResourceProvider("Alpha.archive", 1, "base\\shared.mesh", new string('a', 64)), new ResourceProvider("Beta.archive", 1, "base\\shared.mesh", new string('b', 64))]);
            viewModel.Move(1, -1);
            viewModel.PreviewOrder();
            Assert.AreEqual(1, viewModel.WinnerDeltas.Count);

            viewModel.ApplyOrder();

            Assert.AreEqual(1, scanCount);
            CollectionAssert.AreEqual(ExistingManagedOrder, viewModel.ProposedOrder.ToArray());
            Assert.IsFalse(viewModel.CanApply);
            Assert.IsFalse(viewModel.CanReset);
            Assert.AreEqual(0, viewModel.WinnerDeltas.Count);
            StringAssert.Contains(viewModel.PreviewStatus, "written and verified");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ManualApplyRejectsAnArchiveChangedAfterPreview()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-manual-inventory-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string alphaPath = Path.Combine(root, "Alpha.archive");
            string betaPath = Path.Combine(root, "Beta.archive");
            File.WriteAllText(alphaPath, "alpha");
            File.WriteAllText(betaPath, "beta");
            string alphaHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(alphaPath)));
            string betaHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(betaPath)));
            Mo2ArchiveProfile profile = new("Deployed game", Path.Combine(root, "modlist.txt"), [new Mo2Archive("Game directory", "Alpha.archive", alphaPath, 5, alphaHash), new Mo2Archive("Game directory", "Beta.archive", betaPath, 4, betaHash)], ProfileInitialOrder);
            Mo2ArchiveWriteTarget target = new(profile.ProfileModlistPath, "Game directory", ModManagerKind.Manual);
            ArchiveOrderWorkspaceViewModel viewModel = new(() => new ArchiveOrderWriter(() => DateTimeOffset.UtcNow, () => []));
            viewModel.LoadProfile(profile, target, root, () => profile, ProfileInstallationIdentity.Create("Manual", root));
            viewModel.SetResourceProviders(ProfileInstallationIdentity.Create("Manual", root), profile.ProfileName, []);
            viewModel.Move(1, -1);
            viewModel.PreviewOrder();
            File.WriteAllText(Path.Combine(root, "Gamma.archive"), "gamma");

            Assert.ThrowsExactly<ArchiveOrderException>(viewModel.ApplyOrder);
            Assert.IsFalse(File.Exists(profile.ProfileModlistPath));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ProfileLoadUsesExistingManagedOrderAsThePreviewBaseline()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-profile-existing-" + Guid.NewGuid().ToString("N"));
        try
        {
            string alphaPath = Path.Combine(root, "Alpha.archive");
            string betaPath = Path.Combine(root, "Beta.archive");
            Directory.CreateDirectory(root);
            File.WriteAllText(alphaPath, "alpha");
            File.WriteAllText(betaPath, "beta");
            Mo2ArchiveProfile profile = new("Standard", Path.Combine(root, "profiles", "Standard", "modlist.txt"), [new Mo2Archive("Alpha", "Alpha.archive", alphaPath, 5, new string('a', 64)), new Mo2Archive("Beta", "Beta.archive", betaPath, 4, new string('b', 64))], ["Alpha.archive", "Beta.archive"]);
            Mo2ArchiveWriteTarget target = Mo2ArchiveWriteTargetResolver.Resolve(root);
            Directory.CreateDirectory(Path.GetDirectoryName(target.ModlistPath)!);
            File.WriteAllText(target.ModlistPath, "Beta.archive\r\nAlpha.archive\r\n");
            ArchiveOrderWorkspaceViewModel viewModel = new();

            viewModel.LoadProfile(profile, target, Path.Combine(root, "mods"));

            CollectionAssert.AreEqual(ExistingManagedOrder, viewModel.ProposedOrder.ToArray());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void PreviewExplainsWhenManagerNativeEnforcementOwnsTheOrder()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-enforced-preview-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            Mo2ArchiveProfile profile = new("Standard", Path.Combine(root, "profile.txt"), [new Mo2Archive("Alpha", "Alpha.archive", Path.Combine(root, "Alpha.archive"), 5, new string('a', 64)), new Mo2Archive("Beta", "Beta.archive", Path.Combine(root, "Beta.archive"), 4, new string('b', 64))], ["Alpha.archive", "Beta.archive"]);
            File.WriteAllText(profile.Archives[0].PhysicalPath, "alpha");
            File.WriteAllText(profile.Archives[1].PhysicalPath, "beta");
            Mo2ArchiveWriteTarget target = new(Path.Combine(root, "modlist.txt"), "MO2", ModManagerKind.Mo2, null, "Disable enforce archive load order first.");
            ArchiveOrderWorkspaceViewModel viewModel = new(() => new ArchiveOrderWriter(() => DateTimeOffset.UtcNow, () => []));
            viewModel.LoadProfile(profile, target, root);
            viewModel.SetResourceProviders(ProfileInstallationIdentity.Create(root), "Standard", []);
            viewModel.Move(1, -1);

            viewModel.PreviewOrder();

            Assert.IsFalse(viewModel.CanApply);
            StringAssert.Contains(viewModel.PreviewStatus, "Disable enforce archive load order");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ManagedReloadPreservesUndoForTheSameProfileAndTarget()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-profile-undo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string alphaPath = Path.Combine(root, "Alpha.archive");
            string betaPath = Path.Combine(root, "Beta.archive");
            File.WriteAllText(alphaPath, "alpha");
            File.WriteAllText(betaPath, "beta");
            string alphaHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(alphaPath)));
            string betaHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(betaPath)));
            Mo2ArchiveProfile profile = new("Standard", Path.Combine(root, "profile.txt"), [new Mo2Archive("Alpha", "Alpha.archive", alphaPath, 5, alphaHash), new Mo2Archive("Beta", "Beta.archive", betaPath, 4, betaHash)], ProfileInitialOrder);
            Mo2ArchiveWriteTarget target = Mo2ArchiveWriteTargetResolver.Resolve(root);
            Func<Mo2ArchiveProfile> refresh = () => profile with { EffectiveOrder = File.Exists(target.ModlistPath) ? ArchiveOrderText.ArchiveEntries(File.ReadAllLines(target.ModlistPath)) : ProfileInitialOrder };
            ArchiveOrderWorkspaceViewModel viewModel = new(() => new ArchiveOrderWriter(() => DateTimeOffset.UtcNow, () => []), (_, _) => refresh());
            viewModel.LoadProfile(profile, target, root, refresh, ProfileInstallationIdentity.Create(root));
            viewModel.SetResourceProviders(ProfileInstallationIdentity.Create(root), "Standard", []);
            viewModel.SetProposedOrder(ExistingManagedOrder);
            viewModel.PreviewOrder();
            viewModel.ApplyOrder();

            viewModel.LoadProfile(refresh(), target, root, refresh, ProfileInstallationIdentity.Create(root), true);

            Assert.IsTrue(viewModel.CanUndo);
            viewModel.UndoLastApply();
            Assert.IsFalse(File.Exists(target.ModlistPath));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ApplyRejectsWhenMo2EnforcementIsEnabledAfterPreview()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-profile-enforcement-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string alphaPath = Path.Combine(root, "Alpha.archive");
            string betaPath = Path.Combine(root, "Beta.archive");
            File.WriteAllText(alphaPath, "alpha");
            File.WriteAllText(betaPath, "beta");
            string alphaHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(alphaPath)));
            string betaHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(betaPath)));
            Mo2ArchiveProfile profile = new("Standard", Path.Combine(root, "profile.txt"), [new Mo2Archive("Alpha", "Alpha.archive", alphaPath, 5, alphaHash), new Mo2Archive("Beta", "Beta.archive", betaPath, 4, betaHash)], ProfileInitialOrder);
            Mo2ArchiveWriteTarget target = Mo2ArchiveWriteTargetResolver.Resolve(root);
            ArchiveOrderWorkspaceViewModel viewModel = new(() => new ArchiveOrderWriter(() => DateTimeOffset.UtcNow, () => []), (_, _) => profile);
            viewModel.LoadProfile(profile, target, root, () => profile, ProfileInstallationIdentity.Create(root));
            viewModel.SetResourceProviders(ProfileInstallationIdentity.Create(root), "Standard", []);
            viewModel.SetProposedOrder(ExistingManagedOrder);
            viewModel.PreviewOrder();
            File.WriteAllText(Path.Combine(root, "ModOrganizer.ini"), "Cyberpunk%202077%20Support%20Plugin\\enforce_archive_load_order=true");

            ArchiveOrderException exception = Assert.ThrowsExactly<ArchiveOrderException>(viewModel.ApplyOrder);

            StringAssert.Contains(exception.Message, "enforce archive load order");
            Assert.IsFalse(File.Exists(target.ModlistPath));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ApplyRejectsWhenTheActiveOrderProviderChangesAfterPreview()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-profile-owner-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string alphaPath = Path.Combine(root, "Alpha.archive");
            string betaPath = Path.Combine(root, "Beta.archive");
            File.WriteAllText(alphaPath, "alpha");
            File.WriteAllText(betaPath, "beta");
            string alphaHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(alphaPath)));
            string betaHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(betaPath)));
            Mo2ArchiveProfile profile = new("Standard", Path.Combine(root, "profile.txt"), [new Mo2Archive("Alpha", "Alpha.archive", alphaPath, 5, alphaHash), new Mo2Archive("Beta", "Beta.archive", betaPath, 4, betaHash)], ProfileInitialOrder);
            Mo2ArchiveProfile current = profile;
            Mo2ArchiveWriteTarget target = Mo2ArchiveWriteTargetResolver.Resolve(root);
            ArchiveOrderWorkspaceViewModel viewModel = new(() => new ArchiveOrderWriter(() => DateTimeOffset.UtcNow, () => []), (_, _) => current);
            viewModel.LoadProfile(profile, target, root, () => current, ProfileInstallationIdentity.Create(root));
            viewModel.SetResourceProviders(ProfileInstallationIdentity.Create(root), "Standard", []);
            viewModel.SetProposedOrder(ExistingManagedOrder);
            viewModel.PreviewOrder();
            string newOwner = Path.Combine(root, "mods", "Settings", "archive", "pc", "mod", "modlist.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(newOwner)!);
            File.WriteAllText(newOwner, "Alpha.archive\nBeta.archive\n");
            current = profile with { OrderEvidence = new ArchiveOrderEvidence(ArchiveOrderEvidenceKind.ManagedModlist, "Settings", newOwner, "New owner") };

            ArchiveOrderException exception = Assert.ThrowsExactly<ArchiveOrderException>(viewModel.ApplyOrder);

            StringAssert.Contains(exception.Message, "owner changed");
            Assert.IsFalse(File.Exists(target.ModlistPath));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void VortexApplyRejectsAProfileContextChangeWithTheSameArchiveInventory()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-vortex-profile-change-" + Guid.NewGuid().ToString("N"));
        try
        {
            string game = Path.Combine(root, "game");
            string staging = Path.Combine(root, "staging");
            Directory.CreateDirectory(staging);
            Directory.CreateDirectory(Path.Combine(game, "archive", "pc", "mod"));
            File.WriteAllText(Path.Combine(game, "archive", "pc", "mod", "Alpha.archive"), "alpha");
            File.WriteAllText(Path.Combine(game, "archive", "pc", "mod", "Beta.archive"), "beta");
            string contextPath = Path.Combine(root, "context.json");
            VortexManagerContext context = new(1, new string('a', 64), DateTimeOffset.UtcNow, "profile-a", "Standard", game, staging, true, [], [], ProfileInitialOrder, null);
            File.WriteAllText(contextPath, System.Text.Json.JsonSerializer.Serialize(context));
            Mo2ArchiveProfile profile = VortexArchiveProfileScanner.Scan(context);
            Mo2ArchiveWriteTarget target = VortexArchiveWriteTargetResolver.Resolve(contextPath, context);
            ArchiveOrderWorkspaceViewModel viewModel = new(_ => throw new AssertFailedException("The writer must not be created for a changed Vortex profile."), (_, _) => profile);
            viewModel.LoadProfile(profile, target, staging, () => VortexArchiveProfileScanner.Scan(VortexManagerContextStore.Read(contextPath)), ProfileInstallationIdentity.Create("Vortex", game + "|profile-a"));
            viewModel.SetResourceProviders(ProfileInstallationIdentity.Create("Vortex", game + "|profile-a"), "Standard", []);
            viewModel.SetProposedOrder(ExistingManagedOrder);
            viewModel.PreviewOrder();
            File.WriteAllText(contextPath, System.Text.Json.JsonSerializer.Serialize(context with { ContextId = new string('b', 64), ProfileId = "profile-b" }));

            ArchiveOrderException exception = Assert.ThrowsExactly<ArchiveOrderException>(viewModel.ApplyOrder);

            StringAssert.Contains(exception.Message, "owner changed");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
