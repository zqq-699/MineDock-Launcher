/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Reflection;
using Launcher.App.Services;
using Launcher.App.ViewModels.Settings;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Tests.ViewModels.Settings;

public sealed class InfoSettingsViewModelTests
{
    [Fact]
    public void ReferenceProjectsIncludeMultiplayerAttributions()
    {
        using var persistence = new SettingsPersistenceCoordinator(
            new TestSettingsService(new LauncherSettings()),
            Stub<IStatusService>(),
            NullLogger.Instance);
        var viewModel = new InfoSettingsViewModel(
            persistence,
            Stub<IStatusService>(),
            Stub<IFloatingMessageService>(),
            Stub<IExternalLinkService>(),
            Stub<ILauncherUpdateService>(),
            Stub<ILauncherSelfUpdateService>(),
            Stub<IApplicationExitService>());

        var project = Assert.Single(
            viewModel.ReferenceProjects,
            item => item.Url == TerracottaProjectMetadata.RepositoryUrl);

        Assert.Equal("Terracotta | 陶瓦联机", project.Name);
        Assert.Equal(TerracottaProjectMetadata.ReferencedVersion, project.Version);
        Assert.Equal("Copyright (c) Terracotta contributors", project.CopyrightNotice);
        Assert.Equal("AGPL-3.0-or-later License with exception", project.LicenseText);

        var easyTier = Assert.Single(
            viewModel.ReferenceProjects,
            item => item.Url == EasyTierProjectMetadata.RepositoryUrl);

        Assert.Equal("EasyTier", easyTier.Name);
        Assert.Equal(EasyTierProjectMetadata.ReferencedVersion, easyTier.Version);
        Assert.Equal("Copyright (c) EasyTier contributors", easyTier.CopyrightNotice);
        Assert.Equal("LGPL-3.0 License", easyTier.LicenseText);
    }

    private static T Stub<T>() where T : class =>
        DispatchProxy.Create<T, DefaultInterfaceProxy>();

    public class DefaultInterfaceProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var returnType = targetMethod?.ReturnType;
            if (returnType is null || returnType == typeof(void))
                return null;
            return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
        }
    }
}
