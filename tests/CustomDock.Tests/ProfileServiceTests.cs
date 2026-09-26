using CustomDock.Core;

namespace CustomDock.Tests;

[Collection(ConfigFileCollection.Name)]
public class ProfileServiceTests
{
    private static (ConfigService Service, ProfileService Profiles) NewServices()
    {
        foreach (var file in Directory.EnumerateFiles(AppPaths.Root, "config.json*")) File.Delete(file);
        File.WriteAllText(AppPaths.ConfigFile, "{ \"version\": 2, \"edge\": \"Bottom\", \"items\": [] }");
        var service = new ConfigService();
        service.Load();
        return (service, new ProfileService(service));
    }

    [Fact]
    public void Switching_profiles_swaps_items_and_appearance()
    {
        var (service, profiles) = NewServices();
        var workApp = DockItem.App(@"C:\work.exe");
        service.AddItem(workApp);
        var home = profiles.SaveCurrentAs("Home");
        Assert.Equal(2, profiles.Profiles.Count);

        // Change the "Home" setup.
        service.RemoveItem(workApp.Id);
        service.AddItem(DockItem.App(@"C:\game.exe"));
        service.Config.Edge = DockEdge.Left;

        var first = profiles.Profiles[0];
        profiles.SwitchTo(first.Id);
        Assert.Equal(@"C:\work.exe", Assert.Single(service.Config.Items).Path);
        Assert.Equal(DockEdge.Bottom, service.Config.Edge);

        profiles.SwitchTo(home.Id);
        Assert.Equal(@"C:\game.exe", Assert.Single(service.Config.Items).Path);
        Assert.Equal(DockEdge.Left, service.Config.Edge);
    }

    [Fact]
    public void Display_rule_switches_to_the_matching_profile()
    {
        var (_, profiles) = NewServices();
        var docked = profiles.SaveCurrentAs("Desk");
        var laptop = profiles.Profiles[0];
        profiles.SetAutoDisplayCount(laptop.Id, 1);
        profiles.SetAutoDisplayCount(docked.Id, 2);

        profiles.ApplyDisplayRule(1);
        Assert.Equal(laptop.Id, profiles.Active?.Id);
        profiles.ApplyDisplayRule(2);
        Assert.Equal(docked.Id, profiles.Active?.Id);
    }

    [Fact]
    public void The_active_profile_cannot_be_deleted()
    {
        var (_, profiles) = NewServices();
        var active = profiles.SaveCurrentAs("Only");
        profiles.Delete(active.Id);
        Assert.Contains(profiles.Profiles, p => p.Id == active.Id);
    }
}
