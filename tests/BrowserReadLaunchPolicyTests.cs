using System;
internal static class BrowserReadLaunchPolicyTests
{
    static int Main() {
        if(BrowserReadLaunchPolicy.Merge(null,null)!="Tab.ax") return 1;
        if(BrowserReadLaunchPolicy.Merge("Tab.clipboard", "Tab.ax,Tab.goto")!="Tab.clipboard,Tab.ax,Tab.goto") return 2;
        if(BrowserReadLaunchPolicy.Merge(" Tab.ax, Tab.ax ", "Tab.ax")!="Tab.ax") return 3;
        if(BrowserReadLaunchPolicy.Merge("Browser.history", "Tab.evaluate")!="Browser.history,Tab.evaluate,Tab.ax") return 4;
        if(BrowserReadLaunchPolicy.EnvironmentKey!="BROWSER_USE_DISABLE_API_MEMBERS") return 5;
        Console.WriteLine("PASS 5 launch-policy checks: preserve restrictions, deduplicate, add only Tab.ax");
        return 0;
    }
}
