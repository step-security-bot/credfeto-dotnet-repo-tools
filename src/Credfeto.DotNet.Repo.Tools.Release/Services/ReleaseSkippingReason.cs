using System.ComponentModel;

namespace Credfeto.DotNet.Repo.Tools.Release.Services;

internal enum ReleaseSkippingReason
{
    [Description("INSUFFICIENT UPDATES")]
    INSUFFICIENT_UPDATES,

    [Description("RELEASING NORMAL")]
    RELEASING_NORMAL,

    [Description("INSUFFICIENT DURATION SINCE LAST UPDATE")]
    INSUFFICIENT_DURATION_SINCE_LAST_UPDATE,

    [Description("RELEASING AFTER INACTIVITY")]
    RELEASING_AFTER_INACTIVITY,

    [Description("FOUND PENDING UPDATE BRANCHES")]
    FOUND_PENDING_UPDATE_BRANCHES,

    [Description("CONTAINS PUBLISHABLE EXECUTABLES")]
    CONTAINS_PUBLISHABLE_EXECUTABLES,

    [Description("EXPLICITLY PROHIBITED")]
    EXPLICITLY_PROHIBITED,

    [Description("FAILED RELEASE CHECK")]
    FAILED_RELEASE_CHECK,

    [Description("DOES NOT BUILD")]
    DOES_NOT_BUILD
}