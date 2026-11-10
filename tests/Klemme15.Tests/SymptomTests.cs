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
using Klemme15.Data;
using Xunit;

namespace Klemme15.Tests;

public class SymptomTests
{
    [Fact]
    public void EverySymptomStepPointsAtSomethingThatExists()
    {
        var db = VehicleDatabase.Load(Path.Combine(Path.GetTempPath(), "klemme15-no-overrides"));
        var symptoms = SymptomLibrary.Load();
        Assert.True(symptoms.Count >= 12);
        Assert.Equal(symptoms.Count, symptoms.Select(s => s.Id).Distinct().Count());
        foreach (var s in symptoms)
        {
            Assert.InRange(s.Steps.Count, 2, 8);
            foreach (var step in s.Steps)
            {
                var exists = step.Kind switch
                {
                    "test" => GuidedTests.All.Any(t => t.Id == step.Id),
                    "chain" => db.Chains.Any(c => c.Info.Id == step.Id),
                    "guide" => db.Guides.All.Any(g => g.Id == step.Id),
                    "check" => db.Checks.All.Any(g => g.Id == step.Id),
                    "page" => SymptomLibrary.Pages.Contains(step.Id),
                    _ => false,
                };
                Assert.True(exists, $"{s.Id}: no {step.Kind} called {step.Id}");
                Assert.True(step.Why.Length > 15, $"{s.Id}/{step.Id} needs a reason");
            }
        }
    }
}
