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
