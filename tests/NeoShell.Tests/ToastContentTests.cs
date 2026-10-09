using System.Xml.Linq;
using NeoShell.Interop.Notifications;
using NeoShell.Notifications;

namespace NeoShell.Tests;

public sealed class ToastContentTests
{
    private static ToastContent Parse(string xml, IReadOnlyDictionary<string, string>? data = null) =>
        ToastContent.Parse(XDocument.Parse(xml), source => "file:///" + source.TrimStart('/'), data);

    [Fact]
    public void Images_texts_attribution_and_header_are_read_from_the_generic_binding()
    {
        ToastContent toast = Parse(
            "<toast><header id='h' title='Group' arguments='a'/><visual><binding template='ToastGeneric'>" +
            "<text>Title</text><text>One</text><text placement='attribution'>via Mail</text><text>Two</text>" +
            "<image placement='appLogoOverride' hint-crop='circle' src='logo.png'/><image placement='hero' src='hero.png'/>" +
            "<image src='a.png'/><image src=''/><image src='b.png'/></binding></visual></toast>");

        Assert.Equal(("Title", "One\nTwo"), (toast.Title, toast.Body));
        Assert.Equal("via Mail", toast.Attribution);
        Assert.Equal("Group", toast.Header);
        Assert.Equal(new ToastImage("file:///logo.png", true), toast.Logo);
        Assert.Equal(new ToastImage("file:///hero.png", false), toast.Hero);
        Assert.Equal(["file:///a.png", "file:///b.png"], toast.Images.Select(i => i.Uri));
    }

    [Fact]
    public void Images_the_platform_cant_find_are_left_out()
    {
        ToastContent toast = ToastContent.Parse(
            XDocument.Parse("<toast><visual><binding template='ToastGeneric'><text>T</text><image placement='hero' src='x'/><image src='y'/></binding></visual></toast>"),
            _ => null);

        Assert.Null(toast.Hero);
        Assert.Empty(toast.Images);
    }

    [Fact]
    public void Actions_go_by_their_place_and_the_reply_button_by_its_text_box()
    {
        // As Explorer's toasts send them to the controller: the send button beside the text box is "replyBox", the
        // others count on without it, context menu items and system buttons included.
        ToastContent toast = Parse(
            "<toast><actions><input id='replyBox' type='text' placeHolderContent='Reply'/>" +
            "<input id='time' type='selection' defaultInput='15'><selection id='5' content='5 minutes'/><selection id='15' content='15 minutes'/></input>" +
            "<action content='A' arguments='a'/><action content='Send' arguments='send' hint-inputId='replyBox' imageUri='send.png'/>" +
            "<action content='B' arguments='b' placement='contextMenu'/><action activationType='system' arguments='snooze' hint-inputId='time' content=''/>" +
            "<action activationType='system' arguments='dismiss' content=''/></actions></toast>");

        Assert.Equal(["<0", "replyBox", "<1", "<2", "<3"], toast.Actions.Select(a => a.InvokeId));
        Assert.Equal(["A", "Snooze", "Dismiss"], toast.Buttons.Select(b => b.Content));
        Assert.Equal(["B"], toast.ContextMenu.Select(b => b.Content));
        Assert.Equal("Send", toast.ButtonBeside(toast.Inputs[0])?.Content);
        Assert.Equal("file:///send.png", toast.ButtonBeside(toast.Inputs[0])?.ImageUri);
        Assert.Null(toast.ButtonBeside(toast.Inputs[1]));

        ToastInput selection = toast.Inputs[1];
        Assert.False(selection.IsText);
        Assert.Equal("15", selection.Default);
        Assert.Equal(["5", "15"], selection.Choices.Select(c => c.Id));
        Assert.Equal("Reply", toast.Inputs[0].Placeholder);
    }

    [Fact]
    public void Progress_bars_take_their_data_bound_values()
    {
        const string xml =
            "<toast><visual><binding template='ToastGeneric'><text>Download</text>" +
            "<progress title='{title}' value='{progressValue}' valueStringOverride='{valueString}' status='Downloading...'/>" +
            "</binding></visual></toast>";

        ToastContent toast = Parse(xml, new Dictionary<string, string> { ["title"] = "Playlist", ["progressValue"] = "0.6", ["valueString"] = "15/26 songs" });

        Assert.Equal(new ToastProgress("Playlist", 0.6, "15/26 songs", "Downloading..."), toast.Progress);
        Assert.Equal(new ToastProgress(null, null, null, "Downloading..."), Parse(xml).Progress);
        Assert.Equal(
            new ToastProgress(null, null, null, "Wait"),
            Parse("<toast><visual><binding template='ToastGeneric'><progress value='indeterminate' status='Wait'/></binding></visual></toast>").Progress);
    }

    [Theory]
    [InlineData("reminder", true, true)]
    [InlineData("alarm", true, true)]
    [InlineData("incomingCall", true, true)]
    [InlineData("reminder", false, false)]
    [InlineData("urgent", true, false)]
    [InlineData(null, true, false)]
    public void Reminders_alarms_and_calls_with_a_button_stay_until_dismissed(string? scenario, bool button, bool stays)
    {
        string actions = button ? "<actions><action content='OK' arguments='ok'/></actions>" : "";
        ToastContent toast = Parse($"<toast {(scenario is null ? "" : $"scenario='{scenario}'")}>{actions}</toast>");

        Assert.Equal(stays, toast.StaysUntilDismissed);
        Assert.Equal(stays ? null : TimeSpan.FromSeconds(5), ToastLayout.Duration(toast, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Long_toasts_stay_25_seconds_and_toasts_without_xml_the_systems_time()
    {
        Assert.Equal(TimeSpan.FromSeconds(25), ToastLayout.Duration(Parse("<toast duration='long'/>"), TimeSpan.FromSeconds(5)));
        Assert.Equal(TimeSpan.FromSeconds(7), ToastLayout.Duration(null, TimeSpan.FromSeconds(7)));
    }

    [Fact]
    public void An_incoming_call_has_its_last_button_alone_under_the_others()
    {
        const string actions = "<actions><action content='One' arguments='1'/><action content='Two' arguments='2'/><action content='Three' arguments='3'/></actions>";
        ToastContent call = Parse($"<toast scenario='incomingCall'>{actions}</toast>");
        ToastContent styledCall = Parse($"<toast scenario='incomingCall' useButtonStyle='true'>{actions}</toast>");
        ToastContent plain = Parse($"<toast>{actions}</toast>");

        Assert.Equal([["One", "Two"], ["Three"]], ToastLayout.ButtonRows(call).Select(r => r.Select(b => b.Content)));
        Assert.True(ToastLayout.IsAccent(call, call.Buttons.Last()));
        Assert.Equal([["One", "Two", "Three"]], ToastLayout.ButtonRows(styledCall).Select(r => r.Select(b => b.Content)));
        Assert.False(ToastLayout.IsAccent(styledCall, styledCall.Buttons.Last()));
        Assert.Equal([["One", "Two", "Three"]], ToastLayout.ButtonRows(plain).Select(r => r.Select(b => b.Content)));
        Assert.Empty(ToastLayout.ButtonRows(Parse("<toast/>")));
    }

    [Fact]
    public void Button_styles_and_icons_depend_on_use_button_style()
    {
        const string actions = "<actions><action content='OK' arguments='ok' hint-buttonStyle='Success' imageUri='ok.png'/><action content='No' arguments='no' hint-buttonStyle='Critical'/></actions>";
        ToastContent plain = Parse($"<toast>{actions}</toast>");
        ToastContent styled = Parse($"<toast useButtonStyle='true'>{actions}</toast>");

        // Without useButtonStyle the styles don't count, and an icon goes above the text of taller buttons.
        Assert.Null(ToastLayout.Style(plain, plain.Buttons.First()));
        Assert.True(ToastLayout.IconsAbove(plain));
        Assert.Equal(["Success", "Critical"], styled.Buttons.Select(b => ToastLayout.Style(styled, b)));
        Assert.False(ToastLayout.IconsAbove(styled));
    }

    [Fact]
    public void Logos_cropped_to_a_circle_are_bigger()
    {
        Assert.Equal(60, ToastLayout.LogoSize(new ToastImage("file:///a.png", Circle: true)));
        Assert.Equal(48, ToastLayout.LogoSize(new ToastImage("file:///a.png", Circle: false)));
    }

    [Fact]
    public void Progress_shows_its_own_value_text_else_the_percentage()
    {
        Assert.Equal("60%", ToastLayout.ProgressValueText(new ToastProgress(null, 0.6, null, "")));
        Assert.Equal("15/26 songs", ToastLayout.ProgressValueText(new ToastProgress(null, 0.6, "15/26 songs", "")));
        Assert.Equal("", ToastLayout.ProgressValueText(new ToastProgress(null, null, null, "")));
    }

    [Fact]
    public void Notification_center_cards_expand_only_for_more_than_texts_and_logo()
    {
        Assert.False(ToastLayout.HasMore(null));
        Assert.False(ToastLayout.HasMore(Parse("<toast><visual><binding template='ToastGeneric'><text>T</text><image placement='appLogoOverride' src='l.png'/></binding></visual></toast>")));
        Assert.True(ToastLayout.HasMore(Parse("<toast><actions><action content='OK' arguments='ok'/></actions></toast>")));
        Assert.False(ToastLayout.HasMore(Parse("<toast><actions><action content='OK' arguments='ok' placement='contextMenu'/></actions></toast>")));
    }

    [Fact]
    public void Images_are_found_where_the_notification_platform_looks()
    {
        string root = Path.Combine(Path.GetTempPath(), "NeoShellToastImages" + Guid.NewGuid().ToString("N"));
        string package = Path.Combine(root, "Package");
        string data = Path.Combine(root, "LocalState");
        Directory.CreateDirectory(Path.Combine(package, "Assets"));
        Directory.CreateDirectory(data);
        try
        {
            File.WriteAllBytes(Path.Combine(package, "Assets", "Hero Image.png"), []);
            File.WriteAllBytes(Path.Combine(package, "Assets", "Logo.scale-100.png"), []);
            File.WriteAllBytes(Path.Combine(package, "Assets", "Logo.scale-200.png"), []);
            File.WriteAllBytes(Path.Combine(data, "photo.jpg"), []);

            Assert.Equal(new Uri(Path.Combine(package, "Assets", "Hero Image.png")).AbsoluteUri,
                ToastContent.LocateImage("ms-appx:///Assets/Hero%20Image.png", package, data, allowsWeb: false));
            Assert.Equal(new Uri(Path.Combine(package, "Assets", "Logo.scale-200.png")).AbsoluteUri,
                ToastContent.LocateImage("ms-appx:///Assets/Logo.png", package, data, allowsWeb: false));
            Assert.Equal(new Uri(Path.Combine(data, "photo.jpg")).AbsoluteUri,
                ToastContent.LocateImage("ms-appdata:///local/photo.jpg", package, data, allowsWeb: false));
            Assert.Equal(new Uri(Path.Combine(data, "photo.jpg")).AbsoluteUri,
                ToastContent.LocateImage(new Uri(Path.Combine(data, "photo.jpg")).AbsoluteUri, null, null, allowsWeb: false));
            Assert.Null(ToastContent.LocateImage("ms-appx:///Assets/Missing.png", package, data, allowsWeb: false));
            Assert.Null(ToastContent.LocateImage("ms-appx:///Assets/Hero%20Image.png", null, null, allowsWeb: false));
            Assert.Null(ToastContent.LocateImage("https://example.com/a.png", null, null, allowsWeb: false));
            Assert.Equal("https://example.com/a.png", ToastContent.LocateImage("https://example.com/a.png", package, data, allowsWeb: true));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
