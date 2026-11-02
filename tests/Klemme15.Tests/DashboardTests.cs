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

using Klemme15.Telemetry;
using Xunit;

namespace Klemme15.Tests;

public class DashboardTests
{
    [Fact]
    public void The_phone_dashboard_is_built_and_embedded()
    {
        var html = TelemetryServer.Asset("index.html");
        Assert.NotNull(html);
        Assert.Contains("/dashboard.js", html!);
        var js = TelemetryServer.Asset("dashboard.js");
        Assert.NotNull(js);
        Assert.True(js!.Length > 20_000, "the React bundle looks too small: run 'npm run build' in web/dashboard");
        // It only reads: no endpoint in it changes anything on the car.
        Assert.Contains("/live.json", js);
        Assert.DoesNotContain("/clear", js);
    }
}
