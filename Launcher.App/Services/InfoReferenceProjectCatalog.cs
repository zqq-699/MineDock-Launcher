/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using Launcher.App.Models;
using Microsoft.Extensions.Logging;

namespace Launcher.App.Services;

public interface IInfoReferenceProjectCatalog
{
    IReadOnlyList<InfoReferenceProjectItem> GetProjects();
}

public sealed class EmbeddedInfoReferenceProjectCatalog : IInfoReferenceProjectCatalog
{
    internal const string ResourceName = "Launcher.App.Resources.ReferenceProjects.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false
    };

    private readonly Func<Stream?> resourceStreamFactory;
    private readonly ILogger<EmbeddedInfoReferenceProjectCatalog> logger;
    private readonly Lazy<IReadOnlyList<InfoReferenceProjectItem>> projects;

    public EmbeddedInfoReferenceProjectCatalog(ILogger<EmbeddedInfoReferenceProjectCatalog> logger)
        : this(
            () => typeof(EmbeddedInfoReferenceProjectCatalog).Assembly.GetManifestResourceStream(ResourceName),
            logger)
    {
    }

    internal EmbeddedInfoReferenceProjectCatalog(
        Func<Stream?> resourceStreamFactory,
        ILogger<EmbeddedInfoReferenceProjectCatalog> logger)
    {
        this.resourceStreamFactory = resourceStreamFactory;
        this.logger = logger;
        projects = new Lazy<IReadOnlyList<InfoReferenceProjectItem>>(
            LoadProjects,
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public IReadOnlyList<InfoReferenceProjectItem> GetProjects() => projects.Value;

    private IReadOnlyList<InfoReferenceProjectItem> LoadProjects()
    {
        try
        {
            using var stream = resourceStreamFactory()
                ?? throw new InvalidDataException($"Embedded resource '{ResourceName}' was not found.");
            var parsedProjects = JsonSerializer.Deserialize<InfoReferenceProjectItem?[]>(stream, JsonOptions)
                ?? throw new InvalidDataException("The reference-project catalog does not contain a JSON array.");

            var normalizedProjects = parsedProjects
                .Select(NormalizeAndValidate)
                .ToArray();
            var duplicateName = normalizedProjects
                .GroupBy(project => project.Name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(group => group.Count() > 1)
                ?.Key;
            if (duplicateName is not null)
                throw new InvalidDataException($"The reference-project catalog contains duplicate name '{duplicateName}'.");

            logger.LogDebug(
                "Loaded embedded reference-project catalog. ProjectCount={ProjectCount}",
                normalizedProjects.Length);
            return new ReadOnlyCollection<InfoReferenceProjectItem>(normalizedProjects);
        }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException)
        {
            logger.LogError(ex, "Failed to load the embedded reference-project catalog.");
            return Array.Empty<InfoReferenceProjectItem>();
        }
    }

    private static InfoReferenceProjectItem NormalizeAndValidate(InfoReferenceProjectItem? project)
    {
        if (project is null)
            throw new InvalidDataException("The reference-project catalog contains a null entry.");

        var normalized = new InfoReferenceProjectItem(
            RequireValue(project.Name, "name"),
            RequireValue(project.CopyrightNotice, "copyrightNotice"),
            RequireValue(project.ProjectUrl, "projectUrl"),
            RequireValue(project.LicenseText, "licenseText"));

        if (!Uri.TryCreate(normalized.ProjectUrl, UriKind.Absolute, out var projectUri)
            || projectUri.Scheme is not ("http" or "https"))
        {
            throw new InvalidDataException(
                $"Reference project '{normalized.Name}' has an invalid HTTP(S) project URL.");
        }

        return normalized;
    }

    private static string RequireValue(string? value, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"Reference-project property '{propertyName}' is required.");

        return value.Trim();
    }
}
