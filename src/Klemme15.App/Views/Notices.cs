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

using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Klemme15.App.Views;

/// <summary>
/// "Safety, legal and transparency": docs/LEGAL.md, built into the app so the app and the website say the same thing.
/// The "In short" section is what people acknowledge once before they start.
/// </summary>
public static partial class NoticeText
{
    /// <summary>
    /// Raise when the meaning of the notice changes (not for typo fixes): everyone is asked to read the short version again.
    /// </summary>
    public const int Version = 1;

    public const string SummaryHeading = "In short";

    private static readonly Lazy<string> Text = new(() =>
    {
        using var stream = typeof(NoticeText).Assembly.GetManifestResourceStream("Klemme15.LEGAL.md");
        if (stream is null) return "# Safety, legal and transparency\n\nSee https://github.com/Expos-AI/Klemme15/blob/main/docs/LEGAL.md";
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    public static string Markdown => Text.Value;

    /// <summary>One line of the notice: a heading, a paragraph or a bullet, with Markdown's markers and links removed.</summary>
    public sealed record Block(char Kind, string Text);

    /// <summary>The notice as blocks: 'T' title, 'H' section heading, 'P' paragraph, 'B' bullet, 'S' small print.</summary>
    public static IReadOnlyList<Block> Blocks(bool summaryOnly = false)
    {
        var blocks = new List<Block>();
        var section = "";
        foreach (var raw in Markdown.Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Length == 0) continue;
            Block block = line switch
            {
                _ when line.StartsWith("# ", StringComparison.Ordinal) => new('T', Inline(line[2..])),
                _ when line.StartsWith("## ", StringComparison.Ordinal) => new('H', Inline(line[3..])),
                _ when line.StartsWith("- ", StringComparison.Ordinal) => new('B', Inline(line[2..])),
                _ when line.StartsWith('*') && line.EndsWith('*') && !line.StartsWith("**", StringComparison.Ordinal) => new('S', Inline(line.Trim('*'))),
                _ => new('P', Inline(line)),
            };
            if (block.Kind == 'H') section = block.Text;
            if (summaryOnly && (block.Kind != 'B' || section != SummaryHeading)) continue;
            blocks.Add(block);
        }
        return blocks;
    }

    /// <summary>Plain text: "**bold**" and "`code`" lose their markers, "[text](url)" keeps its text.</summary>
    public static string Inline(string s) => Code().Replace(Link().Replace(s.Replace("**", ""), "$1"), "$1");

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]+\)")]
    private static partial Regex Link();

    [GeneratedRegex("`([^`]+)`")]
    private static partial Regex Code();

    /// <summary>The blocks as text controls, styled like the rest of the app.</summary>
    public static StackPanel Build(IEnumerable<Block> blocks, bool skipTitle = false)
    {
        var panel = new StackPanel { Spacing = 8 };
        foreach (var b in blocks)
        {
            if (b.Kind == 'T' && skipTitle) continue;
            var text = new SelectableTextBlock { Text = b.Kind == 'B' ? "•  " + b.Text : b.Text, TextWrapping = TextWrapping.Wrap };
            switch (b.Kind)
            {
                case 'T': text.Classes.Add("title"); break;
                case 'H': text.Classes.Add("section"); text.Margin = new Thickness(0, 14, 0, 0); break;
                case 'B': text.Margin = new Thickness(10, 0, 0, 0); break;
                case 'S': text.Classes.Add("muted"); text.Classes.Add("small"); text.Margin = new Thickness(0, 14, 0, 0); break;
            }
            panel.Children.Add(text);
        }
        return panel;
    }
}

/// <summary>The whole notice, from the sidebar's "Safety, legal and privacy" button.</summary>
public sealed class NoticesWindow : Window
{
    public NoticesWindow()
    {
        Title = "Safety, legal and transparency · Klemme15";
        Width = 760;
        Height = 760;
        MinWidth = 420;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var close = new Button { Content = "Close", HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(16, 6) };
        close.Click += (_, _) => Close();
        var dock = new DockPanel { Margin = new Thickness(28, 22, 28, 20) };
        DockPanel.SetDock(close, Dock.Bottom);
        close.Margin = new Thickness(0, 14, 0, 0);
        dock.Children.Add(close);
        dock.Children.Add(new ScrollViewer { Content = NoticeText.Build(NoticeText.Blocks()), Padding = new Thickness(0, 0, 14, 0) });
        Content = dock;
    }
}

/// <summary>
/// Shown once (and again when <see cref="NoticeText.Version"/> goes up): the short version, a link to the rest, and a
/// box to tick before carrying on. It records that the person read it; it doesn't add conditions to the GPL.
/// </summary>
public sealed class WelcomeNoticeWindow : Window
{
    public WelcomeNoticeWindow()
    {
        Title = "Before you start · Klemme15";
        Width = 640;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 820;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var heading = new TextBlock { Text = "Before you start", TextWrapping = TextWrapping.Wrap };
        heading.Classes.Add("title");
        var intro = new TextBlock
        {
            Text = "Klemme15 talks to the computers in your car. Most of it only reads, but some functions switch parts on or write to a module. Please read this once.",
            TextWrapping = TextWrapping.Wrap,
        };
        intro.Classes.Add("muted");

        var readAll = new Button { Content = "Read everything (safety, copyright, privacy, how it's made)", HorizontalAlignment = HorizontalAlignment.Left };
        readAll.Classes.Add("quiet");
        readAll.Click += async (_, _) => await new NoticesWindow().ShowDialog(this);

        var agree = new CheckBox { Content = new TextBlock { Text = "I understand: I use Klemme15 at my own risk and I'm responsible for my car.", TextWrapping = TextWrapping.Wrap } };
        var go = new Button { Content = "Continue", IsEnabled = false, Padding = new Thickness(18, 7) };
        go.Classes.Add("primary");
        var quit = new Button { Content = "Quit", Padding = new Thickness(14, 7) };
        agree.IsCheckedChanged += (_, _) => go.IsEnabled = agree.IsChecked == true;
        go.Click += (_, _) => Close(true);
        quit.Click += (_, _) => Close(false);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(quit);
        buttons.Children.Add(go);

        var panel = new StackPanel { Spacing = 14, Margin = new Thickness(28, 24, 28, 22) };
        panel.Children.Add(heading);
        panel.Children.Add(intro);
        panel.Children.Add(NoticeText.Build(NoticeText.Blocks(summaryOnly: true)));
        panel.Children.Add(readAll);
        panel.Children.Add(agree);
        panel.Children.Add(buttons);
        Content = new ScrollViewer { Content = panel };
    }
}

/// <summary>A plain yes/no question with an explanation, for decisions that need a deliberate "yes".</summary>
public sealed class ConfirmWindow : Window
{
    public ConfirmWindow(string title, string message, string confirm)
    {
        Title = title + " · Klemme15";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var heading = new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap };
        heading.Classes.Add("section");
        var body = new SelectableTextBlock { Text = message, TextWrapping = TextWrapping.Wrap };
        var yes = new Button { Content = confirm, Padding = new Thickness(16, 7) };
        yes.Classes.Add("primary");
        var no = new Button { Content = "Cancel", Padding = new Thickness(14, 7) };
        yes.Click += (_, _) => Close(true);
        no.Click += (_, _) => Close(false);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(no);
        buttons.Children.Add(yes);
        var panel = new StackPanel { Spacing = 14, Margin = new Thickness(26, 22, 26, 20) };
        panel.Children.Add(heading);
        panel.Children.Add(body);
        panel.Children.Add(buttons);
        Content = panel;
    }
}
