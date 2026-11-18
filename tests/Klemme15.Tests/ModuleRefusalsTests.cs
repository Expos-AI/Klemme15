// Klemme15: open-source BMW diagnostics
// Copyright (C) 2026 Kindric Castro
//
// This program is free software: you can redistribute it and/or modify it under the terms of the GNU General
// Public License as published by the Free Software Foundation, either version 3 of the License, or (at your
// option) any later version. This program is distributed in the hope that it will be useful, but WITHOUT ANY
// WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
// General Public License for more details. You should have received a copy of the GNU General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.
//
// SPDX-License-Identifier: GPL-3.0-or-later

using Klemme15.Core;
using Xunit;

namespace Klemme15.Tests;

public class ModuleRefusalsTests
{
    [Theory]
    [InlineData("vgsg90 STEUERN_FUNKTIONSPRUEFUNG: ERROR_ECU_GENERAL_REJECT", RefusalKind.GeneralReject)]
    [InlineData("vgsg90 STEUERN_FUNKTIONSPRUEFUNG: ERROR_ECU_CONDITIONS_NOT_CORRECT_OR_REQUEST_SEQUENCE_ERROR", RefusalKind.Conditions)]
    [InlineData("x: ERROR_ECU_SERVICE_NOT_SUPPORTED", RefusalKind.NotSupported)]
    [InlineData("x: ERROR_ECU_SUBFUNCTION_NOT_SUPPORTED_INVALID_FORMAT", RefusalKind.NotSupported)]
    [InlineData("x: ERROR_ECU_BUSY_REPEAT_REQUEST", RefusalKind.Busy)]
    [InlineData("x: ERROR_ECU_REQUEST_OUT_OF_RANGE", RefusalKind.OutOfRange)]
    [InlineData("x: ERROR_ECU_SECURITY_ACCESS_DENIED_REQUESTED", RefusalKind.Security)]
    [InlineData("D_VGSG IDENT: EDIABAS_IFH_0009: NO RESPONSE FROM CONTROLUNIT", RefusalKind.NoAnswer)]
    [InlineData("SGBD not found", RefusalKind.None)]
    [InlineData(null, RefusalKind.None)]
    public void EdiabasErrorsAreRecognised(string? message, RefusalKind kind) => Assert.Equal(kind, ModuleRefusals.Classify(message));

    [Fact]
    public void EveryRefusalHasPlainWordsAndOnlyStateProblemsAskForFaults()
    {
        foreach (var kind in Enum.GetValues<RefusalKind>().Where(k => k != RefusalKind.None))
        {
            var text = ModuleRefusals.Explain(kind, "transfer case");
            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.Contains("transfer case", text);
            Assert.DoesNotContain("ERROR_", text);
        }
        Assert.Null(ModuleRefusals.Explain(RefusalKind.None, "x"));
        Assert.True(ModuleRefusals.StateRelated(RefusalKind.GeneralReject));
        Assert.True(ModuleRefusals.StateRelated(RefusalKind.Conditions));
        Assert.False(ModuleRefusals.StateRelated(RefusalKind.NotSupported));
        Assert.Equal(RefusalKind.GeneralReject, ModuleRefusals.FromCode(0x10));
        Assert.Equal(RefusalKind.Conditions, ModuleRefusals.FromCode(0x22));
        Assert.Equal(RefusalKind.None, ModuleRefusals.FromCode(0x99));
    }

    [Fact]
    public void APartialEdiabasQuickTestSaysWhatToDo()
    {
        var one = CableCheck.EdiabasPartialHelp(answered: 1, engineAnswered: false, missingFiles: 6);
        Assert.NotNull(one);
        Assert.Contains("only one module", one);
        Assert.Contains("no file for 6", one);
        Assert.Contains("untick", one);
        Assert.Null(CableCheck.EdiabasPartialHelp(answered: 12, engineAnswered: true, missingFiles: 0));
        Assert.Contains("no file for 2", CableCheck.EdiabasPartialHelp(answered: 10, engineAnswered: true, missingFiles: 2));
        Assert.DoesNotContain("no file", CableCheck.EdiabasPartialHelp(answered: 0, engineAnswered: false, missingFiles: 0));
    }
}
