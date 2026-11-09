using System.IO;
using System.Text.RegularExpressions;
using Adtd.App.Preview;
using Adtd.App.Services;
using Adtd.App.ViewModels;
using Adtd.Collect;
using Adtd.Collect.Tests;
using Adtd.Layout;
using Adtd.Model.Samples;
using Adtd.PowerPoint;
using Adtd.PowerPoint.Tests;
using DocumentFormat.OpenXml.Packaging;

namespace Adtd.App.Tests;

sealed class FakeDialogs : IDialogs
{
    public Queue<string?> Answers { get; } = new();
    public List<string> Opened { get; } = [];
    public string? AskOpen(string filter, string? initialPath = null) => Answers.Count > 0 ? Answers.Dequeue() : null;
    public string? AskSave(string filter, string suggestedPath) => Answers.Count > 0 ? Answers.Dequeue() : null;
    public void Open(string path) => Opened.Add(path);
    public void ShowInFolder(string path) => Opened.Add("folder:" + path);
}

sealed class MemoryStore : ISettingsStore
{
    public AppSettings Saved { get; private set; } = new();
    public string Json { get; private set; } = "";
    public AppSettings Load() => Saved;
    public void Save(AppSettings settings) { Saved = settings; Json = System.Text.Json.JsonSerializer.Serialize(settings); }
}

/// <summary>A scratch folder per test, deleted afterwards.</summary>
public sealed class Scratch : IDisposable
{
    public string Dir { get; } = Directory.CreateTempSubdirectory("adtd-app-tests-").FullName;
    public string Path(string name) => System.IO.Path.Combine(Dir, name);
    public string Template(bool withSlides = false)
    {
        var p = Path(withSlides ? "with-slides.potx" : "basic.potx");
        File.WriteAllBytes(p, TestTemplates.Basic(withSlides: withSlides));
        return p;
    }
    public void Dispose() { try { Directory.Delete(Dir, true); } catch (IOException) { } }
}

public class SessionTests
{
    static Session NewSession(MemoryStore? store = null) => new(store ?? new MemoryStore(), TimeSpan.Zero);

    [Fact]
    public async Task Plans_the_deck_when_a_forest_arrives_and_again_when_an_option_changes()
    {
        var session = NewSession();
        Assert.Null(session.Plan);
        session.SetSnapshot(SampleForests.Contoso(), "contoso");
        await session.Planning;
        var expected = DeckPlanner.Plan(SampleForests.Contoso(), Session.DefaultFrame, ThemeInfo.OfficeDefault, new Adtd.Imaging.SkiaTextMeasurer(ThemeInfo.OfficeDefault), session.Icon.Aspect);
        Assert.Equal(expected.Slides.Select(s => s.Title), session.Plan!.Slides.Select(s => s.Title));
        Assert.False(session.IsPlanning);

        session.Options = session.Options with { SiteDetails = false };
        await session.Planning;
        Assert.DoesNotContain(session.Plan!.Slides, s => s.Title.StartsWith("Site:"));
    }

    [Fact]
    public async Task A_newer_change_wins_over_a_plan_still_in_progress()
    {
        var session = new Session(new MemoryStore(), TimeSpan.FromMilliseconds(50));
        session.SetSnapshot(SampleForests.Large(), "large");
        var first = session.Planning;
        session.SetSnapshot(SampleForests.Single(), "single");
        await Task.WhenAll(first, session.Planning);
        Assert.Equal("Forest: corp.example", session.Plan!.Slides[1].Title);   // after the section header
    }

    [Fact]
    public async Task Plans_against_the_chosen_layout_and_remembers_it()
    {
        using var scratch = new Scratch();
        var store = new MemoryStore();
        var session = NewSession(store);
        var path = scratch.Template();
        session.LoadTemplate(path);
        Assert.Equal("Title Only", session.Layout!.Name);
        session.Layout = session.Template!.Find("Title and Content");
        session.SetSnapshot(SampleForests.Single(), "single");
        await session.Planning;
        Assert.Equal(session.Template.FrameOf(session.Layout!), session.PlanFrame);
        Assert.Equal(("Title and Content", path), (store.Saved.LayoutName, store.Saved.TemplatePath));

        // Next time, the same template opens on the same layout.
        var again = NewSession(store);
        again.LoadTemplate(path);
        Assert.Equal("Title and Content", again.Layout!.Name);
        Assert.Equal("Section Header", again.SectionLayout!.Name);
    }

    [Fact]
    public void Settings_survive_a_round_trip_and_bad_files_give_the_defaults()
    {
        using var scratch = new Scratch();
        var store = new SettingsStore(scratch.Path("settings.json"));
        store.Save(new AppSettings { Server = "contoso.com", Options = new DeckOptions { Replication = true, MaxDcsPerSlide = 12 } });
        var back = store.Load();
        Assert.Equal("contoso.com", back.Server);
        Assert.True(back.Options.Replication);
        Assert.Equal(12, back.Options.MaxDcsPerSlide);

        File.WriteAllText(scratch.Path("settings.json"), "{ not json");
        Assert.Equal(new AppSettings().Options, store.Load().Options);
    }
}

public class ConnectTests
{
    [Fact]
    public async Task Collects_a_forest_through_the_directory_and_never_saves_the_password()
    {
        var store = new MemoryStore();
        var session = new Session(store, TimeSpan.Zero);
        var source = SampleForests.Contoso();
        var dit = Dit.From(source);
        Adtd.Collect.ConnectionOptions? used = null;
        using var scratch = new Scratch();
        var vm = new ConnectViewModel(session, new FakeDialogs(), o => { used = o; return new FakeFactory(dit); }, () => FakeResolver.For(source), scratch.Path("logs"))
        {
            Server = "contoso.com",
            UseOtherCredentials = true,
            User = @"CONTOSO\admin",
            Password = "S3cret!",
        };
        await vm.Collect();
        Assert.False(vm.StatusIsError, vm.Status);
        Assert.Equal("contoso.com", session.Snapshot!.ForestName);
        Assert.Equal(source.DomainControllers.Count, vm.Counts.Single(c => c.Label == Strings.Get("Count_Dcs")).Value);
        Assert.Equal(("admin", "CONTOSO", "S3cret!"), (used!.Credential!.UserName, used.Credential.Domain, used.Credential.Password));
        Assert.Equal("", vm.Password);
        Assert.Contains(@"CONTOSO\\admin", store.Json);
        Assert.DoesNotContain("S3cret", store.Json);
        // The log has every step, in order, one per line.
        var log = File.ReadAllLines(Directory.GetFiles(scratch.Path("logs")).Single());
        Assert.StartsWith("ADTD for PowerPoint", log[0]);
        Assert.All(log.Skip(2), l => Assert.Matches(@"^\d\d:\d\d:\d\d\.\d{3} (Step|Detail|Warning) ", l));
        Assert.Contains(log, l => l.Contains("Done in"));
        Assert.DoesNotContain(log, l => l.Contains("S3cret"));
    }

    [Fact]
    public async Task A_failed_connection_says_why()
    {
        var vm = new ConnectViewModel(new Session(new MemoryStore(), TimeSpan.Zero), new FakeDialogs(),
            _ => new FakeFactory([], "nowhere.example")) { Server = "nowhere.example" };
        await vm.Validate();
        Assert.True(vm.StatusIsError);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public void Opens_saves_and_exports_snapshots()
    {
        using var scratch = new Scratch();
        var session = new Session(new MemoryStore(), TimeSpan.Zero);
        var dialogs = new FakeDialogs();
        var vm = new ConnectViewModel(session, dialogs) { SelectedSample = "single" };
        Assert.False(vm.SaveSnapshotCommand.CanExecute(null));
        vm.LoadSample();
        Assert.True(vm.HasSnapshot);

        dialogs.Answers.Enqueue(scratch.Path("single.json"));
        vm.SaveSnapshot();
        dialogs.Answers.Enqueue(scratch.Path("single.json"));
        vm.OpenSnapshot();
        Assert.False(vm.StatusIsError, vm.Status);
        Assert.Equal("single.json", session.SnapshotSource);
        Assert.Equal(scratch.Path("single.json"), session.Settings.SnapshotPath);

        File.WriteAllText(scratch.Path("bad.json"), "{");
        Assert.False(vm.OpenSnapshotFile(scratch.Path("bad.json")));
        Assert.True(vm.StatusIsError);
    }

    [Fact]
    public void The_csv_lists_every_dc_with_its_roles()
    {
        var f = SampleForests.Contoso();
        var lines = ConnectViewModel.Csv(f).TrimEnd().Split(Environment.NewLine);
        Assert.Equal(f.DomainControllers.Count + 1, lines.Length);
        Assert.StartsWith("Name,DNS name,Domain,Site", lines[0]);
        var schema = f.DomainControllers.Single(d => d.Key == f.SchemaMaster).Name;
        Assert.Contains(lines, l => l.StartsWith(schema + ",") && l.Contains("Schema"));
    }
}

public class StyleAndDiagramsTests
{
    [Fact]
    public void Shows_the_templates_layouts_theme_and_section_layout()
    {
        using var scratch = new Scratch();
        var session = new Session(new MemoryStore(), TimeSpan.Zero);
        var vm = new StyleViewModel(session, new FakeDialogs());
        Assert.False(vm.HasTemplate);
        Assert.Equal(10, vm.Swatches.Count);

        Assert.True(vm.OpenTemplate(scratch.Template(withSlides: true)));
        Assert.True(vm.HasTemplate);
        Assert.True(vm.HasExistingSlides);
        Assert.Equal(session.Template!.Layouts.Count, vm.Layouts.Count);
        Assert.Equal("Title Only", vm.SelectedLayout!.Layout.Name);
        Assert.NotNull(vm.Wireframe);
        Assert.Contains("Section Header", vm.SectionLayoutText);

        vm.SelectedLayout = vm.Layouts.Single(l => l.Layout.Name == "Diagram");
        Assert.Equal("Diagram", session.Layout!.Name);

        File.WriteAllText(scratch.Path("not.potx"), "nope");
        Assert.False(vm.OpenTemplate(scratch.Path("not.potx")));
        Assert.NotNull(vm.TemplateError);
        Assert.Equal("Diagram", session.Layout!.Name);   // the template in use is kept
    }

    [Fact]
    public void A_bad_icon_is_refused_and_the_built_in_one_kept()
    {
        using var scratch = new Scratch();
        var session = new Session(new MemoryStore(), TimeSpan.Zero);
        var vm = new StyleViewModel(session, new FakeDialogs());
        File.WriteAllText(scratch.Path("bad.svg"), "<svg");
        Assert.False(vm.OpenIcon(scratch.Path("bad.svg")));
        Assert.NotNull(vm.IconError);
        Assert.True(vm.IsDefaultIcon);
        Assert.NotNull(vm.IconImage);
    }

    [Fact]
    public async Task Diagram_switches_change_the_plan()
    {
        var session = new Session(new MemoryStore(), TimeSpan.Zero);
        var vm = new DiagramsViewModel(session);
        session.SetSnapshot(SampleForests.Contoso(), "contoso");
        await session.Planning;
        var before = session.Plan!.Slides.Count;
        vm.DomainDetails = false;
        vm.MaxDcsPerSlide = 0;   // clamped
        await session.Planning;
        Assert.False(session.Options.DomainDetails);
        Assert.Equal(1, session.Options.MaxDcsPerSlide);
        Assert.Equal(1, vm.MaxDcsPerSlide);
        Assert.DoesNotContain(session.Plan!.Slides, s => s.Title.StartsWith("Domain:"));
        Assert.NotEqual(before, session.Plan.Slides.Count);
        Assert.Contains(session.Plan.Slides.Count.ToString(), vm.SummaryText);
    }
}

public class PreviewAndExportTests
{
    [Fact]
    public async Task Every_slide_of_every_sample_can_be_drawn()
    {
        foreach (var name in SampleForests.Names.Where(n => n != "large"))
        {
            var session = new Session(new MemoryStore(), TimeSpan.Zero);
            var vm = new PreviewViewModel(session);
            session.SetSnapshot(SampleForests.ByName(name), name);
            await session.Planning;
            Assert.Equal(session.Plan!.Slides.Count, vm.Slides.Count);
            Assert.Equal(1, vm.Selected!.Number);
            foreach (var slide in vm.Slides)
            {
                var image = PlanRenderer.Render(slide.Slide, new PlanRenderer.Context(session.PlanFrame, session.PlanTheme, session.Icon.Png));
                Assert.Equal(13.333 * 96, image.Width, 1);
                Assert.Equal(7.5 * 96, image.Height, 1);
            }
            Assert.Contains(vm.Slides, s => s.Section == DeckPlanner.SitesSection);
        }
    }

    [Fact]
    public async Task Exports_a_valid_deck_to_the_template_and_opens_it()
    {
        using var scratch = new Scratch();
        var session = new Session(new MemoryStore(), TimeSpan.Zero);
        var dialogs = new FakeDialogs();
        var vm = new ExportViewModel(session, dialogs);
        Assert.Equal(Strings.Get("Export_NeedForest"), vm.Blocker);
        session.SetSnapshot(SampleForests.Contoso(), "contoso");
        Assert.Equal(Strings.Get("Export_NeedTemplate"), vm.Blocker);
        session.LoadTemplate(scratch.Template());
        await session.Planning;
        Assert.Null(vm.Blocker);
        Assert.EndsWith("contoso.com topology.pptx", vm.OutputPath);

        vm.OutputPath = scratch.Path("out\\deck");
        await vm.Export();
        var path = scratch.Path("out\\deck.pptx");
        Assert.Equal(ReportLevel.Success, vm.Report[0].Level);
        Assert.DoesNotContain(vm.Report, r => r.Level == ReportLevel.Error);
        Assert.Equal([path], dialogs.Opened);
        using var doc = PresentationDocument.Open(path, false);
        Assert.Equal(session.Plan!.Slides.Count, doc.PresentationPart!.SlideParts.Count());
        // The section header went on the template's section layout.
        var layouts = doc.PresentationPart.SlideParts.Select(s => s.SlideLayoutPart!.SlideLayout!.CommonSlideData!.Name!.Value).ToList();
        Assert.Contains("Section Header", layouts);
        Assert.Equal(scratch.Path("out"), session.Settings.OutputFolder);
    }
}

public class TextTests
{
    static string SrcDir([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "src", "Adtd.App"));

    [Fact]
    public void Every_string_the_ui_asks_for_exists()
    {
        var used = new HashSet<string>();
        foreach (var f in Directory.EnumerateFiles(SrcDir(), "*.xaml", SearchOption.AllDirectories))
            foreach (Match m in Regex.Matches(File.ReadAllText(f), @"\{l:S (\w+)\}")) used.Add(m.Groups[1].Value);
        foreach (var f in Directory.EnumerateFiles(SrcDir(), "*.cs", SearchOption.AllDirectories))
            foreach (Match m in Regex.Matches(File.ReadAllText(f), @"Strings\.(?:Get|F)\(""(\w+)""")) used.Add(m.Groups[1].Value);
        Assert.True(used.Count > 100);
        Assert.Empty(used.Where(k => Strings.Get(k) == $"[{k}]"));
        foreach (var slot in Enum.GetNames<SchemeSlot>().Where(s => s != "Hlink")) Assert.NotEqual($"[Slot_{slot}]", Strings.Get($"Slot_{slot}"));
    }

    [Fact]
    public void About_credits_adtd_net_and_lists_the_components()
    {
        var about = new AboutViewModel();
        Assert.Contains("Goran Mihelcic", about.Notices);
        Assert.Contains("MIT License", about.Notices);
        Assert.Contains("•  WPF-UI (lepo.co): The Fluent window and controls", about.Notices);
        Assert.DoesNotContain("|", about.Notices);
        Assert.StartsWith("Version ", about.Version);
    }
}
