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

namespace Launcher.Application.Accounts;

public sealed class LaunchAccountSessionException : Exception
{
    public LaunchAccountSessionFailureReason Reason { get; }

    public LaunchAccountSessionException()
        : this(LaunchAccountSessionFailureReason.Unknown, "The launch account session is unavailable.")
    {
    }

    public LaunchAccountSessionException(string message)
        : this(LaunchAccountSessionFailureReason.Unknown, message)
    {
    }

    public LaunchAccountSessionException(string message, Exception innerException)
        : this(LaunchAccountSessionFailureReason.Unknown, message, innerException)
    {
    }

    public LaunchAccountSessionException(
        LaunchAccountSessionFailureReason reason,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Reason = reason;
    }
}
