using Comet;
using CometBaristaNotes.Components;
using CometBaristaNotes.Pages;
using Microsoft.Maui.Graphics;
using TabView = Comet.TabView;

namespace CometBaristaNotes;

public class BaristaApp : CometApp
{
	public BaristaApp()
	{
		Body = CreateRootView;
	}

	public static Comet.View CreateRootView()
	{
		var tabs = TabView();
		tabs.Add(MakeTab(new CoffeeDashboardPage(), "Coffee Lab", "tab_coffee.png"));
		tabs.Add(MakeTab(new ActivityFeedPage(), "Activity", "tab_activity.png"));
		tabs.Add(MakeTab(new SettingsPage(), "Settings", "tab_settings.png"));
		return tabs;
	}

	static NavigationView MakeTab(Comet.View page, string title, string icon)
	{
		var nav = NavigationView(page.Title(title));
		nav.SetEnvironment("NavigationBackgroundColor", Theme.Primary);
		nav.SetEnvironment("NavigationTextColor", Theme.Surface);
		nav.SetAutomationId($"barista-{title.Replace(" ", string.Empty).ToLowerInvariant()}-tab-root");
		nav.TabText(title);
		nav.TabIcon(icon);
		return nav;
	}
}
