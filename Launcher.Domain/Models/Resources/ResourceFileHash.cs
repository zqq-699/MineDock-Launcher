/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

namespace Launcher.Domain.Models;

public enum ResourceFileHashAlgorithm
{
    Sha512,
    Sha1,
    Md5
}

public sealed record ResourceFileHash(ResourceFileHashAlgorithm Algorithm, string Value);
