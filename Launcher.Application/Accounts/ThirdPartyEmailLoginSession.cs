/* BlockHelm Launcher - SPDX-License-Identifier: GPL-3.0-only */
namespace Launcher.Application.Accounts;

public sealed record ThirdPartyEmailLoginSession(
    string AttemptId,
    IReadOnlyList<ThirdPartyProfileOption> Profiles);

public sealed record ThirdPartyProfileOption(
    string Uuid,
    string Name,
    string AvatarSource);
